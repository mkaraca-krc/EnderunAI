"use client";

import { useCallback, useEffect, useState } from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";

import ErpShell from "@/components/erp/erp-shell";
import { Button, EmptyState, Input, Select, StatCard } from "@/components/ui";
import { money, percent, whole } from "@/lib/format/turkish";
import {
  RETAIL_PAYMENT,
  retailReportService,
  type DayEndReport,
  type OpenReceivableRow,
  type StaffSalesRow,
} from "@/services/retail-sale.service";

type Warehouse = { id: string; name: string; companyId: string };

function today() {
  return new Date().toISOString().slice(0, 10);
}

function daysAgo(count: number) {
  const date = new Date();
  date.setDate(date.getDate() - count);
  return date.toISOString().slice(0, 10);
}

/**
 * PERAKENDE RAPORLARI — HEPSİ MEVCUT VERİDEN OKUR.
 *
 * Hiçbir rakam burada yeniden hesaplanmıyor: nakit ve kart fiilen
 * kasaya giren paradan (CashTransaction), açık alacak faturadan
 * (SalesInvoice), elden ise fişin ayrı alanından geliyor. Ekran ikinci
 * bir toplama kaynağı olsaydı iki rakam arasında hangisinin doğru
 * olduğu sorusu doğardı.
 */
const staffColumns: DataTableColumn<StaffSalesRow>[] = [
  { key: "personel", header: "Personel", value: (row) => row.fullName },
  {
    key: "satis",
    header: "Satış",
    numeric: true,
    value: (row) => row.saleCount,
    render: (row) => whole(row.saleCount),
  },
  {
    key: "tutar",
    header: "Tutar",
    numeric: true,
    value: (row) => row.total,
    render: (row) => money(row.total),
  },
  {
    key: "iskonto",
    header: "İskonto",
    numeric: true,
    value: (row) => row.discountTotal,
    render: (row) => money(row.discountTotal),
  },
  {
    key: "iskontoOran",
    header: "İskonto oranı",
    numeric: true,
    value: (row) => row.discountRate,
    render: (row) => percent(row.discountRate),
  },
  {
    key: "onay",
    header: "Onaya düşen",
    numeric: true,
    value: (row) => row.approvalCount,
    render: (row) => whole(row.approvalCount),
  },
];

const receivableColumns: DataTableColumn<OpenReceivableRow>[] = [
  { key: "fis", header: "Fiş No", value: (row) => row.documentNumber },
  { key: "musteri", header: "Müşteri", value: (row) => row.customerTitle ?? "—" },
  {
    key: "odeme",
    header: "Ödeme",
    value: (row) => RETAIL_PAYMENT[row.paymentMethod],
  },
  {
    key: "vade",
    header: "Vade",
    value: (row) =>
      (row.dueDate
        ? row.dueDate.slice(0, 10).split("-").reverse().join(".")
        : "—") + (row.isOverdue ? " (vadesi geçti)" : ""),
    render: (row) => (
      <>
        {row.dueDate
          ? row.dueDate.slice(0, 10).split("-").reverse().join(".")
          : "—"}
        {row.isOverdue && (
          <small className="rw-value-danger" style={{ display: "block" }}>
            vadesi geçti
          </small>
        )}
      </>
    ),
  },
  {
    key: "kalan",
    header: "Kalan",
    numeric: true,
    value: (row) => row.remaining,
    render: (row) => money(row.remaining),
  },
];

