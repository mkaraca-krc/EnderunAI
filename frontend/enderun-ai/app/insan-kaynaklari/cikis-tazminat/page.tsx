"use client";

import RehireAssessmentPanel from "@/components/hr/rehire-assessment-panel";
import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { useModuleActions } from "@/lib/auth/module-actions";
import { amount } from "@/lib/format/turkish";
import { ApiError } from "@/lib/api/api-client";
import { Button } from "@/components/ui";

import {
  PersonnelListItem,
  personnelService,
} from "@/services/personnel.service";

import {
  TerminationStatus,
  terminationService,
  type TerminationCalculation,
  type TerminationComponent,
  type TerminationListItem,
  type TerminationReasonOption,
} from "@/services/termination.service";

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "İşlem tamamlanamadı.";
}

function money(value: number) {
  return amount(value);
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

/**
 * Personel çıkışı ve tazminat hesabı.
 *
 * Tazminat hakları ayrılış türünden otomatik türer; kullanıcı elle
 * işaretleyemez. Elden ödeme farkı yalnızca yetkili kullanıcıya gelir —
 * yetkisiz kullanıcıda alanlar null döndüğü için kart hiç çizilmez.
 */
export default function TerminationPage() {
  /**
   * Düğme -> uç -> izin (PersonnelTerminationsController):
   *   POST personnel-terminations              -> SALARY.manage
   *   POST personnel-terminations/{id}/finalize -> ATTENDANCE-PAYROLL.approve
   *   GET  personnel-terminations/simulate     -> salary.view (kapı yok:
   *        okuma, kayıt oluşturmuyor — düğmenin adı da bunu söylüyor)
   *
   * İKİ AYRI MODÜL: çıkış kaydını AÇMAK maaş yetkisi, KESİNLEŞTİRMEK
   * bordro onayı istiyor. Kesinleştirme tazminatı bordroya bağlıyor,
   * o yüzden onay makamı orada.
   */
  const salaryActions = useModuleActions("salary");
  const payrollActions = useModuleActions("attendance-payroll");

  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [reasons, setReasons] = useState<TerminationReasonOption[]>([]);
  const [terminations, setTerminations] = useState<TerminationListItem[]>([]);

  // Açık değerlendirme paneli — tabloda tek satır için açılır.
  const [assessing, setAssessing] =
    useState<{ id: string; name: string } | null>(null);

  const [personnelId, setPersonnelId] = useState("");
  const [reason, setReason] = useState(0);
  const [terminationDate, setTerminationDate] = useState(today());
  const [leaveOverride, setLeaveOverride] = useState("");
  const [note, setNote] = useState("");

  const [calculation, setCalculation] = useState<TerminationCalculation | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const refreshList = useCallback(async () => {
    try {
      setTerminations(await terminationService.list());
    } catch {
      // Liste okunamazsa hesaplama yine çalışsın.
    }
  }, []);

  useEffect(() => {
    void (async () => {
      try {
        const [people, reasonList] = await Promise.all([
          personnelService.getAll(),
          terminationService.getReasons(),
        ]);

        setPersonnel(people);
        setReasons(reasonList);
      } catch (loadError) {
        setError(getErrorMessage(loadError));
      }
    })();

    void refreshList();
  }, [refreshList]);

  const selectedReason = useMemo(
    () => reasons.find((x) => x.reason === reason) ?? null,
    [reason, reasons]
  );

  const simulate = useCallback(async () => {
    if (!personnelId) {
      setError("Önce personel seçin.");
      return;
    }

    setLoading(true);
    setError("");
    setNotice("");

    try {
      setCalculation(
        await terminationService.simulate({
          personnelId,
          reason,
          terminationDate,
          unusedLeaveDays: leaveOverride === "" ? undefined : Number(leaveOverride),
        })
      );
    } catch (simulateError) {
      setCalculation(null);
      setError(getErrorMessage(simulateError));
    } finally {
      setLoading(false);
    }
  }, [leaveOverride, personnelId, reason, terminationDate]);

  async function createRecord(event: FormEvent) {
    event.preventDefault();
    if (!personnelId || saving) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const result = await terminationService.create({
        personnelId,
        reason,
        terminationDate,
        unusedLeaveDays: leaveOverride === "" ? null : Number(leaveOverride),
        note: note || null,
      });

      setNotice(result.message);
      setNote("");
      await refreshList();
    } catch (createError) {
      setError(getErrorMessage(createError));
    } finally {
      setSaving(false);
    }
  }

  async function finalize(id: string) {
    setError("");
    setNotice("");

    try {
      const result = await terminationService.finalize(id);
      setNotice(result.message);
      await refreshList();
    } catch (finalizeError) {
      setError(getErrorMessage(finalizeError));
    }
  }

  /*
   * SÜTUNLAR VERİ OLARAK (F4p). Eylem sütunu `assessing`, `finalize` ve
   * `payrollActions` üzerine kapandığı için dizi belleğe ALINMIYOR
   * (F4b desen kararı) — "Kesinleştir" geri alınamayan bir işlem,
   * bayat kapanış onu yanlış çıkış üzerinde çalıştırabilirdi.
   */
  const terminationColumns: DataTableColumn<(typeof terminations)[number]>[] = [
    { key: "personel", header: "Personel", value: (row) => row.personnelFullName },
    {
      key: "tarih",
      header: "Tarih",
      value: (row) => new Date(row.terminationDate).toLocaleDateString("tr-TR"),
    },
    {
      key: "kidem",
      header: "Kıdem (gün)",
      numeric: true,
      value: (row) => row.serviceDays,
    },
    {
      key: "net",
      header: "Resmi Net",
      numeric: true,
      value: (row) => money(row.officialNetTotal),
      footer: (rows) =>
        money(rows.reduce((sum, row) => sum + row.officialNetTotal, 0)),
    },
    {
      key: "durum",
      header: "Durum",
      value: (row) =>
        row.status === TerminationStatus.Finalized ? "Kesinleşti" : "Taslak",
      render: (row) =>
        row.status === TerminationStatus.Finalized ? (
          <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-bold text-emerald-700">
            Kesinleşti
          </span>
        ) : (
          <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-bold text-amber-700">
            Taslak
          </span>
        ),
    },
    {
      key: "islem",
      header: "",
      value: () => "",
      render: (row) => (
        <div className="flex justify-end gap-2">
          {/* Değerlendirme geçmiş çıkışlara da eklenebilir: çıkış
              anında yapılamamış olabilir. */}
          <button
            type="button"
            onClick={() =>
              setAssessing(
                assessing?.id === row.id
                  ? null
                  : { id: row.id, name: row.personnelFullName }
              )
            }
            className="rounded-lg border border-slate-300 px-3 py-1.5 text-xs font-bold text-slate-700 hover:bg-slate-50"
          >
            {assessing?.id === row.id ? "Kapat" : "Ayrılış Değerlendirmesi"}
          </button>

          {row.status !== TerminationStatus.Finalized &&
            payrollActions.can("approve") && (
              <button
                type="button"
                onClick={() => void finalize(row.id)}
                className="rounded-lg bg-emerald-700 px-3 py-1.5 text-xs font-bold text-white hover:bg-emerald-800"
              >
                Kesinleştir
              </button>
            )}
        </div>
      ),
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title="Çıkış ve Tazminat"
      description="Personel çıkış kaydı, kıdem/ihbar/izin hesabı ve çıkış simülasyonu"
    >
      {/* Çıkış kayıtları İK tarafından işleniyor. */}
      <div className="mb-4 flex justify-end">
        <Button variant="secondary" onClick={() => void refreshList()}>Yenile</Button>
      </div>
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <h2 className="text-sm font-bold text-slate-900">Hesaplama</h2>
        <p className="mt-1 text-xs text-slate-500">
          Kayıt oluşturmadan &quot;şu an bu nedenle çıksa ne öderim&quot;
          hesabı yapabilirsiniz.
        </p>

        <form onSubmit={createRecord} className="mt-4 space-y-4">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <label className="text-xs font-bold text-slate-600">
              Personel
              <select
                value={personnelId}
                onChange={(e) => setPersonnelId(e.target.value)}
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
              >
                <option value="">Seçin...</option>
                {personnel.map((person) => (
                  <option key={person.id} value={person.id}>
                    {person.firstName} {person.lastName}
                  </option>
                ))}
              </select>
            </label>

            <label className="text-xs font-bold text-slate-600">
              Ayrılış Türü
              <select
                value={reason}
                onChange={(e) => setReason(Number(e.target.value))}
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
              >
                {reasons.map((option) => (
                  <option key={option.reason} value={option.reason}>
                    {option.name}
                  </option>
                ))}
              </select>
            </label>

            <label className="text-xs font-bold text-slate-600">
              Ayrılış Tarihi
              <input
                type="date"
                value={terminationDate}
                onChange={(e) => setTerminationDate(e.target.value)}
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
              />
            </label>

            <label className="text-xs font-bold text-slate-600">
              Kullanılmayan İzin (gün)
              <input
                type="number"
                min={0}
                step={0.5}
                value={leaveOverride}
                placeholder="Otomatik"
                onChange={(e) => setLeaveOverride(e.target.value)}
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
              />
            </label>
          </div>

          {/* Hak matrisi seçime göre canlı yansır; elle değiştirilemez. */}
          {selectedReason && (
            <div className="flex flex-wrap gap-2 text-xs">
              <RightBadge label="Kıdem tazminatı" granted={selectedReason.hasSeverance} />
              <RightBadge label="İhbar tazminatı" granted={selectedReason.hasNotice} />
              <RightBadge
                label="Kullanılmayan izin"
                granted={selectedReason.hasUnusedLeave}
              />
            </div>
          )}

          <label className="block text-xs font-bold text-slate-600">
            Not
            <input
              value={note}
              onChange={(e) => setNote(e.target.value)}
              className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
            />
          </label>

          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => void simulate()}
              disabled={loading}
              className="rounded-xl bg-cyan-700 px-4 py-2.5 text-sm font-bold text-white hover:bg-cyan-800 disabled:opacity-60"
            >
              {loading ? "Hesaplanıyor..." : "Hesapla (kayıt oluşturmaz)"}
            </button>
            {salaryActions.can("manage") && (
              <button
                type="submit"
                disabled={saving || !personnelId}
                className="rounded-xl border border-slate-300 px-4 py-2.5 text-sm font-bold text-slate-700 hover:bg-slate-50 disabled:opacity-60"
              >
                {saving ? "Kaydediliyor..." : "Çıkış Kaydı Oluştur"}
              </button>
            )}
          </div>
        </form>

        {error && (
          <p className="mt-3 rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
            {error}
          </p>
        )}

        {notice && (
          <p className="mt-3 rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-700">
            {notice}
          </p>
        )}
      </section>

      {calculation && <CalculationResult calculation={calculation} />}

      <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <h2 className="text-sm font-bold text-slate-900">Çıkış Kayıtları</h2>

        {terminations.length === 0 ? (
          <p className="mt-2 text-sm text-slate-500">Henüz çıkış kaydı yok.</p>
        ) : (
          <DataTable
            rows={terminations}
            columns={terminationColumns}
            rowKey={(row) => row.id}
            title="Çıkış ve Tazminat Kayıtları"
            /* Personel seçimi listeyi daraltıyor; sayfa 1'e dönmeli. */
            resetKey={personnelId}
          />
        )}

        {assessing ? (
          <div className="mt-4">
            <RehireAssessmentPanel
              key={assessing.id}
              terminationId={assessing.id}
              personnelFullName={assessing.name}
            />
          </div>
        ) : null}
      </section>
    </ErpShell>
  );
}

