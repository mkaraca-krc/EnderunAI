import { apiClient } from "@/lib/api/api-client";

/** Gider merkezinin türü — şube (merkez ofis), proje ya da şantiye. */
export type ExpenseCenterType = "Branch" | "Project" | "ProjectSite";

/** Merkez türünün backend'deki sayısal karşılığı. */
export const EXPENSE_CENTER_TYPE_VALUE: Record<ExpenseCenterType, number> = {
  Branch: 0,
  Project: 1,
  ProjectSite: 2,
};

export type ExpensePaymentMethod = "Bank" | "Cash";

export const EXPENSE_PAYMENT_METHOD_VALUE: Record<ExpensePaymentMethod, number> = {
  Bank: 0,
  Cash: 1,
};

export type ExpenseDocumentType = "None" | "Receipt" | "Invoice";

export const EXPENSE_DOCUMENT_TYPE_VALUE: Record<ExpenseDocumentType, number> = {
  None: 0,
  Receipt: 1,
  Invoice: 2,
};

export interface ExpenseCategory {
  id: string;
  code: string;
  name: string;
  sortOrder: number;
  isSystem: boolean;
  /**
   * Yalnız otomatik kaynaklardan (satın alma, görevlendirme, puantaj)
   * dolan kategori. Elle giriş listesinde GÖSTERİLMEZ: gösterilseydi
   * aynı gider hem otomatik akar hem elle girilir ve iki kez sayılırdı.
   */
  isAutomaticOnly: boolean;
  isActive: boolean;
}

export interface ExpenseCenter {
  type: ExpenseCenterType;
  id: string;
  name: string;
  parentProjectId?: string | null;
  isHeadOffice: boolean;
}

export interface ExpenseEntry {
  id: string;
  expenseDate: string;
  amount: number;
  description: string;
  categoryId: string;
  categoryName: string;
  centerType: ExpenseCenterType;
  centerName: string;
  paymentMethod: ExpensePaymentMethod;
  documentType: ExpenseDocumentType;
  documentNumber?: string | null;
  supplierName?: string | null;
  isRecurring: boolean;
}

export interface ExpenseEntryList {
  items: ExpenseEntry[];
  /** Yalnızca GÖRÜNEN kalemlerin toplamı. */
  total: number;
  hiddenCount: number;
  hiddenNote?: string | null;
}

export interface ExpenseDuplicateHint {
  id: string;
  expenseDate: string;
  amount: number;
  description: string;
}

export interface SaveExpenseEntryPayload {
  companyId: string;
  centerType: number;
  centerId: string;
  expenseCategoryId: string;
  expenseDate: string;
  amount: number;
  description: string;
  paymentMethod: number;
  documentType: number;
  documentNumber?: string | null;
  supplierCurrentAccountId?: string | null;
}

export interface RecurringExpenseTemplate {
  id: string;
  description: string;
  estimatedAmount: number;
  categoryId: string;
  categoryName: string;
  centerType: ExpenseCenterType;
  centerName: string;
  paymentMethod: ExpensePaymentMethod;
  startYear: number;
  startMonth: number;
  endYear?: number | null;
  endMonth?: number | null;
  paymentDay: number;
  isStopped: boolean;
}

export interface RecurringExpensePeriod {
  templateId: string;
  year: number;
  month: number;
  dueDate: string;
  estimatedAmount: number;
  actualEntryId?: string | null;
  actualAmount?: number | null;
  isConfirmed: boolean;
}

export interface RecurringExpenseList {
  templates: RecurringExpenseTemplate[];
  periods?: RecurringExpensePeriod[] | null;
  hiddenCount: number;
  hiddenNote?: string | null;
}

export interface ExpenseReportRow {
  centerType: ExpenseCenterType;
  centerId: string;
  centerName: string;
  categoryCode: string;
  categoryName: string;
  source: string;
  amount: number;
  /** Tekrarlayan giderin henüz gerçekleşmemiş dönemi. */
  isEstimated: boolean;
  /**
   * Otomatik kalemlerde false: kaynağından düzeltilir, yoksa maliyet
   * defteri ile rapor ayrışır.
   */
  isEditableHere: boolean;
}

