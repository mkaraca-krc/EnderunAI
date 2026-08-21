"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { money, quantity } from "@/lib/format/turkish";
import { usePermissions } from "@/lib/use-permissions";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import {
  inventoryService,
  type InventoryItemDetail,
  type InventoryItemType,
} from "@/services/inventory.service";
import {
  stockLevelService,
  type StockLevelRow,
} from "@/services/stock-level.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

const UNITS = ["Adet", "Metre", "Kg", "Takım", "Kutu", "Paket", "Rulo"];

const TYPE_LABELS: Record<number, string> = {
  0: "Stok malzemesi",
  1: "Sarf malzemesi",
  2: "Demirbaş",
};

/** CurrentAccountStatus.Approved */
const APPROVED_STATUS = 2;

export default function InventoryItemDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const { has } = usePermissions();

  const id = params?.id;

  const [item, setItem] = useState<InventoryItemDetail | null>(null);
  const [suppliers, setSuppliers] = useState<CurrentAccountListItem[]>([]);

  /*
   * ASGARİ/AZAMİ KARTTA DEĞİL, DEPODA (S8). Bir kart birden çok depoda
   * bulunabildiği ve her deponun eşiği farklı olduğu için tek alan
   * yerine satır listesi okunuyor.
   */
  const [levels, setLevels] = useState<StockLevelRow[]>([]);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [form, setForm] = useState({
    name: "",
    category: "",
    brand: "",
    model: "",
    unit: "Adet",
    barcode: "",
    type: "0",
    isActive: true,
    preferredSupplierCurrentAccountId: "",
    vatRate: "",
    description: "",
  });

  const canEdit = has("inventory.edit");

  const load = useCallback(async () => {
    if (!id) return;

    setLoading(true);
    setError("");

    try {
      const detail = await inventoryService.getItem(id);
      setItem(detail);

      setForm({
        name: detail.name,
        category: detail.category ?? "",
        brand: detail.brand ?? "",
        model: detail.model ?? "",
        unit: detail.unit,
        barcode: detail.barcode ?? "",
        type: String(detail.type),
        isActive: detail.isActive,
        preferredSupplierCurrentAccountId:
          detail.preferredSupplierCurrentAccountId ?? "",
        vatRate: detail.vatRate == null ? "" : String(detail.vatRate),
        description: detail.description ?? "",
      });

      setLevels(
        (await stockLevelService.list().catch(() => [] as StockLevelRow[]))
          .filter((level) => level.inventoryItemId === detail.id)
      );

      const accounts = await currentAccountService
        .getAll(detail.companyId)
        .catch(() => []);

      setSuppliers(
        accounts.filter((account) => account.status === APPROVED_STATUS)
      );
    } catch (err) {
      setItem(null);
      setLevels([]);
      setError(err instanceof Error ? err.message : "Malzeme kartı açılamadı.");
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 100);
    return () => window.clearTimeout(timer);
  }, [load]);

  const validationErrors: string[] = [];
  if (!form.name.trim()) validationErrors.push("Malzeme adı girin.");
  if (!form.unit.trim()) validationErrors.push("Birim seçin.");
  if (form.vatRate !== "") {
    const rate = Number(form.vatRate);
    if (!(rate >= 0 && rate <= 100)) {
      validationErrors.push("KDV oranı 0-100 arasında olmalı.");
    }
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();

    if (!id) return;

    if (validationErrors.length > 0) {
      setError(validationErrors.join(" "));
      return;
    }

    setSaving(true);
    setError("");
    setNotice("");

    try {
      await inventoryService.updateItem(id, {
        name: form.name.trim(),
        category: form.category.trim() || null,
        brand: form.brand.trim() || null,
        model: form.model.trim() || null,
        unit: form.unit.trim(),
        barcode: form.barcode.trim() || null,
        type: Number(form.type) as InventoryItemType,
        isActive: form.isActive,
        preferredSupplierCurrentAccountId:
          form.preferredSupplierCurrentAccountId || null,
        vatRate: form.vatRate === "" ? null : Number(form.vatRate),
        description: form.description.trim() || null,
      });

      setNotice("Malzeme kartı güncellendi.");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kart güncellenemedi.");
    } finally {
      setSaving(false);
    }
  }

  function update<K extends keyof typeof form>(
    key: K,
    value: (typeof form)[K]
  ) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  if (loading) {
    return (
      <ErpShell design="redwood" title="Malzeme Kartı" description="Kart yükleniyor">
        <div className="erp-loading">Yükleniyor...</div>
      </ErpShell>
    );
  }

  if (!item) {
    return (
      <ErpShell design="redwood" title="Malzeme Kartı" description="Kart bulunamadı">
        <div className="erp-alert error">
          {error || "Malzeme kartı bulunamadı."}
        </div>
        <Link className="erp-secondary-button" href="/depo-stok">
          ← Depo ve Stok
        </Link>
      </ErpShell>
    );
  }

  /*
   * KRİTİKLİK DEPO SEVİYESİNDEN (S8): kartın kendi asgarisi yok.
   * Kart birden çok depoda bulunabildiği için "kritik" tek bir kart
   * özelliği değil; hangi deponun eksik olduğu bilgisiyle anlamlı.
   */
  const criticalLevels = levels.filter((level) => level.isBelowMinimum);
  const critical = criticalLevels.length > 0;

  return (
    <ErpShell
      design="redwood"
      title={`${item.code} — ${item.name}`}
      description={`${TYPE_LABELS[item.type] ?? "Malzeme"} · ${item.companyName}`}
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert">{notice}</div>}

      <div className="erp-page-toolbar">
        <div>
          <strong>
            {quantity(item.totalStock)} {item.unit}
          </strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            Stok: {quantity(item.totalStock)} {item.unit}
            {" · "}
            Stok değeri: {money(item.stockValue)}
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          {critical && <span className="erp-status red">Kritik seviye</span>}
          {!item.isActive && <span className="erp-status gray">Pasif</span>}
          <Link className="erp-secondary-button" href="/depo-stok">
            ← Depo ve Stok
          </Link>
        </div>
      </div>

      <div className="erp-panel">
        <div className="erp-panel-header">
          <h2>Maliyet ve Tedarik</h2>
        </div>

        <div className="erp-detail-grid">
          <div>
            <span className="erp-stat-label">Ağırlıklı ortalama maliyet</span>
            <strong>{money(item.averageUnitCost)}</strong>
          </div>
          <div>
            <span className="erp-stat-label">Son alış fiyatı</span>
            <strong>
              {item.lastPurchasePrice == null
                ? "—"
                : money(item.lastPurchasePrice)}
            </strong>
            {item.lastPurchaseDate && (
              <small>{dateFormat.format(new Date(item.lastPurchaseDate))}</small>
            )}
          </div>
          <div>
            <span className="erp-stat-label">Tercih edilen tedarikçi</span>
            <strong>{item.preferredSupplierTitle ?? "—"}</strong>
          </div>
          <div>
            <span className="erp-stat-label">KDV oranı</span>
            <strong>{item.vatRate == null ? "—" : `%${item.vatRate}`}</strong>
          </div>
        </div>

        <p>
          Stok değeri ortalama maliyetten hesaplanır; son alış fiyatı yalnızca
          &quot;en son kaça aldık&quot; sorusunu cevaplar ve değerlemeye
          girmez.
        </p>
      </div>

      <div className="erp-table-card erp-mt">
        <div className="erp-table-header">
          <h2>Depo Dağılımı</h2>
        </div>

        {item.warehouses.length === 0 ? (
          <div className="erp-empty-state">
            <p>Bu malzeme henüz hiçbir depoda bulunmuyor.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Depo</th>
                  <th className="num">Miktar</th>
                  <th className="num">Asgari</th>
                  <th className="num">Azami</th>
                  <th className="num">Değer</th>
                </tr>
              </thead>
              <tbody>
                {item.warehouses.map((warehouse) => {
                  const level = levels.find(
                    (row) => row.warehouseId === warehouse.warehouseId
                  );

                  return (
                    <tr key={warehouse.warehouseId}>
                      <td>
                        <strong>{warehouse.warehouseName}</strong>
                        <small>{warehouse.warehouseCode}</small>
                      </td>
                      <td className="num">
                        {quantity(warehouse.quantity)} {item.unit}
                        {level?.isBelowMinimum ? (
                          <small className="rw-value-warning">
                            asgarinin altında
                          </small>
                        ) : null}
                      </td>
                      <td className="num">
                        {level ? quantity(level.minimumQuantity) : "—"}
                      </td>
                      <td className="num">
                        {level?.maximumQuantity != null
                          ? quantity(level.maximumQuantity)
                          : "—"}
                      </td>
                      <td className="num">
                        {money(warehouse.quantity * item.averageUnitCost)}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {canEdit && (
        <form className="erp-form-card erp-mt" onSubmit={submit}>
          <div className="erp-form-header">
            <h2>Kartı Düzenle</h2>
            <p>
              Malzeme kodu değiştirilemez: hareket belgelerinde geçtiği için
              geçmiş kayıtların izi kopardı.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Malzeme Adı *</span>
              <input
                type="text"
                value={form.name}
                onChange={(event) => update("name", event.target.value)}
              />
            </label>

            <label>
              <span>Malzeme Tipi</span>
              <select
                value={form.type}
                onChange={(event) => update("type", event.target.value)}
              >
                <option value="0">Stok malzemesi</option>
                <option value="1">Sarf malzemesi</option>
                <option value="2">Demirbaş</option>
              </select>
            </label>

            <label>
              <span>Kategori</span>
              <input
                type="text"
                value={form.category}
                onChange={(event) => update("category", event.target.value)}
              />
            </label>

            <label>
              <span>Birim *</span>
              <select
                value={form.unit}
                onChange={(event) => update("unit", event.target.value)}
              >
                {UNITS.map((unit) => (
                  <option key={unit} value={unit}>
                    {unit}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Marka</span>
              <input
                type="text"
                value={form.brand}
                onChange={(event) => update("brand", event.target.value)}
              />
            </label>

            <label>
              <span>Model</span>
              <input
                type="text"
                value={form.model}
                onChange={(event) => update("model", event.target.value)}
              />
            </label>

            <label>
              <span>Barkod</span>
              <input
                type="text"
                value={form.barcode}
                onChange={(event) => update("barcode", event.target.value)}
              />
            </label>

            <label>
              <span>KDV Oranı (%)</span>
              <input
                type="number"
                min="0"
                max="100"
                step="0.01"
                value={form.vatRate}
                onChange={(event) => update("vatRate", event.target.value)}
                placeholder="Örn. 20"
              />
            </label>

            <label>
              <span>Tercih Edilen Tedarikçi</span>
              <select
                value={form.preferredSupplierCurrentAccountId}
                onChange={(event) =>
                  update(
                    "preferredSupplierCurrentAccountId",
                    event.target.value
                  )
                }
              >
                <option value="">Seçilmedi</option>
                {suppliers.map((supplier) => (
                  <option key={supplier.id} value={supplier.id}>
                    {supplier.code} — {supplier.title}
                  </option>
                ))}
              </select>
              <small>
                Satın almayı kısıtlamaz; teklif isterken kime sorulacağını
                hatırlatır.
              </small>
            </label>


            <label className="erp-check-label">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(event) => update("isActive", event.target.checked)}
              />
              <span>Kart aktif</span>
            </label>

            <label className="span-2">
              <span>Açıklama</span>
              <input
                type="text"
                value={form.description}
                onChange={(event) => update("description", event.target.value)}
                placeholder="Teknik özellik, kullanım notu"
              />
            </label>
          </div>

          <div className="erp-form-actions">
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => router.push("/depo-stok")}
            >
              Vazgeç
            </button>

            <button type="submit" className="erp-primary-button" disabled={saving}>
              {saving ? "Kaydediliyor..." : "Kartı Güncelle"}
            </button>
          </div>
        </form>
      )}
    </ErpShell>
  );
}
