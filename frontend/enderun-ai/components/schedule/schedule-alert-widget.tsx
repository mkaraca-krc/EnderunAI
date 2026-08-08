"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import {
  projectScheduleService,
  type ScheduleAlertResponse,
} from "@/services/project-schedule.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  maximumFractionDigits: 0,
});

/**
 * Dashboard iş programı uyarı kartı.
 *
 * Kaynak, Hızır brifingiyle AYNI servistir; iki ekranın farklı sayı
 * göstermesi kullanıcıyı hangisine güveneceği konusunda tereddüde
 * düşürürdü.
 *
 * Gecikme cezası TUTARI yalnızca yetkiliye gelir (showsPenalty); iş
 * programını okuma yetkisi tutar görme yetkisi değildir.
 *
 * Uyarı yoksa kart sessizdir — "her şey yolunda" satırı dashboard'u
 * gereksiz doldurur.
 */
export default function ScheduleAlertWidget() {
  const [data, setData] = useState<ScheduleAlertResponse | null>(null);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const result = await projectScheduleService.alerts();
        if (!cancelled) setData(result);
      } catch {
        // Yetki yoksa kart hiç çizilmez.
        if (!cancelled) setData(null);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  if (!data || data.items.length === 0) return null;

  const atRisk = data.items.filter((x) => x.deadlineAtRisk).length;

  return (
    <section className="erp-panel erp-mt">
      <div className="erp-panel-header">
        <div>
          <h2>İş Programı Uyarıları</h2>
          <p>
            {atRisk > 0
              ? `${atRisk} projede termin tehlikede`
              : `${data.items.length} projede takip gerekiyor`}
          </p>
        </div>
      </div>

      <div className="erp-table-wrap">
        <table className="erp-table">
          <thead>
            <tr>
              <th>Proje</th>
              <th>İlerleme</th>
              <th>Termin</th>
              <th>Tahmini Bitiş</th>
              <th>Durum</th>
              {data.showsPenalty && <th>Tahmini Ceza</th>}
            </tr>
          </thead>
          <tbody>
            {data.items.map((alert) => (
              <tr key={alert.projectId}>
                <td>
                  <Link href={`/projeler/${alert.projectId}/is-programi`}>
                    <strong>{alert.projectCode}</strong>
                  </Link>
                  <small style={{ display: "block" }}>{alert.projectName}</small>
                </td>
                <td>
                  %
                  {alert.progressRate.toLocaleString("tr-TR", {
                    maximumFractionDigits: 1,
                  })}
                </td>
                <td>
                  {alert.deadline
                    ? alert.deadline.slice(0, 10).split("-").reverse().join(".")
                    : "—"}
                  {alert.daysToDeadline != null && (
                    <small style={{ display: "block" }}>
                      {alert.daysToDeadline >= 0
                        ? `${alert.daysToDeadline} gün kaldı`
                        : `${Math.abs(alert.daysToDeadline)} gün geçti`}
                    </small>
                  )}
                </td>
                <td>
                  {alert.forecastFinish
                    ? alert.forecastFinish
                        .slice(0, 10)
                        .split("-")
                        .reverse()
                        .join(".")
                    : "—"}
                </td>
                <td>
                  {alert.deadlineAtRisk ? (
                    <span className="erp-status red">Termin tehlikede</span>
                  ) : alert.criticalRiskCount > 0 ? (
                    <span className="erp-status orange">
                      Kritik yolda {alert.criticalRiskCount} iş geride
                    </span>
                  ) : alert.delayWorkDays > 0 ? (
                    <span className="erp-status orange">
                      {alert.delayWorkDays} iş günü gecikme
                    </span>
                  ) : (
                    <span className="erp-status blue">Termin yaklaşıyor</span>
                  )}
                </td>
                {data.showsPenalty && (
                  <td>
                    {alert.penalty?.applicable
                      ? money.format(alert.penalty.amount)
                      : "—"}
                    {alert.penalty?.capApplied && (
                      <small style={{ display: "block" }}>tavana dayandı</small>
                    )}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {data.showsPenalty && (
        <p style={{ padding: "8px 16px 12px", fontSize: 12, color: "var(--erp-muted)" }}>
          Ceza tutarları tahminidir; mücbir sebep ve süre uzatımı hesaba
          katılmaz.
        </p>
      )}
    </section>
  );
}
