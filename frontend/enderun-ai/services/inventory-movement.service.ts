
export interface SelectOption { id: string; name: string; code?: string }
export interface InventoryMovement {
  id: string; warehouseName: string; itemCode: string; itemName: string;
  projectName?: string | null; type: number; quantity: number;
  referenceNumber: string; movementDate: string;
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
  receipt: (body: unknown) => request("/api/inventory/receipts", { method: "POST", body: JSON.stringify(body) }),
  issue: (body: unknown) => request("/api/inventory/issues", { method: "POST", body: JSON.stringify(body) }),
  transfer: (body: unknown) => request("/api/inventory/transfers", { method: "POST", body: JSON.stringify(body) }),
};
