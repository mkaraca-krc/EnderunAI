"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

import {
  personnelDutyService,
  PersonnelDutyDetail,
  SettlementDecision,
} from "@/services/personnel-duty.service";
import {
  dutySurveyService,
  SurveyMeasurement,
  SurveyReport,
} from "@/services/duty-survey.service";

type Props = {
  dutyId: string;
  /** Masraf ve mahsup yazma yetkisi (personnel.edit). */
  canEdit: boolean;
  /** Saha raporu yazma yetkisi (projects.edit ya da site-reports.edit). */
  canWriteReport: boolean;
  /** Keşif sonucunu karara bağlama yetkisi (projects.edit). */
  canDecideOutcome: boolean;
  onChanged: () => void;
  onClose: () => void;
};

const currency = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  maximumFractionDigits: 2,
});

/** Tutar yoksa gizlenmiş demektir — sıfır ile karıştırılmamalı. */
function money(value?: number | null) {
  return value === null || value === undefined ? "—" : currency.format(value);
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

type ExpenseForm = {
  travelCost: string;
  accommodationCost: string;
  receiptAmount: string;
};

type ReportForm = {
  reportDate: string;
  summary: string;
  siteConditions: string;
  accessNotes: string;
  risks: string;
  recommendBid: string;
  measurements: SurveyMeasurement[];
};

const emptyReport: ReportForm = {
  reportDate: "",
  summary: "",
  siteConditions: "",
  accessNotes: "",
  risks: "",
  recommendBid: "",
  measurements: [],
};

/**
 * Görevin tüm akışı tek panelde: masraf → fiş → mahsup, keşifse saha
 * raporu ve kazan/kaybet kararı.
 *
 * TUTARLAR: uç extra_payment.view yoksa tutarları null döndürüyor;
 * panel de o alanları hiç göstermiyor. Maskeleme burada değil uçta —
 * ekranın gizlemesi güvenlik değil, nezaket.
 */
export default function DutyDetailPanel({
  dutyId,
  canEdit,
  canWriteReport,
  canDecideOutcome,
  onChanged,
  onClose,
}: Props) {
  const [duty, setDuty] = useState<PersonnelDutyDetail | null>(null);
  const [report, setReport] = useState<SurveyReport | null>(null);

  const [expense, setExpense] = useState<ExpenseForm>({
    travelCost: "0",
    accommodationCost: "0",
    receiptAmount: "0",
  });

  const [reportForm, setReportForm] = useState<ReportForm>(emptyReport);

  const [allowanceValue, setAllowanceValue] = useState("0");
  const [allowanceNote, setAllowanceNote] = useState("");
  const [showAllowanceForm, setShowAllowanceForm] = useState(false);

  const [settlementNote, setSettlementNote] = useState("");
  const [installments, setInstallments] = useState("1");
  const [outcomeNote, setOutcomeNote] = useState("");

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const load = useCallback(async () => {
    const detail = await personnelDutyService.get(dutyId);

    const surveyReport =
      detail.dutyType === 1 ? await dutySurveyService.getReport(dutyId) : null;

    return { detail, surveyReport };
  }, [dutyId]);

  useEffect(() => {
    let active = true;

    async function run() {
      try {
        const { detail, surveyReport } = await load();

        if (!active) return;

        setDuty(detail);
        setReport(surveyReport);

        setExpense({
          travelCost: String(detail.travelCost ?? 0),
          accommodationCost: String(detail.accommodationCost ?? 0),
          receiptAmount: String(detail.receiptAmount ?? 0),
        });

        setAllowanceValue(String(detail.dailyAllowance ?? 0));

        setReportForm(
          surveyReport
            ? {
                reportDate: surveyReport.reportDate.slice(0, 10),
                summary: surveyReport.summary,
                siteConditions: surveyReport.siteConditions ?? "",
                accessNotes: surveyReport.accessNotes ?? "",
                risks: surveyReport.risks ?? "",
                recommendBid:
                  surveyReport.recommendBid === null ||
                  surveyReport.recommendBid === undefined
                    ? ""
                    : String(surveyReport.recommendBid),
                measurements: surveyReport.measurements,
              }
            : {
                ...emptyReport,
                reportDate: detail.startDate.slice(0, 10),
              }
        );

        setError("");
      } catch (err) {
        if (active) {
          setError(
            err instanceof Error ? err.message : "Görev detayı yüklenemedi."
          );
        }
      } finally {
        if (active) setLoading(false);
      }
    }

    run();

    return () => {
      active = false;
    };
  }, [load]);

  async function refresh() {
    const { detail, surveyReport } = await load();

    setDuty(detail);
    setReport(surveyReport);
    onChanged();
  }

  async function run(key: string, action: () => Promise<string>) {
    setBusy(key);
    setError("");
    setSuccess("");

    try {
      setSuccess(await action());
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşlem başarısız.");
    } finally {
      setBusy("");
    }
  }

  function saveExpense(event: FormEvent) {
    event.preventDefault();

    run("expense", async () => {
      const travelCost = Number(expense.travelCost);
      const accommodationCost = Number(expense.accommodationCost);
      const receiptAmount = Number(expense.receiptAmount);

      if (
        !Number.isFinite(travelCost) ||
        !Number.isFinite(accommodationCost) ||
        !Number.isFinite(receiptAmount)
      ) {
        throw new Error("Tutarlar sayı olmalıdır.");
      }

      await personnelDutyService.saveExpense(dutyId, {
        travelCost,
        accommodationCost,
        receiptAmount,
      });

      return "Görev masrafı kaydedildi.";
    });
  }

  function reviseAllowance(event: FormEvent) {
    event.preventDefault();

    run("allowance", async () => {
      const value = Number(allowanceValue);

      if (!Number.isFinite(value) || value < 0) {
        throw new Error("Günlük harcırah negatif olamaz.");
      }

      if (!allowanceNote.trim()) {
        throw new Error("Düzeltme gerekçesi zorunludur.");
      }

      await personnelDutyService.reviseAllowance(
        dutyId,
        value,
        allowanceNote.trim()
      );

      setAllowanceNote("");
      setShowAllowanceForm(false);

      return "Harcırah düzeltildi.";
    });
  }

  function settle(decision: SettlementDecision) {
    run("settle", async () => {
      if (!settlementNote.trim()) {
        throw new Error("Mahsup gerekçesi zorunludur.");
      }

      await personnelDutyService.settle(dutyId, {
        decision,
        note: settlementNote.trim(),
        installmentCount: Number(installments) || 1,
      });

      setSettlementNote("");

      return decision === 0
        ? "Fark personelden kesilmek üzere harcırah mahsubu açıldı."
        : "Fark şirket gideri olarak kabul edildi.";
    });
  }

  function saveReport(event: FormEvent) {
    event.preventDefault();

    run("report", async () => {
      if (!reportForm.summary.trim()) {
        throw new Error("Rapor özeti zorunludur.");
      }

      await dutySurveyService.saveReport(dutyId, {
        reportDate: reportForm.reportDate || null,
        summary: reportForm.summary.trim(),
        siteConditions: reportForm.siteConditions.trim() || null,
        accessNotes: reportForm.accessNotes.trim() || null,
        risks: reportForm.risks.trim() || null,
        recommendBid:
          reportForm.recommendBid === ""
            ? null
            : reportForm.recommendBid === "true",
        measurements: reportForm.measurements.filter((x) =>
          x.description.trim()
        ),
      });

      return "Saha raporu kaydedildi.";
    });
  }

  function setOutcome(outcome: 1 | 2) {
    if (!duty) return;

    run("outcome", async () => {
      if (outcome === 2 && !outcomeNote.trim()) {
        throw new Error("Kaybetme gerekçesi zorunludur.");
      }

      await dutySurveyService.setOutcome(
        duty.targetProjectId,
        outcome,
        outcomeNote.trim() || "Kazanıldı"
      );

      setOutcomeNote("");

      return outcome === 1
        ? "İş kazanıldı; proje aktife alındı ve keşif masrafı projede kaldı."
        : "Teklif kaybedildi; masraf proje keşif gideri olarak kaldı, rapor arşivde.";
    });
  }

  function updateMeasurement(
    index: number,
    patch: Partial<SurveyMeasurement>
  ) {
    setReportForm((current) => ({
      ...current,
      measurements: current.measurements.map((item, position) =>
        position === index ? { ...item, ...patch } : item
      ),
    }));
  }

  if (loading) {
    return (
      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 text-sm text-slate-500 shadow-sm">
        Görev detayı yükleniyor…
      </section>
    );
  }

  if (!duty) {
    return (
      <section className="mb-5 rounded-xl border border-red-200 bg-red-50 p-5 text-sm text-red-700">
        {error || "Görev bulunamadı."}
      </section>
    );
  }

  const isSurvey = duty.dutyType === 1;
  const isApproved = duty.status === 1;

  // Tutar yazma kapısı SUNUCUDAN geliyor: ekran kendi kararını
  // vermiyor, uçtaki kuralı yansıtıyor.
  const canWriteAmounts = canEdit && duty.canWriteAmounts;

  // Mahsup karara bağlanmış olsa da harcırah düzeltilebilir; kesinti
  // açıldıysa avans da yeni farka çekilir.
  const settlementDecided =
    duty.settlementDecision !== null && duty.settlementDecision !== undefined;

  const advanceOpen = settlementDecided && duty.settlementDecision === 0;
  const outcomeDecided = duty.targetProjectSurveyOutcome !== 0;
  const projectInSurvey = duty.targetProjectStatus === 0;

  return (
    <section className="mb-5 rounded-xl border border-blue-200 bg-white shadow-sm">
      <header className="flex flex-wrap items-start justify-between gap-3 border-b border-slate-200 p-5">
        <div>
          <h2 className="text-xl font-bold text-slate-800">
            {duty.personnelFullName} — {duty.dutyTypeName}
          </h2>
          <p className="mt-1 text-sm text-slate-500">
            {duty.targetProjectCode} · {duty.targetProjectName} ·{" "}
            {formatDate(duty.startDate)} – {formatDate(duty.endDate)} (
            {duty.dayCount} gün)
          </p>
          <p className="mt-1 text-sm text-slate-600">{duty.purpose}</p>
        </div>

        <button
          type="button"
          onClick={onClose}
          className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-semibold"
        >
          Kapat
        </button>
      </header>

      {error && (
        <div className="m-5 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          {error}
        </div>
      )}

      {success && (
        <div className="m-5 rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-700">
          {success}
        </div>
      )}

      {!isApproved && (
        <div className="m-5 rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800">
          Görev onaylanmadan masraf hedef projeye yansımaz ve saha
          raporu yazılamaz.
        </div>
      )}

      {/* --- Masraf ve mahsup --- */}
      <div className="border-b border-slate-200 p-5">
        <h3 className="mb-3 text-lg font-bold text-slate-800">
          Masraf, Fiş ve Mahsup
        </h3>

        {duty.amountsHidden ? (
          <p className="rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
            Harcırah ve masraf tutarları elden ödeme niteliğindedir;
            görüntülemek ve girmek için ek ödeme yetkisi gerekir.
            Görevin kendisi ve tarihleri yukarıda görünüyor.
          </p>
        ) : (
          <div className="mb-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            {[
              ["Harcırah (hak edilen)", money(duty.totalAllowance)],
              ["Getirilen fiş", money(duty.receiptAmount)],
              ["Mahsup farkı", money(duty.settlementGap)],
              ["Projeye yansıyan toplam", money(duty.totalExpense)],
            ].map(([label, value]) => (
              <article
                key={label}
                className="rounded-lg border border-slate-200 bg-slate-50 p-4"
              >
                <span className="text-xs font-bold text-slate-500">
                  {label}
                </span>
                <strong className="mt-2 block text-lg text-slate-800">
                  {value}
                </strong>
              </article>
            ))}
          </div>
        )}

        {/* Harcırah düzeltme */}
        {canWriteAmounts && (
          <div className="mb-4 rounded-lg border border-slate-200 bg-slate-50 p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <h4 className="text-sm font-bold text-slate-700">
                  Günlük harcırah: {money(duty.dailyAllowance)}
                </h4>
                <p className="mt-1 text-xs text-slate-500">
                  {advanceOpen
                    ? "Düzeltme iz bırakır ve açılmış harcırah mahsubu " +
                      "avansı da yeni farka çekilir. Bordro bu avanstan " +
                      "kestiyse tutar kesilenin altına indirilemez."
                    : "Düzeltme iz bırakır: kim, ne zaman, hangi gerekçeyle."}
                </p>
              </div>

              <button
                type="button"
                onClick={() => setShowAllowanceForm((x) => !x)}
                className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-semibold"
              >
                {showAllowanceForm ? "Vazgeç" : "Harcırahı Düzelt"}
              </button>
            </div>

            {showAllowanceForm && (
              <form
                onSubmit={reviseAllowance}
                className="mt-3 grid gap-3 md:grid-cols-[1fr_2fr_auto]"
              >
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={allowanceValue}
                  onChange={(e) => setAllowanceValue(e.target.value)}
                  className="rounded-lg border border-slate-300 p-3"
                />

                <input
                  value={allowanceNote}
                  onChange={(e) => setAllowanceNote(e.target.value)}
                  placeholder="Düzeltme gerekçesi (zorunlu)"
                  className="rounded-lg border border-slate-300 p-3"
                />

                <button
                  type="submit"
                  disabled={busy === "allowance"}
                  className="rounded-lg bg-blue-700 px-4 py-3 text-sm font-semibold text-white disabled:opacity-60"
                >
                  {busy === "allowance" ? "Kaydediliyor…" : "Kaydet"}
                </button>
              </form>
            )}

            {duty.allowanceRevisedAtUtc && (
              <p className="mt-3 text-xs text-slate-500">
                Son düzeltme: {formatDate(duty.allowanceRevisedAtUtc)} ·{" "}
                {duty.allowanceRevisionNote}
              </p>
            )}
          </div>
        )}

        {canWriteAmounts && (
          <form onSubmit={saveExpense} className="grid gap-3 md:grid-cols-4">
            <label className="text-sm text-slate-600">
              Yol gideri
              <input
                type="number"
                step="0.01"
                min="0"
                value={expense.travelCost}
                onChange={(e) =>
                  setExpense((x) => ({ ...x, travelCost: e.target.value }))
                }
                className="mt-1 w-full rounded-lg border border-slate-300 p-3"
              />
            </label>

            <label className="text-sm text-slate-600">
              Konaklama gideri
              <input
                type="number"
                step="0.01"
                min="0"
                value={expense.accommodationCost}
                onChange={(e) =>
                  setExpense((x) => ({
                    ...x,
                    accommodationCost: e.target.value,
                  }))
                }
                className="mt-1 w-full rounded-lg border border-slate-300 p-3"
              />
            </label>

            <label className="text-sm text-slate-600">
              Getirilen fiş toplamı
              <input
                type="number"
                step="0.01"
                min="0"
                value={expense.receiptAmount}
                onChange={(e) =>
                  setExpense((x) => ({ ...x, receiptAmount: e.target.value }))
                }
                className="mt-1 w-full rounded-lg border border-slate-300 p-3"
              />
            </label>

            <div className="flex items-end">
              <button
                type="submit"
                disabled={busy === "expense"}
                className="w-full rounded-lg bg-blue-700 px-4 py-3 text-sm font-semibold text-white disabled:opacity-60"
              >
                {busy === "expense" ? "Kaydediliyor…" : "Masrafı Kaydet"}
              </button>
            </div>

            <p className="md:col-span-4 text-xs text-slate-500">
              Yol, konaklama ve harcırah hedef projeye AYRI kalemler
              olarak yazılır; tek toplama çökertilmez. Aynı masrafın
              yeniden kaydı satırı günceller, ikincisini açmaz.
            </p>
          </form>
        )}

        {/* Mahsup kararı */}
        {canWriteAmounts && duty.settlementPending && (
          <div className="mt-5 rounded-lg border border-amber-200 bg-amber-50 p-4">
            <h4 className="text-sm font-bold text-amber-900">
              Mahsup bekliyor — fark {money(duty.settlementGap)}
            </h4>
            <p className="mt-1 text-xs text-amber-800">
              Fiş harcırahı karşılamıyor. Fark ya personelden kesilir ya
              şirket gideri kabul edilir; karar kayıt altına alınır.
              Kesinti, bordroda zaten çalışan avans zincirinden
              &quot;Harcırah Mahsubu&quot; etiketiyle yürür.
            </p>

            <div className="mt-3 grid gap-3 md:grid-cols-3">
              <input
                value={settlementNote}
                onChange={(e) => setSettlementNote(e.target.value)}
                placeholder="Gerekçe (zorunlu)"
                className="rounded-lg border border-slate-300 p-3 md:col-span-2"
              />

              <label className="text-sm text-slate-600">
                Taksit
                <input
                  type="number"
                  min="1"
                  value={installments}
                  onChange={(e) => setInstallments(e.target.value)}
                  className="mt-1 w-full rounded-lg border border-slate-300 p-3"
                />
              </label>
            </div>

            <div className="mt-3 flex flex-wrap gap-2">
              <button
                type="button"
                disabled={busy === "settle"}
                onClick={() => settle(0)}
                className="rounded-lg bg-amber-700 px-4 py-2 text-sm font-semibold text-white disabled:opacity-60"
              >
                Personelden Düş
              </button>

              <button
                type="button"
                disabled={busy === "settle"}
                onClick={() => settle(1)}
                className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-semibold disabled:opacity-60"
              >
                Şirket Gideri Kabul Et
              </button>
            </div>
          </div>
        )}

        {!duty.amountsHidden &&
          duty.settlementDecision !== null &&
          duty.settlementDecision !== undefined && (
            <p className="mt-4 rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
              Mahsup kararı:{" "}
              <strong>
                {duty.settlementDecision === 0
                  ? "Personelden düşüldü (Harcırah Mahsubu avansı açıldı)"
                  : "Şirket gideri kabul edildi"}
              </strong>{" "}
              · {formatDate(duty.settlementAtUtc)} · {duty.settlementNote}
              {advanceOpen && (
                <span className="mt-2 block text-xs text-slate-500">
                  Harcırah ya da fiş düzeltilirse avans tutarı da farkı
                  izler; fark sıfıra inerse avans iptale çekilir.
                </span>
              )}
            </p>
          )}
      </div>

      {/* --- Saha raporu --- */}
      {isSurvey && (
        <div className="border-b border-slate-200 p-5">
          <h3 className="mb-1 text-lg font-bold text-slate-800">
            Keşif Saha Raporu
          </h3>
          <p className="mb-3 text-xs text-slate-500">
            Rapor arşivde kalır: iş kaybedilse de silinmez. Ölçümler ayrı
            satırlarda tutulur ki sonradan poza çevrilebilsin.
          </p>

          {!canWriteReport && !report && (
            <p className="rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
              Bu görev için saha raporu henüz yazılmamış.
            </p>
          )}

          {canWriteReport && (
            <form onSubmit={saveReport} className="grid gap-3">
              <div className="grid gap-3 md:grid-cols-3">
                <label className="text-sm text-slate-600">
                  Rapor tarihi
                  <input
                    type="date"
                    value={reportForm.reportDate}
                    onChange={(e) =>
                      setReportForm((x) => ({
                        ...x,
                        reportDate: e.target.value,
                      }))
                    }
                    className="mt-1 w-full rounded-lg border border-slate-300 p-3"
                  />
                </label>

                <label className="text-sm text-slate-600 md:col-span-2">
                  Teklif önerisi
                  <select
                    value={reportForm.recommendBid}
                    onChange={(e) =>
                      setReportForm((x) => ({
                        ...x,
                        recommendBid: e.target.value,
                      }))
                    }
                    className="mt-1 w-full rounded-lg border border-slate-300 p-3"
                  >
                    <option value="">Görüş belirtilmedi</option>
                    <option value="true">Teklif verilmeli</option>
                    <option value="false">Teklif verilmemeli</option>
                  </select>
                </label>
              </div>

              <textarea
                value={reportForm.summary}
                onChange={(e) =>
                  setReportForm((x) => ({ ...x, summary: e.target.value }))
                }
                rows={3}
                placeholder="Genel değerlendirme (zorunlu)"
                className="rounded-lg border border-slate-300 p-3"
              />

              <div className="grid gap-3 md:grid-cols-3">
                <textarea
                  value={reportForm.siteConditions}
                  onChange={(e) =>
                    setReportForm((x) => ({
                      ...x,
                      siteConditions: e.target.value,
                    }))
                  }
                  rows={3}
                  placeholder="Saha durumu"
                  className="rounded-lg border border-slate-300 p-3"
                />

                <textarea
                  value={reportForm.accessNotes}
                  onChange={(e) =>
                    setReportForm((x) => ({
                      ...x,
                      accessNotes: e.target.value,
                    }))
                  }
                  rows={3}
                  placeholder="Ulaşım, vinç, depolama"
                  className="rounded-lg border border-slate-300 p-3"
                />

                <textarea
                  value={reportForm.risks}
                  onChange={(e) =>
                    setReportForm((x) => ({ ...x, risks: e.target.value }))
                  }
                  rows={3}
                  placeholder="Riskler ve belirsizlikler"
                  className="rounded-lg border border-slate-300 p-3"
                />
              </div>

              <div>
                <div className="mb-2 flex items-center justify-between">
                  <h4 className="text-sm font-bold text-slate-700">
                    Ölçümler
                  </h4>

                  <button
                    type="button"
                    onClick={() =>
                      setReportForm((x) => ({
                        ...x,
                        measurements: [
                          ...x.measurements,
                          { description: "", quantity: null, unit: "" },
                        ],
                      }))
                    }
                    className="rounded-lg border border-slate-300 px-3 py-1 text-xs font-semibold"
                  >
                    + Ölçüm satırı
                  </button>
                </div>

                {reportForm.measurements.length === 0 && (
                  <p className="text-xs text-slate-500">
                    Ölçüm eklenmedi.
                  </p>
                )}

                {reportForm.measurements.map((item, index) => (
                  <div
                    key={index}
                    className="mb-2 grid gap-2 md:grid-cols-[2fr_1fr_1fr_2fr_auto]"
                  >
                    <input
                      value={item.description}
                      onChange={(e) =>
                        updateMeasurement(index, {
                          description: e.target.value,
                        })
                      }
                      placeholder="Ölçüm tanımı"
                      className="rounded-lg border border-slate-300 p-2 text-sm"
                    />
                    <input
                      type="number"
                      step="0.0001"
                      value={item.quantity ?? ""}
                      onChange={(e) =>
                        updateMeasurement(index, {
                          quantity:
                            e.target.value === ""
                              ? null
                              : Number(e.target.value),
                        })
                      }
                      placeholder="Miktar"
                      className="rounded-lg border border-slate-300 p-2 text-sm"
                    />
                    <input
                      value={item.unit ?? ""}
                      onChange={(e) =>
                        updateMeasurement(index, { unit: e.target.value })
                      }
                      placeholder="Birim"
                      className="rounded-lg border border-slate-300 p-2 text-sm"
                    />
                    <input
                      value={item.note ?? ""}
                      onChange={(e) =>
                        updateMeasurement(index, { note: e.target.value })
                      }
                      placeholder="Not"
                      className="rounded-lg border border-slate-300 p-2 text-sm"
                    />
                    <button
                      type="button"
                      onClick={() =>
                        setReportForm((x) => ({
                          ...x,
                          measurements: x.measurements.filter(
                            (_, position) => position !== index
                          ),
                        }))
                      }
                      className="rounded-lg border border-red-200 px-3 text-sm text-red-700"
                    >
                      Sil
                    </button>
                  </div>
                ))}
              </div>

              <div>
                <button
                  type="submit"
                  disabled={busy === "report"}
                  className="rounded-lg bg-blue-700 px-4 py-2 text-sm font-semibold text-white disabled:opacity-60"
                >
                  {busy === "report" ? "Kaydediliyor…" : "Raporu Kaydet"}
                </button>
              </div>
            </form>
          )}

          {report && (
            <div className="mt-4 rounded-lg border border-slate-200 bg-slate-50 p-4">
              <p className="text-xs font-bold text-slate-500">
                KAYITLI RAPOR · {formatDate(report.reportDate)} ·{" "}
                {report.measurements.length} ölçüm · {report.photos.length}{" "}
                fotoğraf
              </p>
              <p className="mt-2 text-sm text-slate-700">{report.summary}</p>
            </div>
          )}
        </div>
      )}

      {/* --- Kazan / kaybet --- */}
      {isSurvey && (
        <div className="p-5">
          <h3 className="mb-1 text-lg font-bold text-slate-800">
            Keşif Sonucu
          </h3>
          <p className="mb-3 text-xs text-slate-500">
            Kazanılırsa proje aktife alınır ve keşif masrafı projenin
            maliyetinde kalır. Kaybedilirse masraf silinmez;
            &quot;{duty.targetProjectName} — Proje Keşfi&quot; gideri
            olarak durur, saha raporu arşivde kalır.
          </p>

          {outcomeDecided ? (
            <p className="rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm text-slate-700">
              Sonuç girilmiş:{" "}
              <strong>
                {duty.targetProjectSurveyOutcome === 1
                  ? "Kazanıldı"
                  : "Kaybedildi"}
              </strong>
              . Sonuç bir kez girilir.
            </p>
          ) : !projectInSurvey ? (
            <p className="rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
              Hedef proje keşif statüsünde değil; keşif sonucu girilmez.
            </p>
          ) : !canDecideOutcome ? (
            <p className="rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm text-slate-600">
              Keşif sonucunu girmek için proje düzenleme yetkisi gerekir.
            </p>
          ) : (
            <div className="grid gap-3 md:grid-cols-[2fr_auto_auto]">
              <input
                value={outcomeNote}
                onChange={(e) => setOutcomeNote(e.target.value)}
                placeholder="Gerekçe (kaybedildi için zorunlu)"
                className="rounded-lg border border-slate-300 p-3"
              />

              <button
                type="button"
                disabled={busy === "outcome"}
                onClick={() => setOutcome(1)}
                className="rounded-lg bg-emerald-700 px-4 py-3 text-sm font-semibold text-white disabled:opacity-60"
              >
                Kazanıldı
              </button>

              <button
                type="button"
                disabled={busy === "outcome"}
                onClick={() => setOutcome(2)}
                className="rounded-lg border border-red-300 px-4 py-3 text-sm font-semibold text-red-700 disabled:opacity-60"
              >
                Kaybedildi
              </button>
            </div>
          )}
        </div>
      )}
    </section>
  );
}