function RightBadge({ label, granted }: { label: string; granted: boolean }) {
  return (
    <span
      className={`rounded-full px-2.5 py-1 font-bold ${
        granted
          ? "bg-emerald-100 text-emerald-700"
          : "bg-slate-100 text-slate-500 line-through"
      }`}
    >
      {label}
    </span>
  );
}

/*
 * HESAP DÖKÜMÜ SÜTUNLARI (F4p) — `ComponentRow` bileşeninin yerine.
 *
 * "Hak doğmadı" durumu artık satırın kendisinden okunuyor: brüt sıfırsa
 * tutar sütunları tire basıyor. Eskiden o satır `colSpan={5}` ile tek
 * hücreye çöküyordu; sütun tabanlı tabloda bu mümkün değil ve gerekli
 * de değil — dışa aktarmada da "Hak doğmadı" açıkça yazılıyor.
 */
function componentRows(calculation: TerminationCalculation) {
  return [
    { label: "Kıdem tazminatı", component: calculation.officialSeverance },
    { label: "İhbar tazminatı", component: calculation.officialNotice },
    { label: "Kullanılmayan yıllık izin", component: calculation.officialLeave },
  ];
}

type ComponentRowData = ReturnType<typeof componentRows>[number];

function moneyOrDash(row: ComponentRowData, pick: (c: TerminationComponent) => number) {
  return row.component.gross === 0 ? "Hak doğmadı" : money(pick(row.component));
}

