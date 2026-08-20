"use client";

import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button } from "@/components/ui";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { money } from "@/lib/format/turkish";
import {
  inventoryService,
  type StockAccountingConsistencyReport,
  type StockAccountingLine,
} from "@/services/inventory.service";

/**
 * STOK ↔ MUHASEBE MUTABAKATI.
 *
 * İki bağımsız kaynağı karşılaştırır: depodaki değer (miktar ×
 * ağırlıklı ortalama maliyet) ve mizandaki 150/153 bakiyesi. Tutmuyorsa
 * bir yerde stok muhasebeye yazılmadan hareket etmiştir.
 *
 * Bu ekran olmasaydı fark ancak dönem sonunda, kimsenin sebebini
 * hatırlamadığı bir tutarsızlık olarak çıkardı.
 */
export default function StockAccountingReconciliationPage() {
  const [report, setReport] = useState<StockAccountingConsistencyReport | null>(
    null
  );
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setReport(await inventoryService.getAccountingConsistency());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Rapor alınamadı.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const columns: DataTableColumn<StockAccountingLine>[] = [
    { key: "tur", header: "Tür", value: (row) => row.kind },
    { key: "hesap", header: "Hesap", value: (row) => row.stockAccountCode },
    {
      key: "depo",
      header: "Depodaki değer",
      numeric: true,
      value: (row) => money(row.stockValue),
    },
    {
      key: "mizan",
      header: "Mizan bakiyesi",
      numeric: true,
      value: (row) => money(row.accountBalance),
    },
    {
      key: "fark",
      header: "Fark",
      numeric: true,
      value: (row) => money(row.difference),
      render: (row) => (
        <span className={`erp-status ${row.difference === 0 ? "green" : "red"}`}>
          {money(row.difference)}
        </span>
      ),
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title="Stok — Muhasebe Mutabakatı"
      description="Depodaki değer ile 150/153 hesap bakiyelerinin karşılaştırması"
    >
      {error && <div className="erp-alert error">{error}</div>}

      {report && (
        <div className={`erp-alert ${report.isConsistent ? "success" : "error"}`}>
          {report.summary}
        </div>
      )}

      <div className="erp-toolbar">
        <div>
          <strong>Faturası gelmemiş mal (379.01)</strong>
          <small>
            {report ? money(report.pendingInvoiceBalance) : "—"} — bu bir
            tutarsızlık değildir: malı aldık, faturası henüz gelmedi. Kalıcı
            bakiye eksik fatura takibidir.
          </small>
        </div>

        <Button variant="secondary" disabled={loading} onClick={() => void load()}>
          Yenile
        </Button>
      </div>

      <DataTable
        rows={report?.lines ?? []}
        columns={columns}
        rowKey={(row) => row.stockAccountCode}
        loading={loading}
        title="Stok — Muhasebe Mutabakatı"
        emptyText="Karşılaştırılacak hesap bulunamadı."
      />
    </ErpShell>
  );
}
