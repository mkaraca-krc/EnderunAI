"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { currencyMoney } from "@/lib/format/turkish";

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
          <button
            type="button"
            disabled={loading}
            onClick={() => void load()}
          >
            Yenile
          </button>

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
        <table className="erp-table">
          <thead>
            <tr>
              <th>Metraj No</th>
              <th>Tarih</th>
              <th>Proje</th>
              <th>Keşif</th>
              <th>Durum</th>
              <th>Kalem</th>
              <th>Bu Dönem</th>
              <th></th>
            </tr>
          </thead>

          <tbody>
            {loading && (
              <tr>
                <td colSpan={8}>
                  Metrajlar yükleniyor...
                </td>
              </tr>
            )}

            {!loading && items.length === 0 && (
              <tr>
                <td colSpan={8}>
                  Henüz metraj kaydı bulunmuyor.
                </td>
              </tr>
            )}

            {items.map((item) => (
              <tr key={item.id}>
                <td>
                  <strong>
                    {item.measurementNumber}
                  </strong>
                </td>

                <td>
                  {formatDate(
                    item.measurementDate
                  )}
                </td>

                <td>
                  {item.projectCode} —{" "}
                  {item.projectName}
                </td>

                <td>
                  {item.boqNumber}
                </td>

                <td>
                  {statusLabels[item.status]}
                </td>

                <td>
                  {item.itemCount}
                </td>

                <td>
                  <strong>
                    {formatMoney(
                      item.totalAmount,
                      item.currencyCode
                    )}
                  </strong>
                </td>

                <td>
                  <Link
                    href={`/metrajlar/${item.id}`}
                  >
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
