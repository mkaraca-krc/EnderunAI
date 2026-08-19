"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";

import ErpShell from "@/components/erp/erp-shell";
import { Button } from "@/components/ui";
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
  const [reloadKey, setReloadKey] = useState(0);

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
  }, [reloadKey]);


  /*
   * Durum sütunu ekranda rozet, dosyada düz metin. Uyarı satır BAŞINA
   * projeden geliyor, o yüzden sütunlar `alerts` üzerine kapanıyor.
   */
  function statusText(item: (typeof items)[number]) {
    const alert = alerts[item.projectId];

    if (!alert) return "Takipte";
    if (alert.deadlineAtRisk) return "Termin tehlikede";
    if (alert.criticalRiskCount > 0)
      return `Kritik yolda ${alert.criticalRiskCount} iş geride`;
    if (alert.delayWorkDays > 0) return `${alert.delayWorkDays} iş günü gecikme`;
    return "Termin yaklaşıyor";
  }

  const columns: DataTableColumn<(typeof items)[number]>[] = [
    {
      key: "proje",
      header: "Proje",
      value: (item) => `${item.projectCode} — ${item.projectName}`,
      render: (item) => (
        <>
          <Link href={`/projeler/${item.projectId}/is-programi`}>
            <strong>{item.projectCode}</strong>
          </Link>
          <small style={{ display: "block" }}>{item.projectName}</small>
        </>
      ),
    },
    { key: "program", header: "Program", value: (item) => item.name },
    {
      key: "aktivite",
      header: "Aktivite",
      numeric: true,
      value: (item) => item.activityCount,
    },
    {
      key: "termin",
      header: "Termin",
      value: (item) =>
        `${formatDate(item.deadline)} (${item.hasContractDeadline ? "sözleşmeden" : "plandan"})`,
      render: (item) => (
        <>
          {formatDate(item.deadline)}
          <small style={{ display: "block" }}>
            {item.hasContractDeadline ? "sözleşmeden" : "plandan"}
          </small>
        </>
      ),
    },
    {
      key: "baseline",
      header: "Baseline",
      value: (item) =>
        item.baselineRevisionNumber === 0
          ? "Kaydedilmedi"
          : `${item.baselineRevisionNumber}. revizyon`,
      render: (item) =>
        item.baselineRevisionNumber === 0 ? (
          <span className="erp-status gray">Kaydedilmedi</span>
        ) : (
          `${item.baselineRevisionNumber}. revizyon`
        ),
    },
    {
      key: "durum",
      header: "Durum",
      value: statusText,
      render: (item) => {
        const alert = alerts[item.projectId];

        if (!alert) return <span className="erp-status green">Takipte</span>;
        if (alert.deadlineAtRisk)
          return <span className="erp-status red">Termin tehlikede</span>;
        if (alert.criticalRiskCount > 0)
          return (
            <span className="erp-status orange">
              Kritik yolda {alert.criticalRiskCount} iş geride
            </span>
          );
        if (alert.delayWorkDays > 0)
          return (
            <span className="erp-status orange">
              {alert.delayWorkDays} iş günü gecikme
            </span>
          );
        return <span className="erp-status blue">Termin yaklaşıyor</span>;
      },
    },
  ];


  return (
    <ErpShell
      design="redwood"
      title="İş Programı"
      description="Projelerin Gantt planı, kritik yolu ve gecikme durumu"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => setReloadKey((key) => key + 1)}>Yenile</Button>
      </div>

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
            <DataTable
              rows={items}
              columns={columns}
              rowKey={(item) => item.id}
              title="İş Programları"
              emptyText="İş programı bulunmuyor."
            />
          </div>
        )}
      </section>
    </ErpShell>
  );
}