const componentColumns: DataTableColumn<ComponentRowData>[] = [
  { key: "kalem", header: "Kalem", value: (row) => row.label },
  {
    key: "brut",
    header: "Brüt",
    numeric: true,
    value: (row) => moneyOrDash(row, (c) => c.gross),
    footer: (rows) =>
      money(rows.reduce((sum, row) => sum + row.component.gross, 0)),
  },
  {
    key: "sgk",
    header: "SGK",
    numeric: true,
    value: (row) => moneyOrDash(row, (c) => c.sgkAmount),
    footer: (rows) =>
      money(rows.reduce((sum, row) => sum + row.component.sgkAmount, 0)),
  },
  {
    key: "gelir",
    header: "Gelir V.",
    numeric: true,
    value: (row) => moneyOrDash(row, (c) => c.incomeTax),
    footer: (rows) =>
      money(rows.reduce((sum, row) => sum + row.component.incomeTax, 0)),
  },
  {
    key: "damga",
    header: "Damga",
    numeric: true,
    value: (row) => moneyOrDash(row, (c) => c.stampTax),
    footer: (rows) =>
      money(rows.reduce((sum, row) => sum + row.component.stampTax, 0)),
  },
  {
    key: "net",
    header: "Net",
    numeric: true,
    value: (row) => moneyOrDash(row, (c) => c.net),
    render: (row) => <strong>{moneyOrDash(row, (c) => c.net)}</strong>,
    // RESMİ TOPLAM (belgelenen) — kalemlerin netlerinin toplamı.
    footer: (rows) => money(rows.reduce((sum, row) => sum + row.component.net, 0)),
  },
];


