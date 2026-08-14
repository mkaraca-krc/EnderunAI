"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { money, percent, quantity } from "@/lib/format/turkish";
import {
  contractSummaryProgressService,
  type ContractSummaryProgress,
  type FieldEmployerDifference,
} from "@/services/contract-summary-progress.service";

/** İlerleme oranı — iki ondalık. */
function rate(value: number) {
  return percent(value, 2);
}

/** Yüzde çubuğu — saha ve işveren kabulü üst üste. */
function ProgressBar({ field, employer }: { field: number; employer: number }) {
  return (
    <div
      style={{
        position: "relative",
        height: "16px",
        borderRadius: "999px",
        background: "var(--color-chart-track)",
        overflow: "hidden",
      }}
      title={`Saha ${rate(field)} · İşveren kabulü ${rate(employer)}`}
    >
      <div
        style={{
          position: "absolute",
          inset: 0,
          width: `${Math.min(100, field)}%`,
          background: "var(--color-chart-3)",
        }}
      />
      <div
        style={{
          position: "absolute",
          inset: 0,
          width: `${Math.min(100, employer)}%`,
          background: "var(--color-chart-1)",
        }}
      />
    </div>
  );
}

export default function ContractSummaryProgressPage() {
  const params = useParams<{ id: string }>();

  const [progress, setProgress] = useState<ContractSummaryProgress | null>(null);
  const [difference, setDifference] = useState<FieldEmployerDifference | null>(
    null
  );

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [tab, setTab] = useState<"ilerleme" | "fark">("ilerleme");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [progressResult, differenceResult] = await Promise.all([
        contractSummaryProgressService.getProgress(params.id),
        contractSummaryProgressService.getDifference(params.id),
      ]);

      setProgress(progressResult);
      setDifference(differenceResult);
    } catch (err) {
      setProgress(null);
      setDifference(null);
      setError(err instanceof Error ? err.message : "İlerleme alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    if (!params.id) return;

    const timer = window.setTimeout(() => void load(), 100);
    return () => window.clearTimeout(timer);
  }, [params.id, load]);

  if (loading) {
    return (
      <ErpShell design="redwood" title="İcmal İlerlemesi" description="Yükleniyor">
        <div className="erp-loading">İlerleme hesaplanıyor...</div>
      </ErpShell>
    );
  }

  if (error) {
    return (
      <ErpShell design="redwood" title="İcmal İlerlemesi" description="Hata">
        <div className="erp-alert error">{error}</div>
      </ErpShell>
    );
  }

  if (!progress?.hasContractSummary) {
    return (
      <ErpShell
      design="redwood"
        title="İcmal İlerlemesi"
        description="Sözleşme icmali tanımlı değil"
      >
        <div className="erp-alert warning">
          Bu projede sözleşme icmali tanımlı değil. İlerleme yüzdesi
          uydurulmuyor — önce icmali kurun.
        </div>

        <div className="erp-row-actions">
          {/* Saha raporu ve hakediş onayı bu oranları dışarıdan değiştiriyor. */}
          <button
            type="button"
            className="erp-secondary-button"
            disabled={loading}
            onClick={() => void load()}
          >
            Yenile
          </button>
          <Link className="erp-secondary-button" href={`/projeler/${params.id}`}>
            ← Proje
          </Link>
          <Link
            className="erp-secondary-button"
            href={`/projeler/${params.id}/kisimlar`}
          >
            Kısımları Tanımla
          </Link>
          <Link className="erp-primary-button" href="/kesifler/yeni">
            İcmal Oluştur
          </Link>
        </div>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      design="redwood"
      title="İcmal İlerlemesi"
      description={`${progress.boqNumber} — sözleşme, saha gerçekleşmesi ve işveren kabulü`}
    >
      <div className="erp-page-toolbar">
        <div>
          <strong>{money(progress.contractAmount)}</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            sözleşme bedeli · saha {rate(progress.fieldRate)} · işveren kabulü{" "}
            {rate(progress.employerRate)}
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <Link className="erp-secondary-button" href={`/projeler/${params.id}`}>
            ← Proje
          </Link>
          {/* Saha raporu ve hakediş onayı bu oranları dışarıdan
              değiştiriyor; tazelemeden ekran eskiyordu. */}
          <button
            type="button"
            className="erp-secondary-button"
            disabled={loading}
            onClick={() => void load()}
          >
            Yenile
          </button>
          <Link
            className="erp-secondary-button"
            href={`/kesifler/${progress.boqId}`}
          >
            İcmali Aç
          </Link>
        </div>
      </div>

      <div className="erp-stat-grid">
        <div className="erp-stat-card">
          <span className="erp-stat-label">Sözleşme Bedeli</span>
          <strong>{money(progress.contractAmount)}</strong>
          <small>icmal genel toplamı</small>
        </div>

        <div className="erp-stat-card">
          <span className="erp-stat-label">Saha Gerçekleşmesi</span>
          <strong>{rate(progress.fieldRate)}</strong>
          <small>{money(progress.fieldAmount)} — onaylı günlük rapor</small>
        </div>

        <div className="erp-stat-card">
          <span className="erp-stat-label">İşveren Kabulü</span>
          <strong>{rate(progress.employerRate)}</strong>
          <small>{money(progress.employerAmount)} — hakedişler</small>
        </div>

        <div className="erp-stat-card">
          <span className="erp-stat-label">Devreden İş</span>
          <strong>
            {difference?.hasContractSummary
              ? money(difference.totalPendingAmount)
              : "—"}
          </strong>
          <small>
            {difference?.hasContractSummary
              ? `${difference.differingItemCount} kalemde fark`
              : "sahada yapılmış, kabul edilmemiş"}
          </small>
        </div>
      </div>

      <div className="erp-project-tabs">
        <a
          className={tab === "ilerleme" ? "active" : ""}
          onClick={() => setTab("ilerleme")}
        >
          Kısım / Satır İlerlemesi
        </a>
        <a
          className={tab === "fark" ? "active" : ""}
          onClick={() => setTab("fark")}
        >
          Saha — İşveren Farkı
        </a>
      </div>

      {tab === "ilerleme" && (
        <>
          <div className="erp-alert">
            Açık turkuaz sahanın yaptığı, koyu turkuaz işverenin kabul ettiği
            iş. İkisi arasındaki boşluk devreden iştir.
          </div>

          {progress.sections.map((section) => (
            <div
              className="erp-table-card erp-mt"
              key={section.sectionId ?? section.name}
            >
              <div className="erp-table-header">
                <div>
                  <h2>{section.name}</h2>
                  <p>
                    {money(section.contractAmount)} · saha{" "}
                    {rate(section.fieldRate)} · işveren{" "}
                    {rate(section.employerRate)}
                  </p>
                </div>

                <div style={{ minWidth: "220px" }}>
                  <ProgressBar
                    field={section.fieldRate}
                    employer={section.employerRate}
                  />
                </div>
              </div>

              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Poz</th>
                      <th>Tanım</th>
                      <th>Birim</th>
                      <th style={{ textAlign: "right" }}>Sözleşme</th>
                      <th style={{ textAlign: "right" }}>Saha</th>
                      <th style={{ textAlign: "right" }}>İşveren Kabulü</th>
                      <th style={{ textAlign: "right" }}>Kalan</th>
                      <th style={{ textAlign: "right" }}>Devreden</th>
                      <th style={{ textAlign: "right" }}>İşveren Tutarı</th>
                      <th style={{ width: "140px" }}>İlerleme</th>
                    </tr>
                  </thead>
                  <tbody>
                    {section.items.map((line) => (
                      <tr key={line.boqItemId}>
                        <td>
                          <strong>{line.positionCode}</strong>
                        </td>
                        <td>{line.description}</td>
                        <td>{line.unit}</td>
                        <td style={{ textAlign: "right" }}>
                          {quantity(line.contractQuantity)}
                        </td>
                        <td style={{ textAlign: "right" }}>
                          {quantity(line.fieldQuantity)}
                          <small>{rate(line.fieldRate)}</small>
                        </td>
                        <td style={{ textAlign: "right" }}>
                          {quantity(line.employerQuantity)}
                          <small>{rate(line.employerRate)}</small>
                        </td>
                        <td style={{ textAlign: "right" }}>
                          {quantity(line.remainingQuantity)}
                        </td>
                        <td style={{ textAlign: "right" }}>
                          {line.pendingQuantity === 0 ? (
                            "—"
                          ) : (
                            <span
                              className={`erp-status ${
                                line.pendingQuantity > 0 ? "yellow" : "blue"
                              }`}
                            >
                              {quantity(line.pendingQuantity)}
                            </span>
                          )}
                        </td>
                        <td style={{ textAlign: "right" }}>
                          {money(line.employerAmount)}
                        </td>
                        <td>
                          <ProgressBar
                            field={line.fieldRate}
                            employer={line.employerRate}
                          />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ))}
        </>
      )}

      {tab === "fark" && difference?.hasContractSummary && (
        <div className="erp-table-card erp-mt">
          <div className="erp-table-header">
            <div>
              <h2>Saha — İşveren Farkı</h2>
              <p>
                Sahada yapılmış ama hakedişte kabul edilmemiş iş. Fark
                sistematik olarak büyüyorsa işveren düzenli eksik kabul ediyor
                demektir.
              </p>
            </div>
          </div>

          {difference.differingItemCount === 0 ? (
            <div className="erp-empty-state">
              <p>Saha ile işveren kabulü arasında fark yok.</p>
            </div>
          ) : (
            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Kısım</th>
                    <th>Poz</th>
                    <th>Tanım</th>
                    <th style={{ textAlign: "right" }}>Saha</th>
                    <th style={{ textAlign: "right" }}>İşveren</th>
                    <th style={{ textAlign: "right" }}>Fark</th>
                    <th style={{ textAlign: "right" }}>Tutar</th>
                  </tr>
                </thead>
                <tbody>
                  {difference.items
                    .filter((line) => line.pendingQuantity !== 0)
                    .map((line) => (
                      <tr key={`${line.sectionName}-${line.positionCode}`}>
                        <td>{line.sectionName}</td>
                        <td>
                          <strong>{line.positionCode}</strong>
                        </td>
                        <td>{line.description}</td>
                        <td style={{ textAlign: "right" }}>
                          {quantity(line.fieldQuantity)} {line.unit}
                        </td>
                        <td style={{ textAlign: "right" }}>
                          {quantity(line.employerQuantity)} {line.unit}
                        </td>
                        <td style={{ textAlign: "right" }}>
                          <span
                            className={`erp-status ${
                              line.pendingQuantity > 0 ? "yellow" : "blue"
                            }`}
                          >
                            {quantity(line.pendingQuantity)}
                          </span>
                        </td>
                        <td style={{ textAlign: "right" }}>
                          <strong>{money(line.pendingAmount)}</strong>
                        </td>
                      </tr>
                    ))}

                  <tr>
                    <td colSpan={6}>
                      <strong>TOPLAM DEVREDEN</strong>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <strong>
                        {money(difference.totalPendingAmount)}
                      </strong>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </ErpShell>
  );
}
