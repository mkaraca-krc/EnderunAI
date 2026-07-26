import { apiClient } from "@/lib/api/api-client";

export type AccountingAccountNature = 0 | 1 | 2;

export type AccountingAccountListItem = {
  id: string;
  companyId: string;
  parentAccountId?: string | null;
  code: string;
  name: string;
  nature: AccountingAccountNature;
  level: number;
  isPostingAllowed: boolean;
  requiresProject: boolean;
  requiresCostCenter: boolean;
  currencyCode?: string | null;
  isActive: boolean;
  childCount: number;
};

export type AccountingAccountDetail = {
  id: string;
  companyId: string;
  parentAccountId?: string | null;
  code: string;
  name: string;
  description?: string | null;
  nature: AccountingAccountNature;
  level: number;
  isPostingAllowed: boolean;
  requiresProject: boolean;
  requiresCostCenter: boolean;
  currencyCode?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
};

export type CreateAccountingAccountRequest = {
  companyId: string;
  parentAccountId?: string | null;
  code: string;
  name: string;
  description?: string | null;
  nature: AccountingAccountNature;
  isPostingAllowed: boolean;
  requiresProject: boolean;
  requiresCostCenter: boolean;
  currencyCode?: string | null;
};

export type UpdateAccountingAccountRequest = {
  parentAccountId?: string | null;
  code: string;
  name: string;
  description?: string | null;
  nature: AccountingAccountNature;
  isPostingAllowed: boolean;
  requiresProject: boolean;
  requiresCostCenter: boolean;
  currencyCode?: string | null;
  isActive: boolean;
};

function buildQuery(filters?: {
  companyId?: string;
  parentAccountId?: string;
  isActive?: boolean;
  search?: string;
}) {
  const query = new URLSearchParams();

  if (filters?.companyId) {
    query.set("companyId", filters.companyId);
  }

  if (filters?.parentAccountId) {
    query.set("parentAccountId", filters.parentAccountId);
  }

  if (filters?.isActive !== undefined) {
    query.set("isActive", String(filters.isActive));
  }

  if (filters?.search?.trim()) {
    query.set("search", filters.search.trim());
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const accountingAccountService = {
  getAll(filters?: {
    companyId?: string;
    parentAccountId?: string;
    isActive?: boolean;
    search?: string;
  }) {
    return apiClient<AccountingAccountListItem[]>(
      `accounting-accounts${buildQuery(filters)}`
    );
  },

  getById(id: string) {
    return apiClient<AccountingAccountDetail>(
      `accounting-accounts/${id}`
    );
  },

  create(request: CreateAccountingAccountRequest) {
    return apiClient<AccountingAccountDetail>(
      "accounting-accounts",
      {
        method: "POST",
        body: request,
      }
    );
  },

  update(
    id: string,
    request: UpdateAccountingAccountRequest
  ) {
    return apiClient<AccountingAccountDetail>(
      `accounting-accounts/${id}`,
      {
        method: "PUT",
        body: request,
      }
    );
  },

  deactivate(id: string) {
    return apiClient<{ message: string }>(
      `accounting-accounts/${id}/deactivate`,
      {
        method: "POST",
      }
    );
  },

  seedStandardPlan(companyId: string) {
    return apiClient<{
      createdCount: number;
      existingCount: number;
      totalCount: number;
      message: string;
    }>(
      `accounting-account-seed/${companyId}`,
      {
        method: "POST",
      }
    );
  },
};
