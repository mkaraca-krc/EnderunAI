"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

import { usePermissions } from "@/lib/use-permissions";
import { unitPrice as formatUnitPrice } from "@/lib/format/turkish";
import {
  POSITION_PRICE_INSTITUTION_LABELS,
  PositionPriceInstitution,
  positionPriceService,
  type PositionPriceRow,
} from "@/services/engineering-position.service";


const dateFormat = new Intl.DateTimeFormat("tr-TR");

/**
 * Pozun yıl/kurum bazlı birim fiyat geçmişi.
 *
 * Fiyat poz kaydının üstüne yazılmaz, satır olarak eklenir: ÇŞB 2024,
 * ÇŞB 2025 ve TEDAŞ 2025 yan yana durur. Eski bir teklif hangi kitapla
 * hesaplandıysa o rakamla açıklanabilmeli, bu yüzden geçmiş silinmez.
 */
export default function PositionPriceHistory({
  positionId,
}: {
  positionId: string;
}) {
  const { has } = usePermissions();
  const canManage = has("engineering.manage");

  const [rows, setRows] = useState<PositionPriceRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [saving, setSaving] = useState(false);
  const [reloadToken, setReloadToken] = useState(0);

  const [year, setYear] = useState(new Date().getFullYear());
  const [institution, setInstitution] = useState<number>(
    PositionPriceInstitution.Csb
  );
  const [unitPrice, setUnitPrice] = useState("");
  const [sourceNote, setSourceNote] = useState("");

  const fetchRows = useCallback(
    () => positionPriceService.getHistory(positionId),
    [positionId]
  );

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const data = await fetchRows();
        if (cancelled) return;

        setRows(data);
        setError("");
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Fiyatlar alınamadı.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [fetchRows, reloadToken]);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    setSaving(true);
    setError("");
    setNotice("");

    try {
      await positionPriceService.upsert(positionId, {
        year,
        institution,
        unitPrice: Number(unitPrice),
        currencyCode: "TRY",
        effectiveFrom: null,
        sourceNote: sourceNote.trim() || null,
      });

      setUnitPrice("");
      setSourceNote("");
      setNotice("Fiyat kaydedildi.");
      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fiyat kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(priceId: string) {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      await positionPriceService.remove(priceId);
      setNotice("Fiyat kaydı silindi.");
      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fiyat silinemedi.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {canManage && (
        <form
          onSubmit={handleSubmit}
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(5, minmax(0, 1fr))",
            gap: 12,
            alignItems: "end",
            marginBottom: 20,
          }}
        >
          <label>
            <span>Yıl *</span>
            <input
              className="erp-input"
              type="number"
              required
              min={2000}
              max={2100}
              value={year}
              onChange={(event) => setYear(Number(event.target.value))}
            />
          </label>

          <label>
            <span>Kurum *</span>
            <select
              className="erp-input"
              value={institution}
              onChange={(event) => setInstitution(Number(event.target.value))}
            >
              {Object.entries(POSITION_PRICE_INSTITUTION_LABELS).map(
                ([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                )
              )}
            </select>
          </label>

          <label>
            <span>Birim Fiyat (TL) *</span>
            <input
              className="erp-input"
              type="number"
              required
              step="0.0001"
              min="0"
              value={unitPrice}
              onChange={(event) => setUnitPrice(event.target.value)}
            />
          </label>

          <label>
            <span>Kaynak / Kitap</span>
            <input
              className="erp-input"
              value={sourceNote}
              placeholder="ÇŞB 2025 Birim Fiyat Kitabı"
              onChange={(event) => setSourceNote(event.target.value)}
            />
          </label>

          <button type="submit" className="erp-primary-button" disabled={saving}>
            {saving ? "Kaydediliyor..." : "Ekle / Güncelle"}
          </button>
        </form>
      )}

      {loading ? (
        <div className="erp-loading">Fiyatlar yükleniyor...</div>
      ) : rows.length === 0 ? (
        <div className="erp-empty-state">
          <strong>Bu poza tanımlı birim fiyat yok</strong>
          <p>
            Keşifte bu pozu fiyatlandırmak için ya yıllık birim fiyat girin ya
            da reçete analizini kullanın.
          </p>
        </div>
      ) : (
        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Yıl</th>
                <th>Kurum</th>
                <th>Birim Fiyat</th>
                <th>Yürürlük</th>
                <th>Kaynak</th>
                {canManage && <th>İşlem</th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td>
                    <strong>{row.year}</strong>
                  </td>
                  <td>{row.institutionName}</td>
                  <td>
                    {formatUnitPrice(row.unitPrice, row.currencyCode)}
                  </td>
                  <td>
                    {row.effectiveFrom
                      ? dateFormat.format(new Date(row.effectiveFrom))
                      : "yıl geneli"}
                  </td>
                  <td>{row.sourceNote || "—"}</td>
                  {canManage && (
                    <td>
                      <button
                        type="button"
                        className="erp-secondary-button"
                        disabled={saving}
                        onClick={() => void handleDelete(row.id)}
                      >
                        Sil
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
