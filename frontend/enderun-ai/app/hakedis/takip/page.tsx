"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";

import ErpShell from "@/components/erp/erp-shell";
import { amount, percent } from "@/lib/format/turkish";
import { Button, Input, Modal } from "@/components/ui";
import { ApiError } from "@/lib/api/api-client";
import { usePermissions } from "@/lib/use-permissions";
import {
  barterService,
  type BarterLedger,
} from "@/services/financial-instrument.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  progressPaymentService,
  type HakedisTracking,
} from "@/services/progress-payment.service";

function money(value: number) {
  return amount(value);
}

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "Takip tablosu yüklenemedi.";
}

/**
 * Hakediş takip tablosu — NATURA'daki Hak.Takip sayfasının karşılığı.
 *
 * Projenin tüm hakedişleri dönem sırasıyla tek tabloda; her kesintinin
 * dönem bazında seyri ve kümülatifi, barter ile ihzarat açık bakiyeleri.
 */
export default function HakedisTrackingPage() {
  const { has } = usePermissions();

  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [projectId, setProjectId] = useState("");
  const [tracking, setTracking] = useState<HakedisTracking | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const [barter, setBarter] = useState<BarterLedger | null>(null);
  const [receiptOpen, setReceiptOpen] = useState(false);
  const [receiptBusy, setReceiptBusy] = useState(false);
  const [receiptError, setReceiptError] = useState("");
  const [receiptForm, setReceiptForm] = useState({
    entryDate: new Date().toISOString().slice(0, 10),
    amount: "",
    description: "",
  });

  const loadBarter = useCallback(async () => {
    if (!projectId) {
      setBarter(null);
      return;
    }

    try {
      setBarter(await barterService.get(projectId));
    } catch {
      setBarter(null);
    }
  }, [projectId]);

  useEffect(() => {
    void (async () => {
      await loadBarter();
    })();
  }, [loadBarter]);

  async function saveReceipt() {
    setReceiptBusy(true);
    setReceiptError("");

    try {
      await barterService.addReceipt({
        projectId,
        entryDate: receiptForm.entryDate,
        amount: Number(receiptForm.amount.replace(",", ".")) || 0,
        description: receiptForm.description,
      });

      setReceiptOpen(false);
      setReceiptForm({
        entryDate: new Date().toISOString().slice(0, 10),
        amount: "",
        description: "",
      });

      await loadBarter();
    } catch (saveError) {
      setReceiptError(getErrorMessage(saveError));
    } finally {
      setReceiptBusy(false);
    }
  }

  useEffect(() => {
    projectService
      .getAll()
      .then(setProjects)
      .catch((loadError) => setError(getErrorMessage(loadError)));
  }, []);

  const load = useCallback(async (id: string) => {
    if (!id) {
      setTracking(null);
      return;
    }

    setLoading(true);
    setError("");

    try {
      setTracking(await progressPaymentService.getTracking(id));
    } catch (loadError) {
      setTracking(null);
      setError(getErrorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, []);

  // setState doğrudan efekt gövdesinde çağrılmıyor: await'ten sonra
  // çalışması gerekiyor, yoksa kaskad render uyarısı doğuyor.
  useEffect(() => {
    void (async () => {
      await load(projectId);
    })();
  }, [load, projectId]);

  return (
    <ErpShell
      design="redwood"
      title="Hakediş Takip"
      description="Projenin tüm hakedişleri, kesinti geçmişi ve açık bakiyeler"
    >
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <label className="block text-xs font-bold text-slate-600">
          Proje
          <select
            value={projectId}
            onChange={(event) => setProjectId(event.target.value)}
            className="mt-1 w-full max-w-md rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal text-slate-900"
          >
            <option value="">Seçin...</option>
            {projects.map((project) => (
              <option key={project.id} value={project.id}>
                {project.code} — {project.name}
              </option>
            ))}
          </select>
        </label>

        {error && (
          <p className="mt-3 rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
            {error}
          </p>
        )}
      </section>

      {loading && (
        <p className="mt-6 text-sm text-slate-500">Takip tablosu yükleniyor...</p>
      )}

      {tracking && !loading && (
        <>
          <section className="mt-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <Stat
              label="Kümülatif Hakediş"
              value={money(tracking.totals.cumulativeTotalAmount)}
              hint={
                tracking.project.contractAmount
                  ? `Sözleşmenin ${percent(tracking.totals.completionRate)}'i`
                  : undefined
              }
            />
            <Stat
              label="Açık İhzarat"
              value={money(tracking.totals.openAdvanceMaterialAmount)}
              hint="Henüz imalata dönmemiş"
            />
            <Stat
              label="Toplam Kesinti"
              value={money(tracking.totals.totalDeduction)}
            />
            <Stat
              label="Barter Bakiyesi"
              value={money(tracking.barter.openBalance)}
              hint={`Kesilen ${money(tracking.barter.totalDeducted)} · Teslim ${money(
                tracking.barter.totalReceived
              )}`}
            />
          </section>

          <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h2 className="text-sm font-bold text-slate-900">Barter Defteri</h2>
                <p className="mt-1 text-xs text-slate-500">
                  Hakedişten kesilen barter, işverenden alınacak mal/hizmet
                  alacağıdır. NAKİT DEĞİLDİR: nakit akış takviminde &quot;nakit
                  değil&quot; olarak görünür ve bakiyeye girmez.
                </p>
              </div>

              {has("hakedis.edit") ? (
                <Button type="button" onClick={() => setReceiptOpen(true)}>
                  Teslim alma gir
                </Button>
              ) : null}
            </div>

            {!barter || barter.entries.length === 0 ? (
              <p className="mt-3 text-sm text-slate-500">
                Bu projede barter hareketi yok.
              </p>
            ) : (
              <div className="mt-3 overflow-x-auto">
                <table className="min-w-full text-sm">
                  <thead className="bg-slate-50 text-left text-xs uppercase text-slate-500">
                    <tr>
                      <th className="px-3 py-2">Tarih</th>
                      <th className="px-3 py-2">Tür</th>
                      <th className="px-3 py-2">Açıklama</th>
                      <th className="px-3 py-2 text-right">Tutar</th>
                    </tr>
                  </thead>
                  <tbody>
                    {barter.entries.map((entry) => (
                      <tr key={entry.id} className="border-t border-slate-100">
                        <td className="px-3 py-2">
                          {new Date(entry.entryDate).toLocaleDateString("tr-TR")}
                        </td>
                        <td className="px-3 py-2">
                          {entry.entryType === 0 ? (
                            <span className="rounded bg-violet-100 px-1.5 py-0.5 text-[11px] text-violet-800">
                              kesinti — alacak doğdu
                            </span>
                          ) : (
                            <span className="rounded bg-emerald-100 px-1.5 py-0.5 text-[11px] text-emerald-800">
                              teslim alındı — alacak düştü
                            </span>
                          )}
                        </td>
                        <td className="px-3 py-2 text-slate-600">
                          {entry.description}
                          {entry.progressPaymentNumber ? (
                            <span className="ml-1 text-xs text-slate-400">
                              ({entry.progressPaymentNumber})
                            </span>
                          ) : null}
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums">
                          {money(entry.amount)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr className="border-t border-slate-200 bg-slate-50 font-medium">
                      <td className="px-3 py-2" colSpan={3}>
                        Açık barter alacağı
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums">
                        {money(barter.openBalance)}
                      </td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            )}
          </section>

          <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="text-sm font-bold text-slate-900">Dönemler</h2>

            {tracking.periods.length === 0 ? (
              <p className="mt-2 text-sm text-slate-500">
                Bu projede henüz hakediş yok.
              </p>
            ) : (
              <div className="mt-3 overflow-x-auto">
                <table className="w-full min-w-[1100px] text-sm">
                  <thead>
                    <tr className="border-b border-slate-200 text-left text-xs uppercase text-slate-500">
                      <th className="py-2">Dönem</th>
                      <th className="py-2">Tarih</th>
                      <th className="py-2 text-right">Kümülatif İmalat</th>
                      <th className="py-2 text-right">Açık İhzarat</th>
                      <th className="py-2 text-right">Bu Hakediş</th>
                      <th className="py-2 text-right">KDV</th>
                      <th className="py-2 text-right">Tevkifat</th>
                      <th className="py-2 text-right">Stopaj</th>
                      <th className="py-2 text-right">Kesinti</th>
                      <th className="py-2 text-right">Tahsil Edilecek</th>
                    </tr>
                  </thead>
                  <tbody>
                    {tracking.periods.map((period) => (
                      <tr key={period.id} className="border-b border-slate-100">
                        <td className="py-2">
                          <Link
                            href={`/hakedis/${period.id}`}
                            className="font-bold text-cyan-700 hover:underline"
                          >
                            {period.periodNumber}. {period.progressPaymentNumber}
                          </Link>
                        </td>
                        <td className="py-2 text-slate-600">
                          {new Date(period.progressPaymentDate).toLocaleDateString("tr-TR")}
                        </td>
                        <td className="py-2 text-right tabular-nums">
                          {money(period.cumulativeWorkAmount)}
                        </td>
                        <td className="py-2 text-right tabular-nums">
                          {money(period.cumulativeAdvanceMaterialAmount)}
                        </td>
                        <td className="py-2 text-right font-bold tabular-nums">
                          {money(period.currentAmount)}
                        </td>
                        <td className="py-2 text-right tabular-nums">
                          {money(period.vatAmount)}
                        </td>
                        <td className="py-2 text-right tabular-nums">
                          {money(period.withholdingAmount)}
                        </td>
                        <td className="py-2 text-right tabular-nums">
                          {money(period.incomeTaxWithholdingAmount)}
                        </td>
                        <td className="py-2 text-right tabular-nums">
                          {money(period.totalDeductionAmount)}
                        </td>
                        <td className="py-2 text-right font-bold tabular-nums">
                          {money(period.netPayableAmount)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          {/* Her kesinti türünün dönem dönem seyri — NATURA'daki asıl
              takip kısmı. */}
          {tracking.deductionTypes.length > 0 && (
            <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h2 className="text-sm font-bold text-slate-900">Kesinti Geçmişi</h2>

              <div className="mt-3 overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-slate-200 text-left text-xs uppercase text-slate-500">
                      <th className="py-2">Kesinti</th>
                      {tracking.periods.map((period) => (
                        <th key={period.id} className="py-2 text-right">
                          {period.periodNumber}. dönem
                        </th>
                      ))}
                      <th className="py-2 text-right">Toplam</th>
                    </tr>
                  </thead>
                  <tbody>
                    {tracking.deductionTypes.map((type) => (
                      <tr key={type.deductionType} className="border-b border-slate-100">
                        <td className="py-2 text-slate-800">{type.name}</td>
                        {tracking.periods.map((period) => {
                          const line = period.deductions.find(
                            (x) => x.deductionType === type.deductionType
                          );

                          return (
                            <td
                              key={period.id}
                              className="py-2 text-right tabular-nums text-slate-600"
                            >
                              {line ? money(line.amount) : "-"}
                            </td>
                          );
                        })}
                        <td className="py-2 text-right font-bold tabular-nums">
                          {money(type.totalAmount)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
        </>
      )}

      <Modal
        open={receiptOpen}
        onClose={() => setReceiptOpen(false)}
        title="Barter teslim alma"
        description="İşverenden teslim alınan mal/hizmet, açık barter alacağını düşürür."
        busy={receiptBusy}
      >
        <div className="space-y-3">
          {receiptError ? (
            <div className="rounded-md border border-rose-300 bg-rose-50 p-3 text-sm text-rose-800">
              {receiptError}
            </div>
          ) : null}

          <div className="grid grid-cols-2 gap-3">
            <label className="block text-xs text-slate-600">
              Tarih
              <Input
                type="date"
                value={receiptForm.entryDate}
                onChange={(event) =>
                  setReceiptForm({ ...receiptForm, entryDate: event.target.value })
                }
                className="mt-1 w-full"
              />
            </label>

            <label className="block text-xs text-slate-600">
              Tutar
              <Input
                value={receiptForm.amount}
                onChange={(event) =>
                  setReceiptForm({ ...receiptForm, amount: event.target.value })
                }
                className="mt-1 w-full"
                placeholder="0,00"
              />
            </label>
          </div>

          <label className="block text-xs text-slate-600">
            Açıklama
            <Input
              value={receiptForm.description}
              onChange={(event) =>
                setReceiptForm({ ...receiptForm, description: event.target.value })
              }
              className="mt-1 w-full"
              placeholder="Ör. B blok 12 no'lu daire teslim alındı"
            />
          </label>

          <p className="text-[11px] text-slate-500">
            Teslim alma tutarı açık bakiyeyi aşamaz — aşarsa alacağımızdan
            fazlasını almış görünürdük.
          </p>

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => setReceiptOpen(false)}
              disabled={receiptBusy}
            >
              Vazgeç
            </Button>
            <Button
              type="button"
              onClick={() => void saveReceipt()}
              disabled={receiptBusy}
            >
              Kaydet
            </Button>
          </div>
        </div>
      </Modal>
    </ErpShell>
  );
}

function Stat({
  label,
  value,
  hint,
}: {
  label: string;
  value: string;
  hint?: string;
}) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
      <p className="text-xs font-bold uppercase tracking-wide text-slate-500">
        {label}
      </p>
      <p className="mt-1 text-lg font-bold tabular-nums text-slate-900">{value}</p>
      {hint && <p className="mt-0.5 text-xs text-slate-500">{hint}</p>}
    </div>
  );
}
