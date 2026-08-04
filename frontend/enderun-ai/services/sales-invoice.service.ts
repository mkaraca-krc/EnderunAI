import { apiClient } from "@/lib/api/api-client";

export const SalesInvoiceStatus = {
  Draft: 0,
  Posted: 1,
  Cancelled: 2,
} as const;

export const SALES_INVOICE_STATUS_LABELS: Record<number, string> = {
  0: "Taslak",
  1: "Kesinleşti",
  2: "İptal",
};

/** erp-status.{renk} sınıfıyla eşleşir. */
export const SALES_INVOICE_STATUS_COLORS: Record<number, string> = {
  0: "gray",
  1: "green",
  2: "gray",
};

/** Faturayı hangi katman okudu. */
export const PARSE_SOURCE_LABELS: Record<number, string> = {
  0: "Elle girildi",
  1: "Standart okuma",
  2: "AI ile okundu",
};

export type SalesInvoiceItem = {
  id: string;
  lineNumber: number;
  description: string;
  quantity: number;
  unit: string;
  unitPrice: number;
  vatRate: number;
  lineSubtotal: number;
  vatAmount: number;
  lineTotal: number;
};

export type SalesInvoiceListItem = {
  id: string;
  internalNumber: string;
  officialInvoiceNumber?: string | null;
  invoiceDate: string;
  customerCurrentAccountId: string;
  customerTitle: string;
  projectId?: string | null;
  projectCode?: string | null;
  projectName?: string | null;
  currencyCode: string;
  subtotal: number;
  vatTotal: number;
  withholdingAmount: number;
  grandTotal: number;
  netReceivableAmount: number;
  status: number;
  requiresManualReview: boolean;
  parseSource?: number | null;
  accountingVoucherNumber?: string | null;
};

export type SalesInvoiceDetail = {
  id: string;
  companyId: string;
  internalNumber: string;
  officialInvoiceNumber?: string | null;
  invoiceDate: string;
  dueDate?: string | null;
  customerCurrentAccountId: string;
  customerTitle: string;
  projectId?: string | null;
  projectCode?: string | null;
  projectName?: string | null;
  currencyCode: string;
  exchangeRate: number;
  subtotal: number;
  vatTotal: number;
  withholdingAmount: number;
  grandTotal: number;
  netReceivableAmount: number;
  description?: string | null;
  notes?: string | null;
  status: number;
  postedAtUtc?: string | null;
  cancelledAtUtc?: string | null;
  cancellationReason?: string | null;
  requiresManualReview: boolean;
  parseSource?: number | null;
  hasSourceXml: boolean;
  accountingVoucherId?: string | null;
  accountingVoucherNumber?: string | null;
  items: SalesInvoiceItem[];
};

export type SalesInvoiceItemPayload = {
  description: string;
  quantity: number;
  unit: string;
  unitPrice: number;
  vatRate: number;
};

export type CreateSalesInvoicePayload = {
  companyId: string;
  customerCurrentAccountId: string;
  projectId?: string | null;
  officialInvoiceNumber?: string | null;
  invoiceDate: string;
  dueDate?: string | null;
  currencyCode: string;
  exchangeRate: number;
  withholdingAmount: number;
  description?: string | null;
  notes?: string | null;
  items: SalesInvoiceItemPayload[];
};

export type UpdateSalesInvoicePayload = Omit<CreateSalesInvoicePayload, "companyId">;

export type SalesInvoiceActionResult = {
  id: string;
  internalNumber: string;
  status: number;
  message: string;
};

const root = "sales-invoices";

export const salesInvoiceService = {
  getAll(
    filters: {
      companyId?: string;
      status?: number;
      projectId?: string;
      customerId?: string;
      search?: string;
    } = {}
  ) {
    const params = new URLSearchParams();
    if (filters.companyId) params.set("companyId", filters.companyId);
    if (filters.status !== undefined) params.set("status", String(filters.status));
    if (filters.projectId) params.set("projectId", filters.projectId);
    if (filters.customerId) params.set("customerId", filters.customerId);
    if (filters.search) params.set("search", filters.search);
    const query = params.toString();

    return apiClient<SalesInvoiceListItem[]>(`${root}${query ? `?${query}` : ""}`);
  },

  getById(id: string) {
    return apiClient<SalesInvoiceDetail>(`${root}/${id}`);
  },

  create(payload: CreateSalesInvoicePayload) {
    return apiClient<SalesInvoiceDetail>(root, { method: "POST", body: payload });
  },

  update(id: string, payload: UpdateSalesInvoicePayload) {
    return apiClient<SalesInvoiceDetail>(`${root}/${id}`, { method: "PUT", body: payload });
  },

  post(id: string) {
    return apiClient<SalesInvoiceActionResult>(`${root}/${id}/post`, { method: "POST" });
  },

  cancel(id: string, reason: string) {
    return apiClient<SalesInvoiceActionResult>(`${root}/${id}/cancel`, {
      method: "POST",
      body: { reason },
    });
  },
};
