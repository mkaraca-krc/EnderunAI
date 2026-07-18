import { apiClient } from "@/lib/api/api-client";

export type PurchaseRequestPriority = 0 | 1 | 2 | 3;
export type PurchaseRequestStatus = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7;

export type PurchaseRequestItem = {
  id: string;
  lineNumber: number;
  materialDescription: string;
  quantity: number;
  unit: string;
  requestedDeliveryDate?: string | null;
  notes?: string | null;
  isActive: boolean;
};

export type PurchaseRequestListItem = {
  id: string;
  companyId: string;
  companyName: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  requestNumber: string;
  requestDate: string;
  neededByDate?: string | null;
  requestedByName: string;
  description?: string | null;
  priority: PurchaseRequestPriority;
  status: PurchaseRequestStatus;
  isActive: boolean;
  itemCount: number;
  totalQuantity: number;
};

export type PurchaseRequestDetail = Omit<
  PurchaseRequestListItem,
  "itemCount" | "totalQuantity"
> & {
  approvedByUserId?: string | null;
  approvedAtUtc?: string | null;
  cancelledByUserId?: string | null;
  cancelledAtUtc?: string | null;
  cancellationReason?: string | null;
  items: PurchaseRequestItem[];
};

export type PurchaseRequestItemPayload = {
  materialDescription: string;
  quantity: number;
  unit: string;
  requestedDeliveryDate?: string | null;
  notes?: string | null;
};

export type CreatePurchaseRequestPayload = {
  companyId: string;
  projectId: string;
  requestNumber: string;
  requestDate: string;
  neededByDate?: string | null;
  requestedByName: string;
  description?: string | null;
  priority: PurchaseRequestPriority;
  items: PurchaseRequestItemPayload[];
};

export type UpdatePurchaseRequestPayload = {
  requestDate: string;
  neededByDate?: string | null;
  requestedByName: string;
  description?: string | null;
  priority: PurchaseRequestPriority;
  items: PurchaseRequestItemPayload[];
};

function buildQuery(params?: {
  companyId?: string;
  projectId?: string;
  status?: number;
  search?: string;
}) {
  const query = new URLSearchParams();

  if (params?.companyId) query.set("companyId", params.companyId);
  if (params?.projectId) query.set("projectId", params.projectId);
  if (params?.status !== undefined) query.set("status", String(params.status));
  if (params?.search) query.set("search", params.search);

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const purchaseRequestService = {
  getAll(params?: {
    companyId?: string;
    projectId?: string;
    status?: number;
    search?: string;
  }) {
    return apiClient<PurchaseRequestListItem[]>(
      `purchase-requests${buildQuery(params)}`
    );
  },

  getById(id: string) {
    return apiClient<PurchaseRequestDetail>(`purchase-requests/${id}`);
  },

  create(payload: CreatePurchaseRequestPayload) {
    return apiClient<{
      message: string;
      id: string;
      requestNumber: string;
      status: PurchaseRequestStatus;
    }>("purchase-requests", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: UpdatePurchaseRequestPayload) {
    return apiClient<{ message: string }>(`purchase-requests/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  submit(id: string) {
    return apiClient<{ message: string }>(
      `purchase-requests/${id}/submit`,
      { method: "POST" }
    );
  },

  approve(id: string) {
    return apiClient<{ message: string }>(
      `purchase-requests/${id}/approve`,
      { method: "POST" }
    );
  },

  cancel(id: string, reason?: string) {
    return apiClient<{ message: string }>(
      `purchase-requests/${id}/cancel`,
      {
        method: "POST",
        body: { reason: reason || null },
      }
    );
  },
};