function CalculationResult({ calculation }: { calculation: TerminationCalculation }) {
  return (
    <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-sm font-bold text-slate-900">
          {calculation.personnelFullName} — {calculation.reasonName}
        </h2>
        <p className="text-xs text-slate-500">
          Kıdem: {calculation.fullServiceYears} yıl ({calculation.serviceDays} gün)
          {calculation.hasNoticeRight &&
            ` · İhbar süresi: ${calculation.noticeWeeks} hafta`}
          {` · Kullanılmayan izin: ${calculation.unusedLeaveDays} gün`}
        </p>
      </div>

      {calculation.warnings.length > 0 && (
        <ul className="mt-3 space-y-1">
          {calculation.warnings.map((warning) => (
            <li
              key={warning}
              className="rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800"
            >
              {warning}
            </li>
          ))}
        </ul>
      )}

      <div className="mt-4 overflow-x-auto">
        <DataTable
          rows={componentRows(calculation)}
          columns={componentColumns}
          rowKey={(row) => row.label}
          title="Tazminat Hesap Dökümü"
        />
      </div>

      {/* Elden kısım: yalnızca yetkili kullanıcıya gelir. */}
      {calculation.actualNetTotal !== null && (
        <div className="mt-4 rounded-xl border border-slate-300 bg-slate-50 p-4">
          <p className="text-xs font-bold uppercase tracking-wide text-slate-500">
            Ek ödeme dahil (gizli — yalnızca yetkili görür)
          </p>

          <dl className="mt-2 space-y-1 text-sm">
            <div className="flex justify-between">
              <dt className="text-slate-600">Aylık elden ödeme</dt>
              <dd className="tabular-nums text-slate-800">
                {money(calculation.extraMonthlyAmount ?? 0)}
              </dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-slate-600">Gerçek (ödenecek) toplam</dt>
              <dd className="tabular-nums font-bold text-slate-900">
                {money(calculation.actualNetTotal)}
              </dd>
            </div>
            <div className="flex justify-between border-t border-slate-300 pt-1">
              <dt className="font-bold text-slate-700">
                Elden ödenecek fark (ek ödeme kasası)
              </dt>
              <dd className="tabular-nums font-bold text-slate-900">
                {money(calculation.extraPaymentDifference ?? 0)}
              </dd>
            </div>
          </dl>

          <p className="mt-2 text-xs text-slate-500">
            Bu tutar resmi muhasebeye hiçbir kayıt üretmez.
          </p>
        </div>
      )}
    </section>
  );
}