export default function RetailReportsPage() {
  const [companyId, setCompanyId] = useState("");
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [date, setDate] = useState(today());
  const [from, setFrom] = useState(daysAgo(30));
  const [to, setTo] = useState(today());

  const [dayEnd, setDayEnd] = useState<DayEndReport | null>(null);
  const [staff, setStaff] = useState<StaffSalesRow[]>([]);
  const [receivables, setReceivables] = useState<OpenReceivableRow[]>([]);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const resources = (await fetch("/api/backend/perakende/kaynaklar", {
        credentials: "include",
      }).then((response) => response.json())) as { warehouses: Warehouse[] };

      setWarehouses(resources.warehouses);

      const company = companyId || resources.warehouses[0]?.companyId || "";
      if (!company) {
        setLoading(false);
        return;
      }

      if (!companyId) setCompanyId(company);

      const [dayEndData, staffData, receivableData] = await Promise.all([
        retailReportService.dayEnd(company, date),
        retailReportService.byStaff(company, from, to),
        retailReportService.openReceivables(company),
      ]);

      setDayEnd(dayEndData);
      setStaff(staffData);
      setReceivables(receivableData);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Raporlar yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [companyId, date, from, to]);

  useEffect(() => {
    void load();
  }, [load]);

  const overdue = receivables.filter((row) => row.isOverdue);

  return (
    <ErpShell
      design="redwood"
      title="Perakende Raporları"
      description="Gün sonu kasa, personel satışları ve açık vade"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      <section className="erp-panel">
        <div className="erp-panel-header">
          <h2>Gün Sonu Kasa</h2>
          <p>Seçilen günde kasaya giren para; iade ve iptaller düşülmüştür.</p>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>Şirket</span>
            <Select
              value={companyId}
              onChange={(event) => setCompanyId(event.target.value)}
              options={warehouses.map((item) => ({
                value: item.companyId,
                label: item.name,
              }))}
            />
          </label>

          <label>
            <span>Tarih</span>
            <Input type="date" value={date} onChange={(event) => setDate(event.target.value)} />
          </label>
        </div>

        {dayEnd && (
          <>
            <div className="erp-stat-grid erp-mt">
              <StatCard title="Nakit" value={money(dayEnd.cash)} />
              <StatCard title="Kredi kartı" value={money(dayEnd.card)} />
              <StatCard title="Çek" value={money(dayEnd.cheque)} />
              <StatCard title="Vadeli" value={money(dayEnd.term)} />
            </div>

            <div className="erp-toolbar erp-mt">
              <div>
                <strong>{money(dayEnd.recordedTotal)}</strong>
                <small style={{ display: "block" }}>
                  kayıtlı toplam · {whole(dayEnd.saleCount)} satış
                  {dayEnd.returnCount > 0 && ` · ${whole(dayEnd.returnCount)} iade`}
                </small>
              </div>
            </div>

            {/*
              ELDEN AYRI SATIRDA ve kayıtlı toplama EKLENMİYOR.
              Eklenseydi resmî ciro ile kasa dökümü birbirini tutmazdı.
            */}
            {dayEnd.cashAmount != null && dayEnd.cashAmount !== 0 && (
              <div className="erp-alert">
                Elden: <strong>{money(dayEnd.cashAmount)}</strong> — kayıtlı
                toplama dahil değildir, resmî gelire girmez.
              </div>
            )}

            {dayEnd.hiddenCount > 0 && (
              <div className="erp-alert warning">
                {whole(dayEnd.hiddenCount)} fişte elden tutar gizli.
              </div>
            )}
          </>
        )}
      </section>

      <section className="erp-panel erp-mt">
        <div className="erp-panel-header">
          <h2>Personel Bazında Satış</h2>
          <p>Kimin ne sattığı ve ne kadar iskonto verdiği.</p>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>Başlangıç</span>
            <Input type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
          </label>

          <label>
            <span>Bitiş</span>
            <Input type="date" value={to} onChange={(event) => setTo(event.target.value)} />
          </label>
        </div>

        {loading ? (
          <div className="erp-loading">Rapor hesaplanıyor...</div>
        ) : staff.length === 0 ? (
          <EmptyState title="Satış yok" description="Seçilen aralıkta tamamlanmış satış bulunmuyor." />
        ) : (
          <div className="erp-table-wrap">
            <DataTable
              rows={staff}
              columns={staffColumns}
              rowKey={(row) => row.userId ?? row.fullName}
              title="Personel Satış Performansı"
              emptyText="Bu dönemde satış yok."
            />
          </div>
        )}
      </section>

      <section className="erp-panel erp-mt">
        <div className="erp-panel-header">
          <h2>Açık Vade ve Alacak</h2>
          <p>
            {receivables.length} açık kayıt
            {overdue.length > 0 && ` · ${overdue.length} tanesi vadesi geçmiş`}
          </p>
        </div>

        {receivables.length === 0 ? (
          <EmptyState title="Açık alacak yok" description="Vadesi gelmemiş ya da geçmiş alacak bulunmuyor." />
        ) : (
          <div className="erp-table-wrap">
            <DataTable
              rows={receivables}
              columns={receivableColumns}
              rowKey={(row) => row.id}
              title="Açık Tahsilatlar"
              emptyText="Açık tahsilat yok."
            />
          </div>
        )}
      </section>
    </ErpShell>
  );
}
