"use client";

import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { Button, EmptyState, Input } from "@/components/ui";
import { money, percent, unitPrice } from "@/lib/format/turkish";
import {
  retailPricingService,
  type RetailPricingRow,
} from "@/services/retail-sale.service";

type Draft = {
  salesPrice: string;
  maxDiscountRate: string;
};

/**
 * PERAKENDE FİYATLANDIRMA — YÖNETİM EKRANI.
 *
 * Satış ekranı maliyeti görmez; BURASI GÖRÜR ve görmesi şart. Satış
 * fiyatını ve iskonto tavanını maliyetten habersiz koymak, tavana
 * kadar iskonto yapan personelin farkında olmadan maliyet altına
 * satmasına yol açıyor. Tavanı koyan kişi marjı görmeli.
 *
 * Maliyet görünürlüğü sunucuda `inventory.view`e bağlı (stok
 * maliyetini bugün fiilen koruyan izin o); yoksa null gelir ve kaç
 * kalemde gizlendiği söylenir.
 */
/** Taslaktaki fiyat ve tavandan marjı hesaplar. */
function computeMargin(price: number | null, cost: number | null): number | null {
  if (price == null || cost == null || price === 0) return null;
  return ((price - cost) / price) * 100;
}

export default function RetailPricingPage() {
  const [rows, setRows] = useState<RetailPricingRow[]>([]);
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [hiddenCount, setHiddenCount] = useState(0);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const data = await retailPricingService.list(search);

      setRows(data.items);
      setHiddenCount(data.hiddenCount);
      setDrafts(
        Object.fromEntries(
          data.items.map((row) => [
            row.id,
            {
              salesPrice: row.salesPrice == null ? "" : String(row.salesPrice),
              maxDiscountRate: String(row.maxDiscountRate),
            },
          ]),
        ),
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fiyat listesi yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [search]);

  useEffect(() => {
    void load();
  }, [load]);

  const changed = rows.filter((row) => {
    const draft = drafts[row.id];
    if (!draft) return false;

    const price = draft.salesPrice.trim() === "" ? null : Number(draft.salesPrice);
    const cap = Number(draft.maxDiscountRate) || 0;

    return price !== (row.salesPrice ?? null) || cap !== row.maxDiscountRate;
  });

  async function save() {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      const result = await retailPricingService.save(
        changed.map((row) => {
          const draft = drafts[row.id];

          return {
            inventoryItemId: row.id,
            salesPrice: draft.salesPrice.trim() === "" ? null : Number(draft.salesPrice),
            maxDiscountRate: Number(draft.maxDiscountRate) || 0,
          };
        }),
      );

      setNotice(`${result.updated} kalem güncellendi.`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fiyatlar kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  /*
   * MARJ ve TAVAN HESABI TASLAKTAN OKUR: kullanıcı fiyatı değiştirdiği
   * anda sonucu görmeli, kaydettikten sonra değil.
   */
  function marginOf(row: (typeof rows)[number]): number | null {
    const draft = drafts[row.id];
    const price =
      draft && draft.salesPrice.trim() !== "" ? Number(draft.salesPrice) : null;

    return computeMargin(price, row.averageUnitCost ?? null);
  }

  function cappedOf(row: (typeof rows)[number]): {
    capped: number | null;
    belowCost: boolean;
  } {
    const draft = drafts[row.id];
    const price =
      draft && draft.salesPrice.trim() !== "" ? Number(draft.salesPrice) : null;
    const cap = Number(draft?.maxDiscountRate) || 0;
    const cost = row.averageUnitCost ?? null;

    const capped = price == null ? null : price * (1 - cap / 100);

    return {
      capped,
      belowCost: capped != null && cost != null && capped < cost,
    };
  }

  /*
   * SÜTUNLAR VERİ OLARAK (F4k).
   *
   * TOPLU DÜZENLEME TABLOSU: her satırda fiyat ve tavan girişi var.
   * Taslaklar satır KİMLİĞİNE göre `drafts` durumunda tutulduğu için
   * sayfa değiştirmek girilen değerleri kaybettirmiyor; "N kalemi
   * kaydet" düğmesi de tablonun dışında ve tüm taslakları görüyor.
   *
   * Dışa aktarmada girilen taslak değil KAYITLI değer yazılıyor:
   * dosyaya henüz kaydedilmemiş bir fiyatı yazmak, o fiyat geçerliymiş
   * gibi bir liste üretirdi.
   */
  const priceColumns: DataTableColumn<(typeof rows)[number]>[] = [
    { key: "kod", header: "Kod", value: (row) => row.code },
    { key: "urun", header: "Ürün", value: (row) => row.name },
    {
      key: "maliyet",
      header: "Maliyet",
      numeric: true,
      value: (row) => (row.averageUnitCost == null ? "—" : unitPrice(row.averageUnitCost)),
    },
    {
      key: "fiyat",
      header: "Satış Fiyatı",
      value: (row) =>
        row.salesPrice == null ? "satışa kapalı" : unitPrice(row.salesPrice),
      render: (row) => {
        const draft = drafts[row.id] ?? { salesPrice: "", maxDiscountRate: "0" };

        return (
          <Input
            value={draft.salesPrice}
            placeholder="satışa kapalı"
            onChange={(event) =>
              setDrafts((current) => ({
                ...current,
                [row.id]: { ...draft, salesPrice: event.target.value },
              }))
            }
          />
        );
      },
    },
    {
      key: "tavan",
      header: "Tavan %",
      value: (row) => String(row.maxDiscountRate ?? 0),
      render: (row) => {
        const draft = drafts[row.id] ?? { salesPrice: "", maxDiscountRate: "0" };

        return (
          <Input
            value={draft.maxDiscountRate}
            onChange={(event) =>
              setDrafts((current) => ({
                ...current,
                [row.id]: { ...draft, maxDiscountRate: event.target.value },
              }))
            }
          />
        );
      },
    },
    {
      key: "marj",
      header: "Marj",
      numeric: true,
      value: (row) => {
        const margin = marginOf(row);
        return margin == null ? "—" : percent(margin);
      },
      render: (row) => {
        const margin = marginOf(row);

        return margin == null ? (
          "—"
        ) : (
          <span className={margin < 0 ? "rw-value-danger" : undefined}>
            {percent(margin)}
          </span>
        );
      },
    },
    {
      key: "tavanli",
      header: "Tavan uygulanınca",
      /*
       * ASIL DEĞER BU HESAP: kart tek başına sağlıklı görünürken
       * (marj %30) tavan yüksek konmuşsa (%40), personel tavana kadar
       * indirim yapınca maliyetin altına düşüyor. Burada fiyat
       * girilirken söyleniyor — satış olduktan sonra değil.
       */
      value: (row) => {
        const { capped, belowCost } = cappedOf(row);
        if (capped == null) return "—";
        return belowCost
          ? `${money(capped)} · maliyet altı satış mümkün`
          : money(capped);
      },
      render: (row) => {
        const { capped, belowCost } = cappedOf(row);

        if (capped == null) return "—";

        return (
          <>
            {money(capped)}
            {belowCost && (
              <small className="rw-value-danger" style={{ display: "block" }}>
                Bu tavanla maliyet altı satış mümkün
              </small>
            )}
          </>
        );
      },
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title="Perakende Fiyatlandırma"
      description="Satış fiyatı ve iskonto tavanı — marj ve maliyet altı uyarısıyla"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {hiddenCount > 0 && (
        <div className="erp-alert warning">
          {hiddenCount} kalemde maliyet gizli — marj ve maliyet altı uyarısı
          hesaplanamıyor. Maliyeti görmek için stok görüntüleme yetkisi gerekir.
        </div>
      )}

      <section className="erp-panel">
        <div className="erp-panel-header">
          <h2>Stok Kartları</h2>
          <p>
            Satış fiyatı boş bırakılan kalem perakende satışa kapalıdır.
            {changed.length > 0 && ` · ${changed.length} kalemde değişiklik var`}
          </p>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>Ara (kod, ad, barkod)</span>
            <Input value={search} onChange={(event) => setSearch(event.target.value)} />
          </label>
        </div>

        {loading ? (
          <div className="erp-loading">Fiyat listesi yükleniyor...</div>
        ) : rows.length === 0 ? (
          <EmptyState title="Kalem yok" description="Aramaya uyan stok kartı bulunamadı." />
        ) : (
          <DataTable
            rows={rows}
            columns={priceColumns}
            rowKey={(row) => row.id}
            title="Perakende Fiyat Listesi"
            resetKey={search}
          />
        )}

        <div className="erp-toolbar rw-toolbar-end erp-mt">
          <Button disabled={saving || changed.length === 0} onClick={() => void save()}>
            {changed.length === 0 ? "Değişiklik yok" : `${changed.length} kalemi kaydet`}
          </Button>
        </div>
      </section>
    </ErpShell>
  );
}
