import { apiClient } from "@/lib/api/api-client";

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

export const inventoryService = {
  async getItems(params?: {
    companyId?: string;
    search?: string;
  }): Promise<InventoryItemListItem[]> {
    const query = new URLSearchParams();

    if (params?.companyId) query.set("companyId", params.companyId);
    if (params?.search) query.set("search", params.search);

    const suffix = query.size > 0 ? `?${query.toString()}` : "";
    return apiClient<InventoryItemListItem[]>(`inventory/items${suffix}`);
  },

  async createItem(
    payload: CreateInventoryItemRequest,
  ): Promise<{ id: string; code: string; name: string; message: string }> {
    return apiClient("inventory/items", {
      method: "POST",
      body: payload,
    });
  },

  async getCompanies(): Promise<CompanyOption[]> {
    const result = await apiClient<
      CompanyOption[] | { items?: CompanyOption[]; data?: CompanyOption[] }
    >("companies");

    if (Array.isArray(result)) return result;
    return result.items ?? result.data ?? [];
  },
};
