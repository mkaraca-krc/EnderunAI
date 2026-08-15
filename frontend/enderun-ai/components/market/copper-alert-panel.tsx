"use client";

import { useCallback, useEffect, useState } from "react";
import { whole } from "@/lib/format/turkish";

import {
  CommodityAlertDirection,
  commodityService,
  type CommodityAlertStatus,
} from "@/services/market.service";


const dateFormat = new Intl.DateTimeFormat("tr-TR");

/**
 * Şirketin bakır alım/risk eşiği ve bekleyen uyarıları.
 *
 * Eşik ŞİRKET BAZLIDIR: aynı bakır fiyatı, stok politikası ve nakit
 * durumu farklı iki şirket için farklı anlama gelir. Bu yüzden
 * varsayılan bir eşik önerilmez; kullanıcı girene kadar uyarı da yok.
 */
export default function CopperAlertPanel({
  companyId,
  canManage,
}: {
  companyId: string;
  canManage: boolean;
}) {
  const [status, setStatus] = useState<CommodityAlertStatus | null>(null);
  const [buyBelow, setBuyBelow] = useState("");
  const [alertAbove, setAlertAbove] = useState("");
  const [isEnabled, setIsEnabled] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");

  const applyStatus = useCallback((next: CommodityAlertStatus) => {
    setStatus(next);
    setBuyBelow(next.buyBelowUsdPerTon?.toString() ?? "");
    setAlertAbove(next.alertAboveUsdPerTon?.toString() ?? "");
    setIsEnabled(next.isEnabled);
  }, []);

  useEffect(() => {
    if (!companyId) return;

    let cancelled = false;

    void (async () => {
      try {
        const next = await commodityService.getCopperAlert(companyId);
        if (!cancelled) {
          applyStatus(next);
          setError("");
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Eşik alınamadı.");
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [companyId, applyStatus]);

  async function handleSave() {
    setSaving(true);
    setError("");
    setMessage("");

    try {
      const next = await commodityService.saveCopperAlert({
        companyId,
        // Boş alan "eşik yok" demektir; sıfır yazmak fiyatın altına
        // hiç inemeyeceği bir eşik kurmak olurdu.
        buyBelowUsdPerTon: buyBelow.trim() ? Number(buyBelow) : null,
        alertAboveUsdPerTon: alertAbove.trim() ? Number(alertAbove) : null,
        isEnabled,
        notes: null,
      });

      applyStatus(next);
      setMessage("Eşik kaydedildi ve fiyat arşivi hemen değerlendirildi.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Eşik kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function handleAcknowledge(triggerId: string) {
    try {
      await commodityService.acknowledgeAlert(triggerId);
      applyStatus(await commodityService.getCopperAlert(companyId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşaretlenemedi.");
    }
  }

  const state = status?.currentState;

  return (
    <section className="erp-table-card" style={{ marginTop: 16 }}>
      <div className="erp-table-header">
        <h2>Alım Fırsatı Eşiği</h2>
        <small>USD/ton üzerinden; TL karşılığı kur hareketini içerir</small>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {message && <div className="erp-alert success">{message}</div>}

      <div style={{ padding: "12px 16px" }}>
        <div
          style={{
            display: "flex",
            gap: 12,
            flexWrap: "wrap",
            alignItems: "flex-end",
          }}
        >
          <label>
            <span style={{ display: "block", fontSize: 11 }}>
              Alım eşiği (bu fiyatın altı fırsat)
            </span>
            <input
              type="number"
              min={0}
              step={50}
              value={buyBelow}
              disabled={!canManage}
              onChange={(e) => setBuyBelow(e.target.value)}
              placeholder="örn. 9000"
            />
          </label>

          <label>
            <span style={{ display: "block", fontSize: 11 }}>
              Risk eşiği (bu fiyatın üstü uyarı)
            </span>
            <input
              type="number"
              min={0}
              step={50}
              value={alertAbove}
              disabled={!canManage}
              onChange={(e) => setAlertAbove(e.target.value)}
              placeholder="örn. 11000"
            />
          </label>

          <label style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <input
              type="checkbox"
              checked={isEnabled}
              disabled={!canManage}
              onChange={(e) => setIsEnabled(e.target.checked)}
            />
            <span style={{ fontSize: 12 }}>Uyarılar açık</span>
          </label>

          {canManage && (
            <button
              type="button"
              className="erp-primary-button"
              disabled={saving}
              onClick={() => void handleSave()}
            >
              {saving ? "Kaydediliyor..." : "Eşiği Kaydet"}
            </button>
          )}
        </div>

        <p style={{ marginTop: 12, fontSize: 12 }}>
          Uyarı, fiyat eşiği <strong>geçtiğinde</strong> bir kez üretilir;
          eşiğin altında kaldığı her gün tekrarlanmaz. Fiyat çıkıp tekrar
          inerse yeni bir uyarı gelir.
        </p>

        {status?.latestPriceUsdPerTon != null && (
          <p style={{ marginTop: 8, fontSize: 13 }}>
            Son fiyat: <strong>{whole(status.latestPriceUsdPerTon)} USD/ton</strong>
            {status.latestPriceDate &&
              ` (${dateFormat.format(new Date(status.latestPriceDate))})`}
            {state === CommodityAlertDirection.BuyOpportunity && (
              <span className="erp-status green" style={{ marginLeft: 8 }}>
                Alım bölgesinde
              </span>
            )}
            {state === CommodityAlertDirection.CostRisk && (
              <span className="erp-status red" style={{ marginLeft: 8 }}>
                Risk bölgesinde
              </span>
            )}
          </p>
        )}
      </div>

      {status && status.pendingTriggers.length > 0 && (
        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Tarih</th>
                <th>Uyarı</th>
                <th>Fiyat</th>
                <th>Eşik</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {status.pendingTriggers.map((trigger) => {
                const isBuy =
                  trigger.direction === CommodityAlertDirection.BuyOpportunity;

                return (
                  <tr key={trigger.id}>
                    <td>{dateFormat.format(new Date(trigger.priceDate))}</td>
                    <td>
                      <span className={isBuy ? "erp-status green" : "erp-status red"}>
                        {isBuy ? "Alım fırsatı" : "Maliyet riski"}
                      </span>
                    </td>
                    <td>{whole(trigger.priceUsdPerTon)} USD/ton</td>
                    <td>{whole(trigger.thresholdUsdPerTon)} USD/ton</td>
                    <td>
                      <button
                        type="button"
                        className="erp-secondary-button"
                        onClick={() => void handleAcknowledge(trigger.id)}
                      >
                        Görüldü
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
