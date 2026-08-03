import { apiClient } from "@/lib/api/api-client";

export const SupplierInvoiceStatus = {
  Draft: 0,
  PendingApproval: 1,
  Approved: 2,
  Rejected: 3,
  Cancelled: 4,
} as const;

export const SUPPLIER_INVOICE_STATUS_LABELS: Record<number, string> = {
  0: "Taslak",
  1: "Onay Bekliyor",
  2: "Onaylandı",
  3: "Reddedildi",
  4: "İptal",
};

/** erp-status.{renk} sınıfıyla eşleşir. */
export const SUPPLIER_INVOICE_STATUS_COLORS: Record<number, string> = {
  0: "gray",
  1: "yellow",
  2: "green",
  3: "gray",
  4: "gray",
};

export const MATCH_STATUS_LABELS: Record<number, string> = {
  0: "Uygulanmadı",
  1: "Eşleşti",
  2: "Tolerans Dışı",
};

export const MATCH_STATUS_COLORS: Record<number, string> = {
  0: "gray",
  1: "green",
  2: "yellow",
};

export type SupplierInvoiceItem = {
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
  purchaseOrderItemId?: string | null;
};

export type SupplierInvoiceListItem = {
  id: string;
  internalNumber: string;
  invoiceNumber: string;
  invoiceDate: string;
  supplierCurrentAccountId: string;
  supplierTitle: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  currencyCode: string;
  subtotal: number;
  vatTotal: number;
  grandTotal: number;
  status: number;
  matchStatus: number;
  requiresGmApproval: boolean;
  purchaseOrderNumber?: string | null;
  accountingVoucherNumber?: string | null;
};

export type SupplierInvoiceDetail = {
  id: string;
  companyId: string;
  internalNumber: string;
  invoiceNumber: string;
  invoiceDate: string;
  dueDate?: string | null;
  supplierCurrentAccountId: string;
  supplierTitle: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  purchaseOrderId?: string | null;
  purchaseOrderNumber?: string | null;
  goodsReceiptId?: string | null;
  goodsReceiptNumber?: string | null;
  currencyCode: string;
  exchangeRate: number;
  subtotal: number;
  vatTotal: number;
  grandTotal: number;
  description?: string | null;
  status: number;
  matchStatus: number;
  matchDifferenceAmount: number;
  matchNote?: string | null;
  requiresGmApproval: boolean;
  submittedAtUtc?: string | null;
  approvedAtUtc?: string | null;
  rejectedAtUtc?: string | null;
  rejectionReason?: string | null;
  accountingVoucherId?: string | null;
  accountingVoucherNumber?: string | null;
  items: SupplierInvoiceItem[];
};

export type SupplierInvoiceItemPayload = {
  description: string;
  quantity: number;
  unit: string;
  unitPrice: number;
  vatRate: number;
  purchaseOrderItemId?: string | null;
};

export type CreateSupplierInvoicePayload = {
  companyId: string;
  supplierCurrentAccountId: string;
  projectId: string;
  purchaseOrderId?: string | null;
  goodsReceiptId?: string | null;
  invoiceNumber: string;
  invoiceDate: string;
  dueDate?: string | null;
  currencyCode: string;
  exchangeRate: number;
  description?: string | null;
  items: SupplierInvoiceItemPayload[];
};

export type SupplierInvoiceActionResult = {
  id: string;
  internalNumber: string;
  status: number;
  message: string;
};

const root = "supplier-invoices";

export const supplierInvoiceService = {
  getAll(filters: {
    companyId?: string;
    status?: number;
    projectId?: string;
    supplierId?: string;
    search?: string;
  } = {}) {
    const params = new URLSearchParams();
    if (filters.companyId) params.set("companyId", filters.companyId);
    if (filters.status !== undefined) params.set("status", String(filters.status));
    if (filters.projectId) params.set("projectId", filters.projectId);
    if (filters.supplierId) params.set("supplierId", filters.supplierId);
    if (filters.search) params.set("search", filters.search);
    const query = params.toString();

    return apiClient<SupplierInvoiceListItem[]>(`${root}${query ? `?${query}` : ""}`);
  },

  getById(id: string) {
    return apiClient<SupplierInvoiceDetail>(`${root}/${id}`);
  },

  create(payload: CreateSupplierInvoicePayload) {
    return apiClient<SupplierInvoiceDetail>(root, { method: "POST", body: payload });
  },

  submit(id: string) {
    return apiClient<SupplierInvoiceActionResult>(`${root}/${id}/submit`, { method: "POST" });
  },

  approve(id: string) {
    return apiClient<SupplierInvoiceActionResult>(`${root}/${id}/approve`, { method: "POST" });
  },

  reject(id: string, reason: string) {
    return apiClient<SupplierInvoiceActionResult>(`${root}/${id}/reject`, {
      method: "POST",
      body: { reason },
    });
  },

  cancel(id: string) {
    return apiClient<SupplierInvoiceActionResult>(`${root}/${id}/cancel`, { method: "POST" });
  },
};

export type CompanyFinanceSettings = {
  companyId: string;
  gmApprovalThresholdTry: number;
  threeWayTolerancePercent: number;
  defaultVatRate: number;
  vatInAccountId?: string | null;
  vatOutAccountId?: string | null;
  salesAccountId?: string | null;
  expenseAccountId?: string | null;
  payablesAccountId?: string | null;
  receivablesAccountId?: string | null;
  factoringExpenseAccountId?: string | null;
  deductionAccountId?: string | null;
};

export const financeSettingsService = {
  get() {
    return apiClient<CompanyFinanceSettings>("company-settings/finance-settings");
  },
  update(payload: Omit<CompanyFinanceSettings, "companyId">) {
    return apiClient<{ message: string; settings: CompanyFinanceSettings }>(
      "company-settings/finance-settings",
      { method: "PUT", body: payload }
    );
  },
};
