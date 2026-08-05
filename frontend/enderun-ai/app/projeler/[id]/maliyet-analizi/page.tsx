"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  projectCostAnalysisService,
  ProjectCostClass,
  type ProjectCostAnalysis,
} from "@/services/project-cost-analysis.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  maximumFractionDigits: 0,
});

const moneyDetailed = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

function percent(value?: number | null) {
  if (value === null || value === undefined) return "—";
  return `%${value.toLocaleString("tr-TR", { maximumFractionDigits: 1 })}`;
}

/**
 * Aşım kırmızı, tasarruf yeşil. Maliyet tarafında "fazla" her zaman kötü
 * olduğu için işaret doğrudan renge çevrilebiliyor.
 */
function varianceColor(variance: number) {
  if (variance > 0) return "#b91c1c";
  if (variance < 0) return "#15803d";
  return "inherit";
}

/**
 * Aylık trend — maliyet bileşenleri yığılmış kolon, gelir çizgi olarak.
 * Grafik kütüphanesi yok: tek eksenli basit bir yığın, ölçek en büyük
 * aya göre normalize ediliyor.
 */
function MonthlyTrend({ analysis }: { analysis: ProjectCostAnalysis }) {
  const max = useMemo(
    () =>
      analysis.monthly.reduce(
        (highest, point) =>
          Math.max(highest, point.totalAmount, point.revenueAmount),
        0
      ),
    [analysis.monthly]
  );

  if (analysis.monthly.length === 0) {
    return (
      <div className="erp-empty-state">
        <strong>Henüz maliyet veya hakediş kaydı yok</strong>
        <p>Sarf, fatura ve puantaj girildikçe aylık seyir burada oluşur.</p>
      </div>
    );
  }

  const segments: {
    key: keyof ProjectCostAnalysis["monthly"][number];
    label: string;
    color: string;
  }[] = [
    { key: "materialAmount", label: "Malzeme", color: "#18797c" },
    { key: "laborAmount", label: "İşçilik", color: "#2db9be" },
    { key: "subcontractorLaborAmount", label: "Taşeron", color: "#7fd4d6" },
    { key: "overheadAmount", label: "Genel gider", color: "#f1a522" },
  ];

  return (
    <div style={{ display: "grid", gap: "12px" }}>
      <div style={{ display: "flex", gap: "14px", flexWrap: "wrap" }}>
        {segments.map((segment) => (
          <span key={segment.label} style={{ fontSize: "12px" }}>
            <span
              style={{
                display: "inline-block",
                width: "10px",
                height: "10px",
                borderRadius: "2px",
                background: segment.color,
                marginRight: "5px",
              }}
            />
            {segment.label}
          </span>
        ))}
        <span style={{ fontSize: "12px" }}>
          <span
            style={{
              display: "inline-block",
              width: "10px",
              height: "2px",
              background: "#0f4648",
              marginRight: "5px",
              verticalAlign: "middle",
            }}
          />
          Hakediş geliri
        </span>
      </div>

      <div
        style={{
          display: "flex",
          alignItems: "flex-end",
          gap: "10px",
          minHeight: "180px",
          overflowX: "auto",
          paddingBottom: "4px",
        }}
      >
        {analysis.monthly.map((point) => (
          <div
            key={point.label}
            style={{ minWidth: "56px", textAlign: "center" }}
            title={
              `${point.label} — maliyet ${moneyDetailed.format(point.totalAmount)}, ` +
              `gelir ${moneyDetailed.format(point.revenueAmount)}`
            }
          >
            <div
              style={{
                position: "relative",
                height: "150px",
                display: "flex",
                flexDirection: "column-reverse",
              }}
            >
              {segments.map((segment) => {
                const value = point[segment.key] as number;
                if (value <= 0) return null;

                return (
                  <div
                    key={segment.label}
                    style={{
                      height: `${max > 0 ? (value / max) * 100 : 0}%`,
                      background: segment.color,
                    }}
                  />
                );
              })}

              {point.revenueAmount > 0 && (
                <div
                  style={{
                    position: "absolute",
                    left: 0,
                    right: 0,
                    bottom: `${max > 0 ? (point.revenueAmount / max) * 100 : 0}%`,
                    borderTop: "2px solid #0f4648",
                  }}
                />
              )}
            </div>

            <small style={{ fontSize: "11px" }}>{point.label}</small>
          </div>
        ))}
      </div>
    </div>
  );
}

