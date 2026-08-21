"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Badge, Button, EmptyState, Input, Select } from "@/components/ui";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { useModuleActions } from "@/lib/auth/module-actions";
import { usePermissions } from "@/lib/use-permissions";
import { amount, money } from "@/lib/format/turkish";
import {
  inventoryService,
  type InventoryItemListItem,
} from "@/services/inventory.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  stockLevelService,
  type StockLevelRow,
} from "@/services/stock-level.service";
import {
  warehouseService,
  type WarehouseListItem,
} from "@/services/warehouse.service";

/**
 * ASGARİ / AZAMİ STOK SEVİYELERİ ve satın alma talebi önerisi.
 *
 * Seviye DEPOYA ait: merkez deposunda bulundurulacak asgari ile biten
 * bir şantiye deposununki aynı sayı olamaz. Bu yüzden ekran önce depo
 * seçtiriyor, tanımlar da uyarılar da o deponun.
 *
 * ÖNERİ MİKTARI = AZAMİ − MEVCUT. Azami tanımlı değilse öneri
 * ÜRETİLMEZ ve satır seçilemez: kaç adet alınacağı işletme kararıdır,
 * uydurma bir katsayıyla tahmin edilmez.
 */
export default function StockLevelsPage() {
  const actions = useModuleActions("inventory");
  const { has } = usePermissions();

  const canEditLevels = actions.can("edit");
  const canCreateRequest = has("purchasing-requests.create");

  const [warehouses, setWarehouses] = useState<WarehouseListItem[]>([]);
  const [items, setItems] = useState<InventoryItemListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [levels, setLevels] = useState<StockLevelRow[]>([]);

  const [warehouseId, setWarehouseId] = useState("");
  const [belowOnly, setBelowOnly] = useState(false);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  // Yeni/güncel seviye tanımı
  const [formItemId, setFormItemId] = useState("");
  const [formMinimum, setFormMinimum] = useState("");
  const [formMaximum, setFormMaximum] = useState("");
  const [formNote, setFormNote] = useState("");
  const [saving, setSaving] = useState(false);

  // Talep önerisi
  const [selected, setSelected] = useState<Record<string, string>>({});
  const [projectId, setProjectId] = useState("");
  const [requestedByName, setRequestedByName] = useState("");
  const [priority, setPriority] = useState("2");
  const [neededByDate, setNeededByDate] = useState("");
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [warehouseList, itemList, projectList] = await Promise.all([
        warehouseService.getAll(),
        inventoryService.getItems(),
        projectService.getAll().catch(() => [] as ProjectListItem[]),
      ]);

      setWarehouses(warehouseList);
      setItems(itemList);
      setProjects(projectList);
      setWarehouseId((current) => current || warehouseList[0]?.id || "");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Depo verileri alınamadı.");
    } finally {
      setLoading(false);
    }
  }, []);

  const loadLevels = useCallback(async () => {
    if (!warehouseId) {
      setLevels([]);
      return;
    }

    try {
      setLevels(
        await stockLevelService.list({
          warehouseId,
          belowMinimumOnly: belowOnly,
        })
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Stok seviyeleri alınamadı.");
    }
  }, [belowOnly, warehouseId]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    void loadLevels();
    setSelected({});
  }, [loadLevels]);

  const warehouse = warehouses.find((row) => row.id === warehouseId);

  /** Yalnız seçili deponun şirketindeki kartlar tanımlanabilir. */
  const selectableItems = useMemo(
    () =>
      items.filter(
        (item) => item.isActive && (!warehouse || item.companyId === warehouse.companyId)
      ),
    [items, warehouse]
  );

  const companyProjects = useMemo(
    () =>
      projects.filter((project) => !warehouse || project.companyId === warehouse.companyId),
    [projects, warehouse]
  );

  const belowLevels = levels.filter((row) => row.isBelowMinimum);

  /**
   * Öneri satırı seçilebilir mi. Azami tanımsızsa miktar hesaplanamaz;
   * satır listede kalır ama seçilemez — eksik olanın ne olduğu
   * gizlenmeden gösterilir.
   */
  function selectable(row: StockLevelRow): boolean {
    return row.isBelowMinimum && row.suggestedQuantity != null;
  }

  function toggle(row: StockLevelRow) {
    setSelected((current) => {
      const next = { ...current };

      if (next[row.inventoryItemId] !== undefined) {
        delete next[row.inventoryItemId];
      } else {
        next[row.inventoryItemId] = String(row.suggestedQuantity ?? 0);
      }

      return next;
    });
  }

  async function saveLevel() {
    if (!warehouseId || !formItemId) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      await stockLevelService.save({
        warehouseId,
        inventoryItemId: formItemId,
        minimumQuantity: Number(formMinimum),
        maximumQuantity: formMaximum.trim() === "" ? null : Number(formMaximum),
        note: formNote.trim() || null,
      });

      setFormItemId("");
      setFormMinimum("");
      setFormMaximum("");
      setFormNote("");
      setNotice("Stok seviyesi kaydedildi.");
      await loadLevels();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Stok seviyesi kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function removeLevel(row: StockLevelRow) {
    setError("");
    setNotice("");

    try {
      await stockLevelService.remove(row.id);
      setNotice(`${row.itemName} için seviye takibi kaldırıldı.`);
      await loadLevels();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Seviye takibi kaldırılamadı.");
    }
  }

  async function createRequest() {
    const lines = Object.entries(selected)
      .map(([inventoryItemId, value]) => ({
        inventoryItemId,
        quantity: Number(value),
      }))
      .filter((line) => line.quantity > 0);

    if (!warehouseId || !projectId || lines.length === 0) return;

    setCreating(true);
    setError("");
    setNotice("");

    try {
      const result = await stockLevelService.createPurchaseRequest({
        warehouseId,
        projectId,
        requestedByName: requestedByName.trim(),
        priority: Number(priority),
        neededByDate: neededByDate || null,
        lines,
      });

      setSelected({});
      setNotice(
        `${result.requestNumber} numaralı satın alma talebi ${result.lineCount} kalemle oluşturuldu.`
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Satın alma talebi oluşturulamadı.");
    } finally {
      setCreating(false);
    }
  }

  const columns: DataTableColumn<StockLevelRow>[] = [
    { key: "kod", header: "Kod", value: (row) => row.itemCode },
    { key: "malzeme", header: "Malzeme", value: (row) => row.itemName },
    {
      key: "mevcut",
      header: "Mevcut",
      numeric: true,
      value: (row) => `${amount(row.currentQuantity)} ${row.unit}`,
      render: (row) => (
        <>
          {amount(row.currentQuantity)} {row.unit}
          {row.isDepleted ? (
            <Badge variant="danger">tükendi</Badge>
          ) : row.isBelowMinimum ? (
            <Badge variant="warning">asgari altı</Badge>
          ) : null}
        </>
      ),
    },
    {
      key: "asgari",
      header: "Asgari",
      numeric: true,
      value: (row) => amount(row.minimumQuantity),
    },
    {
      key: "azami",
      header: "Azami",
      numeric: true,
      value: (row) =>
        row.maximumQuantity == null ? "tanımsız" : amount(row.maximumQuantity),
    },
    {
      key: "oneri",
      header: "Öneri",
      numeric: true,
      value: (row) =>
        row.suggestedQuantity == null ? "—" : amount(row.suggestedQuantity),
      render: (row) =>
        row.suggestedQuantity == null ? (
          <span title="Azami seviye tanımlanmadan sipariş miktarı hesaplanamaz.">
            —
          </span>
        ) : (
          <>
            {amount(row.suggestedQuantity)} {row.unit}
            {row.suggestedCost != null && (
              <small style={{ display: "block" }}>{money(row.suggestedCost)}</small>
            )}
          </>
        ),
    },
    {
      key: "tedarikci",
      header: "Tercihli Tedarikçi",
      value: (row) => row.preferredSupplierTitle ?? "—",
    },
  ];

  if (canEditLevels) {
    columns.push({
      key: "kaldir",
      header: "",
      value: () => "",
      render: (row) => (
        <Button variant="secondary" onClick={() => void removeLevel(row)}>
          Takibi kaldır
        </Button>
      ),
    });
  }

  /**
   * ÖNERİ TABLOSU DA STANDART BİLEŞENDE.
   *
   * Ham tablo etiketi yazmak kolaydı ama ekran o zaman sayfalama, dışa
   * aktarma ve yazdırma olmadan doğardı — taşınma borcunu sayan cırcır
   * testi de bir artardı. Seçim kutusu ve miktar girişi `render` ile,
   * kâğıda giden düz değer `value` ile veriliyor.
   */
  const suggestionColumns: DataTableColumn<StockLevelRow>[] = [
    {
      key: "sec",
      header: "",
      value: (row) =>
        selected[row.inventoryItemId] !== undefined ? "seçildi" : "",
      render: (row) => (
        <input
          type="checkbox"
          checked={selected[row.inventoryItemId] !== undefined}
          disabled={!selectable(row)}
          onChange={() => toggle(row)}
        />
      ),
    },
    {
      key: "malzeme",
      header: "Malzeme",
      value: (row) => `${row.itemCode} — ${row.itemName}`,
      render: (row) => (
        <>
          <strong>{row.itemName}</strong>
          <small style={{ display: "block" }}>{row.itemCode}</small>
          {row.suggestedQuantity == null && (
            <small className="rw-value-warning" style={{ display: "block" }}>
              Azami tanımlı değil — miktar önerilemiyor.
            </small>
          )}
        </>
      ),
    },
    {
      key: "mevcut",
      header: "Mevcut",
      numeric: true,
      value: (row) => `${amount(row.currentQuantity)} ${row.unit}`,
    },
    {
      key: "asgari",
      header: "Asgari",
      numeric: true,
      value: (row) => amount(row.minimumQuantity),
    },
    {
      key: "talep",
      header: "Talep miktarı",
      numeric: true,
      value: (row) =>
        selected[row.inventoryItemId] ??
        (row.suggestedQuantity == null ? "—" : amount(row.suggestedQuantity)),
      render: (row) =>
        selected[row.inventoryItemId] !== undefined ? (
          <Input
            type="number"
            min="0"
            step="0.0001"
            value={selected[row.inventoryItemId]}
            onChange={(event) =>
              setSelected((current) => ({
                ...current,
                [row.inventoryItemId]: event.target.value,
              }))
            }
          />
        ) : (
          <span>
            {row.suggestedQuantity == null
              ? "—"
              : `${amount(row.suggestedQuantity)} ${row.unit}`}
          </span>
        ),
    },
  ];

  const selectedCount = Object.keys(selected).length;

  return (
    <ErpShell
      design="redwood"
      title="Stok Seviyeleri"
      description="Depo bazlı asgari/azami tanımı, uyarılar ve satın alma talebi önerisi"
    >
      {error && <p className="erp-form-error">{error}</p>}
      {notice && <p className="erp-alert">{notice}</p>}

      <section className="erp-card">
        <div className="erp-form-header">
          <h2>Depo</h2>
          <p>
            Asgari seviye deponun kendi eşiğidir. Şantiye deposunda takip
            edilmeyecek bir kalem için satır açılmaz — takip etmemek de bir
            karardır.
          </p>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>Depo *</span>
            <Select
              value={warehouseId}
              onChange={(event) => setWarehouseId(event.target.value)}
              options={warehouses.map((row) => ({
                value: row.id,
                label: row.code ? `${row.code} — ${row.name}` : row.name,
              }))}
            />
          </label>

          <label className="erp-check-label">
            <input
              type="checkbox"
              checked={belowOnly}
              onChange={(event) => setBelowOnly(event.target.checked)}
            />
            <span>Yalnızca asgarinin altındakiler</span>
          </label>
        </div>

        <Button variant="secondary" onClick={() => void loadLevels()}>
          Yenile
        </Button>
      </section>

      {canEditLevels && (
        <section className="erp-card">
          <div className="erp-form-header">
            <h2>Seviye tanımla</h2>
            <p>
              Aynı malzeme için ikinci satır açılmaz; mevcut tanım güncellenir.
              Azami girilmezse uyarı yine çıkar, sipariş miktarı önerilmez.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Malzeme *</span>
              <Select
                value={formItemId}
                onChange={(event) => setFormItemId(event.target.value)}
                options={[
                  { value: "", label: "Seçin" },
                  ...selectableItems.map((item) => ({
                    value: item.id,
                    label: `${item.code} — ${item.name}`,
                  })),
                ]}
              />
            </label>

            <label>
              <span>Asgari *</span>
              <Input
                type="number"
                min="0"
                step="0.0001"
                value={formMinimum}
                onChange={(event) => setFormMinimum(event.target.value)}
              />
            </label>

            <label>
              <span>Azami</span>
              <Input
                type="number"
                min="0"
                step="0.0001"
                value={formMaximum}
                onChange={(event) => setFormMaximum(event.target.value)}
              />
              <small>Sipariş önerisi azamiden mevcut düşülerek bulunur.</small>
            </label>

            <label>
              <span>Not</span>
              <Input
                value={formNote}
                onChange={(event) => setFormNote(event.target.value)}
              />
            </label>
          </div>

          <Button
            onClick={() => void saveLevel()}
            disabled={
              saving ||
              !warehouseId ||
              !formItemId ||
              formMinimum.trim() === "" ||
              Number(formMinimum) <= 0
            }
          >
            {saving ? "Kaydediliyor…" : "Seviyeyi Kaydet"}
          </Button>
        </section>
      )}

      <section className="erp-card">
        <div className="erp-form-header">
          <h2>Tanımlı seviyeler</h2>
          <p>
            {belowLevels.length} kalem asgari seviyede veya altında.
          </p>
        </div>

        {loading ? (
          <p>Yükleniyor…</p>
        ) : levels.length === 0 ? (
          <EmptyState title="Bu depoda tanımlı stok seviyesi yok" />
        ) : (
          <DataTable
            rows={levels}
            columns={columns}
            rowKey={(row) => row.id}
            title="Depo Stok Seviyeleri"
            resetKey={`${warehouseId}|${belowOnly}`}
          />
        )}
      </section>

      {canCreateRequest && belowLevels.length > 0 && (
        <section className="erp-card">
          <div className="erp-form-header">
            <h2>Satın alma talebi önerisi</h2>
            <p>
              Miktarlar öneridir, değiştirilebilir. Talep <strong>taslak</strong>
              olarak açılır ve normal onay yolundan geçer — bu ekran sipariş
              vermez, talep açar.
            </p>
          </div>

          <DataTable
            rows={belowLevels}
            columns={suggestionColumns}
            rowKey={(row) => row.id}
            title="Satın Alma Talebi Önerisi"
            resetKey={warehouseId}
          />

          <div className="erp-form-grid">
            <label>
              <span>Proje *</span>
              <Select
                value={projectId}
                onChange={(event) => setProjectId(event.target.value)}
                options={[
                  { value: "", label: "Seçin" },
                  ...companyProjects.map((project) => ({
                    value: project.id,
                    label: project.name,
                  })),
                ]}
              />
              <small>
                Depo ikmali projesiz bir iştir; ancak talep kaydı bütçe onayı
                ve raporlama için bir projeye bağlanmak zorunda.
              </small>
            </label>

            <label>
              <span>Talep eden *</span>
              <Input
                value={requestedByName}
                onChange={(event) => setRequestedByName(event.target.value)}
              />
            </label>

            <label>
              <span>Öncelik</span>
              <Select
                value={priority}
                onChange={(event) => setPriority(event.target.value)}
                options={[
                  { value: "0", label: "Düşük" },
                  { value: "1", label: "Normal" },
                  { value: "2", label: "Yüksek" },
                  { value: "3", label: "Kritik" },
                ]}
              />
            </label>

            <label>
              <span>İhtiyaç tarihi</span>
              <Input
                type="date"
                value={neededByDate}
                onChange={(event) => setNeededByDate(event.target.value)}
              />
            </label>
          </div>

          <Button
            onClick={() => void createRequest()}
            disabled={
              creating ||
              selectedCount === 0 ||
              !projectId ||
              requestedByName.trim() === ""
            }
          >
            {creating
              ? "Oluşturuluyor…"
              : `Satın Alma Talebi Oluştur (${selectedCount} kalem)`}
          </Button>
        </section>
      )}
    </ErpShell>
  );
}
