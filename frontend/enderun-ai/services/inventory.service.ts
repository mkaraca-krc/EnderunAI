export type InventoryItemType = 0 | 1 | 2;

export interface InventoryItemListItem {
  id: string;
  companyId: string;
  companyName: string;
  code: string;
  name: string;
  category?: string | null;
  brand?: string | null;
  model?: string | null;
  unit: string;
  barcode?: string | null;
  minimumStock: number;
  maximumStock: number;
  type: InventoryItemType;
  isActive: boolean;
  totalStock: number;
  availableStock: number;
}

export interface CreateInventoryItemRequest {
  companyId: string;
  code: string;
  name: string;
  category?: string;
  brand?: string;
  model?: string;
  unit: string;
  barcode?: string;
  minimumStock: number;
  maximumStock: number;
  type: InventoryItemType;
}

export interface CompanyOption {
  id: string;
  name: string;
}

function getApiBaseUrl(): string {
  return process.env.NEXT_PUBLIC_API_URL ?? "";
}

async function request<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    ...options,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(options.headers ?? {}),
    },
  });

  if (!response.ok) {
    let message = "İşlem sırasında bir hata oluştu.";

    try {
      const body = (await response.json()) as { message?: string };
      message = body.message ?? message;
    } catch {
      // API JSON dönmezse genel hata mesajını kullan.
    }

    throw new Error(message);
  }

  return response.json() as Promise<T>;
}

export const inventoryService = {
  async getItems(params?: {
    companyId?: string;
    search?: string;
  }): Promise<InventoryItemListItem[]> {
    const query = new URLSearchParams();

    if (params?.companyId) query.set("companyId", params.companyId);
    if (params?.search) query.set("search", params.search);

    const suffix = query.size > 0 ? `?${query.toString()}` : "";
    return request<InventoryItemListItem[]>(`/api/inventory/items${suffix}`);
  },

  async createItem(
    payload: CreateInventoryItemRequest,
  ): Promise<{ id: string; code: string; name: string; message: string }> {
    return request("/api/inventory/items", {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  async getCompanies(): Promise<CompanyOption[]> {
    const result = await request<
      CompanyOption[] | { items?: CompanyOption[]; data?: CompanyOption[] }
    >("/api/companies");

    if (Array.isArray(result)) return result;
    return result.items ?? result.data ?? [];
  },
};
