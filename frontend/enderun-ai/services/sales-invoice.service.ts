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
  /** Doluysa STOKLU satır: kesinleştirmede depodan mal çıkar. */
  inventoryItemId?: string | null;
  inventoryItemCode?: string | null;
  /** Dondurulmuş maliyet ve kâr — yetki yoksa null gelir. */
  lineCost?: number | null;
  lineProfit?: number | null;
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
  isReturn?: boolean;
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
  /** Stoklu satırların çıktığı depo. */
  warehouseId?: string | null;
  warehouseName?: string | null;
  /** Maliyeti yetki nedeniyle gizlenen satır sayısı. */
  hiddenCostCount: number;
  items: SalesInvoiceItem[];
  /** Bu belge bir iade faturası mı. */
  isReturn: boolean;
  originalInvoiceId?: string | null;
  originalInvoiceNumber?: string | null;
  /** İptalde üretilen ters fiş. */
  reversalVoucherId?: string | null;
  reversalVoucherNumber?: string | null;
};

export type SalesInvoiceItemPayload = {
  description: string;
  quantity: number;
  unit: string;
  unitPrice: number;
  vatRate: number;
  /**
   * Stok kartı — doluysa satır STOKLUDUR: kesinleştirmede depodan mal
   * çıkar ve fişe 621 maliyet satırı eklenir. Boşsa hizmet satırıdır,
   * yalnız gelir yazılır. İkisi aynı faturada karışabilir.
   */
  inventoryItemId?: string | null;
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
  /** Stoklu kalem varsa zorunlu. Merkez depoyla sınırlı değil. */
  warehouseId?: string | null;
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

  createReturn(
    id: string,
    payload: {
      invoiceNumber: string;
      invoiceDate: string;
      items: { originalItemId: string; quantity: number }[];
      description?: string | null;
    }
  ) {
    return apiClient<SalesInvoiceDetail>(`${root}/${id}/returns`, {
      method: "POST",
      body: payload,
    });
  },

  cancel(id: string, reason: string) {
    return apiClient<SalesInvoiceActionResult>(`${root}/${id}/cancel`, {
      method: "POST",
      body: { reason },
    });
  },
};
