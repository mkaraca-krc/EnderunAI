"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import { Button } from "@/components/ui";
import {
  projectBoqService,
  ProjectBoqStatus,
  type ProjectBoqListItem,
} from "@/services/project-boq.service";

const statusLabels: Record<ProjectBoqStatus, string> = {
  [ProjectBoqStatus.Draft]: "Taslak",
  [ProjectBoqStatus.Approved]: "Onaylandı",
  [ProjectBoqStatus.Superseded]: "Eski Revizyon",
  [ProjectBoqStatus.Archived]: "Arşivlendi",
};

const columns: DataTableColumn<ProjectBoqListItem>[] = [
  {
    key: "no",
    header: "Keşif No",
    value: (item) => item.boqNumber,
    render: (item) => <strong>{item.boqNumber}</strong>,
  },
  {
    key: "proje",
    header: "Proje",
    value: (item) => `${item.projectCode} — ${item.projectName}`,
  },
  { key: "ad", header: "Adı", value: (item) => item.name },
  { key: "revizyon", header: "Revizyon", value: (item) => item.revisionCode },
  {
    key: "durum",
    header: "Durum",
    value: (item) =>
      statusLabels[item.status] + (item.isCurrentRevision ? " · Güncel" : ""),
  },
  { key: "kalem", header: "Kalem", numeric: true, value: (item) => item.itemCount },
  {
    key: "toplam",
    header: "Toplam",
    numeric: true,
    value: (item) => item.totalAmount,
    render: (item) => <strong>{money(item.totalAmount)}</strong>,
  },
  {
    key: "ac",
    header: "",
    value: () => "",
    render: (item) => <Link href={`/kesifler/${item.id}`}>Aç</Link>,
  },
];

export default function ProjectBoqListPage() {
  const [items, setItems] = useState<ProjectBoqListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  async function load() {
    setLoading(true);
    setError("");

    try {
      const result = await projectBoqService.getAll();
      setItems(result);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Keşif listesi yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const totalAmount = useMemo(
    () => items.reduce(
      (sum, item) => sum + item.totalAmount,
      0
    ),
    [items]
  );

  return (
    <ErpShell
      design="redwood"
      title="Keşifler"
      description="Proje keşifleri, revizyonları ve sözleşme kalemleri"
    >
      <div className="erp-toolbar">
        <div>
          <strong>Keşif Yönetimi</strong>
          <small>
            {items.length} kayıt · {money(totalAmount)}
          </small>
        </div>

        <div className="erp-actions">
          {/* Keşif listesi başka kullanıcının onayıyla değişiyor;
              sayfayı yeniden yüklemeden görebilmek gerekiyor. */}
          <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>

          <Link href="/kesifler/yeni">
            + Yeni Keşif
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
          title="Keşifler"
          emptyText="Henüz keşif kaydı bulunmuyor."
        />
      </div>
    </ErpShell>
  );
}
