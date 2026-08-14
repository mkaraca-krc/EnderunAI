"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  projectScheduleService,
  type ScheduleAlert,
  type ScheduleListItem,
} from "@/services/project-schedule.service";

function formatDate(iso?: string | null) {
  return iso ? iso.slice(0, 10).split("-").reverse().join(".") : "—";
}

/**
 * İş programı listesi.
 *
 * Proje listesine giremeyen saha (Şantiye Şefi, Formen) kendi
 * şantiyelerinin projelerine buradan ulaşır. Kapsam SUNUCUDA
 * uygulanıyor: bu ekran ne isterse istesin, uç yalnızca kullanıcının
 * projelerini döndürür.
 */
export default function ScheduleListPage() {
  const [items, setItems] = useState<ScheduleListItem[]>([]);
  const [alerts, setAlerts] = useState<Record<string, ScheduleAlert>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const result = await projectScheduleService.list();
        if (cancelled) return;

        setItems(result.items);

        // Uyarılar ayrı uçtan geliyor; gecikme durumunu listede
        // göstermek için eşleştiriliyor.
        try {
          const alertResult = await projectScheduleService.alerts();

          if (!cancelled) {
            setAlerts(
              Object.fromEntries(
                alertResult.items.map((alert) => [alert.projectId, alert])
              )
            );
          }
        } catch {
          // Uyarı alınamazsa liste yine çalışır.
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Liste alınamadı.");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <ErpShell
      design="redwood"
      title="İş Programı"
      description="Projelerin Gantt planı, kritik yolu ve gecikme durumu"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <section className="erp-table-card">
        <div className="erp-table-header">
          <h2>İş Programları</h2>
          <small>{items.length} proje</small>
        </div>

        {loading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : items.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Görebildiğiniz projelerde iş programı yok</strong>
            <p>
              İş programı proje bazında açılır ve icmal kısımlarından doğar.
              Atandığınız projede program açıldığında burada görünür.
            </p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Proje</th>
                  <th>Program</th>
                  <th>Aktivite</th>
                  <th>Termin</th>
                  <th>Baseline</th>
                  <th>Durum</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => {
                  const alert = alerts[item.projectId];

                  return (
                    <tr key={item.id}>
                      <td>
                        <Link href={`/projeler/${item.projectId}/is-programi`}>
                          <strong>{item.projectCode}</strong>
                        </Link>
                        <small style={{ display: "block" }}>
                          {item.projectName}
                        </small>
                      </td>
                      <td>{item.name}</td>
                      <td>{item.activityCount}</td>
                      <td>
                        {formatDate(item.deadline)}
                        <small style={{ display: "block" }}>
                          {item.hasContractDeadline ? "sözleşmeden" : "plandan"}
                        </small>
                      </td>
                      <td>
                        {item.baselineRevisionNumber === 0 ? (
                          <span className="erp-status gray">Kaydedilmedi</span>
                        ) : (
                          `${item.baselineRevisionNumber}. revizyon`
                        )}
                      </td>
                      <td>
                        {!alert ? (
                          <span className="erp-status green">Takipte</span>
                        ) : alert.deadlineAtRisk ? (
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
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </ErpShell>
  );
}
