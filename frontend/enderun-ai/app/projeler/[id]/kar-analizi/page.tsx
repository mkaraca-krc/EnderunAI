"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  projectProfitService,
  type BoqLineProfit,
  type ProjectProfitBreakdown,
} from "@/services/project-profit.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  maximumFractionDigits: 0,
});

const unitMoney = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const quantity = new Intl.NumberFormat("tr-TR", {
  maximumFractionDigits: 3,
});

function percent(value?: number | null) {
  if (value === null || value === undefined) return "—";
  return `%${value.toLocaleString("tr-TR", { maximumFractionDigits: 1 })}`;
}

/** Kâr tarafında artı iyi, eksi kötü — maliyet tablosunun tersi. */
function profitColor(value: number) {
  if (value > 0) return "#15803d";
  if (value < 0) return "#b91c1c";
  return "inherit";
}

/**
 * Referans fiyatlar kurum kurum, yan yana. Toplanmaz, ortalaması
 * alınmaz: hangi kurumun kitabında ne yazdığı ayrı bir bilgidir.
 */
function ReferenceCell({ line }: { line: BoqLineProfit }) {
  if (line.references.length === 0) {
    return <small>—</small>;
  }

  return (
    <>
      {line.references.map((reference) => (
        <small key={reference.institutionName} style={{ display: "block" }}>
          {reference.institutionName}
          {reference.year ? ` ${reference.year}` : ""}:{" "}
          {reference.unitPrice == null
            ? "—"
            : `${unitMoney.format(reference.unitPrice)} TL`}
        </small>
      ))}
    </>
  );
}

/**
 * Şirket ortalaması ya bir rakamdır ya da neden hesaplanamadığının
 * gerekçesi. Yetersiz veriden ortalama üretmek, sonraki teklifleri
 * yanlış fiyatlandırır.
 */
function CompanyAverageCell({ line }: { line: BoqLineProfit }) {
  const average = line.companyAverage;

  if (!average.hasEnoughData) {
    return (
      <small style={{ color: "#6b7280" }} title={average.explanation}>
        Ölçüm yetersiz
      </small>
    );
  }

  return (
    <>
      <strong>{unitMoney.format(average.averageUnitCost ?? 0)} TL</strong>
      <small style={{ display: "block" }}>
        {average.projectCount} proje · {unitMoney.format(average.minUnitCost ?? 0)}{" "}
        – {unitMoney.format(average.maxUnitCost ?? 0)}
      </small>
    </>
  );
}

