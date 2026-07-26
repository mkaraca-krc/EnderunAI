import { apiClient } from "@/lib/api/api-client";

export type StockReservationStatus =
  | 0
  | 1
  | 2
  | 3
  | 4;

export type StockReservationManagementListItem = {
  id: string;
  reservationNumber: string;

  companyId: string;
  companyName: string;

  projectId: string;
  projectCode: string;
  projectName: string;

  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;

  inventoryItemId: string;
  inventoryItemCode: string;
  inventoryItemName: string;
  unit: string;

  purchaseRequestId: string;
  requestNumber: string;
  purchaseRequestItemId: string;
  purchaseRequestStatus: number;

  requestedQuantity: number;
  reservedQuantity: number;
  consumedQuantity: number;
  remainingQuantity: number;

  reservationDate: string;
  expirationDate?: string | null;

  status: StockReservationStatus;
  statusName: string;

  description?: string | null;
  isExpired: boolean;
  isActive: boolean;
};

export type StockReservationListParams = {
  companyId?: string;
  projectId?: string;
  warehouseId?: string;
  status?: number;
  search?: string;
  activeOnly?: boolean;
  expiredOnly?: boolean;
};

function buildQuery(
  params?: StockReservationListParams,
): string {
  const query = new URLSearchParams();

  if (params?.companyId) {
    query.set("companyId", params.companyId);
  }

  if (params?.projectId) {
    query.set("projectId", params.projectId);
  }

  if (params?.warehouseId) {
    query.set("warehouseId", params.warehouseId);
  }

  if (params?.status !== undefined) {
    query.set("status", String(params.status));
  }

  if (params?.search?.trim()) {
    query.set("search", params.search.trim());
  }

  if (params?.activeOnly !== undefined) {
    query.set(
      "activeOnly",
      String(params.activeOnly),
    );
  }

  if (params?.expiredOnly !== undefined) {
    query.set(
      "expiredOnly",
      String(params.expiredOnly),
    );
  }

  const value = query.toString();

  return value ? `?${value}` : "";
}

export const stockReservationService = {
  getAll(params?: StockReservationListParams) {
    return apiClient<
      StockReservationManagementListItem[]
    >(`stock-reservations${buildQuery(params)}`);
  },
};