export interface ExpenseReport {
  from: string;
  to: string;
  total: number;
  hiddenCount: number;
  hiddenNote?: string | null;
  notes: string[];
  centerTotals: {
    centerType: ExpenseCenterType;
    centerId: string;
    centerName: string;
    amount: number;
  }[];
  categoryTotals: {
    categoryCode: string;
    categoryName: string;
    amount: number;
  }[];
  rows: ExpenseReportRow[];
}

export const expenseService = {
  listCategories(companyId: string, includeInactive = false) {
    return apiClient<ExpenseCategory[]>(
      `expenses/kategoriler?companyId=${companyId}` +
        (includeInactive ? "&includeInactive=true" : ""),
    );
  },

  listCenters(companyId: string) {
    return apiClient<ExpenseCenter[]>(`expenses/merkezler?companyId=${companyId}`);
  },

  listEntries(params: {
    companyId: string;
    from?: string;
    to?: string;
    centerType?: number;
    centerId?: string;
    categoryId?: string;
  }) {
    const query = new URLSearchParams({ companyId: params.companyId });

    if (params.from) query.set("from", params.from);
    if (params.to) query.set("to", params.to);
    if (params.centerType !== undefined && params.centerId) {
      query.set("centerType", String(params.centerType));
      query.set("centerId", params.centerId);
    }
    if (params.categoryId) query.set("categoryId", params.categoryId);

    return apiClient<ExpenseEntryList>(`expenses/kayitlar?${query.toString()}`);
  },

  /** Kaydetmeden önce "bu gider zaten girilmiş olabilir" uyarısı. */
  findDuplicates(payload: SaveExpenseEntryPayload) {
    return apiClient<ExpenseDuplicateHint[]>("expenses/kayitlar/benzer-kayitlar", {
      method: "POST",
      body: payload,
    });
  },

  createEntry(payload: SaveExpenseEntryPayload) {
    return apiClient<{ id: string }>("expenses/kayitlar", {
      method: "POST",
      body: payload,
    });
  },

  updateEntry(id: string, payload: SaveExpenseEntryPayload) {
    return apiClient<{ id: string }>(`expenses/kayitlar/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  deleteEntry(id: string) {
    return apiClient<{ id: string }>(`expenses/kayitlar/${id}`, {
      method: "DELETE",
    });
  },

  listRecurring(companyId: string, year?: number, month?: number) {
    const query = new URLSearchParams({ companyId });

    if (year && month) {
      query.set("year", String(year));
      query.set("month", String(month));
    }

    return apiClient<RecurringExpenseList>(`expenses/tekrarlayan?${query.toString()}`);
  },

  createRecurring(payload: {
    companyId: string;
    centerType: number;
    centerId: string;
    expenseCategoryId: string;
    description: string;
    estimatedAmount: number;
    paymentMethod: number;
    supplierCurrentAccountId?: string | null;
    startYear: number;
    startMonth: number;
    endYear?: number | null;
    endMonth?: number | null;
    paymentDay: number;
  }) {
    return apiClient<{ id: string }>("expenses/tekrarlayan", {
      method: "POST",
      body: payload,
    });
  },

  stopRecurring(id: string) {
    return apiClient<{ id: string }>(`expenses/tekrarlayan/${id}/durdur`, {
      method: "POST",
    });
  },

  confirmRecurringPeriod(
    id: string,
    payload: {
      year: number;
      month: number;
      actualAmount: number;
      documentType: number;
      documentNumber?: string | null;
    },
  ) {
    return apiClient<{ entryId: string }>(`expenses/tekrarlayan/${id}/gerceklesen`, {
      method: "POST",
      body: payload,
    });
  },

  getReport(companyId: string, from: string, to: string) {
    return apiClient<ExpenseReport>(
      `expenses/rapor?companyId=${companyId}&from=${from}&to=${to}`,
    );
  },
};
