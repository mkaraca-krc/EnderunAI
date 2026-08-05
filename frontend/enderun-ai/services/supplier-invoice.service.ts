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

export const SupplierInvoiceType = {
  /** Stok kartına bağlı malzeme alışı; onayda depoya girer. */
  Stock: 0,
  /** Elektrik, kira, müşavirlik gibi giderler; stoğa girmez. */
  Expense: 1,
} as const;

export const SUPPLIER_INVOICE_TYPE_LABELS: Record<number, string> = {
  0: "Alış (Stok)",
  1: "Gider",
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
  inventoryItemId?: string | null;
  inventoryItemCode?: string | null;
  inventoryItemName?: string | null;
  warehouseId?: string | null;
  warehouseName?: string | null;
  expenseAccountId?: string | null;
  expenseAccountCode?: string | null;
  expenseAccountName?: string | null;
  costCenterCode?: string | null;
};

export type SupplierInvoiceListItem = {
  id: string;
  internalNumber: string;
  invoiceNumber: string;
  invoiceDate: string;
  supplierCurrentAccountId: string;
  supplierTitle: string;
  projectId?: string | null;
  projectCode?: string | null;
  projectName?: string | null;
  invoiceType: number;
  invoiceTypeName: string;
  costCenterCode?: string | null;
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
  projectId?: string | null;
  projectCode?: string | null;
  projectName?: string | null;
  invoiceType: number;
  invoiceTypeName: string;
  costCenterCode?: string | null;
  warehouseId?: string | null;
  warehouseName?: string | null;
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
  /** ALIŞ faturasında stok girişi yapılacaksa kalemin stok kartı. */
  inventoryItemId?: string | null;
  /** Kalemin deposu; boşsa faturanın deposu kullanılır. */
  warehouseId?: string | null;
  /** GİDER faturasında zorunlu — kalemin gider hesabı. */
  expenseAccountId?: string | null;
  /** Kalemin masraf merkezi; boşsa faturanınki kullanılır. */
  costCenterCode?: string | null;
};

export type CreateSupplierInvoicePayload = {
  companyId: string;
  supplierCurrentAccountId: string;
  /** Merkez giderinde boş bırakılır. */
  projectId?: string | null;
  purchaseOrderId?: string | null;
  goodsReceiptId?: string | null;
  invoiceNumber: string;
  invoiceDate: string;
  dueDate?: string | null;
  currencyCode: string;
  exchangeRate: number;
  description?: string | null;
  items: SupplierInvoiceItemPayload[];
  /** 0 = Alış (stok), 1 = Gider. */
  invoiceType?: number;
  /** ALIŞ faturasının varsayılan deposu. */
  warehouseId?: string | null;
  /** Faturanın varsayılan masraf merkezi. */
  costCenterCode?: string | null;
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
  /** Stok hesabı (153/150); alış faturası buraya yazılır. */
  inventoryAccountId?: string | null;
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
