import { apiClient } from "@/lib/api/api-client";

export type AccountingVoucherType = 0 | 1 | 2 | 3 | 4;
export type AccountingVoucherStatus = 0 | 1 | 2;

export type AccountingVoucherListItem = {
  id: string;
  companyId: string;
  voucherNumber: string;
  voucherType: AccountingVoucherType;
  status: AccountingVoucherStatus;
  voucherDate: string;
  fiscalYear: number;
  fiscalPeriod: number;
  currencyCode: string;
  exchangeRate: number;
  description?: string | null;
  referenceNumber?: string | null;
  sourceModule?: string | null;
  totalDebit: number;
  totalCredit: number;
  lineCount: number;
};

export type AccountingVoucherLine = {
  id: string;
  lineNumber: number;
  accountingAccountId: string;
  accountCode: string;
  accountName: string;
  description?: string | null;
  debitAmount: number;
  creditAmount: number;
  currencyCode: string;
  exchangeRate: number;
  debitAmountLocal: number;
  creditAmountLocal: number;
  currentAccountId?: string | null;
  currentAccountTitle?: string | null;
  projectId?: string | null;
  projectCode?: string | null;
  projectName?: string | null;
  projectHierarchyNodeId?: string | null;
  projectHierarchyNodeCode?: string | null;
  projectHierarchyNodeName?: string | null;
  costCenterCode?: string | null;
  documentNumber?: string | null;
  documentDate?: string | null;
  dueDate?: string | null;
};

export type AccountingVoucherDetail = AccountingVoucherListItem & {
  sourceEntityId?: string | null;
  postedAtUtc?: string | null;
  cancelledAtUtc?: string | null;
  cancellationReason?: string | null;
  lines: AccountingVoucherLine[];
};


export type AccountingVoucherLineRequest = {
  accountingAccountId: string;
  description?: string | null;
  debitAmount: number;
  creditAmount: number;
  currencyCode: string;
  exchangeRate: number;
  currentAccountId?: string | null;
  projectId?: string | null;
  projectHierarchyNodeId?: string | null;
  costCenterCode?: string | null;
  documentNumber?: string | null;
  documentDate?: string | null;
  dueDate?: string | null;
};

export type CreateAccountingVoucherRequest = {
  companyId: string;
  voucherType: AccountingVoucherType;
  voucherDate: string;
  currencyCode: string;
  exchangeRate: number;
  description?: string | null;
  referenceNumber?: string | null;
  sourceModule?: string | null;
  sourceEntityId?: string | null;
  lines: AccountingVoucherLineRequest[];
};

export type UpdateAccountingVoucherRequest = {
  voucherType: AccountingVoucherType;
  voucherDate: string;
  currencyCode: string;
  exchangeRate: number;
  description?: string | null;
  referenceNumber?: string | null;
  lines: AccountingVoucherLineRequest[];
};

function buildQuery(filters?: {
  companyId?: string;
  status?: number;
  voucherType?: number;
  startDate?: string;
  endDate?: string;
  search?: string;
  hierarchyNodeId?: string;
}) {
  const query = new URLSearchParams();

  if (filters?.companyId) {
    query.set("companyId", filters.companyId);
  }

  if (filters?.status !== undefined) {
    query.set("status", String(filters.status));
  }

  if (filters?.voucherType !== undefined) {
    query.set("voucherType", String(filters.voucherType));
  }

  if (filters?.startDate) {
    query.set("startDate", filters.startDate);
  }

  if (filters?.endDate) {
    query.set("endDate", filters.endDate);
  }

  if (filters?.search?.trim()) {
    query.set("search", filters.search.trim());
  }

  if (filters?.hierarchyNodeId) {
    query.set("hierarchyNodeId", filters.hierarchyNodeId);
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const accountingVoucherService = {
  getAll(filters?: {
    companyId?: string;
    status?: number;
    voucherType?: number;
    startDate?: string;
    endDate?: string;
    search?: string;
    hierarchyNodeId?: string;
  }) {
    return apiClient<AccountingVoucherListItem[]>(
      `accounting-vouchers${buildQuery(filters)}`
    );
  },

  getById(id: string) {
    return apiClient<AccountingVoucherDetail>(
      `accounting-vouchers/${id}`
    );
  },


  create(request: CreateAccountingVoucherRequest) {
    return apiClient<AccountingVoucherDetail>(
      "accounting-vouchers",
      {
        method: "POST",
        body: request,
      }
    );
  },

  update(
    id: string,
    request: UpdateAccountingVoucherRequest
  ) {
    return apiClient<AccountingVoucherDetail>(
      `accounting-vouchers/${id}`,
      {
        method: "PUT",
        body: request,
      }
    );
  },

  post(id: string) {
    return apiClient<{
      id: string;
      voucherNumber: string;
      status: AccountingVoucherStatus;
      message: string;
    }>(`accounting-vouchers/${id}/post`, {
      method: "POST",
    });
  },

  cancel(id: string, reason: string) {
    return apiClient<{
      id: string;
      voucherNumber: string;
      status: AccountingVoucherStatus;
      message: string;
    }>(`accounting-vouchers/${id}/cancel`, {
      method: "POST",
      body: { reason },
    });
  },
};
