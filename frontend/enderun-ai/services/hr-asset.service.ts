import { apiClient } from "@/lib/api/api-client";

export enum HrAssetAssignmentStatus {
  Assigned = 0,
  Returned = 1,
  Lost = 2,
  Damaged = 3,
  Cancelled = 4,
}

export type AssetAssignment = {
  id: string;
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  assetType: string;
  assetCode: string;
  assetName: string;
  serialNumber?: string | null;
  assignmentDate: string;
  plannedReturnDate?: string | null;
  actualReturnDate?: string | null;
  conditionAtAssignment?: string | null;
  conditionAtReturn?: string | null;
  documentPath?: string | null;
  status: number;
  statusName: string;
  isActive: boolean;
  isOverdue: boolean;
  overdueDays?: number | null;
  notes?: string | null;
  createdAtUtc: string;
};

export type AssetTypeSummary = {
  assetType: string;
  totalCount: number;
  assignedCount: number;
  returnedCount: number;
  lostCount: number;
  damagedCount: number;
  overdueCount: number;
};

export type AssetDashboard = {
  companyId?: string | null;
  projectId?: string | null;
  totalCount: number;
  assignedCount: number;
  returnedCount: number;
  lostCount: number;
  damagedCount: number;
  cancelledCount: number;
  overdueCount: number;
  assetTypes: AssetTypeSummary[];
};

export type PersonnelAssetAnalysis = {
  personnelId: string;
  fullName: string;
  totalAssignmentCount: number;
  activeAssignmentCount: number;
  returnedCount: number;
  lostCount: number;
  damagedCount: number;
  overdueCount: number;
  riskLevel: string;
  riskScore: number;
  summary: string;
  findings: string[];
  recommendations: string[];
  assets: AssetAssignment[];
};

export type CreateAssetAssignmentRequest = {
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  assetType: string;
  assetCode: string;
  assetName: string;
  serialNumber?: string | null;
  assignmentDate: string;
  plannedReturnDate?: string | null;
  conditionAtAssignment?: string | null;
  documentPath?: string | null;
  notes?: string | null;
};

export type CreateAssetFromInventoryRequest = {
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  warehouseId: string;
  inventoryItemId: string;
  // Sunucuda isteğe bağlı, varsayılanı 1. Sarf malzemede birden çok
  // adet zimmetlenebiliyor (10 çift eldiven gibi).
  miktar?: number;
  serialNumber?: string | null;
  assignmentDate: string;
  plannedReturnDate?: string | null;
  conditionAtAssignment?: string | null;
  documentPath?: string | null;
  notes?: string | null;
};

export type AssetInventoryActionResponse = {
  assetAssignmentId: string;
  warehouseId: string;
  inventoryItemId: string;
  stockMovementId: string;
  referenceNumber: string;
  message: string;
};

export type UpdateAssetAssignmentRequest = {
  personnelId: string;
  projectId?: string | null;
  assetType: string;
  assetCode: string;
  assetName: string;
  serialNumber?: string | null;
  assignmentDate: string;
  plannedReturnDate?: string | null;
  actualReturnDate?: string | null;
  conditionAtAssignment?: string | null;
  conditionAtReturn?: string | null;
  documentPath?: string | null;
  status: number;
  notes?: string | null;
};

