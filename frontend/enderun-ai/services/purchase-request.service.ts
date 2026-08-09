import { apiClient } from "@/lib/api/api-client";

export type PurchaseRequestType = 0 | 1;
export type PurchaseRequestPriority = 0 | 1 | 2 | 3;
export type PurchaseRequestStatus =
  | 0
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6
  | 7
  | 8;

export const PURCHASE_REQUEST_STATUS = {
  Draft: 0,
  Submitted: 1,
  Approved: 2,
  Quotation: 3,
  Ordered: 4,
  Completed: 5,
  Cancelled: 6,
  Rejected: 7,
  /** Düzeltmeye iade edildi — talep sahibi düzeltip yeniden gönderir. */
  ReturnedForRevision: 8,
} as const;

export const PURCHASE_REQUEST_STATUS_LABELS: Record<number, string> = {
  0: "Taslak",
  1: "Onaya Gönderildi",
  2: "Onaylandı",
  3: "Teklif",
  4: "Sipariş",
  5: "Tamamlandı",
  6: "İptal",
  7: "Reddedildi",
  8: "Düzeltmeye İade",
};

export type PurchaseRequestItem = {
  id: string;
  lineNumber: number;

  inventoryItemId?: string | null;
  inventoryItemCode?: string | null;
  inventoryItemName?: string | null;

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
  requestType: PurchaseRequestType;
  requestTypeName: string;

  requestDate: string;
  neededByDate?: string | null;
  requestedByName: string;
  description?: string | null;

  priority: PurchaseRequestPriority;
  status: PurchaseRequestStatus;

  /** Red gerekçesi — Reddedildi durumunda dolu. */
  rejectionReason?: string | null;
  rejectedAtUtc?: string | null;
  /** İade gerekçesi — talep sahibinin neyi düzelteceği. */
  returnReason?: string | null;
  returnedAtUtc?: string | null;
  /** Kaç kez düzeltilip yeniden gönderildi. */
  revisionCount?: number | null;

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
  inventoryItemId?: string | null;
  materialDescription: string;
  quantity: number;
  unit: string;
  requestedDeliveryDate?: string | null;
  notes?: string | null;
};

export type CreatePurchaseRequestPayload = {
  companyId: string;
  projectId: string;
  requestType: PurchaseRequestType;
  requestDate: string;
  neededByDate?: string | null;
  requestedByName: string;
  description?: string | null;
  priority: PurchaseRequestPriority;
  items: PurchaseRequestItemPayload[];
};

export type UpdatePurchaseRequestPayload = {
  requestType: PurchaseRequestType;
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
  requestType?: number;
  status?: number;
  search?: string;
}) {
  const query = new URLSearchParams();

  if (params?.companyId) {
    query.set("companyId", params.companyId);
  }

  if (params?.projectId) {
    query.set("projectId", params.projectId);
  }

  if (params?.requestType !== undefined) {
    query.set(
      "requestType",
      String(params.requestType),
    );
  }

  if (params?.status !== undefined) {
    query.set("status", String(params.status));
  }

  if (params?.search) {
    query.set("search", params.search);
  }

  const value = query.toString();

  return value ? `?${value}` : "";
}

export const purchaseRequestService = {
  getAll(params?: {
    companyId?: string;
    projectId?: string;
    requestType?: number;
    status?: number;
    search?: string;
  }) {
    return apiClient<PurchaseRequestListItem[]>(
      `purchase-requests${buildQuery(params)}`,
    );
  },

  getById(id: string) {
    return apiClient<PurchaseRequestDetail>(
      `purchase-requests/${id}`,
    );
  },

  create(payload: CreatePurchaseRequestPayload) {
    return apiClient<{
      message: string;
      id: string;
      requestNumber: string;
      requestType: PurchaseRequestType;
      requestTypeName: string;
      status: PurchaseRequestStatus;
    }>("purchase-requests", {
      method: "POST",
      body: payload,
    });
  },

  update(
    id: string,
    payload: UpdatePurchaseRequestPayload,
  ) {
    return apiClient<{ message: string }>(
      `purchase-requests/${id}`,
      {
        method: "PUT",
        body: payload,
      },
    );
  },

  submit(id: string) {
    return apiClient<{ message: string }>(
      `purchase-requests/${id}/submit`,
      {
        method: "POST",
      },
    );
  },

  approve(id: string) {
    return apiClient<{ message: string }>(
      `purchase-requests/${id}/approve`,
      {
        method: "POST",
      },
    );
  },

  /** Talebi reddeder — nihai. Gerekçe zorunlu. */
  reject(id: string, reason: string) {
    return apiClient<{ message: string }>(
      `purchase-requests/${id}/reject`,
      {
        method: "POST",
        body: { reason },
      },
    );
  },

  /**
   * Talebi düzeltmeye iade eder — talep sahibi düzeltip yeniden
   * gönderebilir. Gerekçe zorunlu.
   */
  returnForRevision(id: string, reason: string) {
    return apiClient<{ message: string }>(
      `purchase-requests/${id}/iade`,
      {
        method: "POST",
        body: { reason },
      },
    );
  },

  cancel(id: string, reason?: string) {
    return apiClient<{ message: string }>(
      `purchase-requests/${id}/cancel`,
      {
        method: "POST",
        body: {
          reason: reason || null,
        },
      },
    );
  },






};
