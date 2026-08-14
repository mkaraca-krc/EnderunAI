"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { amount } from "@/lib/format/turkish";
import {
  inventoryMovementService,
  type InventoryMovement,
} from "@/services/inventory-movement.service";

const MOVEMENT_LABELS: Record<number, string> = {
  0: "Giriş",
  1: "Çıkış",
  2: "Transfer çıkış",
  3: "Transfer giriş",
  4: "İade",
  5: "Sayım düzeltme",
  6: "Sayım",
};

const dateFormat = new Intl.DateTimeFormat("tr-TR");
/** Giriş yeşil, çıkış sarı, düzeltme mavi — rozet tek bakışta okunur. */
function movementColor(type: number) {
  if (type === 0 || type === 3 || type === 4) return "green";
  if (type === 1 || type === 2) return "yellow";
  return "blue";
}

export default function StockMovementsPage() {
  const [items, setItems] = useState<InventoryMovement[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    void inventoryMovementService
      .getMovements()
      .then(setItems)
      .catch((err) =>
        setError(err instanceof Error ? err.message : "Hareketler yüklenemedi.")
      )
      .finally(() => setLoading(false));
  }, []);

  return (
    <ErpShell
      design="redwood"
      title="Stok Hareketleri"
      description="Giriş, çıkış, transfer ve sayım hareketlerinin tek defteri"
    >
      <div className="erp-page-toolbar">
        <div>
          <strong>{items.length} hareket</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            Mal kabulden gelen hareketlerde belge numarası, kabul kaydına
            gider.
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <Link className="erp-primary-button" href="/depo-stok/giris">
            Stok Girişi
          </Link>
          <Link className="erp-secondary-button" href="/depo-stok/cikis">
            Stok Çıkışı
          </Link>
          <Link className="erp-secondary-button" href="/depo-stok/transfer">
            Transfer
          </Link>
          <Link className="erp-secondary-button" href="/depo-stok/sayim">
            Sayım / Düzeltme
          </Link>
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Hareket Defteri</h2>
        </div>

        {loading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : items.length === 0 ? (
          <div className="erp-empty-state">
            <p>Henüz stok hareketi yok.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Tarih</th>
                  <th>Hareket</th>
                  <th>Malzeme</th>
                  <th>Depo</th>
                  <th>Proje / Şantiye</th>
                  <th className="num">Miktar</th>
                  <th className="num">Tutar (TRY)</th>
                  <th>Belge No</th>
                </tr>
              </thead>
              <tbody>
                {items.map((movement) => (
                  <tr key={movement.id}>
                    <td>{dateFormat.format(new Date(movement.movementDate))}</td>
                    <td>
                      <span
                        className={`erp-status ${movementColor(movement.type)}`}
                      >
                        {MOVEMENT_LABELS[movement.type] ??
                          `Hareket ${movement.type}`}
                      </span>
                    </td>
                    <td>
                      <strong>{movement.itemName}</strong>
                      <small>{movement.itemCode}</small>
                    </td>
                    <td>{movement.warehouseName}</td>
                    <td>
                      {movement.projectName || "—"}
                      {movement.projectSiteName && (
                        <small>{movement.projectSiteName}</small>
                      )}
                    </td>
                    <td className="num">{movement.quantity}</td>
                    <td className="num">
                      {movement.totalCost != null
                        ? amount(movement.totalCost)
                        : "—"}
                    </td>
                    <td>
                      {movement.goodsReceiptId ? (
                        <Link
                          className="erp-row-link"
                          href={`/depo-stok/mal-kabul/${movement.goodsReceiptId}`}
                        >
                          {movement.referenceNumber}
                        </Link>
                      ) : (
                        movement.referenceNumber
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </ErpShell>
  );
}
