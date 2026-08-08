"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  PURCHASE_RETURN_STATUS,
  purchaseReturnService,
  type PurchaseReturnDetail,
} from "@/services/goods-receipt.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function money(value: number, currency = "TRY") {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}

function number(value: number) {
  return new Intl.NumberFormat("tr-TR", {
    maximumFractionDigits: 2,
  }).format(value);
}

function statusClass(status: number) {
  if (status === PURCHASE_RETURN_STATUS.Completed) return "erp-status green";
  if (status === PURCHASE_RETURN_STATUS.Cancelled) return "erp-status gray";
  if (status === PURCHASE_RETURN_STATUS.Sent) return "erp-status blue";
  return "erp-status orange";
}

/**
 * Alış iadesi belgesi — tedarikçiye ne, ne kadar, hangi gerekçeyle
 * iade edildiğinin kaydı.
 *
 * Gerekçe mal kabulden KOPYALANIR: mal kabul sonradan düzeltilse bile
 * belge, iade anındaki gerekçeyi olduğu gibi taşımalı.
 */
export default function PurchaseReturnDetailPage() {
  const params = useParams<{ id: string }>();
  const [item, setItem] = useState<PurchaseReturnDetail | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const result = await purchaseReturnService.getById(params.id);
        if (!cancelled) setItem(result);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Belge alınamadı.");
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [params.id]);

  return (
    <ErpShell
      title={item ? `Alış İadesi ${item.returnNumber}` : "Alış İadesi"}
      description="Reddedilen ve hasarlı malın tedarikçiye iadesi"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <div className="erp-page-toolbar">
        <Link className="erp-secondary-button" href="/depo-stok/iadeler">
          İade Listesi
        </Link>

        {item && (
          <Link
            className="erp-secondary-button"
            href={`/depo-stok/mal-kabul/${item.goodsReceiptId}`}
          >
            Mal Kabul: {item.receiptNumber}
          </Link>
        )}
      </div>

      {!item ? (
        <div className="erp-panel erp-loading">Belge yükleniyor...</div>
      ) : (
        <>
          <div className="erp-quick-grid">
            <div className="erp-panel">
              <small style={{ display: "block" }}>Durum</small>
              <strong>
                <span className={statusClass(item.status)}>
                  {item.statusName}
                </span>
              </strong>
            </div>

            <div className="erp-panel">
              <small style={{ display: "block" }}>Tedarikçi</small>
              <strong>{item.supplierName}</strong>
            </div>

            <div className="erp-panel">
              <small style={{ display: "block" }}>İade Tutarı</small>
              <strong>{money(item.totalAmount, item.currencyCode)}</strong>
              <small style={{ display: "block" }}>alım fiyatı üzerinden</small>
            </div>

            <div className="erp-panel">
              <small style={{ display: "block" }}>İade Tarihi</small>
              <strong>
                {dateFormat.format(new Date(item.returnDate))}
              </strong>
            </div>
          </div>

          <section className="erp-table-card" style={{ marginTop: 16 }}>
            <div className="erp-table-header">
              <h2>Künye</h2>
            </div>
            <div className="erp-detail-grid" style={{ padding: "12px 16px" }}>
              <div>
                <span>Sipariş</span>
                <strong>{item.orderNumber}</strong>
              </div>
              <div>
                <span>Proje</span>
                <strong>
                  {item.projectCode} — {item.projectName}
                </strong>
              </div>
              <div>
                <span>Gönderildi</span>
                <strong>
                  {item.sentAtUtc
                    ? dateFormat.format(new Date(item.sentAtUtc))
                    : "—"}
                </strong>
              </div>
              <div>
                <span>Kapandı</span>
                <strong>
                  {item.completedAtUtc
                    ? dateFormat.format(new Date(item.completedAtUtc))
                    : "—"}
                </strong>
              </div>
            </div>

            {item.notes && (
              <p style={{ padding: "0 16px 12px", fontSize: 13 }}>
                {item.notes}
              </p>
            )}

            {item.cancellationReason && (
              <p style={{ padding: "0 16px 16px", fontSize: 13 }}>
                <strong>İptal gerekçesi:</strong> {item.cancellationReason}
              </p>
            )}
          </section>

          <section className="erp-table-card" style={{ marginTop: 16 }}>
            <div className="erp-table-header">
              <h2>İade Kalemleri</h2>
              <small>{item.items.length} satır</small>
            </div>

            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Sıra</th>
                    <th>Malzeme</th>
                    <th>Neden</th>
                    <th>Gerekçe</th>
                    <th>Miktar</th>
                    <th>Birim Fiyat</th>
                    <th>Tutar</th>
                  </tr>
                </thead>
                <tbody>
                  {item.items.map((line) => (
                    <tr key={line.id}>
                      <td>{line.lineNumber}</td>
                      <td>{line.materialDescription}</td>
                      <td>
                        <span
                          className={
                            line.reasonKind === 1
                              ? "erp-status orange"
                              : "erp-status red"
                          }
                        >
                          {line.reasonKindName}
                        </span>
                      </td>
                      <td style={{ fontSize: 13 }}>{line.reason || "—"}</td>
                      <td>
                        {number(line.quantity)} {line.unit}
                      </td>
                      <td>{money(line.unitPrice, item.currencyCode)}</td>
                      <td>
                        <strong>
                          {money(line.lineTotal, item.currencyCode)}
                        </strong>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}
    </ErpShell>
  );
}
