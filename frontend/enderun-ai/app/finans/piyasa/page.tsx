"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  commodityService,
  marketService,
  type CommoditySummary,
  type ExchangeRateFreshness,
  type ExchangeRateRow,
} from "@/services/market.service";

const usd = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "USD",
  maximumFractionDigits: 0,
});

const tl = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  maximumFractionDigits: 0,
});

const rate = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 4,
  maximumFractionDigits: 4,
});

const dateFormat = new Intl.DateTimeFormat("tr-TR");

const WINDOWS = [7, 30, 90] as const;

function formatPercent(value?: number | null) {
  if (value === null || value === undefined) return "—";

  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toLocaleString("tr-TR", {
    minimumFractionDigits: 1,
    maximumFractionDigits: 2,
  })}%`;
}

function toneOf(value?: number | null) {
  if (value === null || value === undefined) return "gray";
  if (value > 0) return "red";
  if (value < 0) return "green";
  return "gray";
}

/**
 * Bakır ve kur trendi.
 *
 * Bakırda USD ve TL değişimi ayrı gösterilir: TL değişimi hem emtiayı
 * hem kuru içerir ve ikisi karıştırılırsa "bakır mı pahalandı, lira mı
 * değer kaybetti" sorusu cevapsız kalır. Maliyet kararı bu ayrımı
 * bilmeden verilemez.
 */
export default function MarketPage() {
  const [days, setDays] = useState<number>(30);
  const [copper, setCopper] = useState<CommoditySummary | null>(null);
  const [usdRates, setUsdRates] = useState<ExchangeRateRow[]>([]);
  const [eurRates, setEurRates] = useState<ExchangeRateRow[]>([]);
  const [freshness, setFreshness] = useState<ExchangeRateFreshness | null>(null);

  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async (window: number) => {
    setLoading(true);
    setError("");

    const to = new Date();
    const from = new Date(to);
    from.setDate(from.getDate() - window);

    const iso = (value: Date) => value.toISOString().slice(0, 10);

    try {
      const [copperData, usdData, eurData, freshnessData] = await Promise.all([
        commodityService.getCopper(window),
        marketService.getRates("USD", iso(from), iso(to)),
        marketService.getRates("EUR", iso(from), iso(to)),
        marketService.getFreshness(),
      ]);

      setCopper(copperData);
      setUsdRates(usdData);
      setEurRates(eurData);
      setFreshness(freshnessData);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Piyasa verisi alınamadı.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load(days);
  }, [load, days]);

  const latestUsd = usdRates.at(-1);
  const latestEur = eurRates.at(-1);

  const usdChange = useMemo(() => {
    if (usdRates.length < 2) return null;
    const first = usdRates[0].forexBuying;
    const last = usdRates[usdRates.length - 1].forexBuying;

    return first === 0 ? null : ((last - first) / first) * 100;
  }, [usdRates]);

  const eurChange = useMemo(() => {
    if (eurRates.length < 2) return null;
    const first = eurRates[0].forexBuying;
    const last = eurRates[eurRates.length - 1].forexBuying;

    return first === 0 ? null : ((last - first) / first) * 100;
  }, [eurRates]);

  const chart = useMemo(() => {
    const points = copper?.trend ?? [];
    if (points.length < 2) return null;

    const values = points.map((x) => x.priceUsdPerTon);
    const min = Math.min(...values);
    const max = Math.max(...values);
    const span = max - min || 1;

    const coords = points.map((point, index) => {
      const x = (index / (points.length - 1)) * 100;
      const y = 100 - ((point.priceUsdPerTon - min) / span) * 100;

      return `${x.toFixed(2)},${y.toFixed(2)}`;
    });

    return { path: coords.join(" "), min, max };
  }, [copper]);

  async function handleRefresh() {
    setRefreshing(true);
    setError("");
    setNotice("");

    try {
      const [rateResult, commodityResult] = await Promise.all([
        marketService.refresh(7),
        commodityService.refresh(days),
      ]);

      setNotice(`${rateResult.message} ${commodityResult.message}`);
      await load(days);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Güncelleme başarısız.");
    } finally {
      setRefreshing(false);
    }
  }

  return (
    <ErpShell
      title="Piyasa"
      description="Bakır fiyatı ve TCMB günlük kurları"
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {freshness?.warning && (
        <div className="erp-alert warning">{freshness.warning}</div>
      )}

      {copper?.warning && <div className="erp-alert warning">{copper.warning}</div>}

      <div className="erp-toolbar">
        <div className="erp-toolbar-group">
          {WINDOWS.map((window) => (
            <button
              key={window}
              type="button"
              className={`erp-btn ${days === window ? "" : "ghost"}`}
              onClick={() => setDays(window)}
            >
              {window} gün
            </button>
          ))}
        </div>

        <button
          type="button"
          className="erp-btn ghost"
          disabled={refreshing}
          onClick={handleRefresh}
        >
          {refreshing ? "Güncelleniyor..." : "Şimdi güncelle"}
        </button>
      </div>

      {loading ? (
        <div className="erp-panel erp-loading">Piyasa verisi yükleniyor...</div>
      ) : (
        <>
          <section className="erp-stat-grid">
            <div className="erp-stat-card">
              <span>Bakır (USD/ton)</span>
              <strong>
                {copper?.latestUsdPerTon ? usd.format(copper.latestUsdPerTon) : "—"}
              </strong>
              <small className={`erp-status ${toneOf(copper?.changePercentUsd)}`}>
                {formatPercent(copper?.changePercentUsd)} · {days} gün
              </small>
              <small>{copper?.sourceLabel}</small>
            </div>

            <div className="erp-stat-card">
              <span>Bakır (TL/ton)</span>
              <strong>
                {copper?.latestTryPerTon ? tl.format(copper.latestTryPerTon) : "—"}
              </strong>
              <small className={`erp-status ${toneOf(copper?.changePercentTry)}`}>
                {formatPercent(copper?.changePercentTry)} · emtia + kur
              </small>
              <small>
                {copper?.latestDate
                  ? dateFormat.format(new Date(copper.latestDate))
                  : "veri yok"}
              </small>
            </div>

            <div className="erp-stat-card">
              <span>USD (TCMB döviz alış)</span>
              <strong>
                {latestUsd ? rate.format(latestUsd.forexBuying) : "—"}
              </strong>
              <small className={`erp-status ${toneOf(usdChange)}`}>
                {formatPercent(usdChange)} · {days} gün
              </small>
              <small>
                {latestUsd
                  ? dateFormat.format(new Date(latestUsd.rateDate))
                  : "veri yok"}
              </small>
            </div>

            <div className="erp-stat-card">
              <span>EUR (TCMB döviz alış)</span>
              <strong>
                {latestEur ? rate.format(latestEur.forexBuying) : "—"}
              </strong>
              <small className={`erp-status ${toneOf(eurChange)}`}>
                {formatPercent(eurChange)} · {days} gün
              </small>
              <small>
                {latestEur
                  ? dateFormat.format(new Date(latestEur.rateDate))
                  : "veri yok"}
              </small>
            </div>
          </section>

          <section className="erp-panel">
            <div className="erp-panel-header">
              <div>
                <h3>Bakır trendi ({days} gün)</h3>
                <p>
                  Kaynak: {copper?.sourceLabel} ({copper?.sourceSymbol})
                  {copper && !copper.isLme && (
                    <>
                      {" "}
                      — LME resmî fiyatı değildir. Türkiye&apos;deki kablo alımları
                      LME&apos;ye endeksli olduğu için yön doğru, seviye sapmalı
                      olabilir.
                    </>
                  )}
                </p>
              </div>
            </div>

            <div className="erp-panel-body">
              {chart ? (
                <>
                  <svg
                    viewBox="0 0 100 100"
                    preserveAspectRatio="none"
                    role="img"
                    aria-label={`Bakır fiyatı son ${days} gün`}
                    style={{ width: "100%", height: 180 }}
                  >
                    <polyline
                      points={chart.path}
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="1.5"
                      vectorEffect="non-scaling-stroke"
                    />
                  </svg>

                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      fontSize: 12,
                    }}
                  >
                    <span>En düşük: {usd.format(chart.min)}</span>
                    <span>En yüksek: {usd.format(chart.max)}</span>
                  </div>
                </>
              ) : (
                <p>Grafik için yeterli veri yok.</p>
              )}
            </div>
          </section>

          <section className="erp-table-card">
            <div className="erp-table-header">
              <h2>Günlük kayıtlar</h2>
            </div>

            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Tarih</th>
                    <th>Bakır (USD/ton)</th>
                    <th>Kur (USD)</th>
                    <th>Bakır (TL/ton)</th>
                  </tr>
                </thead>
                <tbody>
                  {[...(copper?.trend ?? [])].reverse().map((point) => (
                    <tr key={point.priceDate}>
                      <td>{dateFormat.format(new Date(point.priceDate))}</td>
                      <td>{usd.format(point.priceUsdPerTon)}</td>
                      <td>{point.usdRate ? rate.format(point.usdRate) : "—"}</td>
                      <td>
                        {point.priceTryPerTon
                          ? tl.format(point.priceTryPerTon)
                          : "kur yok"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}
    </ErpShell>
  );
}
