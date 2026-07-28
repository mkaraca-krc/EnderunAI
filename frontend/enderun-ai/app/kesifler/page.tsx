"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
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

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  minimumFractionDigits: 2,
});

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
      title="Keşifler"
      description="Proje keşifleri, revizyonları ve sözleşme kalemleri"
    >
      <div className="erp-toolbar">
        <div>
          <strong>Keşif Yönetimi</strong>
          <small>
            {items.length} kayıt · {money.format(totalAmount)}
          </small>
        </div>

        <Link href="/kesifler/yeni">
          + Yeni Keşif
        </Link>
      </div>

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <div className="erp-table-card">
        <table className="erp-table">
          <thead>
            <tr>
              <th>Keşif No</th>
              <th>Proje</th>
              <th>Adı</th>
              <th>Revizyon</th>
              <th>Durum</th>
              <th>Kalem</th>
              <th>Toplam</th>
              <th></th>
            </tr>
          </thead>

          <tbody>
            {loading && (
              <tr>
                <td colSpan={8}>
                  Keşifler yükleniyor...
                </td>
              </tr>
            )}

            {!loading && items.length === 0 && (
              <tr>
                <td colSpan={8}>
                  Henüz keşif kaydı bulunmuyor.
                </td>
              </tr>
            )}

            {items.map((item) => (
              <tr key={item.id}>
                <td>
                  <strong>{item.boqNumber}</strong>
                </td>
                <td>
                  {item.projectCode} — {item.projectName}
                </td>
                <td>{item.name}</td>
                <td>{item.revisionCode}</td>
                <td>
                  {statusLabels[item.status]}
                  {item.isCurrentRevision
                    ? " · Güncel"
                    : ""}
                </td>
                <td>{item.itemCount}</td>
                <td>
                  <strong>
                    {money.format(item.totalAmount)}
                  </strong>
                </td>
                <td>
                  <Link href={`/kesifler/${item.id}`}>
                    Aç
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </ErpShell>
  );
}
