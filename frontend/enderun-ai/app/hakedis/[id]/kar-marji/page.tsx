"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  progressPaymentService,
  type HakedisProfit,
} from "@/services/progress-payment.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  maximumFractionDigits: 0,
});

const detailed = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const quantity = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 3 });

function percent(value?: number | null) {
  if (value === null || value === undefined) return "—";
  return `%${value.toLocaleString("tr-TR", { maximumFractionDigits: 1 })}`;
}

/** Kâr tarafında artı iyi, eksi kötü. */
function profitColor(value?: number | null) {
  if (value === null || value === undefined) return "inherit";
  if (value > 0) return "#15803d";
  if (value < 0) return "#b91c1c";
  return "inherit";
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

export default function HakedisProfitPage() {
  const params = useParams<{ id: string }>();

  const [profit, setProfit] = useState<HakedisProfit | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const fetchProfit = useCallback(
    () => progressPaymentService.getProfit(params.id),
    [params.id]
  );

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const data = await fetchProfit();
        if (cancelled) return;

        setProfit(data);
        setError("");
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Kâr marjı alınamadı.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [fetchProfit]);

  return (
    <ErpShell
      title="Hakediş Kâr Marjı"
      description={
        profit
          ? `${profit.progressPaymentNumber} · ${profit.periodNumber}. dönem`
          : "Dönem geliri, maliyeti ve kârı"
      }
    >
      {error && <div className="erp-alert error">{error}</div>}

      {loading ? (
        <div className="erp-panel erp-loading">Kâr marjı hesaplanıyor...</div>
      ) : !profit ? (
        <div className="erp-panel erp-empty-state">
          <strong>Hakediş bulunamadı</strong>
        </div>
      ) : (
        <>
          <div className="erp-page-toolbar">
            <div>
              <strong>
                {formatDate(profit.periodStartDate)} —{" "}
                {formatDate(profit.periodEndDate)}
              </strong>
              <small style={{ display: "block", marginTop: "4px" }}>
                Hakediş tutarı {money.format(profit.hakedisAmount)} · imalat{" "}
                {money.format(profit.productionRevenue)}
                {profit.priceDifferenceAmount !== 0 &&
                  ` · fiyat farkı ${money.format(profit.priceDifferenceAmount)}`}
                {profit.advanceMaterialMovement !== 0 &&
                  ` · ihzarat ${money.format(profit.advanceMaterialMovement)}`}
              </small>
            </div>

            <Link className="erp-secondary-button" href={`/hakedis/${params.id}`}>
              Hakedişe Dön
            </Link>
          </div>

          {profit.advanceMaterialMovement !== 0 && (
            <div className="erp-alert warning">
              Hakediş tutarının{" "}
              {money.format(profit.advanceMaterialMovement)} kadarı ihzarat
              hareketidir. Henüz yapılmamış imalatın malzeme bedeli olduğu için
              kâr hesabına girmez; imalata döndüğü dönemde geliri o dönemde
              görünür.
            </div>
          )}

          {!profit.includesExtraPayments && (
            <div className="erp-alert warning">
              İşçilik yalnızca resmi bordro maliyetidir; elden ödemeler yetkiniz
              olmadığı için dahil edilmemiştir. Gerçek kâr burada görünenden
              düşüktür.
            </div>
          )}

          {profit.revenueWithoutCost > 0 && (
            <div className="erp-alert warning">
              {money.format(profit.revenueWithoutCost)} tutarındaki gelirin
              maliyeti hesaplanamadı. İmalata düşen maliyet bu kadar eksik, kâr
              o oranda iyimser görünüyor.
            </div>
          )}

          <section className="erp-panel">
            <div className="erp-panel-header">
              <div>
                <h2>Dönem Kârı — İki Ayrı Taban</h2>
                <p>
                  İki rakam farklı soruya cevap verir ve bilerek toplanmaz.
                  &quot;İmalata düşen&quot; gelirle aynı işi karşılaştırır ama
                  bir dağıtımdır; &quot;tarih bazlı&quot; gerçek defter
                  kaydıdır ama peşin alınan malzeme ya da geç gelen fatura onu
                  dönemler arasında kaydırır.
                </p>
              </div>
            </div>

            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Taban</th>
                    <th>Gelir</th>
                    <th>Maliyet</th>
                    <th>Kâr</th>
                    <th>Marj</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>
                      <strong>İmalata düşen</strong>
                      <small style={{ display: "block" }}>
                        Satırın gerçekleşen birim maliyeti × bu dönem miktarı
                      </small>
                    </td>
                    <td>
                      {money.format(
                        profit.productionRevenue + profit.priceDifferenceAmount
                      )}
                    </td>
                    <td>{money.format(profit.costByProduction)}</td>
                    <td style={{ color: profitColor(profit.profitByProduction) }}>
                      <strong>{money.format(profit.profitByProduction)}</strong>
                    </td>
                    <td style={{ color: profitColor(profit.profitByProduction) }}>
                      {percent(profit.marginByProductionPercent)}
                    </td>
                  </tr>

                  <tr>
                    <td>
                      <strong>Tarih bazlı</strong>
                      <small style={{ display: "block" }}>
                        {profit.costByDateBasis}
                      </small>
                    </td>
                    <td>
                      {money.format(
                        profit.productionRevenue + profit.priceDifferenceAmount
                      )}
                    </td>
                    <td>
                      {profit.costByDate == null
                        ? "—"
                        : money.format(profit.costByDate)}
                    </td>
                    <td style={{ color: profitColor(profit.profitByDate) }}>
                      <strong>
                        {profit.profitByDate == null
                          ? "—"
                          : money.format(profit.profitByDate)}
                      </strong>
                    </td>
                    <td style={{ color: profitColor(profit.profitByDate) }}>
                      {percent(profit.marginByDatePercent)}
                    </td>
                  </tr>

                  <tr>
                    <td>
                      <strong>Kümülatif (proje başından)</strong>
                      <small style={{ display: "block" }}>
                        Kümülatif imalat geliri ve projenin tüm gerçekleşen
                        maliyeti
                      </small>
                    </td>
                    <td>{money.format(profit.cumulativeRevenue)}</td>
                    <td>{money.format(profit.cumulativeCost)}</td>
                    <td style={{ color: profitColor(profit.cumulativeProfit) }}>
                      <strong>{money.format(profit.cumulativeProfit)}</strong>
                    </td>
                    <td style={{ color: profitColor(profit.cumulativeProfit) }}>
                      {percent(profit.cumulativeMarginPercent)}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Satır Bazında</h2>
                <p>
                  Birim maliyet, pozun proje başından beri gerçekleşen
                  maliyetinin kümülatif metraja bölümüdür.
                </p>
              </div>
            </div>

            {profit.lines.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Bu hakedişte imalat satırı yok</strong>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Poz</th>
                      <th>Bu dönem</th>
                      <th>Birim fiyat</th>
                      <th>Gelir</th>
                      <th>Birim maliyet</th>
                      <th>Maliyet</th>
                      <th>Kâr</th>
                      <th>Marj</th>
                    </tr>
                  </thead>
                  <tbody>
                    {profit.lines.map((line) => (
                      <tr key={line.itemId}>
                        <td>
                          <strong>{line.positionCode}</strong>
                          <small style={{ display: "block" }}>
                            {line.description}
                          </small>
                        </td>
                        <td>
                          {quantity.format(line.currentQuantity)} {line.unit}
                        </td>
                        <td>{detailed.format(line.unitPrice)}</td>
                        <td>{money.format(line.currentAmount)}</td>
                        <td>
                          {line.unitCost == null ? (
                            <small
                              style={{ color: "#6b7280" }}
                              title={line.costBasis}
                            >
                              hesaplanamadı
                            </small>
                          ) : (
                            <>
                              {detailed.format(line.unitCost)}
                              {line.measuredRatio < 1 && (
                                <small style={{ display: "block" }}>
                                  ölçüm payı {percent(line.measuredRatio * 100)}
                                </small>
                              )}
                            </>
                          )}
                        </td>
                        <td>
                          {line.periodCost == null
                            ? "—"
                            : money.format(line.periodCost)}
                        </td>
                        <td style={{ color: profitColor(line.profit) }}>
                          <strong>
                            {line.profit == null
                              ? "—"
                              : money.format(line.profit)}
                          </strong>
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
                {profit.assumptions.map((assumption, index) => (
                  <li key={index}>
                    <small>{assumption}</small>
                  </li>
                ))}
              </ul>
            </div>
          </section>
        </>
      )}
    </ErpShell>
  );
}
