
export interface SelectOption { id: string; name: string; code?: string }
export interface InventoryMovement {
  id: string; warehouseName: string; itemCode: string; itemName: string;
  projectName?: string | null; projectSiteId?: string | null; projectSiteName?: string | null;
  goodsReceiptId?: string | null;
  type: number; quantity: number; unitCost?: number | null; totalCost?: number | null;
  referenceNumber: string; movementDate: string;
}
export interface WarehouseStockRow {
  inventoryItemId: string;
  quantity: number;
}
export interface CriticalStockAlert {
  warehouseId: string; warehouseName: string;
  inventoryItemId: string; itemCode: string; itemName: string; unit: string;
  minimumStock: number;
}
async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const backendPath = path.replace(/^\/api\//, "");
  const response = await fetch(`/api/backend/${backendPath}`, {
    ...options,
    credentials: "include",
    headers: { "Content-Type": "application/json", ...(options.headers ?? {}) },
  });
  if (!response.ok) {
    let message = "İşlem sırasında hata oluştu.";
    try { message = ((await response.json()) as {message?: string}).message ?? message; } catch {}
    throw new Error(message);
  }
  return response.json() as Promise<T>;
}

function normalize(value: unknown): SelectOption[] {
  const raw = Array.isArray(value) ? value :
    ((value as {items?: unknown[]; data?: unknown[]})?.items ??
     (value as {data?: unknown[]})?.data ?? []);
  return raw.map((x) => {
    const r = x as Record<string, unknown>;
    return { id: String(r.id ?? ""), name: String(r.name ?? r.title ?? r.code ?? "İsimsiz"), code: typeof r.code === "string" ? r.code : undefined };
  }).filter(x => x.id);
}

export const inventoryMovementService = {
  getMovements: () => request<InventoryMovement[]>("/api/inventory/movements"),
  getWarehouses: async () => normalize(await request<unknown>("/api/warehouses")),
  getProjects: async () => normalize(await request<unknown>("/api/projects")),
  getItems: async () => normalize(await request<unknown>("/api/inventory/items")),
  getProjectSites: async (projectId: string) =>
    normalize(await request<unknown>(`/api/projects/${projectId}/sites`)),
  /** İcmal kısımları — sarfın hangi imalata gittiğini işaretlemek için. */
  getProjectSections: async (projectId: string) =>
    normalize(await request<unknown>(`/api/projects/${projectId}/hakedis-sections`)),
  /**
   * Projenin taşeron sözleşmeleri — sarfın hangi taşerona verildiğini
   * işaretlemek için.
   *
   * Boş bırakmak "bizim sarfımız" demektir ve taşerona yazılmaz;
   * seçilirse bedeli o taşeronun hakedişinde malzeme kesintisi olarak
   * otomatik önerilir. Yetkisi olmayan kullanıcıda liste boş döner ve
   * alan hiç görünmez.
   */
  getSubcontractorContracts: async (projectId: string) => {
    try {
      const raw = await request<unknown>(
        `/api/subcontractor-contracts?projectId=${encodeURIComponent(projectId)}`
      );

      const rows = Array.isArray(raw) ? raw : [];

      return rows.map((x) => {
        const r = x as Record<string, unknown>;
        return {
          id: String(r.id ?? ""),
          // Sözleşme numarası + taşeron unvanı birlikte gösterilir;
          // aynı taşeronun birden fazla sözleşmesi olabiliyor.
          name: [r.contractNumber, r.subcontractorTitle]
            .filter(Boolean)
            .join(" · ") || "İsimsiz sözleşme",
          code: typeof r.contractNumber === "string" ? r.contractNumber : undefined,
        };
      }).filter((x) => x.id);
    } catch {
      // Taşeron görme yetkisi yoksa alan hiç gösterilmez.
      return [] as SelectOption[];
    }
  },
  getCriticalStockAlerts: () => request<CriticalStockAlert[]>("/api/inventory/critical-stock-alerts"),
  /** Bir deponun stok satırları; sayım ekranı mevcut miktarı buradan okur. */
  getWarehouseStocks: (warehouseId: string) =>
    request<WarehouseStockRow[]>(`/api/inventory/warehouses/${warehouseId}/stocks`),
  /**
   * Depo çıkışı. `accountingVoucherId` BOŞ dönebilir ve bu bilgidir:
   * ortalama maliyeti sıfır olan kart hiç faturalı girmemiş demektir,
   * maliyeti bilinmiyordur ve sıfır tutarlı fiş kesilmez.
   */
  issue: (body: unknown) => request<{
    referenceNumber: string;
    unitCost: number;
    totalCost: number;
    accountingVoucherId?: string | null;
  }>("/api/inventory/issues", { method: "POST", body: JSON.stringify(body) }),
  transfer: (body: unknown) => request<{ referenceNumber: string }>(
    "/api/inventory/transfers", { method: "POST", body: JSON.stringify(body) }),
  adjustment: (body: unknown) => request<{
    referenceNumber: string;
    delta: number;
    newQuantity: number;
    accountingVoucherId?: string | null;
  }>("/api/inventory/adjustments", { method: "POST", body: JSON.stringify(body) }),
  updateItem: (id: string, body: unknown) => request(`/api/inventory/items/${id}`, { method: "PUT", body: JSON.stringify(body) }),
};
