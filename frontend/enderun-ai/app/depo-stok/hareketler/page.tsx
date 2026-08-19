"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { amount } from "@/lib/format/turkish";
import { Button } from "@/components/ui";
import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";
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

/**
 * SÜTUNLAR — ekranda ne göründüğü ile dosyaya/kâğıda ne yazıldığı
 * AYRI tanımlanıyor.
 *
 * `render` rozet, bağlantı ve iki satırlı hücre üretiyor; bunları
 * CSV'ye olduğu gibi yazmak "▲ Giriş" gibi saçma değerler doğururdu.
 * `value` her sütunun düz karşılığını verir.
 */
const columns: DataTableColumn<InventoryMovement>[] = [
  {
    key: "tarih",
    header: "Tarih",
    value: (movement) => dateFormat.format(new Date(movement.movementDate)),
  },
  {
    key: "hareket",
    header: "Hareket",
    value: (movement) =>
      MOVEMENT_LABELS[movement.type] ?? `Hareket ${movement.type}`,
    render: (movement) => (
      <span className={`erp-status ${movementColor(movement.type)}`}>
        {MOVEMENT_LABELS[movement.type] ?? `Hareket ${movement.type}`}
      </span>
    ),
  },
  {
    key: "malzeme",
    header: "Malzeme",
    // Dosyada tek hücrede kod ve ad birlikte anlamlı.
    value: (movement) => `${movement.itemName} (${movement.itemCode})`,
    render: (movement) => (
      <>
        <strong>{movement.itemName}</strong>
        <small>{movement.itemCode}</small>
      </>
    ),
  },
  { key: "depo", header: "Depo", value: (movement) => movement.warehouseName },
  {
    key: "yer",
    header: "Proje / Şantiye",
    value: (movement) =>
      [movement.projectName, movement.projectSiteName]
        .filter(Boolean)
        .join(" / ") || "—",
    render: (movement) => (
      <>
        {movement.projectName || "—"}
        {movement.projectSiteName && <small>{movement.projectSiteName}</small>}
      </>
    ),
  },
  {
    key: "miktar",
    header: "Miktar",
    numeric: true,
    value: (movement) => movement.quantity,
  },
  {
    key: "tutar",
    header: "Tutar (TRY)",
    numeric: true,
    value: (movement) => movement.totalCost ?? "",
    render: (movement) =>
      movement.totalCost != null ? amount(movement.totalCost) : "—",
  },
  {
    key: "belge",
    header: "Belge No",
    value: (movement) => movement.referenceNumber,
    render: (movement) =>
      movement.goodsReceiptId ? (
        <Link
          className="erp-row-link"
          href={`/depo-stok/mal-kabul/${movement.goodsReceiptId}`}
        >
          {movement.referenceNumber}
        </Link>
      ) : (
        movement.referenceNumber
      ),
  },
];

export default function StockMovementsPage() {
  const [items, setItems] = useState<InventoryMovement[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    void inventoryMovementService
      .getMovements()
      .then(setItems)
      .catch((err) =>
        setError(err instanceof Error ? err.message : "Hareketler yüklenemedi.")
      )
      .finally(() => setLoading(false));
  }, [reloadKey]);

  return (
    <ErpShell
      design="redwood"
      title="Stok Hareketleri"
      description="Giriş, çıkış, transfer ve sayım hareketlerinin tek defteri"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => setReloadKey((key) => key + 1)}>Yenile</Button>
      </div>

      <div className="erp-page-toolbar">
        <div>
          <strong>{items.length} hareket</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            Mal kabulden gelen hareketlerde belge numarası, kabul kaydına
            gider.
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          {/*
            Stok girişi artık serbest değil: giriş yalnız mal kabulden
            (siparişe bağlı, maliyetli) yapılır. Buton oraya gider.
          */}
          <Link className="erp-primary-button" href="/depo-stok/mal-kabul">
            Mal Kabul
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

        <DataTable
          rows={items}
          columns={columns}
          rowKey={(movement) => movement.id}
          loading={loading}
          emptyText="Henüz stok hareketi yok."
          title="Stok Hareket Defteri"
        />
      </div>
    </ErpShell>
  );
}
