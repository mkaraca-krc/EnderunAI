"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import {
  inventoryMovementService,
  type SelectOption,
} from "@/services/inventory-movement.service";

export default function SayimPage() {
  const [warehouses, setWarehouses] = useState<SelectOption[]>([]);
  const [items, setItems] = useState<SelectOption[]>([]);
  const [warehouseId, setWarehouseId] = useState("");
  const [inventoryItemId, setInventoryItemId] = useState("");
  const [currentQuantity, setCurrentQuantity] = useState<number | null>(null);
  const [countedQuantity, setCountedQuantity] = useState<number | "">("");
  const [movementDate, setMovementDate] = useState(new Date().toISOString().slice(0, 10));
  const [description, setDescription] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    void Promise.all([
      inventoryMovementService.getWarehouses().then(setWarehouses),
      inventoryMovementService.getItems().then(setItems),
    ]).catch((e) => setError(e instanceof Error ? e.message : "Veriler yüklenemedi."));
  }, []);

  useEffect(() => {
    setCurrentQuantity(null);
    if (!warehouseId || !inventoryItemId) return;

    let cancelled = false;
    (async () => {
      try {
        const backendPath = `warehouses/${warehouseId}/stocks`;
        const response = await fetch(`/api/backend/inventory/${backendPath}`, {
          credentials: "include",
        });
        if (!response.ok) return;
        const stocks = (await response.json()) as { inventoryItemId: string; quantity: number }[];
        const found = stocks.find((s) => s.inventoryItemId === inventoryItemId);
        if (!cancelled) setCurrentQuantity(found?.quantity ?? 0);
      } catch {
        if (!cancelled) setCurrentQuantity(null);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [warehouseId, inventoryItemId]);

  const delta =
    typeof countedQuantity === "number" && currentQuantity !== null
      ? countedQuantity - currentQuantity
      : null;

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setNotice("");

    if (!warehouseId || !inventoryItemId || countedQuantity === "") {
      setError("Depo, malzeme ve sayılan miktar zorunludur.");
      return;
    }

    try {
      setSaving(true);
      const result = await inventoryMovementService.adjustment({
        warehouseId,
        inventoryItemId,
        countedQuantity,
        movementDate,
        description: description || undefined,
      });
      setNotice(
        `Kaydedildi — belge no: ${result.referenceNumber}, fark: ${result.delta > 0 ? "+" : ""}${result.delta}, yeni miktar: ${result.newQuantity}`
      );
      setCurrentQuantity(result.newQuantity);
      setCountedQuantity("");
    } catch (e) {
      setError(e instanceof Error ? e.message : "İşlem kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6">
      <div>
        <Link href="/depo-stok/hareketler" className="text-sm font-medium text-slate-600">
          ← Stok hareketleri
        </Link>
        <h1 className="mt-2 text-2xl font-semibold">Sayım / Düzeltme</h1>
        <p className="text-sm text-slate-500">
          Fiziksel sayımda bulunan gerçek miktarı girin; sistemdeki miktarla farkı otomatik hesaplanıp bir düzeltme
          hareketi olarak kaydedilir.
        </p>
      </div>

      <form onSubmit={submit} className="space-y-5 rounded-xl border bg-white p-6 shadow-sm">
        {error && <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">{error}</div>}
        {notice && (
          <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-700">
            {notice}
          </div>
        )}

        <div className="grid gap-4 md:grid-cols-2">
          <label className="block space-y-2">
            <span className="text-sm font-medium">Depo</span>
            <select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} className="w-full rounded-lg border px-3 py-2">
              <option value="">Seçin</option>
              {warehouses.map((x) => (
                <option key={x.id} value={x.id}>
                  {x.code ? `${x.code} - ` : ""}
                  {x.name}
                </option>
              ))}
            </select>
          </label>

          <label className="block space-y-2">
            <span className="text-sm font-medium">Malzeme</span>
            <select value={inventoryItemId} onChange={(e) => setInventoryItemId(e.target.value)} className="w-full rounded-lg border px-3 py-2">
              <option value="">Seçin</option>
              {items.map((x) => (
                <option key={x.id} value={x.id}>
                  {x.code ? `${x.code} - ` : ""}
                  {x.name}
                </option>
              ))}
            </select>
          </label>

          <div className="rounded-lg border bg-slate-50 px-3 py-2 text-sm">
            <span className="block text-slate-500">Sistemdeki mevcut miktar</span>
            <strong>{currentQuantity === null ? "—" : currentQuantity}</strong>
          </div>

          <label className="block space-y-2">
            <span className="text-sm font-medium">Sayılan (fiziksel) miktar</span>
            <input
              type="number"
              min="0"
              step="0.01"
              value={countedQuantity}
              onChange={(e) => setCountedQuantity(e.target.value === "" ? "" : Number(e.target.value))}
              className="w-full rounded-lg border px-3 py-2"
            />
          </label>

          <label className="block space-y-2">
            <span className="text-sm font-medium">Sayım tarihi</span>
            <input type="date" value={movementDate} onChange={(e) => setMovementDate(e.target.value)} className="w-full rounded-lg border px-3 py-2" />
          </label>

          {delta !== null && (
            <div
              className={`rounded-lg border px-3 py-2 text-sm ${delta === 0 ? "border-slate-200 bg-slate-50" : delta > 0 ? "border-emerald-200 bg-emerald-50 text-emerald-700" : "border-amber-200 bg-amber-50 text-amber-700"}`}
            >
              <span className="block text-slate-500">Fark</span>
              <strong>
                {delta > 0 ? "+" : ""}
                {delta} {delta > 0 ? "(fazla)" : delta < 0 ? "(eksik)" : ""}
              </strong>
            </div>
          )}
        </div>

        <label className="block space-y-2">
          <span className="text-sm font-medium">Açıklama</span>
          <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={4} className="w-full rounded-lg border px-3 py-2" />
        </label>

        <div className="flex justify-end gap-3 border-t pt-4">
          <Link href="/depo-stok/hareketler" className="rounded-lg border px-4 py-2 text-sm">
            İptal
          </Link>
          <button disabled={saving} className="rounded-lg bg-slate-950 px-5 py-2 text-sm text-white">
            {saving ? "Kaydediliyor..." : "Düzeltmeyi Kaydet"}
          </button>
        </div>
      </form>
    </div>
  );
}