export default function ProjectProfitPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;

  const [breakdown, setBreakdown] = useState<ProjectProfitBreakdown | null>(null);
  const [referenceYear, setReferenceYear] = useState<number | undefined>(
    new Date().getFullYear()
  );
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const fetchBreakdown = useCallback(
    () => projectProfitService.get(projectId, referenceYear),
    [projectId, referenceYear]
  );

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const data = await fetchBreakdown();
        if (cancelled) return;

        setBreakdown(data);
        setError("");
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Kâr analizi alınamadı.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [fetchBreakdown]);

  const currentYear = new Date().getFullYear();
  const years = [currentYear, currentYear - 1, currentYear - 2];

  return (
    <ErpShell
      title="Poz Kâr Analizi"
      description="İcmal satırında dört fiyat: sözleşme, referans, şirket gerçekleşmesi ve anlık maliyet"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <div className="erp-page-toolbar">
        <div>
          {breakdown && (
            <>
              <strong>
                Sözleşme {money.format(breakdown.contractTotal)} · Maliyet{" "}
                {money.format(breakdown.actualCostTotal)}
              </strong>
              <small style={{ display: "block", marginTop: "4px" }}>
                Kâr{" "}
                <strong style={{ color: profitColor(breakdown.profit) }}>
                  {money.format(breakdown.profit)}
                </strong>{" "}
                · Marj {percent(breakdown.profitMarginPercent)}
              </small>
              <small style={{ display: "block", marginTop: "2px" }}>
                Maliyetin {money.format(breakdown.measuredCostTotal)} tutarı
                ölçülmüş, {money.format(breakdown.allocatedCostTotal)} tutarı
                dağıtılmış (tahmin).
              </small>
            </>
          )}
        </div>

        <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
          <label htmlFor="referenceYear">
            <small>Referans yılı</small>
          </label>
          <select
            id="referenceYear"
            value={referenceYear ?? ""}
            onChange={(event) =>
              setReferenceYear(
                event.target.value ? Number(event.target.value) : undefined
              )
            }
          >
            <option value="">En güncel</option>
            {years.map((year) => (
              <option key={year} value={year}>
                {year}
              </option>
            ))}
          </select>

          <Link className="erp-secondary-button" href={`/projeler/${projectId}`}>
            Projeye Dön
          </Link>
        </div>
      </div>

      {loading ? (
        <div className="erp-panel erp-loading">Kâr analizi hesaplanıyor...</div>
      ) : !breakdown ? (
        <div className="erp-panel erp-empty-state">
          <strong>Analiz bulunamadı</strong>
        </div>
      ) : (
        <>
          {breakdown.unassignedCost > 0 && (
            <div className="erp-alert warning">
              {money.format(breakdown.unassignedCost)} tutarındaki maliyet hiçbir
              kısma ya da poza bağlanamadı; satır kârlarına yansımıyor. Proje kârı
              bu tutar kadar iyimser görünüyor.
            </div>
          )}

          {!breakdown.includesExtraPayments && (
            <div className="erp-alert warning">
              İşçilik yalnızca resmi bordro maliyetidir; elden ödemeler yetkiniz
              olmadığı için dahil edilmemiştir. Gerçek kâr burada görünenden
              düşüktür.
            </div>
          )}

          <section className="erp-panel">
            <div className="erp-panel-header">
              <div>
                <h2>Satır Bazında Dört Fiyat</h2>
                <p>
                  Kâr her zaman sözleşme eksi gerçekleşen maliyettir. Referans ve
                  şirket ortalaması karara yardımcı bilgilerdir, kâr hesabına
                  girmez.
                </p>
              </div>
            </div>

            {breakdown.lines.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Sözleşme icmali yok</strong>
                <p>
                  Bu projede güncel revizyon icmal bulunmuyor; kâr karşılaştırması
                  yapılamaz.
                </p>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Poz</th>
                      <th>Miktar</th>
                      <th>Sözleşme BF</th>
                      <th>Referans BF</th>
                      <th>Şirket ort. maliyet</th>
                      <th>Sözleşme tutarı</th>
                      <th>Anlık maliyet</th>
                      <th>Kâr</th>
                      <th>Marj</th>
                    </tr>
                  </thead>
                  <tbody>
                    {breakdown.lines.map((line) => (
                      <tr key={line.boqItemId}>
                        <td>
                          <strong>{line.positionCode}</strong>
                          <small style={{ display: "block" }}>
                            {line.description}
                          </small>
                        </td>
                        <td>
                          {quantity.format(line.contractQuantity)} {line.unit}
                        </td>
                        <td>
                          <strong>
                            {unitMoney.format(line.contractUnitPrice)} TL
                          </strong>
                          {(line.contractMaterialUnitPrice > 0 ||
                            line.contractLaborUnitPrice > 0) && (
                            <small style={{ display: "block" }}>
                              malzeme{" "}
                              {unitMoney.format(line.contractMaterialUnitPrice)} ·
                              montaj{" "}
                              {unitMoney.format(line.contractLaborUnitPrice)}
                            </small>
                          )}
                        </td>
                        <td>
                          <ReferenceCell line={line} />
                        </td>
                        <td>
                          <CompanyAverageCell line={line} />
                        </td>
                        <td>{money.format(line.contractTotal)}</td>
                        <td>
                          <strong>{money.format(line.actualCost)}</strong>
                          <small style={{ display: "block" }}>
                            ölçülmüş {money.format(line.measuredCost)} · dağıtılmış{" "}
                            {money.format(line.allocatedCost)}
                          </small>
                        </td>
                        <td style={{ color: profitColor(line.profit) }}>
                          <strong>{money.format(line.profit)}</strong>
                        </td>
                        <td style={{ color: profitColor(line.profit) }}>
                          {percent(line.profitMarginPercent)}
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
                <h2>Hesap Varsayımları</h2>
                <p>Rakamların neye dayandığı — okurken bilinmesi gerekenler.</p>
              </div>
            </div>

            <div style={{ padding: "16px" }}>
              <ul style={{ margin: 0, paddingLeft: "18px" }}>
                {breakdown.assumptions.map((assumption, index) => (
                  <li key={index}>
                    <small>{assumption}</small>
                  </li>
                ))}
                <li>
                  <small>
                    Şirket ortalaması yalnızca poza etiketlenmiş maliyetten ve en
                    az iki ayrı projeden hesaplanır; dağıtılmış tutarlar
                    ortalamaya girmez.
                  </small>
                </li>
              </ul>
            </div>
          </section>
        </>
      )}
    </ErpShell>
  );
}
