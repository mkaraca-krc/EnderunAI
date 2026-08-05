"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import {
  inventoryMovementService,
  type SelectOption,
} from "@/services/inventory-movement.service";

type MovementMode = "receipt" | "issue" | "transfer";

const TITLES: Record<MovementMode, { title: string; description: string }> = {
  receipt: {
    title: "Stok Girişi",
    description: "İrsaliye karşılığı depoya malzeme girişi",
  },
  issue: {
    title: "Stok Çıkışı",
    description:
      "Sarfiyat çıkışı; proje seçilirse tutar proje maliyetine işlenir",
  },
  transfer: {
    title: "Depolar Arası Transfer",
    description: "Kaynak depodan hedef depoya malzeme aktarımı",
  },
};

export function InventoryMovementForm({ mode }: { mode: MovementMode }) {
  const router = useRouter();

  const [warehouses, setWarehouses] = useState<SelectOption[]>([]);
  const [projects, setProjects] = useState<SelectOption[]>([]);
  const [items, setItems] = useState<SelectOption[]>([]);
  const [sites, setSites] = useState<SelectOption[]>([]);

  const [warehouseId, setWarehouseId] = useState("");
  const [targetWarehouseId, setTargetWarehouseId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [projectSiteId, setProjectSiteId] = useState("");
  const [inventoryItemId, setInventoryItemId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [referenceNumber, setReferenceNumber] = useState("");
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
      inventoryMovementService.getProjects().then(setProjects),
      inventoryMovementService.getItems().then(setItems),
    ]).catch((err) =>
      setError(err instanceof Error ? err.message : "Veriler yüklenemedi.")
    );
  }, []);

  useEffect(() => {
    setProjectSiteId("");

    if (mode !== "issue" || !projectId) {
      setSites([]);
      return;
    }

    void inventoryMovementService
      .getProjectSites(projectId)
      .then(setSites)
      .catch(() => setSites([]));
  }, [projectId, mode]);

  const validationErrors: string[] = [];
  if (!warehouseId) {
    validationErrors.push(
      mode === "transfer" ? "Kaynak depo seçin." : "Depo seçin."
    );
  }
  if (mode === "transfer" && !targetWarehouseId) {
    validationErrors.push("Hedef depo seçin.");
  }
  if (!inventoryItemId) validationErrors.push("Malzeme seçin.");
  if (!(Number(quantity) > 0)) {
    validationErrors.push("Miktar sıfırdan büyük olmalı.");
  }
  if (mode === "receipt" && !referenceNumber.trim()) {
    validationErrors.push("Depo girişinde irsaliye/referans numarası zorunlu.");
  }
  if (!movementDate) validationErrors.push("Hareket tarihi girin.");

  async function submit(event: React.FormEvent) {
    event.preventDefault();

    if (validationErrors.length > 0) {
      setError(validationErrors.join(" "));
      return;
    }

    setError("");
    setNotice("");
    setSaving(true);

    const common = {
      inventoryItemId,
      projectId: projectId || undefined,
      quantity: Number(quantity),
      referenceNumber: referenceNumber.trim() || undefined,
      movementDate,
      description: description || undefined,
    };

    try {
      if (mode === "receipt") {
        await inventoryMovementService.receipt({ warehouseId, ...common });
        router.push("/depo-stok/hareketler");
        router.refresh();
        return;
      }

      if (mode === "issue") {
        const result = await inventoryMovementService.issue({
          warehouseId,
          projectSiteId: projectSiteId || undefined,
          ...common,
        });

        setNotice(
          `Kaydedildi — belge no: ${result.referenceNumber}, ` +
            `tutar: ${result.totalCost.toFixed(2)} TRY`
        );
      } else {
        const result = await inventoryMovementService.transfer({
          sourceWarehouseId: warehouseId,
          targetWarehouseId,
          ...common,
        });

        setNotice(`Kaydedildi — belge no: ${result.referenceNumber}`);
      }

      setQuantity("");
      setDescription("");
      setReferenceNumber("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşlem kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  const labels = TITLES[mode];

  return (
    <ErpShell title={labels.title} description={labels.description}>
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert">{notice}</div>}

      <form className="erp-form-card" onSubmit={submit}>
        <div className="erp-form-header">
          <h2>Hareket Bilgileri</h2>
          <p>
            Malzeme, tanımlı stok kartlarından seçilir; elle malzeme adı
            yazılmaz.
          </p>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>{mode === "transfer" ? "Kaynak Depo *" : "Depo *"}</span>
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

          {mode === "transfer" && (
            <label>
              <span>Hedef Depo *</span>
              <select
                value={targetWarehouseId}
                onChange={(event) => setTargetWarehouseId(event.target.value)}
              >
                <option value="">Seçin</option>
                {warehouses
                  .filter((option) => option.id !== warehouseId)
                  .map((option) => (
                    <option key={option.id} value={option.id}>
                      {option.code ? `${option.code} — ` : ""}
                      {option.name}
                    </option>
                  ))}
              </select>
            </label>
          )}

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
            <span>
              {mode === "issue"
                ? "Proje (boş = merkez sarfiyatı)"
                : "Proje / Şantiye"}
            </span>
            <select
              value={projectId}
              onChange={(event) => setProjectId(event.target.value)}
            >
              <option value="">Seçmeden devam et</option>
              {projects.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.code ? `${option.code} — ` : ""}
                  {option.name}
                </option>
              ))}
            </select>
          </label>

          {mode === "issue" && projectId && (
            <label>
              <span>Şantiye (ops.)</span>
              <select
                value={projectSiteId}
                onChange={(event) => setProjectSiteId(event.target.value)}
              >
                <option value="">Proje geneli</option>
                {sites.map((option) => (
                  <option key={option.id} value={option.id}>
                    {option.code ? `${option.code} — ` : ""}
                    {option.name}
                  </option>
                ))}
              </select>
            </label>
          )}

          <label>
            <span>Miktar *</span>
            <input
              type="number"
              min="0.01"
              step="0.01"
              value={quantity}
              onChange={(event) => setQuantity(event.target.value)}
            />
          </label>

          <label>
            <span>
              {mode === "receipt"
                ? "İrsaliye / Referans No *"
                : "Not / Referans (ops.)"}
            </span>
            <input
              type="text"
              value={referenceNumber}
              onChange={(event) => setReferenceNumber(event.target.value)}
            />
            {mode !== "receipt" && (
              <small>
                Belge numarası ({mode === "issue" ? "CIKIS" : "TRF"}-yıl-sıra)
                otomatik üretilir.
              </small>
            )}
          </label>

          <label>
            <span>Hareket Tarihi *</span>
            <input
              type="date"
              value={movementDate}
              onChange={(event) => setMovementDate(event.target.value)}
            />
          </label>

          <label className="span-2">
            <span>Açıklama</span>
            <input
              type="text"
              value={description}
              onChange={(event) => setDescription(event.target.value)}
            />
          </label>
        </div>

        {mode === "issue" && (
          <p>
            Proje seçilirse malzeme tutarı, seçiliyse şantiyesiyle birlikte
            projenin maliyet kaydına otomatik işlenir.
          </p>
        )}

        <div className="erp-form-actions">
          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => router.push("/depo-stok/hareketler")}
          >
            Vazgeç
          </button>

          <button type="submit" className="erp-primary-button" disabled={saving}>
            {saving ? "Kaydediliyor..." : "Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
