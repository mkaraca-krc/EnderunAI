"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";

import ErpShell from "@/components/erp/erp-shell";
import { currencyMoney } from "@/lib/format/turkish";
import { Button } from "@/components/ui";

import {
  projectMeasurementService,
  ProjectMeasurementStatus,
  type ProjectMeasurementListItem,
} from "@/services/project-measurement.service";

const statusLabels: Record<
  ProjectMeasurementStatus,
  string
> = {
  [ProjectMeasurementStatus.Draft]: "Taslak",
  [ProjectMeasurementStatus.PendingApproval]:
    "Onay Bekliyor",
  [ProjectMeasurementStatus.Approved]:
    "Onaylandı",
  [ProjectMeasurementStatus.TransferredToProgressPayment]:
    "Hakedişe Aktarıldı",
  [ProjectMeasurementStatus.Cancelled]:
    "İptal Edildi",
};

function formatMoney(
  amount: number,
  currencyCode: string
) {
  return currencyMoney(amount, currencyCode);
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("tr-TR").format(
    new Date(value)
  );
}

const columns: DataTableColumn<ProjectMeasurementListItem>[] = [
  {
    key: "no",
    header: "Metraj No",
    value: (item) => item.measurementNumber,
    render: (item) => <strong>{item.measurementNumber}</strong>,
  },
  {
    key: "tarih",
    header: "Tarih",
    value: (item) => formatDate(item.measurementDate),
  },
  {
    key: "proje",
    header: "Proje",
    value: (item) => `${item.projectCode} — ${item.projectName}`,
  },
  { key: "kesif", header: "Keşif", value: (item) => item.boqNumber },
  {
    key: "durum",
    header: "Durum",
    value: (item) => statusLabels[item.status],
  },
  {
    key: "kalem",
    header: "Kalem",
    numeric: true,
    value: (item) => item.itemCount,
  },
  {
    key: "tutar",
    header: "Bu Dönem",
    numeric: true,
    value: (item) => item.totalAmount,
    render: (item) => (
      <strong>{formatMoney(item.totalAmount, item.currencyCode)}</strong>
    ),
  },
  {
    key: "ac",
    header: "",
    value: () => "",
    render: (item) => <Link href={`/metrajlar/${item.id}`}>Aç</Link>,
  },
];

export default function ProjectMeasurementListPage() {
  const [items, setItems] =
    useState<ProjectMeasurementListItem[]>([]);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  async function load() {
    setLoading(true);
    setError("");

    try {
      const result =
        await projectMeasurementService.getAll();

      setItems(result);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Metraj listesi yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const totalAmount = useMemo(
    () =>
      items.reduce(
        (sum, item) => sum + item.totalAmount,
        0
      ),
    [items]
  );

  return (
    <ErpShell
      design="redwood"
      title="Metrajlar"
      description="Saha ilerlemeleri, keşif kalemleri ve dönemsel miktarlar"
    >
      <div className="erp-toolbar">
        <div>
          <strong>Metraj Yönetimi</strong>
          <small>
            {items.length} kayıt ·{" "}
            {formatMoney(totalAmount, "TRY")}
          </small>
        </div>

        <div className="erp-actions">
          {/* Metrajın durumu onaycı tarafında değişiyor; listeyi
              tazelemeden "onay bekliyor" satırı öylece kalıyordu. */}
          <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>

          <Link href="/metrajlar/yeni">
            + Yeni Metraj
          </Link>
        </div>
      </div>

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <div className="erp-table-card">
        <DataTable
          rows={items}
          columns={columns}
          rowKey={(item) => item.id}
          loading={loading}
          title="Metrajlar"
          emptyText="Henüz metraj kaydı bulunmuyor."
        />
      </div>
    </ErpShell>
  );
}
