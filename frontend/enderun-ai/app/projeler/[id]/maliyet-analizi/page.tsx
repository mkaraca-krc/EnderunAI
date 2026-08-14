"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { money, moneyWhole, percent } from "@/lib/format/turkish";
import {
  projectCostAnalysisService,
  ProjectCostClass,
  type ProjectCostAnalysis,
} from "@/services/project-cost-analysis.service";

/**
 * Proje geneli toplu bakış — kuruşsuz.
 *
 * TABLO HÜCRESİNDE KULLANILMAZ. Bu ayrım daha önce yanlış tarafta
 * duruyordu: bileşen tahmin/gerçekleşme/sapma sütunları ve bölüm
 * malzeme/işçilik/taşeron/genel gider tutarları da kuruşsuz
 * basılıyordu, yani yuvarlanmış satırların toplamı gösterilen
 * toplamla tutmuyordu.
 */
const summaryMoney = moneyWhole;

/**
 * Aşım kırmızı, tasarruf yeşil. Maliyet tarafında "fazla" her zaman kötü
 * olduğu için işaret doğrudan renge çevrilebiliyor.
 */
function varianceColor(variance: number) {
  if (variance > 0) return "var(--color-semantic-danger)";
  if (variance < 0) return "var(--color-semantic-success)";
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
    { key: "materialAmount", label: "Malzeme", color: "var(--color-chart-1)" },
    { key: "laborAmount", label: "İşçilik", color: "var(--color-chart-2)" },
    {
      key: "subcontractorLaborAmount",
      label: "Taşeron",
      color: "var(--color-chart-3)",
    },
    { key: "overheadAmount", label: "Genel gider", color: "var(--color-chart-4)" },
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
              background: "var(--color-chart-axis)",
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
              `${point.label} — maliyet ${money(point.totalAmount)}, ` +
              `gelir ${money(point.revenueAmount)}`
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
                    borderTop: "2px solid var(--color-chart-axis)",
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
      design="redwood"
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
                Gelir {summaryMoney(analysis.revenueAmount)} · Maliyet{" "}
                {summaryMoney(analysis.totalCost)}
              </strong>
              <small style={{ display: "block", marginTop: "4px" }}>
                Vergi öncesi kâr{" "}
                <strong style={{ color: varianceColor(-analysis.profit) }}>
                  {summaryMoney(analysis.profit)}
                </strong>{" "}
                · Marj {percent(analysis.profitMarginPercent)} · İlerleme{" "}
                {percent(analysis.progressRatio * 100)}
              </small>
              <small style={{ display: "block", marginTop: "2px" }}>
                Tahmini vergi (%{analysis.taxRate}){" "}
                {summaryMoney(analysis.estimatedTax)} · Net kâr{" "}
                <strong style={{ color: varianceColor(-analysis.netProfitAfterTax) }}>
                  {summaryMoney(analysis.netProfitAfterTax)}
                </strong>
              </small>
            </div>

            {/* Maliyet fişleri ve hakediş ilerlemesi bu analizi
                dışarıdan besliyor; tazelemeden rakamlar eskiyordu. */}
            <button
              type="button"
              className="erp-secondary-button"
              disabled={loading}
              onClick={() => void load()}
            >
              Yenile
            </button>
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
                              {money(subcontractor.actual)}
                            </small>
                          )}
                      </td>
                      <td>{money(component.forecastContract)}</td>
                      <td>{money(component.forecastEarned)}</td>
                      <td>{money(component.actual)}</td>
                      <td style={{ color: varianceColor(component.variance) }}>
                        <strong>{money(component.variance)}</strong>
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
                        <td>{money(section.materialAmount)}</td>
                        <td>{money(section.laborAmount)}</td>
                        <td>{money(section.subcontractorLaborAmount)}</td>
                        <td>{money(section.overheadAmount)}</td>
                        <td>
                          <strong>{money(section.totalAmount)}</strong>
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
                <h2>Maliyet Nereden Geldi</h2>
                <p>
                  Gerçekleşen maliyetin kaynak kırılımı — toplamı yukarıdaki
                  gerçekleşen maliyete eşittir.
                </p>
              </div>
            </div>

            <div style={{ padding: "16px" }}>
              {analysis.costSources.length === 0 ? (
                <div className="erp-empty-state">
                  Bu projede henüz gerçekleşen maliyet kaydı yok.
                </div>
              ) : (
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Kaynak</th>
                      <th style={{ textAlign: "right" }}>Tutar</th>
                      <th style={{ textAlign: "right" }}>Pay</th>
                    </tr>
                  </thead>
                  <tbody>
                    {analysis.costSources.map((source) => (
                      <tr key={source.source}>
                        <td>{source.sourceName}</td>
                        <td style={{ textAlign: "right" }}>
                          {money(source.amount)}
                        </td>
                        <td style={{ textAlign: "right" }}>
                          {percent(
                            analysis.totalCost === 0
                              ? 0
                              : (source.amount / analysis.totalCost) * 100
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}

              {analysis.unlinkedToBoqItemAmount > 0 && (
                <p style={{ marginTop: "12px" }}>
                  <small>
                    <strong>
                      {money(analysis.unlinkedToBoqItemAmount)}
                    </strong>{" "}
                    tutarındaki maliyet bir POZA bağlı değil; poz kâr
                    analizinde ölçülmüş maliyet olarak görünmez.
                    {analysis.unlinkedToSectionAmount > 0 && (
                      <>
                        {" "}
                        Bunun{" "}
                        <strong>
                          {money(
                            analysis.unlinkedToSectionAmount
                          )}
                        </strong>{" "}
                        kadarı bir KISMA da bağlı değil — kısımdaki pozlara
                        dağıtılamaz, proje geneli maliyet olarak kalır.
                      </>
                    )}
                  </small>
                </p>
              )}
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
                      {money(analysis.extraPaymentLaborCost ?? 0)}.
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
