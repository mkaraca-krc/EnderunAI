"use client";

import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
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
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Kod</th>
                  <th>Ürün</th>
                  <th style={{ textAlign: "right" }}>Maliyet</th>
                  <th style={{ width: 150 }}>Satış Fiyatı</th>
                  <th style={{ width: 120 }}>Tavan %</th>
                  <th style={{ textAlign: "right" }}>Marj</th>
                  <th>Tavan uygulanınca</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => {
                  const draft = drafts[row.id] ?? { salesPrice: "", maxDiscountRate: "0" };
                  const price = draft.salesPrice.trim() === "" ? null : Number(draft.salesPrice);
                  const cap = Number(draft.maxDiscountRate) || 0;
                  const cost = row.averageUnitCost;

                  /*
                   * ASIL DEĞER BU HESAP: kart tek başına sağlıklı
                   * görünürken (marj %30) tavan yüksek konmuşsa
                   * (%40), personel tavana kadar indirim yapınca
                   * maliyetin altına düşüyor. Burada fiyat girilirken
                   * söyleniyor — satış olduktan sonra değil.
                   */
                  const cappedPrice = price == null ? null : price * (1 - cap / 100);
                  const margin =
                    price == null || cost == null || price === 0
                      ? null
                      : ((price - cost) / price) * 100;
                  const belowCost =
                    cappedPrice != null && cost != null && cappedPrice < cost;

                  return (
                    <tr key={row.id}>
                      <td>{row.code}</td>
                      <td>{row.name}</td>
                      <td style={{ textAlign: "right" }}>
                        {cost == null ? "—" : unitPrice(cost)}
                      </td>
                      <td>
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
                      </td>
                      <td>
                        <Input
                          value={draft.maxDiscountRate}
                          onChange={(event) =>
                            setDrafts((current) => ({
                              ...current,
                              [row.id]: { ...draft, maxDiscountRate: event.target.value },
                            }))
                          }
                        />
                      </td>
                      <td style={{ textAlign: "right" }}>
                        {margin == null ? (
                          "—"
                        ) : (
                          <span className={margin < 0 ? "rw-value-danger" : undefined}>
                            {percent(margin)}
                          </span>
                        )}
                      </td>
                      <td>
                        {cappedPrice == null ? (
                          "—"
                        ) : (
                          <>
                            {money(cappedPrice)}
                            {belowCost && (
                              <small className="rw-value-danger" style={{ display: "block" }}>
                                Bu tavanla maliyet altı satış mümkün
                              </small>
                            )}
                          </>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
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
