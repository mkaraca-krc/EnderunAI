import { apiClient } from "@/lib/api/api-client";

export const WAREHOUSE_TYPES = [
  { value: 0, label: "Merkez deposu" },
  { value: 1, label: "Şantiye deposu" },
  { value: 2, label: "Araç" },
  { value: 3, label: "Geçici" },
];

export interface WarehouseListItem {
  id: string;
  companyId: string;
  companyName: string;
  branchId: string;
  branchName: string;
  projectId?: string | null;
  projectCode?: string | null;
  projectName?: string | null;
  projectSiteId?: string | null;
  siteName?: string | null;
  code: string;
  name: string;
  type: number;
  address?: string | null;
  isActive: boolean;
  /** Bu depoda stok satırı olan malzeme sayısı. */
  stockLineCount: number;
  /** Depodaki stoğun ağırlıklı ortalama maliyetle değeri. */
  stockValue: number;
}

export interface CreateWarehouseRequest {
  companyId: string;
  branchId: string;
  projectId?: string | null;
  projectSiteId?: string | null;
  code: string;
  name: string;
  type: number;
  address?: string | null;
}

export interface UpdateWarehouseRequest {
  branchId: string;
  projectId?: string | null;
  projectSiteId?: string | null;
  name: string;
  type: number;
  address?: string | null;
  isActive: boolean;
}

export const warehouseService = {
  getAll(params?: {
    companyId?: string;
    projectId?: string;
    includeInactive?: boolean;
  }) {
    const query = new URLSearchParams();

    if (params?.companyId) query.set("companyId", params.companyId);
    if (params?.projectId) query.set("projectId", params.projectId);
    if (params?.includeInactive) query.set("includeInactive", "true");

    const suffix = query.size > 0 ? `?${query.toString()}` : "";
    return apiClient<WarehouseListItem[]>(`warehouses${suffix}`);
  },

  /** Deponun bölgeleri — dönemsel sayımda kapsam seçimi için. */
  getZones(warehouseId: string) {
    return apiClient<WarehouseZoneListItem[]>(`warehouses/${warehouseId}/locations`);
  },

  create(payload: CreateWarehouseRequest) {
    return apiClient<{ message: string; id: string }>("warehouses", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: UpdateWarehouseRequest) {
    return apiClient<{ message: string }>(`warehouses/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
};

export interface WarehouseZoneListItem {
  id: string;
  code: string;
  name: string;
  kind: number;
  sortOrder: number;
}
