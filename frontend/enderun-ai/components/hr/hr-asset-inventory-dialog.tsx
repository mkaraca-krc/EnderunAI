"use client";

import { useEffect, useMemo, useState } from "react";
import { apiClient } from "@/lib/api/api-client";
import { inventoryService, type InventoryItemListItem } from "@/services/inventory.service";
import type { CompanyListItem } from "@/services/company.service";
import type { PersonnelListItem } from "@/services/personnel.service";
import type { ProjectListItem } from "@/services/project.service";
import { hrAssetService } from "@/services/hr-asset.service";

type WarehouseOption = {
  id: string;
  code?: string;
  name: string;
  companyId?: string;
};

type Props = {
  companies: CompanyListItem[];
  initialCompanyId: string;
  personnel: PersonnelListItem[];
  projects: ProjectListItem[];
  onClose: () => void;
  onSuccess: (message: string) => Promise<void> | void;
};

const today = () => new Date().toISOString().slice(0, 10);

const input = {
  width: "100%",
  minHeight: 42,
  border: "1px solid #cbd5e1",
  borderRadius: 10,
  padding: "8px 11px",
  background: "#fff",
  color: "#0f172a",
  boxSizing: "border-box",
} as const;

function normalizeWarehouses(value: unknown): WarehouseOption[] {
  const raw = Array.isArray(value)
    ? value
    : ((value as { items?: unknown[]; data?: unknown[] })?.items ??
       (value as { data?: unknown[] })?.data ??
       []);

  return raw
    .map((item) => {
      const row = item as Record<string, unknown>;
      return {
        id: String(row.id ?? ""),
        code: typeof row.code === "string" ? row.code : undefined,
        name: String(row.name ?? row.title ?? row.code ?? "İsimsiz Depo"),
        companyId: typeof row.companyId === "string" ? row.companyId : undefined,
      };
    })
    .filter((x) => x.id);
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

function Field({
  label,
  children,
  required,
}: {
  label: string;
  children: React.ReactNode;
  required?: boolean;
}) {
  return (
    <label style={{ display: "grid", gap: 7 }}>
      <span style={{ fontSize: 13, fontWeight: 800, color: "#334155" }}>
        {label}{required ? " *" : ""}
      </span>
      {children}
    </label>
  );
}

export default function HrAssetInventoryDialog({
  companies,
  initialCompanyId,
  personnel,
  projects,
  onClose,
  onSuccess,
}: Props) {
  const [companyId, setCompanyId] = useState(initialCompanyId);
  const [warehouseId, setWarehouseId] = useState("");
  const [inventoryItemId, setInventoryItemId] = useState("");
  const [personnelId, setPersonnelId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [serialNumber, setSerialNumber] = useState("");
  const [assignmentDate, setAssignmentDate] = useState(today());
  const [plannedReturnDate, setPlannedReturnDate] = useState("");
  const [conditionAtAssignment, setConditionAtAssignment] = useState("İyi");
  const [notes, setNotes] = useState("");
  const [search, setSearch] = useState("");

  const [warehouses, setWarehouses] = useState<WarehouseOption[]>([]);
  const [inventoryItems, setInventoryItems] = useState<InventoryItemListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const filteredPersonnel = useMemo(
    () => personnel.filter((x) => x.companyId === companyId && x.isActive),
    [companyId, personnel]
  );

  const filteredProjects = useMemo(
    () => projects.filter((x) => x.companyId === companyId),
    [companyId, projects]
  );

  const filteredWarehouses = useMemo(
    () => warehouses.filter((x) => !x.companyId || x.companyId === companyId),
    [companyId, warehouses]
  );

  const equipmentItems = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");

    return inventoryItems
      .filter(
        (x) =>
          x.companyId === companyId &&
          x.type === 1 &&
          x.isActive &&
          Number(x.availableStock) > 0
      )
      .filter((x) => {
        if (!term) return true;
        return [x.code, x.name, x.category, x.brand, x.model, x.barcode]
          .filter(Boolean)
          .some((value) =>
            String(value).toLocaleLowerCase("tr-TR").includes(term)
          );
      });
  }, [companyId, inventoryItems, search]);

  const selectedItem = inventoryItems.find((x) => x.id === inventoryItemId);

  useEffect(() => {
    let active = true;

    async function load() {
      setLoading(true);
      setError("");

      try {
        const [warehouseResult, itemResult] = await Promise.all([
          apiClient<unknown>("warehouses"),
          inventoryService.getItems({ companyId }),
        ]);

        if (!active) return;
        setWarehouses(normalizeWarehouses(warehouseResult));
        setInventoryItems(itemResult);
      } catch (err) {
        if (active) setError(messageOf(err));
      } finally {
        if (active) setLoading(false);
      }
    }

    void load();
    return () => {
      active = false;
    };
  }, [companyId]);

  useEffect(() => {
    setWarehouseId("");
    setInventoryItemId("");
    setPersonnelId("");
    setProjectId("");
  }, [companyId]);

  async function submit(event: React.FormEvent) {
    event.preventDefault();

    if (!companyId || !warehouseId || !inventoryItemId || !personnelId || !assignmentDate) {
      setError("Şirket, depo, ekipman, personel ve zimmet tarihi zorunludur.");
      return;
    }

    setSaving(true);
    setError("");

    try {
      const result = await hrAssetService.createFromInventory({
        companyId,
        personnelId,
        projectId: projectId || null,
        warehouseId,
        inventoryItemId,
        serialNumber: serialNumber.trim() || null,
        assignmentDate,
        plannedReturnDate: plannedReturnDate || null,
        conditionAtAssignment: conditionAtAssignment.trim() || null,
        documentPath: null,
        notes: notes.trim() || null,
      });

      await onSuccess(result.message);
      onClose();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div style={overlay} role="dialog" aria-modal="true">
      <form onSubmit={submit} style={modal}>
        <header style={modalHeader}>
          <div>
            <h2 style={{ margin: 0 }}>Depodan Zimmet Oluştur</h2>
            <p style={{ margin: "5px 0 0", color: "#64748b" }}>
              Ekipman stoktan düşülür ve personele tek işlemde zimmetlenir.
            </p>
          </div>
          <button type="button" onClick={onClose} style={closeButton}>×</button>
        </header>

        <div style={{ padding: 20, display: "grid", gridTemplateColumns: "repeat(2,minmax(0,1fr))", gap: 14, maxHeight: "72vh", overflow: "auto" }}>
          {error && (
            <div style={{ gridColumn: "1 / -1", padding: 12, borderRadius: 10, background: "#fef2f2", color: "#b91c1c", fontWeight: 800 }}>
              {error}
            </div>
          )}

          <Field label="Şirket" required>
            <select value={companyId} onChange={(e) => setCompanyId(e.target.value)} style={input}>
              {companies.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </Field>

          <Field label="Depo" required>
            <select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} style={input} disabled={loading}>
              <option value="">Seçiniz</option>
              {filteredWarehouses.map((x) => (
                <option key={x.id} value={x.id}>
                  {x.code ? `${x.code} · ` : ""}{x.name}
                </option>
              ))}
            </select>
          </Field>

          <div style={{ gridColumn: "1 / -1" }}>
            <Field label="Ekipman Arama">
              <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Kod, ad, marka, model veya barkod..." style={input} />
            </Field>
          </div>

          <div style={{ gridColumn: "1 / -1" }}>
            <Field label="Stoktaki Ekipman" required>
              <select value={inventoryItemId} onChange={(e) => setInventoryItemId(e.target.value)} style={input} disabled={loading}>
                <option value="">Seçiniz</option>
                {equipmentItems.map((x) => (
                  <option key={x.id} value={x.id}>
                    {x.code} · {x.name}{x.brand ? ` · ${x.brand}` : ""}{x.model ? ` ${x.model}` : ""} · Kullanılabilir: {x.availableStock} {x.unit}
                  </option>
                ))}
              </select>
            </Field>
          </div>

          {selectedItem && (
            <div style={{ gridColumn: "1 / -1", padding: 13, borderRadius: 12, background: "#f0fdfa", border: "1px solid #99f6e4", color: "#134e4a" }}>
              <strong>{selectedItem.code} · {selectedItem.name}</strong>
              <div style={{ marginTop: 5, fontSize: 13 }}>
                Marka/Model: {selectedItem.brand || "-"} / {selectedItem.model || "-"} · Kullanılabilir stok: {selectedItem.availableStock} {selectedItem.unit}
              </div>
            </div>
          )}

          <Field label="Personel" required>
            <select value={personnelId} onChange={(e) => setPersonnelId(e.target.value)} style={input}>
              <option value="">Seçiniz</option>
              {filteredPersonnel.map((x) => <option key={x.id} value={x.id}>{x.employeeNumber} · {x.fullName}</option>)}
            </select>
          </Field>

          <Field label="Proje">
            <select value={projectId} onChange={(e) => setProjectId(e.target.value)} style={input}>
              <option value="">Projesiz</option>
              {filteredProjects.map((x) => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}
            </select>
          </Field>

          <Field label="Seri Numarası">
            <input value={serialNumber} onChange={(e) => setSerialNumber(e.target.value)} style={input} />
          </Field>

          <Field label="Teslim Durumu">
            <input value={conditionAtAssignment} onChange={(e) => setConditionAtAssignment(e.target.value)} style={input} />
          </Field>

          <Field label="Zimmet Tarihi" required>
            <input type="date" value={assignmentDate} onChange={(e) => setAssignmentDate(e.target.value)} style={input} />
          </Field>

          <Field label="Planlanan İade">
            <input type="date" value={plannedReturnDate} onChange={(e) => setPlannedReturnDate(e.target.value)} style={input} />
          </Field>

          <div style={{ gridColumn: "1 / -1" }}>
            <Field label="Notlar">
              <textarea rows={4} value={notes} onChange={(e) => setNotes(e.target.value)} style={{ ...input, resize: "vertical" }} />
            </Field>
          </div>
        </div>

        <footer style={modalFooter}>
          <button type="button" onClick={onClose} style={secondaryButton}>Vazgeç</button>
          <button type="submit" disabled={saving || loading} style={primaryButton}>
            {saving ? "Stok ve zimmet işleniyor..." : "Depodan Zimmetle"}
          </button>
        </footer>
      </form>
    </div>
  );
}

const overlay = { position: "fixed", inset: 0, zIndex: 1300, display: "grid", placeItems: "center", padding: 20, background: "rgba(15,23,42,.62)" } as const;
const modal = { width: "min(850px,100%)", maxHeight: "94vh", overflow: "hidden", borderRadius: 18, background: "#fff", border: "1px solid #e2e8f0", boxShadow: "0 24px 70px rgba(15,23,42,.32)" } as const;
const modalHeader = { display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 12, padding: "18px 20px", borderBottom: "1px solid #e2e8f0" } as const;
const modalFooter = { display: "flex", justifyContent: "flex-end", gap: 10, padding: "16px 20px", borderTop: "1px solid #e2e8f0" } as const;
const closeButton = { width: 34, height: 34, border: 0, borderRadius: 9, background: "#f1f5f9", color: "#334155", fontSize: 22, cursor: "pointer" } as const;
const primaryButton = { minHeight: 42, border: 0, borderRadius: 10, padding: "0 18px", background: "#0f766e", color: "#fff", fontWeight: 900, cursor: "pointer" } as const;
const secondaryButton = { minHeight: 42, border: "1px solid #cbd5e1", borderRadius: 10, padding: "0 18px", background: "#fff", color: "#334155", fontWeight: 900, cursor: "pointer" } as const;
