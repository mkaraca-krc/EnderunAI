"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import { companyService } from "@/services/company.service";
import { percent, whole } from "@/lib/format/turkish";
import {
  CommodityAlertDirection,
  commodityService,
  type CommodityAlertStatus,
  type CommoditySummary,
} from "@/services/market.service";



const dateFormat = new Intl.DateTimeFormat("tr-TR");

/**
 * Dashboard piyasa kartı: bakırın son fiyatı, 30 günlük değişimi ve
 * varsa bekleyen eşik uyarısı.
 *
 * USD ve TL değişimi AYRI gösterilir; ikisini tek sayıya indirmek
 * "bakır mı pahalandı, lira mı değer kaybetti" sorusunu cevapsız
 * bırakır.
 *
 * Veri yoksa kart sayı UYDURMAZ, "veri yok" der: dashboard'da uydurma
 * rakam, hiç rakam olmamasından kötüdür.
 */
export default function MarketSummaryWidget() {
  const [copper, setCopper] = useState<CommoditySummary | null>(null);
  const [alert, setAlert] = useState<CommodityAlertStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const summary = await commodityService.getCopper(30);
        if (cancelled) return;
        setCopper(summary);

        // Eşik şirket bazlı; kartta ilk şirketin eşiği gösterilir,
        // ayrıntı için piyasa ekranında şirket seçilebiliyor.
        const companies = await companyService.getAll();
        if (cancelled || companies.length === 0) return;

        setAlert(await commodityService.getCopperAlert(companies[0].id));
      } catch {
        if (!cancelled) setFailed(true);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const pending = alert?.pendingTriggers ?? [];
  const state = alert?.currentState;

  return (
    <article className="erp-panel">
      <header
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "baseline",
          marginBottom: 8,
        }}
      >
        <h3 style={{ margin: 0 }}>Piyasa</h3>
        <Link href="/finans/piyasa" style={{ fontSize: 12 }}>
          Ayrıntı
        </Link>
      </header>

      {loading ? (
        <p style={{ fontSize: 13 }}>Yükleniyor...</p>
      ) : failed || !copper?.latestUsdPerTon ? (
        <p style={{ fontSize: 13 }}>
          Bakır fiyat verisi yok. Piyasa ekranından arşivi yenileyin.
        </p>
      ) : (
        <>
          <div style={{ marginBottom: 8 }}>
            <strong style={{ fontSize: 20 }}>
              {whole(copper.latestUsdPerTon)} USD/ton
            </strong>
            <small style={{ display: "block" }}>
              {copper.sourceLabel}
              {copper.latestDate &&
                ` · ${dateFormat.format(new Date(copper.latestDate))}`}
            </small>
          </div>

          <div style={{ display: "flex", gap: 16, fontSize: 13 }}>
            <span>
              30g USD:{" "}
              <strong>
                {copper.changePercentUsd != null
                  ? percent(copper.changePercentUsd)
                  : "—"}
              </strong>
            </span>
            <span>
              30g TL:{" "}
              <strong>
                {copper.changePercentTry != null
                  ? percent(copper.changePercentTry)
                  : "—"}
              </strong>
            </span>
          </div>

          {copper.isStale && (
            <p style={{ marginTop: 8, fontSize: 12 }}>
              <span className="erp-status red">Veri bayat</span>{" "}
              {copper.warning}
            </p>
          )}

          {state === CommodityAlertDirection.BuyOpportunity && (
            <p style={{ marginTop: 8, fontSize: 13 }}>
              <span className="erp-status green">Alım bölgesinde</span> — eşik{" "}
              {alert?.buyBelowUsdPerTon != null
                ? `${whole(alert.buyBelowUsdPerTon)} USD/ton`
                : "tanımlı"}
            </p>
          )}

          {state === CommodityAlertDirection.CostRisk && (
            <p style={{ marginTop: 8, fontSize: 13 }}>
              <span className="erp-status red">Risk bölgesinde</span> — açık
              tekliflerdeki bakır maliyetini gözden geçirin.
            </p>
          )}

          {pending.length > 0 && (
            <p style={{ marginTop: 8, fontSize: 12 }}>
              {pending.length} okunmamış eşik uyarısı.
            </p>
          )}

          {alert && !alert.isEnabled && (
            <p style={{ marginTop: 8, fontSize: 12 }}>
              Alım eşiği tanımlı değil — piyasa ekranından belirleyin.
            </p>
          )}
        </>
      )}
    </article>
  );
}
