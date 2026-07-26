import { apiClient } from "@/lib/api/api-client";

export type RfqStatus = 0 | 1 | 2 | 3 | 4 | 5;

export type RfqListItem = {
  id: string;
  companyId: string;
  purchaseRequestId: string;
  purchaseRequestNumber: string;
  rfqNumber: string;
  title: string;
  issueDate: string;
  responseDeadline?: string | null;
  status: RfqStatus;
  currency: string;
  itemCount: number;
  supplierCount: number;
  responseCount: number;
};

export type RfqItem = {
  id: string;
  lineNumber: number;
  materialDescription: string;
  quantity: number;
  unit: string;
  requestedDeliveryDate?: string | null;
  notes?: string | null;
};

export type RfqSupplier = {
  id: string;
  supplierCurrentAccountId: string;
  supplierCode: string;
  supplierTitle: string;
  status: number;
  sentAtUtc?: string | null;
  respondedAtUtc?: string | null;
  contactName?: string | null;
  contactEmail?: string | null;
  quotationId?: string | null;
  quotationTotal?: number | null;
  deliveryDays?: number | null;
  paymentTerm?: string | null;
};

export type RfqDetail = {
  id: string;
  companyId: string;
  purchaseRequestId: string;
  purchaseRequestNumber: string;
  rfqNumber: string;
  title: string;
  issueDate: string;
  responseDeadline?: string | null;
  status: RfqStatus;
  currency: string;
  description?: string | null;
  notes?: string | null;
  items: RfqItem[];
  suppliers: RfqSupplier[];
};

export type CreateRfqPayload = {
  title: string;
  responseDeadline?: string | null;
  currency: string;
  description?: string | null;
  notes?: string | null;
  supplierCurrentAccountIds: string[];
};

export type SaveQuotationItemPayload = {
  rfqItemId: string;
  quantity: number;
  unitPrice: number;
  discountRate: number;
  brand?: string | null;
  model?: string | null;
  deliveryDays?: number | null;
  notes?: string | null;
};

export type SaveQuotationPayload = {
  supplierQuotationNumber?: string | null;
  quotationDate: string;
  validUntil?: string | null;
  currency: string;
  exchangeRate: number;
  deliveryDays?: number | null;
  paymentTerm?: string | null;
  notes?: string | null;
  items: SaveQuotationItemPayload[];
};

export type RfqComparisonSupplier = {
  rfqSupplierId: string;
  supplierCurrentAccountId: string;
  supplierTitle: string;
  hasQuotation: boolean;
  currency: string;
  grandTotal: number;
  deliveryDays?: number | null;
  paymentTerm?: string | null;
  items: {
    rfqItemId: string;
    materialDescription: string;
    requestedQuantity: number;
    unit: string;
    unitPrice: number;
    netUnitPrice: number;
    totalPrice: number;
    brand?: string | null;
    model?: string | null;
    deliveryDays?: number | null;
  }[];
};

export type AwardRfqResponse = {
  rfqId: string;
  rfqSupplierId: string;
  supplierCurrentAccountId: string;
  supplierTitle: string;
  grandTotal: number;
};

export type RfqComparison = {
  rfqId: string;
  rfqNumber: string;
  lowestTotal: number;
  lowestSupplierId?: string | null;
  lowestSupplierTitle?: string | null;
  suppliers: RfqComparisonSupplier[];
};

function buildQuery(params?: {
  companyId?: string;
  status?: number;
}) {
  const query = new URLSearchParams();

  if (params?.companyId) {
    query.set("companyId", params.companyId);
  }

  if (params?.status !== undefined) {
    query.set("status", String(params.status));
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const rfqService = {
  getAll(params?: {
    companyId?: string;
    status?: number;
  }) {
    return apiClient<RfqListItem[]>(
      `rfq${buildQuery(params)}`
    );
  },

  getById(id: string) {
    return apiClient<RfqDetail>(`rfq/${id}`);
  },

  createFromPurchaseRequest(
    purchaseRequestId: string,
    payload: CreateRfqPayload
  ) {
    return apiClient<{
      id: string;
      rfqNumber: string;
      itemCount: number;
      supplierCount: number;
    }>(
      `rfq/create-from-purchase-request/${purchaseRequestId}`,
      {
        method: "POST",
        body: payload,
      }
    );
  },

  send(id: string) {
    return apiClient<{ message: string }>(
      `rfq/${id}/send`,
      {
        method: "POST",
      }
    );
  },

  saveQuotation(
    rfqId: string,
    rfqSupplierId: string,
    payload: SaveQuotationPayload
  ) {
    return apiClient<{ message: string }>(
      `rfq/${rfqId}/suppliers/${rfqSupplierId}/quotation`,
      {
        method: "POST",
        body: payload,
      }
    );
  },

  getComparison(id: string) {
    return apiClient<RfqComparison>(
      `rfq/${id}/comparison`
    );
  },

  award(id: string, rfqSupplierId: string) {
    return apiClient<AwardRfqResponse>(
      `rfq/${id}/award/${rfqSupplierId}`,
      {
        method: "POST",
      }
    );
  },

  close(id: string) {
    return apiClient<{ message: string }>(
      `rfq/${id}/close`,
      {
        method: "POST",
      }
    );
  },
};
