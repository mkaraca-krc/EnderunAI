import { apiClient } from "@/lib/api/api-client";

export type PurchaseOrderStatus =
  | 0
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6;

export type PurchaseOrderListItem = {
  id: string;
  companyId: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  rfqId: string;
  rfqNumber: string;
  supplierCurrentAccountId: string;
  supplierCode: string;
  supplierTitle: string;
  orderNumber: string;
  orderDate: string;
  expectedDeliveryDate?: string | null;
  status: PurchaseOrderStatus;
  currency: string;
  grandTotal: number;
  itemCount: number;
};

export type PurchaseOrderItem = {
  id: string;
  rfqItemId?: string | null;
  rfqSupplierQuotationItemId?: string | null;
  lineNumber: number;
  materialDescription: string;
  /** Tedarikçinin VERDİĞİ marka (kabul edilen tekliften). */
  brand?: string | null;
  model?: string | null;
  quantity: number;
  receivedQuantity: number;
  unit: string;
  unitPrice: number;
  discountRate: number;
  netUnitPrice: number;
  totalPrice: number;
  deliveryDays?: number | null;
  expectedDeliveryDate?: string | null;
  notes?: string | null;

  /** Talep edenin İSTEDİĞİ marka; brand ile yan yana gösterilir. */
  requestedBrand?: string | null;
  brandIrrelevant?: boolean;
};

export type PurchaseOrderDetail = {
  id: string;
  companyId: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  rfqId: string;
  rfqNumber: string;
  purchaseRequestId: string;
  purchaseRequestNumber: string;
  supplierCurrentAccountId: string;
  supplierCode: string;
  supplierTitle: string;
  supplierAuthorizedPerson?: string | null;
  supplierPhone?: string | null;
  supplierEmail?: string | null;
  supplierAddress?: string | null;
  orderNumber: string;
  orderDate: string;
  expectedDeliveryDate?: string | null;
  status: PurchaseOrderStatus;
  currency: string;
  exchangeRate: number;
  paymentTerm?: string | null;
  deliveryAddress?: string | null;
  description?: string | null;
  notes?: string | null;
  subtotal: number;
  discountTotal: number;
  grandTotal: number;
  approvedAtUtc?: string | null;
  cancelledAtUtc?: string | null;
  cancellationReason?: string | null;
  items: PurchaseOrderItem[];
};


export type PurchaseOrderActionResponse = {
  id: string;
  orderNumber: string;
  status: PurchaseOrderStatus;
  message: string;
};

export type CreatePurchaseOrderFromRfqResponse = {
  id: string;
  orderNumber: string;
  rfqId: string;
  supplierCurrentAccountId: string;
  supplierTitle: string;
  grandTotal: number;
  currency: string;
};

function buildQuery(params?: {
  companyId?: string;
  projectId?: string;
  status?: number;
}) {
  const query = new URLSearchParams();

  if (params?.companyId) {
    query.set("companyId", params.companyId);
  }

  if (params?.projectId) {
    query.set("projectId", params.projectId);
  }

  if (params?.status !== undefined) {
    query.set("status", String(params.status));
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const purchaseOrderService = {
  getAll(params?: {
    companyId?: string;
    projectId?: string;
    status?: number;
  }) {
    return apiClient<PurchaseOrderListItem[]>(
      `purchase-orders${buildQuery(params)}`
    );
  },

  getById(id: string) {
    return apiClient<PurchaseOrderDetail>(
      `purchase-orders/${id}`
    );
  },

  createFromRfq(rfqId: string) {
    return apiClient<CreatePurchaseOrderFromRfqResponse>(
      `purchase-orders/create-from-rfq/${rfqId}`,
      {
        method: "POST",
      }
    );
  },

  submit(id: string) {
    return apiClient<PurchaseOrderActionResponse>(
      `purchase-orders/${id}/submit`,
      {
        method: "POST",
      }
    );
  },

  approve(id: string) {
    return apiClient<PurchaseOrderActionResponse>(
      `purchase-orders/${id}/approve`,
      {
        method: "POST",
      }
    );
  },

  reject(id: string, reason: string) {
    return apiClient<PurchaseOrderActionResponse>(
      `purchase-orders/${id}/reject`,
      {
        method: "POST",
        body: {
          reason,
        },
      }
    );
  },

  cancel(id: string, reason: string) {
    return apiClient<PurchaseOrderActionResponse>(
      `purchase-orders/${id}/cancel`,
      {
        method: "POST",
        body: {
          reason,
        },
      }
    );
  },
};
