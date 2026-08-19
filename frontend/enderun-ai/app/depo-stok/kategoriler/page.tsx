"use client";

import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button } from "@/components/ui";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { useModuleActions } from "@/lib/auth/module-actions";
import {
  inventoryService,
  type InventoryCategory,
} from "@/services/inventory.service";

/**
 * STOK KATEGORİLERİ VE ÖZELLİK ŞABLONLARI.
 *
 * Kategori SİSTEM GENELİ — şirkete bağlı değil. Kartın adı, mükerrer
 * engeli ve birim kilidi buradaki şablondan türeyecek (S2), o yüzden
 * bu ekran stok paketinin temel bakım noktası.
 *
 * STANDART kategoride değerler açılır listeden seçilir; serbest yazım
 * "200mm" / "200 mm" / "200MM" gibi üç ayrı gerçek üretir ve mükerrer
 * engeli çalışamaz.
 */
export default function InventoryCategoriesPage() {
  const actions = useModuleActions("depo-stok");

  const [categories, setCategories] = useState<InventoryCategory[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setCategories(await inventoryService.getCategories());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kategoriler yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const selected = categories.find((x) => x.id === selectedId) ?? null;

  const columns: DataTableColumn<InventoryCategory>[] = [
    { key: "kod", header: "Kod", value: (row) => row.code },
    {
      key: "ad",
      header: "Kategori",
      value: (row) => row.name,
      render: (row) => <strong>{row.name}</strong>,
    },
    {
      key: "tip",
      header: "Tip",
      value: (row) => (row.kind === 0 ? "STANDART" : "SERBEST"),
      render: (row) => (
        <span className={`erp-status ${row.kind === 0 ? "blue" : "gray"}`}>
          {row.kind === 0 ? "STANDART" : "SERBEST"}
        </span>
      ),
    },
    {
      key: "birimler",
      header: "İzin verilen birimler",
      /* Çok birimli kategoriler var: topraklama adet+metre, sarf
         kg+paket+adet. Kart açılırken biri seçilip sabitlenir. */
      value: (row) => row.units.join(", "),
    },
    {
      key: "ozellik",
      header: "Özellik",
      numeric: true,
      value: (row) => row.attributes.length,
    },
    {
      key: "ac",
      header: "",
      value: () => "",
      render: (row) => (
        <button
          type="button"
          className="erp-secondary-button"
          onClick={() =>
            setSelectedId((current) => (current === row.id ? null : row.id))
          }
        >
          {selectedId === row.id ? "Kapat" : "Özellikler"}
        </button>
      ),
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title="Stok Kategorileri"
      description="Özellik şablonları, izin verilen birimler ve kategori tipleri"
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      <div className="erp-toolbar">
        <div>
          <strong>{categories.length} kategori</strong>
          <small>
            Kategori sistem genelidir — her şirkette aynı şablon kullanılır.
          </small>
        </div>

        <Button variant="secondary" disabled={loading} onClick={() => void load()}>
          Yenile
        </Button>
      </div>

      <DataTable
        rows={categories}
        columns={columns}
        rowKey={(row) => row.id}
        loading={loading}
        title="Stok Kategorileri"
        emptyText="Kategori bulunmuyor."
      />

      {selected && (
        <section className="erp-form-card">
          <h2>
            {selected.name} — özellikler
          </h2>

          {selected.kind === 1 ? (
            <p>
              <strong>SERBEST kategori.</strong> Ad elle yazılır, özellik
              şablonu yoktur ve mükerrer engeli uygulanmaz. Dekoratif
              aydınlatma ve özel imalat gibi her biri tekil ürünler için.
            </p>
          ) : selected.attributes.length === 0 ? (
            <p>Bu kategoride henüz özellik tanımlı değil.</p>
          ) : (
            <div className="erp-table-scroll">
              <table className="erp-data-table-grid">
                <thead>
                  <tr>
                    <th>Özellik</th>
                    <th>Kod</th>
                    <th>Zorunlu</th>
                    <th>Seçenekler</th>
                  </tr>
                </thead>

                <tbody>
                  {selected.attributes.map((attribute) => (
                    <tr key={attribute.id}>
                      <td>
                        <strong>{attribute.name}</strong>
                      </td>
                      <td>{attribute.code}</td>
                      <td>{attribute.isRequired ? "Evet" : "Hayır"}</td>
                      <td>
                        {attribute.options.map((option) => option.display).join(" · ")}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {!actions.can("manage") && (
            <small>
              Kategori ve özellik düzenlemek için depo yönetimi yetkisi gerekir.
            </small>
          )}
        </section>
      )}
    </ErpShell>
  );
}
