"use client";

import { use, useCallback, useEffect, useState } from "react";
import Link from "next/link";

import ErpShell from "@/components/erp/erp-shell";
import { Button } from "@/components/ui";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { apiClient } from "@/lib/api/api-client";
import { quantity } from "@/lib/format/turkish";

/**
 * RAF QR'I OKUTULUNCA AÇILAN SAYFA — "bu rafta ne var".
 *
 * Depo görevlisi telefonun kamerasıyla rafın etiketini okutur ve
 * karşısındaki rafın içeriğini görür. QR'a ham kimlik değil URL
 * yazıldığı için ayrı bir uygulama gerekmiyor.
 */

type ShelfItem = {
  id: string;
  code: string;
  name: string;
  unit: string;
  levelCode: string | null;
  onHand: number;
};

type ShelfResponse = {
  shelf: { id: string; code: string; zoneName: string; warehouseName: string };
  items: ShelfItem[];
};

export default function ShelfContentsPage({
  params,
}: {
  params: Promise<{ warehouseId: string; shelfId: string }>;
}) {
  const { warehouseId, shelfId } = use(params);

  const [data, setData] = useState<ShelfResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setData(
        await apiClient<ShelfResponse>(
          `warehouses/${warehouseId}/locations/shelves/${shelfId}/items`
        )
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Raf içeriği okunamadı.");
    } finally {
      setLoading(false);
    }
  }, [warehouseId, shelfId]);

  useEffect(() => {
    void load();
  }, [load]);

  const columns: DataTableColumn<ShelfItem>[] = [
    { key: "kod", header: "Kod", value: (row) => row.code },
    {
      key: "ad",
      header: "Malzeme",
      value: (row) => row.name,
      render: (row) => (
        <Link href={`/depo-stok/malzeme/${row.id}`} className="erp-row-link">
          {row.name}
        </Link>
      ),
    },
    { key: "kat", header: "Kat", value: (row) => row.levelCode ?? "—" },
    {
      key: "stok",
      header: "Stok",
      numeric: true,
      value: (row) => row.onHand,
      render: (row) => `${quantity(row.onHand)} ${row.unit}`,
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title={
        data
          ? `${data.shelf.zoneName} · ${data.shelf.code}`
          : "Raf İçeriği"
      }
      description={data?.shelf.warehouseName ?? "QR ile açılan raf listesi"}
    >
      {error && <div className="erp-alert error">{error}</div>}

      <div className="erp-toolbar">
        <div>
          <strong>{data ? `${data.items.length} malzeme` : "…"}</strong>
          <small>Bu rafta duran stok kartları.</small>
        </div>

        <Button variant="secondary" disabled={loading} onClick={() => void load()}>
          Yenile
        </Button>
      </div>

      <DataTable
        rows={data?.items ?? []}
        columns={columns}
        rowKey={(row) => row.id}
        loading={loading}
        title={data ? `${data.shelf.zoneName} · ${data.shelf.code}` : "Raf"}
        emptyText="Bu rafta kayıtlı malzeme yok."
      />
    </ErpShell>
  );
}
