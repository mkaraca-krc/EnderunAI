import { apiClient } from "@/lib/api/api-client";

export type PayrollBankAccount = {
  id: string;
  companyId: string;
  bankName: string;
  accountName: string;
  iban?: string | null;
  currencyCode: string;
  accountingAccountId?: string | null;
  isActive?: boolean;
};

export type PayrollCashAccount = {
  id: string;
  companyId: string;
  code: string;
  name: string;
  currencyCode: string;
  accountingAccountId: string;
  isActive?: boolean;
};

function normalizeList<T>(
  payload: unknown
): T[] {
  if (Array.isArray(payload)) {
    return payload as T[];
  }

  if (
    payload &&
    typeof payload === "object"
  ) {
    const value =
      payload as {
        items?: unknown;
        data?: unknown;
        result?: unknown;
      };

    if (Array.isArray(value.items)) {
      return value.items as T[];
    }

    if (Array.isArray(value.data)) {
      return value.data as T[];
    }

    if (Array.isArray(value.result)) {
      return value.result as T[];
    }
  }

  return [];
}

export const payrollPaymentAccountService = {
  async getBankAccounts(
    companyId: string
  ) {
    const response =
      await apiClient<unknown>(
        "bank-accounts"
      );

    return normalizeList<
      PayrollBankAccount
    >(response).filter(
      (item) =>
        item.companyId === companyId &&
        item.isActive !== false
    );
  },

  async getCashAccounts(
    companyId: string
  ) {
    const query =
      new URLSearchParams({
        companyId,
      });

    const response =
      await apiClient<unknown>(
        `cash-accounts?${query.toString()}`
      );

    return normalizeList<
      PayrollCashAccount
    >(response).filter(
      (item) =>
        item.companyId === companyId &&
        item.isActive !== false
    );
  },
};
