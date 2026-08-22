"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button } from "@/components/ui";
import { formatLocation, itemQrTarget, toDataUrl } from "@/lib/inventory/qr";
import {
  inventoryService,
  type InventoryItemListItem,
} from "@/services/inventory.service";
import { foldTurkish } from "@/lib/search/fold";

/**
 * A4 ETİKET ÇIKTISI — QR + ad + kod + konum.
 *
 * Tek malzeme ya da toplu seçim. Yazdırma stili global
 * `@media print` bloğundan geliyor (F2): menü, kenar çubuğu ve
 * düğmeler kâğıda basılmaz.
 *
 * QR'lar ÖNCEDEN üretilip veri URL'i olarak gömülüyor: yazdırma
 * penceresi açıldığında tarayıcı bekleyemez, o an ekranda ne varsa
 * onu basar. Sonradan üretilseydi etiketler boş çıkardı.
 */
export default function InventoryLabelsPage() {
  const [items, setItems] = useState<InventoryItemListItem[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [qrCodes, setQrCodes] = useState<Record<string, string>>({});
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [preparing, setPreparing] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setItems(await inventoryService.getItems());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Malzemeler yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const visible = useMemo(() => {
    const term = foldTurkish(search);
    if (!term) return items;

    return items.filter((item) =>
      foldTurkish(`${item.code} ${item.name}`).includes(term)
    );
  }, [items, search]);

  const chosen = useMemo(
    () => items.filter((item) => selected.has(item.id)),
    [items, selected]
  );

  function toggle(id: string) {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function prepareAndPrint() {
    if (chosen.length === 0) return;

    setPreparing(true);
    setError("");

    try {
      const origin = window.location.origin;
      const codes: Record<string, string> = {};

      for (const item of chosen) {
        codes[item.id] = await toDataUrl(itemQrTarget(origin, item.id));
      }

      setQrCodes(codes);

      // QR'lar ekrana basıldıktan SONRA yazdır: `print()` o an
      // görüneni basar, bekleyemez.
      window.requestAnimationFrame(() =>
        window.requestAnimationFrame(() => window.print())
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "QR üretilemedi.");
    } finally {
      setPreparing(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Etiket Yazdır"
      description="QR + ad + kod + konum — tek malzeme veya toplu seçim"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <div className="erp-toolbar no-print">
        <div>
          <strong>{chosen.length} malzeme seçildi</strong>
          <small>Seçtiklerinizin etiketi A4 sayfaya sığacak şekilde dizilir.</small>
        </div>

        <div className="erp-actions">
          <input
            className="erp-input"
            type="search"
            placeholder="Kod veya ad ara"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />

          <Button
            variant="secondary"
            disabled={loading}
            onClick={() => setSelected(new Set(visible.map((item) => item.id)))}
          >
            Görünenleri Seç
          </Button>

          <Button variant="secondary" onClick={() => setSelected(new Set())}>
            Temizle
          </Button>

          {/* Etiket seçerken başka kullanıcı kart açmış olabilir. */}
          <Button variant="secondary" disabled={loading} onClick={() => void load()}>
            Yenile
          </Button>

          <button
            type="button"
            className="erp-primary-button"
            disabled={chosen.length === 0 || preparing}
            onClick={() => void prepareAndPrint()}
          >
            {preparing ? "Hazırlanıyor…" : "Etiketleri Yazdır"}
          </button>
        </div>
      </div>

      <div className="erp-table-scroll no-print">
        <table className="erp-data-table-grid">
          <thead>
            <tr>
              <th />
              <th>Kod</th>
              <th>Malzeme</th>
              <th>Konum</th>
            </tr>
          </thead>

          <tbody>
            {visible.map((item) => (
              <tr key={item.id}>
                <td>
                  <input
                    type="checkbox"
                    checked={selected.has(item.id)}
                    onChange={() => toggle(item.id)}
                    aria-label={`${item.name} seç`}
                  />
                </td>
                <td>{item.code}</td>
                <td>{item.name}</td>
                <td>
                  {formatLocation(item.zoneName, item.shelfCode, item.levelCode)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* ETİKET SAYFASI — yalnız kâğıtta görünür. */}
      <div className="print-only erp-label-sheet">
        {chosen.map((item) => (
          <div key={item.id} className="erp-label">
            {qrCodes[item.id] && (
              /* eslint-disable-next-line @next/next/no-img-element */
              <img src={qrCodes[item.id]} alt="" className="erp-label-qr" />
            )}

            <div className="erp-label-body">
              <strong>{item.name}</strong>
              <span className="erp-label-code">{item.code}</span>
              <span className="erp-label-location">
                {formatLocation(item.zoneName, item.shelfCode, item.levelCode)}
              </span>
            </div>
          </div>
        ))}
      </div>
    </ErpShell>
  );
}
