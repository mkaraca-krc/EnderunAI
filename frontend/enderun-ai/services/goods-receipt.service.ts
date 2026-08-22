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
  /** Red / hasar gerekçesi — reddedilen/hasarlı miktar varsa dolu. */
  rejectionReason?: string | null;
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

export type GoodsReceiptInventoryOption = {
  id: string;
  code: string;
  name: string;
  category?: string | null;
  brand?: string | null;
  model?: string | null;
  unit: string;
};

export type UpdateGoodsReceiptItemRequest = {
  id: string;
  inventoryItemId?: string | null;
  deliveredQuantity: number;
  acceptedQuantity: number;
  rejectedQuantity: number;
  damagedQuantity: number;
  lotNumber?: string | null;
  serialNumber?: string | null;
  productionDate?: string | null;
  expiryDate?: string | null;
  warrantyEndDate?: string | null;
  shelfLocation?: string | null;
  notes?: string | null;
  /**
   * Red / hasar gerekçesi. Reddedilen ya da hasarlı miktar varsa
   * kesinleştirmede zorunlu.
   */
  rejectionReason?: string | null;
};

export type GoodsReceiptActionResponse = {
  id: string;
  receiptNumber: string;
  status: GoodsReceiptStatus;
  stockMovementCount: number;
  message: string;
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
  /**
   * SAYFALANMIŞ mal kabul listesi.
   *
   * Tümünü çekip ön yüzde dilimlemek yerine sunucu kırpıyor: kayıt
   * sayısı zamanla büyüyen bir küme ve taşınan veri de tarayıcıdaki
   * dizi de doğrusal büyürdü. Arama da sunucuda ve katlanmış —
   * ekranla aynı kural (bkz. enderun_fold / lib/search/fold.ts).
   */
  getAll(
    params?: {
      companyId?: string;
      warehouseId?: string;
      purchaseOrderId?: string;
      status?: number;
      search?: string;
      page?: number;
      pageSize?: number;
    },
    signal?: AbortSignal,
  ) {
    return apiClient<{
      items: GoodsReceiptListItem[];
      total: number;
      take: number;
      hasMore: boolean;
      page: number;
    }>(`goods-receipts${buildQuery(params)}`, { signal });
  },

  /** Özet kartları — süzgeçlere uyan TÜM kayıtlardan, sayfadan değil. */
  getSummary(
    params?: {
      companyId?: string;
      warehouseId?: string;
      purchaseOrderId?: string;
      search?: string;
    },
    signal?: AbortSignal,
  ) {
    return apiClient<{
      total: number;
      draft: number;
      posted: number;
      cancelled: number;
      acceptedQuantity: number;
    }>(`goods-receipts/ozet${buildQuery(params)}`, { signal });
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

  getInventoryOptions(id: string, search?: string) {
    const query = new URLSearchParams();
    if (search?.trim()) {
      query.set("search", search.trim());
    }

    const suffix = query.size > 0 ? `?${query.toString()}` : "";
    return apiClient<GoodsReceiptInventoryOption[]>(
      `goods-receipts/${id}/inventory-options${suffix}`,
    );
  },

  updateDraft(
    id: string,
    items: UpdateGoodsReceiptItemRequest[],
  ) {
    return apiClient<GoodsReceiptActionResponse>(
      `goods-receipts/${id}/draft`,
      {
        method: "PUT",
        body: { items },
      },
    );
  },

  post(id: string) {
    return apiClient<GoodsReceiptActionResponse>(
      `goods-receipts/${id}/post`,
      { method: "POST" },
    );
  },

  cancel(id: string, reason: string) {
    return apiClient<GoodsReceiptActionResponse>(
      `goods-receipts/${id}/cancel`,
      {
        method: "POST",
        body: { reason },
      },
    );
  },
};



/** Alış iadesi belge durumları. */
export const PURCHASE_RETURN_STATUS = {
  Draft: 0,
  Sent: 1,
  Completed: 2,
  Cancelled: 3,
} as const;

export type PurchaseReturnListItem = {
  id: string;
  companyId: string;
  returnNumber: string;
  returnDate: string;
  status: number;
  statusName: string;
  currencyCode: string;
  totalAmount: number;
  goodsReceiptId: string;
  receiptNumber: string;
  purchaseOrderId: string;
  orderNumber: string;
  supplierCurrentAccountId: string;
  supplierName: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  itemCount: number;
  totalQuantity: number;
};

export type PurchaseReturnDetail = PurchaseReturnListItem & {
  exchangeRate: number;
  notes?: string | null;
  sentAtUtc?: string | null;
  completedAtUtc?: string | null;
  cancelledAtUtc?: string | null;
  cancellationReason?: string | null;
  items: {
    id: string;
    lineNumber: number;
    materialDescription: string;
    unit: string;
    quantity: number;
    unitPrice: number;
    lineTotal: number;
    reasonKind: number;
    reasonKindName: string;
    reason: string;
  }[];
};

/**
 * Alış iadesi belgeleri — mal kabulde reddedilen/hasarlı miktar için
 * kabul kesinleşirken otomatik doğar.
 */
export const purchaseReturnService = {
  getAll(params: {
    companyId?: string;
    projectId?: string;
    goodsReceiptId?: string;
    status?: number;
    openOnly?: boolean;
  } = {}) {
    const query = new URLSearchParams();

    if (params.companyId) query.set("companyId", params.companyId);
    if (params.projectId) query.set("projectId", params.projectId);
    if (params.goodsReceiptId)
      query.set("goodsReceiptId", params.goodsReceiptId);
    if (params.status !== undefined) query.set("status", String(params.status));
    if (params.openOnly) query.set("openOnly", "true");

    const suffix = query.toString() ? `?${query.toString()}` : "";
    return apiClient<PurchaseReturnListItem[]>(`purchase-returns${suffix}`);
  },

  getById(id: string) {
    return apiClient<PurchaseReturnDetail>(`purchase-returns/${id}`);
  },

  /** Taslak → Gönderildi → Kapandı, ya da İptal (gerekçe zorunlu). */
  advance(id: string, status: number, note?: string | null) {
    return apiClient<{ message: string; status: number }>(
      `purchase-returns/${id}/durum`,
      { method: "POST", body: { status, note: note ?? null } },
    );
  },
};
