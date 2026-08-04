"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";

import ErpShell from "@/components/erp/erp-shell";
import { ApiError } from "@/lib/api/api-client";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  progressPaymentService,
  type HakedisTracking,
} from "@/services/progress-payment.service";

function money(value: number) {
  return value.toLocaleString("tr-TR", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
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
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [projectId, setProjectId] = useState("");
  const [tracking, setTracking] = useState<HakedisTracking | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

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

  useEffect(() => {
    void load(projectId);
  }, [load, projectId]);

  return (
    <ErpShell
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
                  ? `Sözleşmenin %${tracking.totals.completionRate.toLocaleString("tr-TR")}'i`
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
