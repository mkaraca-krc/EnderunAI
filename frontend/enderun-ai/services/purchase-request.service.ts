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
  | 7;

export type PurchaseRequestItem = {
  id: string;
  lineNumber: number;

  inventoryItemId?: string | null;
  inventoryItemCode?: string | null;
  inventoryItemName?: string | null;

  materialDescription: string;
  quantity: number;
  reservedQuantity: number;
  issuedQuantity: number;
  remainingQuantity: number;
  availableToReserveQuantity: number;

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

export type StockReservationListItem = {
  id: string;
  reservationNumber: string;

  warehouseId: string;
  warehouseName: string;

  inventoryItemId: string;
  inventoryItemCode: string;
  inventoryItemName: string;

  projectId: string;
  purchaseRequestId: string;
  purchaseRequestItemId: string;

  reservedQuantity: number;
  consumedQuantity: number;
  remainingQuantity: number;

  reservationDate: string;
  expirationDate?: string | null;

  status: number;
  statusName: string;
  description?: string | null;
  isExpired: boolean;
};

export type PurchaseRequestStockStatusLine = {
  purchaseRequestItemId: string;
  lineNumber: number;

  inventoryItemId: string;
  inventoryItemCode: string;
  inventoryItemName: string;

  materialDescription: string;
  unit: string;

  requestedQuantity: number;
  reservedQuantity: number;
  issuedQuantity: number;

  remainingRequestQuantity: number;
  unreservedQuantity: number;

  warehouseQuantity: number;
  warehouseReservedQuantity: number;
  warehouseAvailableQuantity: number;

  reservableQuantity: number;
  missingQuantity: number;
};

export type PurchaseRequestStockStatus = {
  purchaseRequestId: string;
  requestNumber: string;
  requestType: number;
  status: number;

  companyId: string;
  projectId: string;

  warehouseId?: string | null;
  warehouseName?: string | null;

  totalRequestedQuantity: number;
  totalReservedQuantity: number;
  totalIssuedQuantity: number;
  totalRemainingQuantity: number;
  totalUnreservedQuantity: number;
  totalReservableQuantity: number;
  totalMissingQuantity: number;

  isFullyReserved: boolean;
  isFullyIssued: boolean;

  lines: PurchaseRequestStockStatusLine[];
};

export type ReservePurchaseRequestPayload = {
  warehouseId: string;
  expirationDate?: string | null;
  description?: string | null;
};

export type ReservePurchaseRequestResponse = {
  purchaseRequestId: string;
  requestNumber: string;
  warehouseId: string;
  reservedLineCount: number;
  totalNewlyReservedQuantity: number;
  totalMissingQuantity: number;
};

export type IssueStockReservationPayload = {
  stockReservationId: string;
  quantity: number;
  movementDate: string;
  description?: string | null;
};

export type IssueStockReservationResponse = {
  stockMovementId: string;
  accountingVoucherId?: string | null;
  accountingVoucherNumber?: string | null;
  averageUnitCost?: number | null;
  totalCost?: number | null;
  reservationRemaining?: number | null;
  requestStatus?: number | null;
  accountingVoucherStatus?: number | null;
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

  getReservations(id: string) {
    return apiClient<StockReservationListItem[]>(
      `purchase-requests/${id}/reservations`,
    );
  },

  getStockStatus(
    id: string,
    warehouseId?: string,
  ) {
    const query = warehouseId
      ? `?warehouseId=${encodeURIComponent(
          warehouseId,
        )}`
      : "";

    return apiClient<PurchaseRequestStockStatus>(
      `purchase-requests/${id}/stock-status${query}`,
    );
  },

  reserve(
    id: string,
    payload: ReservePurchaseRequestPayload,
  ) {
    return apiClient<ReservePurchaseRequestResponse>(
      `purchase-requests/${id}/reserve`,
      {
        method: "POST",
        body: payload,
      },
    );
  },

  issueReservation(
    id: string,
    payload: IssueStockReservationPayload,
  ) {
    return apiClient<IssueStockReservationResponse>(
      `purchase-requests/${id}/reservations/issue`,
      {
        method: "POST",
        body: payload,
      },
    );
  },

  releaseReservation(
    id: string,
    reservationId: string,
    reason?: string,
  ) {
    return apiClient(
      `purchase-requests/${id}/reservations/${reservationId}/release`,
      {
        method: "POST",
        body: {
          reason: reason || null,
        },
      },
    );
  },

  cancelReservation(
    id: string,
    reservationId: string,
    reason?: string,
  ) {
    return apiClient(
      `purchase-requests/${id}/reservations/${reservationId}/cancel`,
      {
        method: "POST",
        body: {
          reason: reason || null,
        },
      },
    );
  },
};
