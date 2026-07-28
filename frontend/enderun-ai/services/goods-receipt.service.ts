import { apiClient } from "@/lib/api/api-client";

export type GoodsReceiptStatus = 0 | 1 | 2;

export type GoodsReceiptListItem = {
  id: string;
  companyId: string;
  purchaseOrderId: string;
  purchaseOrderNumber: string;
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  supplierCurrentAccountId: string;
  supplierTitle: string;
  receiptNumber: string;
  receiptDate: string;
  status: GoodsReceiptStatus;
  dispatchNoteNumber?: string | null;
  receivedByName: string;
  itemCount: number;
  deliveredQuantity: number;
  acceptedQuantity: number;
  rejectedQuantity: number;
  damagedQuantity: number;
};

export type GoodsReceiptItem = {
  id: string;
  purchaseOrderItemId: string;
  inventoryItemId?: string | null;
  inventoryItemCode?: string | null;
  inventoryItemName?: string | null;
  lineNumber: number;
  materialDescription: string;
  brand?: string | null;
  model?: string | null;
  orderedQuantity: number;
  previouslyReceivedQuantity: number;
  deliveredQuantity: number;
  acceptedQuantity: number;
  rejectedQuantity: number;
  damagedQuantity: number;
  unit: string;
  lotNumber?: string | null;
  serialNumber?: string | null;
  productionDate?: string | null;
  expiryDate?: string | null;
  warrantyEndDate?: string | null;
  shelfLocation?: string | null;
  notes?: string | null;
};

export type GoodsReceiptDetail = {
  id: string;
  companyId: string;
  purchaseOrderId: string;
  purchaseOrderNumber: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  supplierCurrentAccountId: string;
  supplierCode: string;
  supplierTitle: string;
  receiptNumber: string;
  receiptDate: string;
  status: GoodsReceiptStatus;
  dispatchNoteNumber?: string | null;
  dispatchNoteDate?: string | null;
  invoiceNumber?: string | null;
  invoiceDate?: string | null;
  receivedByName: string;
  vehiclePlate?: string | null;
  driverName?: string | null;
  description?: string | null;
  notes?: string | null;
  postedAtUtc?: string | null;
  cancelledAtUtc?: string | null;
  cancellationReason?: string | null;

  accountingVoucherId?: string | null;
  accountingVoucherNumber?: string | null;
  accountingVoucherStatus?: number | null;
  accountingVoucherTotal?: number | null;

  items: GoodsReceiptItem[];
};

export type CreateGoodsReceiptRequest = {
  warehouseId: string;
  receiptDate: string;
  receivedByName: string;
  dispatchNoteNumber?: string | null;
  dispatchNoteDate?: string | null;
  invoiceNumber?: string | null;
  invoiceDate?: string | null;
  vehiclePlate?: string | null;
  driverName?: string | null;
  description?: string | null;
  notes?: string | null;
};

export type CreateGoodsReceiptResponse = {
  id: string;
  receiptNumber: string;
  purchaseOrderId: string;
  purchaseOrderNumber: string;
  warehouseId: string;
  warehouseName: string;
  itemCount: number;
  status: GoodsReceiptStatus;
};

function buildQuery(params?: {
  companyId?: string;
  warehouseId?: string;
  purchaseOrderId?: string;
  status?: number;
}) {
  const query = new URLSearchParams();

  if (params?.companyId) {
    query.set("companyId", params.companyId);
  }

  if (params?.warehouseId) {
    query.set("warehouseId", params.warehouseId);
  }

  if (params?.purchaseOrderId) {
    query.set("purchaseOrderId", params.purchaseOrderId);
  }

  if (params?.status !== undefined) {
    query.set("status", String(params.status));
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const goodsReceiptService = {
  getAll(params?: {
    companyId?: string;
    warehouseId?: string;
    purchaseOrderId?: string;
    status?: number;
  }) {
    return apiClient<GoodsReceiptListItem[]>(
      `goods-receipts${buildQuery(params)}`,
    );
  },

  getById(id: string) {
    return apiClient<GoodsReceiptDetail>(
      `goods-receipts/${id}`,
    );
  },

  createFromPurchaseOrder(
    purchaseOrderId: string,
    payload: CreateGoodsReceiptRequest,
  ) {
    return apiClient<CreateGoodsReceiptResponse>(
      `goods-receipts/create-from-purchase-order/${purchaseOrderId}`,
      {
        method: "POST",
        body: payload,
      },
    );
  },
};
