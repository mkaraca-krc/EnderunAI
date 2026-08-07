"use client";

import { useEffect, useState } from "react";

import {
  positionPurchaseIntelligenceService,
  type PositionPurchaseIntelligence,
} from "@/services/engineering-position.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const quantityFormat = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 0,
  maximumFractionDigits: 4,
});

const percentFormat = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

const dateFormat = new Intl.DateTimeFormat("tr-TR");

/** Gerçek maliyetin resmî fiyata göre farkı, yüzde. */
function deltaPercent(actual: number, official: number) {
  if (official <= 0) return null;
  return ((actual - official) / official) * 100;
}

/**
 * Pozun GERÇEK alış maliyeti ile resmî birim fiyatının karşılaştırması.
 *
 * Zincirin (poz → reçete → stok kartı → alış faturası) koptuğu yerde
 * sayı gösterilmez, nedeni yazılır. Eksik bir toplam "bu poz ucuza mal
 * oluyor" diye okunur ve teklif fiyatı yanlış kurulur.
 */
export default function PositionPurchaseIntelligence({
  positionId,
  companyId,
  months = 12,
}: {
  positionId: string;
  companyId: string;
  months?: number;
}) {
  const [data, setData] = useState<PositionPurchaseIntelligence | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!positionId || !companyId) return;

    let cancelled = false;

    void (async () => {
      try {
        const result = await positionPurchaseIntelligenceService.get(
          positionId,
          companyId,
          { months }
        );

        if (!cancelled) {
          setData(result);
          setError("");
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Analiz alınamadı.");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [positionId, companyId, months]);

  if (loading) {
    return <div className="erp-panel erp-loading">Alış geçmişi taranıyor...</div>;
  }

  if (error) {
    return <div className="erp-alert error">{error}</div>;
  }

  if (!data) return null;

  const official = data.officialUnitPrice ?? 0;

  const lastDelta =
    data.lastPurchaseMaterialCost != null
      ? deltaPercent(data.lastPurchaseMaterialCost, official)
      : null;

  const averageDelta =
    data.weightedAverageMaterialCost != null
      ? deltaPercent(data.weightedAverageMaterialCost, official)
      : null;

  return (
    <section className="erp-table-card" style={{ marginTop: 16 }}>
      <div className="erp-table-header">
        <h2>Gerçek Alış Maliyeti</h2>
        <small>
          Son {months} ayın onaylı alış faturalarından; reçete üzerinden
        </small>
      </div>

      {data.warnings.map((warning) => (
        <div key={warning} className="erp-alert warning">
          {warning}
        </div>
      ))}

      <div className="erp-quick-grid" style={{ padding: "12px 16px" }}>
        <div className="erp-panel">
          <small style={{ display: "block", marginBottom: 4 }}>
            Resmî Birim Fiyat
            {data.officialYear ? ` (${data.officialYear})` : ""}
          </small>
          <strong>
            {data.officialUnitPrice != null
              ? money.format(data.officialUnitPrice)
              : "—"}
          </strong>
        </div>

        <div className="erp-panel">
          <small style={{ display: "block", marginBottom: 4 }}>
            Son Alışla Malzeme
          </small>
          <strong>
            {data.lastPurchaseMaterialCost != null
              ? money.format(data.lastPurchaseMaterialCost)
              : "hesaplanamadı"}
          </strong>
          {lastDelta != null && (
            <small>
              resmî fiyata göre %{percentFormat.format(lastDelta)}
            </small>
          )}
        </div>

        <div className="erp-panel">
          <small style={{ display: "block", marginBottom: 4 }}>
            Ağırlıklı Ortalama
          </small>
          <strong>
            {data.weightedAverageMaterialCost != null
              ? money.format(data.weightedAverageMaterialCost)
              : "hesaplanamadı"}
          </strong>
          {averageDelta != null && (
            <small>
              resmî fiyata göre %{percentFormat.format(averageDelta)}
            </small>
          )}
        </div>

        <div className="erp-panel">
          <small style={{ display: "block", marginBottom: 4 }}>
            Fiyatlanan Malzeme
          </small>
          <strong>
            {data.pricedMaterialCount} / {data.materialCount}
          </strong>
          <small>{data.linkedMaterialCount} stok kartına bağlı</small>
        </div>
      </div>

      {data.materials.length > 0 && (
        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Malzeme</th>
                <th>Miktar</th>
                <th>Son Alış</th>
                <th>Tedarikçi</th>
                <th>Ağırlıklı Ort.</th>
                <th>Fatura</th>
              </tr>
            </thead>
            <tbody>
              {data.materials.map((material) => (
                <tr key={`${material.materialCode}-${material.materialName}`}>
                  <td>
                    {material.materialName}
                    <small>{material.materialCode}</small>
                  </td>
                  <td>
                    {quantityFormat.format(material.effectiveQuantity)}{" "}
                    {material.unit}
                  </td>
                  <td>
                    {material.lastPurchaseUnitPrice != null ? (
                      <>
                        {money.format(material.lastPurchaseUnitPrice)}
                        {material.lastPurchaseDate && (
                          <small>
                            {dateFormat.format(
                              new Date(material.lastPurchaseDate)
                            )}
                          </small>
                        )}
                      </>
                    ) : (
                      <small>{material.message}</small>
                    )}
                  </td>
                  <td>{material.lastSupplierTitle ?? "—"}</td>
                  <td>
                    {material.weightedAverageUnitPrice != null
                      ? money.format(material.weightedAverageUnitPrice)
                      : "—"}
                  </td>
                  <td>{material.invoiceCount || "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
