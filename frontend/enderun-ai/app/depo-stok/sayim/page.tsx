"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import {
  inventoryMovementService,
  type SelectOption,
} from "@/services/inventory-movement.service";

export default function StockCountPage() {
  const router = useRouter();

  const [warehouses, setWarehouses] = useState<SelectOption[]>([]);
  const [items, setItems] = useState<SelectOption[]>([]);

  const [warehouseId, setWarehouseId] = useState("");
  const [inventoryItemId, setInventoryItemId] = useState("");
  const [currentQuantity, setCurrentQuantity] = useState<number | null>(null);
  const [countedQuantity, setCountedQuantity] = useState("");
  const [movementDate, setMovementDate] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [description, setDescription] = useState("");

  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    void Promise.all([
      inventoryMovementService.getWarehouses().then(setWarehouses),
      inventoryMovementService.getItems().then(setItems),
    ]).catch((err) =>
      setError(err instanceof Error ? err.message : "Veriler yüklenemedi.")
    );
  }, []);

  const loadCurrentQuantity = useCallback(async () => {
    if (!warehouseId || !inventoryItemId) {
      setCurrentQuantity(null);
      return;
    }

    try {
      const stocks = await inventoryMovementService.getWarehouseStocks(
        warehouseId
      );

      const found = stocks.find(
        (stock) => stock.inventoryItemId === inventoryItemId
      );

      // Kayıt yoksa 0: o malzeme bu depoda hiç bulunmamış demektir,
      // "bilinmiyor" değil.
      setCurrentQuantity(found?.quantity ?? 0);
    } catch {
      setCurrentQuantity(null);
    }
  }, [warehouseId, inventoryItemId]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadCurrentQuantity(), 100);
    return () => window.clearTimeout(timer);
  }, [loadCurrentQuantity]);

  const delta =
    countedQuantity !== "" && currentQuantity !== null
      ? Number(countedQuantity) - currentQuantity
      : null;

  const validationErrors: string[] = [];
  if (!warehouseId) validationErrors.push("Depo seçin.");
  if (!inventoryItemId) validationErrors.push("Malzeme seçin.");
  if (countedQuantity === "") validationErrors.push("Sayılan miktarı girin.");
  else if (!(Number(countedQuantity) >= 0)) {
    validationErrors.push("Sayılan miktar negatif olamaz.");
  }
  if (!movementDate) validationErrors.push("Sayım tarihi girin.");
  // Gerekçe ZORUNLU: sayım düzeltmesi, belgeye bağlı olmadan stok
  // değiştirebilen tek yol. Ne olduğu yazılmazsa serbest giriş kapısı
  // arkadan açılır. Uç de aynı kuralı uyguluyor.
  if (!description.trim()) {
    validationErrors.push("Düzeltme gerekçesi zorunludur.");
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();

    if (validationErrors.length > 0) {
      setError(validationErrors.join(" "));
      return;
    }

    setError("");
    setNotice("");
    setSaving(true);

    try {
      const result = await inventoryMovementService.adjustment({
        warehouseId,
        inventoryItemId,
        countedQuantity: Number(countedQuantity),
        movementDate,
        description: description.trim(),
      });

      setNotice(
        `Kaydedildi — belge no: ${result.referenceNumber}, ` +
          `fark: ${result.delta > 0 ? "+" : ""}${result.delta}, ` +
          `yeni miktar: ${result.newQuantity}`
      );

      setCurrentQuantity(result.newQuantity);
      setCountedQuantity("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşlem kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Stok Sayımı / Düzeltme"
      description="Fiziksel sayım sonucunu girin; fark otomatik hesaplanıp düzeltme hareketi olarak kaydedilir"
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert">{notice}</div>}

      <form className="erp-form-card" onSubmit={submit}>
        <div className="erp-form-header">
          <h2>Sayım Bilgileri</h2>
          <p>
            Sistemdeki miktar ile sayılan miktar arasındaki fark, izi sürülebilir
            bir düzeltme hareketi olarak deftere yazılır.
          </p>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>Depo *</span>
            <select
              value={warehouseId}
              onChange={(event) => setWarehouseId(event.target.value)}
            >
              <option value="">Seçin</option>
              {warehouses.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.code ? `${option.code} — ` : ""}
                  {option.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Malzeme *</span>
            <select
              value={inventoryItemId}
              onChange={(event) => setInventoryItemId(event.target.value)}
            >
              <option value="">Seçin</option>
              {items.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.code ? `${option.code} — ` : ""}
                  {option.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Sayılan (fiziksel) Miktar *</span>
            <input
              type="number"
              min="0"
              step="0.01"
              value={countedQuantity}
              onChange={(event) => setCountedQuantity(event.target.value)}
            />
          </label>

          <label>
            <span>Sayım Tarihi *</span>
            <input
              type="date"
              value={movementDate}
              onChange={(event) => setMovementDate(event.target.value)}
            />
          </label>

          <label className="span-2">
            <span>Düzeltme Gerekçesi *</span>
            <input
              type="text"
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              placeholder="Fire, kayıp, hatalı giriş, sayım ekibi notu…"
            />
            <small>
              Farkın nedeni yazılmadan düzeltme kaydedilmez.
            </small>
          </label>
        </div>

        <div className="erp-detail-grid">
          <div>
            <span className="erp-stat-label">Sistemdeki mevcut miktar</span>
            <strong>{currentQuantity === null ? "—" : currentQuantity}</strong>
          </div>

          <div>
            <span className="erp-stat-label">Fark</span>
            <strong>
              {delta === null ? (
                "—"
              ) : (
                <span
                  className={`erp-status ${
                    delta === 0 ? "gray" : delta > 0 ? "green" : "yellow"
                  }`}
                >
                  {delta > 0 ? "+" : ""}
                  {delta}
                  {delta > 0 ? " (fazla)" : delta < 0 ? " (eksik)" : ""}
                </span>
              )}
            </strong>
          </div>
        </div>

        <div className="erp-form-actions">
          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => router.push("/depo-stok/hareketler")}
          >
            Vazgeç
          </button>

          <button type="submit" className="erp-primary-button" disabled={saving}>
            {saving ? "Kaydediliyor..." : "Düzeltmeyi Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