export default function ProjectCostAnalysisPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;

  const [analysis, setAnalysis] = useState<ProjectCostAnalysis | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setAnalysis(await projectCostAnalysisService.get(projectId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Maliyet analizi alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  useEffect(() => {
    void load();
  }, [load]);

  // Taşeron satırı işçilik bileşeninin içinde de sayıldığı için ana
  // karşılaştırma tablosunda ayrı satır olarak gösterilmez; alt kırılım
  // olarak işçilik satırının altına yazılır.
  const mainComponents = useMemo(
    () =>
      (analysis?.components ?? []).filter(
        (component) => component.costClass !== ProjectCostClass.SubcontractorLabor
      ),
    [analysis]
  );

  const subcontractor = useMemo(
    () =>
      (analysis?.components ?? []).find(
        (component) => component.costClass === ProjectCostClass.SubcontractorLabor
      ),
    [analysis]
  );

  return (
    <ErpShell
      title="Maliyet Analizi"
      description={
        analysis
          ? `${analysis.projectCode} — ${analysis.projectName}`
          : "Şantiye maliyeti, icmal öngörüsü ve kâr"
      }
    >
      {error && <div className="erp-alert error">{error}</div>}

      {loading ? (
        <div className="erp-panel erp-loading">Maliyet analizi hesaplanıyor...</div>
      ) : !analysis ? (
        <div className="erp-panel erp-empty-state">
          <strong>Analiz bulunamadı</strong>
        </div>
      ) : (
        <>
          <div className="erp-page-toolbar">
            <div>
              <strong>
                Gelir {money.format(analysis.revenueAmount)} · Maliyet{" "}
                {money.format(analysis.totalCost)}
              </strong>
              <small style={{ display: "block", marginTop: "4px" }}>
                Kâr{" "}
                <strong style={{ color: varianceColor(-analysis.profit) }}>
                  {money.format(analysis.profit)}
                </strong>{" "}
                · Marj {percent(analysis.profitMarginPercent)} · İlerleme{" "}
                {percent(analysis.progressRatio * 100)}
              </small>
            </div>

            <Link className="erp-secondary-button" href={`/projeler/${projectId}`}>
              Projeye Dön
            </Link>
          </div>

          {!analysis.hasContractBaseline && (
            <div className="erp-alert warning">
              Bu projede sözleşme referansı icmal yok; öngörü sütunları boş.
              Keşif ekranından bir icmali sözleşme referansı olarak işaretleyin.
            </div>
          )}

          <section className="erp-panel">
            <div className="erp-panel-header">
              <div>
                <h2>Üç Bileşen Karşılaştırması</h2>
                <p>
                  İcmalin üç bileşeni bizim satış fiyatı kırılımımızdır, maliyet
                  bütçesi değil: bu tablo bileşen bazında kâr olup olmadığını
                  gösterir. Karşılaştırma, hakediş ilerlemesine göre düzeltilmiş
                  öngörü üzerinden yapılır.
                </p>
              </div>
            </div>

            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Bileşen</th>
                    <th>Sözleşme öngörüsü</th>
                    <th>Hakedilen öngörü</th>
                    <th>Gerçekleşen</th>
                    <th>Fark</th>
                    <th>Fark %</th>
                  </tr>
                </thead>
                <tbody>
                  {mainComponents.map((component) => (
                    <tr key={component.costClass}>
                      <td>
                        <strong>{component.costClassName}</strong>
                        {component.costClass === ProjectCostClass.Labor &&
                          subcontractor &&
                          subcontractor.actual > 0 && (
                            <small>
                              içinde taşeron:{" "}
                              {moneyDetailed.format(subcontractor.actual)}
                            </small>
                          )}
                      </td>
                      <td>{money.format(component.forecastContract)}</td>
                      <td>{money.format(component.forecastEarned)}</td>
                      <td>{money.format(component.actual)}</td>
                      <td style={{ color: varianceColor(component.variance) }}>
                        <strong>{money.format(component.variance)}</strong>
                      </td>
                      <td style={{ color: varianceColor(component.variance) }}>
                        {percent(component.variancePercent)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Kısım Bazında Maliyet</h2>
                <p>
                  Kısım seçilmeden girilen sarf ve puantaj &quot;Genel&quot;
                  satırında toplanır.
                </p>
              </div>
            </div>

            {analysis.sections.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Kısım kırılımı oluşmadı</strong>
                <p>Henüz bu projeye maliyet işlenmemiş.</p>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Kısım</th>
                      <th>Malzeme</th>
                      <th>İşçilik</th>
                      <th>Taşeron</th>
                      <th>Genel gider</th>
                      <th>Toplam</th>
                    </tr>
                  </thead>
                  <tbody>
                    {analysis.sections.map((section) => (
                      <tr key={section.sectionId ?? "genel"}>
                        <td>{section.sectionName}</td>
                        <td>{money.format(section.materialAmount)}</td>
                        <td>{money.format(section.laborAmount)}</td>
                        <td>{money.format(section.subcontractorLaborAmount)}</td>
                        <td>{money.format(section.overheadAmount)}</td>
                        <td>
                          <strong>{money.format(section.totalAmount)}</strong>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Aylık Seyir</h2>
                <p>Maliyet bileşenleri ve hakediş geliri aynı eksende.</p>
              </div>
            </div>

            <div style={{ padding: "16px" }}>
              <MonthlyTrend analysis={analysis} />
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Hesap Varsayımları</h2>
                <p>Rakamların neye dayandığı — okurken bilinmesi gerekenler.</p>
              </div>
            </div>

            <div style={{ padding: "16px" }}>
              <ul style={{ margin: 0, paddingLeft: "18px" }}>
                {analysis.assumptions.map((assumption, index) => (
                  <li key={index}>
                    <small>{assumption}</small>
                  </li>
                ))}

                {analysis.includesExtraPayments ? (
                  <li>
                    <small>
                      İşçiliğe elden ödeme payı dahil:{" "}
                      {moneyDetailed.format(analysis.extraPaymentLaborCost ?? 0)}.
                      Bu tutar resmi bordroya ve muhasebeye yansımaz.
                    </small>
                  </li>
                ) : (
                  <li>
                    <small>
                      İşçilik yalnızca resmi bordro maliyetidir; elden ödemeler
                      yetkiniz olmadığı için dahil edilmemiştir.
                    </small>
                  </li>
                )}
              </ul>
            </div>
          </section>
        </>
      )}
    </ErpShell>
  );
}
