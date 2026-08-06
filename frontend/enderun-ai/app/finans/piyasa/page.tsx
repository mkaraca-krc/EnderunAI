"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  commodityService,
  copperImpactService,
  marketService,
  type CommoditySummary,
  type ExchangeRateFreshness,
  type ExchangeRateRow,
  type ProjectCopperImpact,
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
  const [impacts, setImpacts] = useState<ProjectCopperImpact[]>([]);
  const [editingProjectId, setEditingProjectId] = useState<string | null>(null);
  const [tonnageDraft, setTonnageDraft] = useState("");
  const [savingTonnage, setSavingTonnage] = useState(false);
  /** Değeri değiştikçe veri yeniden çekilir — elle tazeleme için. */
  const [reloadToken, setReloadToken] = useState(0);

  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  /**
   * Veriyi getirir; DURUM GÜNCELLEMESİ YAPMAZ. Efekt gövdesinde senkron
   * setState çağırmak zincirleme render'a yol açtığı için yükleme
   * bayrağı efektin dışında, kullanıcı etkileşiminde ayarlanıyor.
   */
  const fetchAll = useCallback(async (window: number) => {
    const to = new Date();
    const from = new Date(to);
    from.setDate(from.getDate() - window);

    const iso = (value: Date) => value.toISOString().slice(0, 10);

    const [copperData, usdData, eurData, freshnessData, impactData] =
      await Promise.all([
        commodityService.getCopper(window),
        marketService.getRates("USD", iso(from), iso(to)),
        marketService.getRates("EUR", iso(from), iso(to)),
        marketService.getFreshness(),
        copperImpactService.getPortfolio(),
      ]);

    return { copperData, usdData, eurData, freshnessData, impactData };
  }, []);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const data = await fetchAll(days);
        if (cancelled) return;

        setCopper(data.copperData);
        setUsdRates(data.usdData);
        setEurRates(data.eurData);
        setFreshness(data.freshnessData);
        setImpacts(data.impactData);
        setError("");
      } catch (err) {
        if (cancelled) return;

        setError(
          err instanceof Error ? err.message : "Piyasa verisi alınamadı."
        );
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [fetchAll, days, reloadToken]);

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

  async function handleSaveTonnage(projectId: string) {
    setSavingTonnage(true);
    setError("");
    setNotice("");

    try {
      const trimmed = tonnageDraft.trim();

      await copperImpactService.save(projectId, {
        // Boş bırakmak "bilinmiyor" demektir; 0 ile karıştırılmamalı.
        remainingTons: trimmed === "" ? null : Number(trimmed),
      });

      setEditingProjectId(null);
      setTonnageDraft("");
      setReloadToken((value) => value + 1);
      setNotice("Kalan bakır tonajı kaydedildi.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Tonaj kaydedilemedi.");
    } finally {
      setSavingTonnage(false);
    }
  }

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
      setReloadToken((value) => value + 1);
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
              onClick={() => {
                setLoading(true);
                setDays(window);
              }}
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
              <h2>Projelere tahmini etki</h2>
              <small>
                Kalan bakır tonajı x (bugünkü TL/ton − taban TL/ton). Etki
                emtia, kur ve birleşik artık olarak ayrı gösterilir.
              </small>
            </div>

            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Proje</th>
                    <th>Sözleşme</th>
                    <th>Kalan bakır</th>
                    <th>Bakır etkisi</th>
                    <th>Kur etkisi</th>
                    <th>Birleşik</th>
                    <th>Toplam</th>
                  </tr>
                </thead>
                <tbody>
                  {impacts.length === 0 ? (
                    <tr>
                      <td colSpan={7}>Açık proje bulunamadı.</td>
                    </tr>
                  ) : (
                    impacts.map((impact) => (
                      <tr key={impact.projectId}>
                        <td>
                          <strong>{impact.projectCode}</strong>
                          <small>{impact.projectName}</small>
                        </td>
                        <td>
                          <span
                            className={`erp-status ${
                              impact.isCostRisk ? "yellow" : "gray"
                            }`}
                          >
                            {impact.contractTypeName}
                          </span>
                          {!impact.isCostRisk && <small>bilgi amaçlı</small>}
                        </td>
                        <td>
                          {editingProjectId === impact.projectId ? (
                            <div style={{ display: "flex", gap: 6 }}>
                              <input
                                type="number"
                                step="0.001"
                                min="0"
                                style={{ width: 100 }}
                                value={tonnageDraft}
                                placeholder="ton"
                                onChange={(event) =>
                                  setTonnageDraft(event.target.value)
                                }
                              />
                              <button
                                type="button"
                                className="erp-btn"
                                disabled={savingTonnage}
                                onClick={() => handleSaveTonnage(impact.projectId)}
                              >
                                Kaydet
                              </button>
                              <button
                                type="button"
                                className="erp-btn ghost"
                                disabled={savingTonnage}
                                onClick={() => setEditingProjectId(null)}
                              >
                                Vazgeç
                              </button>
                            </div>
                          ) : (
                            <button
                              type="button"
                              className="erp-btn ghost"
                              onClick={() => {
                                setEditingProjectId(impact.projectId);
                                setTonnageDraft(
                                  impact.remainingTons?.toString() ?? ""
                                );
                              }}
                            >
                              {impact.remainingTons === null ||
                              impact.remainingTons === undefined
                                ? "Bilinmiyor — gir"
                                : `${impact.remainingTons.toLocaleString("tr-TR")} ton`}
                            </button>
                          )}
                          <small>{impact.tonnageSourceName}</small>
                        </td>
                        <td>
                          {impact.copperEffect === null ||
                          impact.copperEffect === undefined
                            ? "—"
                            : tl.format(impact.copperEffect)}
                          <small>{formatPercent(impact.copperChangePercent)}</small>
                        </td>
                        <td>
                          {impact.fxEffect === null || impact.fxEffect === undefined
                            ? "—"
                            : tl.format(impact.fxEffect)}
                          <small>{formatPercent(impact.fxChangePercent)}</small>
                        </td>
                        <td>
                          {impact.combinedEffect === null ||
                          impact.combinedEffect === undefined
                            ? "—"
                            : tl.format(impact.combinedEffect)}
                        </td>
                        <td>
                          <strong
                            className={`erp-status ${toneOf(impact.totalEffect)}`}
                          >
                            {impact.totalEffect === null ||
                            impact.totalEffect === undefined
                              ? "hesaplanamadı"
                              : tl.format(impact.totalEffect)}
                          </strong>
                          {impact.baselineDate && (
                            <small>
                              taban{" "}
                              {dateFormat.format(new Date(impact.baselineDate))}
                            </small>
                          )}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {impacts.some((x) => x.assumptions.length > 0) && (
              <div className="erp-panel-body">
                <h4>Varsayımlar ve eksikler</h4>
                <ul>
                  {impacts
                    .filter((x) => x.assumptions.length > 0)
                    .map((impact) => (
                      <li key={impact.projectId}>
                        <strong>{impact.projectCode}:</strong>{" "}
                        {impact.assumptions.join(" ")}
                      </li>
                    ))}
                </ul>
              </div>
            )}
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