function buildQuery(params?: {
  companyId?: string;
  personnelId?: string;
  projectId?: string;
  status?: number | "";
  assetType?: string;
  search?: string;
  overdueOnly?: boolean;
}) {
  const query = new URLSearchParams();

  if (params?.companyId) query.set("companyId", params.companyId);
  if (params?.personnelId) query.set("personnelId", params.personnelId);
  if (params?.projectId) query.set("projectId", params.projectId);
  if (params?.status !== undefined && params.status !== "") {
    query.set("status", String(params.status));
  }
  if (params?.assetType?.trim()) query.set("assetType", params.assetType.trim());
  if (params?.search?.trim()) query.set("search", params.search.trim());
  if (params?.overdueOnly) query.set("overdueOnly", "true");

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const hrAssetService = {
  getAll(params?: {
    companyId?: string;
    personnelId?: string;
    projectId?: string;
    status?: number | "";
    assetType?: string;
    search?: string;
    overdueOnly?: boolean;
  }) {
    return apiClient<AssetAssignment[]>(`hr/assets${buildQuery(params)}`);
  },

  getById(id: string) {
    return apiClient<AssetAssignment>(`hr/assets/${id}`);
  },

  create(payload: CreateAssetAssignmentRequest) {
    return apiClient<AssetAssignment>("hr/assets", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: UpdateAssetAssignmentRequest) {
    return apiClient<AssetAssignment>(`hr/assets/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  returnAsset(
    id: string,
    payload: {
      returnDate?: string | null;
      conditionAtReturn?: string | null;
      documentPath?: string | null;
      notes?: string | null;
    }
  ) {
    return apiClient<AssetAssignment>(`hr/assets/${id}/return`, {
      method: "POST",
      body: payload,
    });
  },

  markLost(
    id: string,
    payload: {
      eventDate?: string | null;
      reason: string;
      documentPath?: string | null;
    }
  ) {
    return apiClient<AssetAssignment>(`hr/assets/${id}/lost`, {
      method: "POST",
      body: payload,
    });
  },

  markDamaged(
    id: string,
    payload: {
      eventDate?: string | null;
      damageDescription: string;
      documentPath?: string | null;
    }
  ) {
    return apiClient<AssetAssignment>(`hr/assets/${id}/damaged`, {
      method: "POST",
      body: payload,
    });
  },

  changeProject(
    id: string,
    payload: {
      projectId?: string | null;
      notes?: string | null;
    }
  ) {
    return apiClient<AssetAssignment>(`hr/assets/${id}/change-project`, {
      method: "POST",
      body: payload,
    });
  },

  transferPersonnel(
    id: string,
    payload: {
      newPersonnelId: string;
      newProjectId?: string | null;
      transferDate: string;
      conditionAtTransfer?: string | null;
      notes?: string | null;
    }
  ) {
    return apiClient<AssetAssignment>(`hr/assets/${id}/transfer-personnel`, {
      method: "POST",
      body: payload,
    });
  },

  cancel(id: string, reason: string) {
    return apiClient<AssetAssignment>(`hr/assets/${id}/cancel`, {
      method: "POST",
      body: { reason: reason.trim() },
    });
  },

  getDashboard(companyId?: string, projectId?: string) {
    return apiClient<AssetDashboard>(
      `hr/assets/dashboard${buildQuery({ companyId, projectId })}`
    );
  },

  getOverdue(companyId?: string, projectId?: string) {
    return apiClient<AssetAssignment[]>(
      `hr/assets/overdue${buildQuery({ companyId, projectId })}`
    );
  },

  analyzePersonnel(personnelId: string) {
    return apiClient<PersonnelAssetAnalysis>(
      `hr/assets/analysis/${personnelId}`
    );
  },

  createFromInventory(payload: CreateAssetFromInventoryRequest) {
    return apiClient<AssetInventoryActionResponse>(
      "hr/assets/from-inventory",
      {
        method: "POST",
        body: payload,
      }
    );
  },

  returnToWarehouse(
    id: string,
    payload: {
      returnDate?: string | null;
      conditionAtReturn?: string | null;
      documentPath?: string | null;
      notes?: string | null;
      // Durum değiştiren uç: kaydın sürümü zorunlu.
      rowVersion: string;
    }
  ) {
    return apiClient<AssetInventoryActionResponse>(
      `hr/assets/${id}/return-to-warehouse`,
      {
        method: "POST",
        body: payload,
      }
    );
  },

  delete(id: string) {
    return apiClient<{ message: string }>(`hr/assets/${id}`, {
      method: "DELETE",
    });
  },
};
