import { apiClient } from "@/lib/api/api-client";

export const CashAccountType = {
  Cash: 0,
  Bank: 1,
} as const;

export const CASH_ACCOUNT_TYPE_LABELS: Record<number, string> = {
  0: "Kasa",
  1: "Banka",
};

export const CashTransactionType = {
  Collection: 0,
  Payment: 1,
  ChequeCollection: 2,
  ChequePayment: 3,
  Factoring: 4,
} as const;

export const CASH_TRANSACTION_TYPE_LABELS: Record<number, string> = {
  0: "Tahsilat",
  1: "Ödeme",
  2: "Çek tahsili",
  3: "Çek ödemesi",
  4: "Faktoring",
};

export type CashAccount = {
  id: string;
  companyId: string;
  type: number;
  typeName: string;
  code: string;
  name: string;
  bankName?: string | null;
  iban?: string | null;
  currencyCode: string;
  openingBalance: number;
  accountingAccountId: string;
  accountingAccountCode: string;
  accountingAccountName: string;
  totalIn: number;
  totalOut: number;
  balance: number;
  movementCount: number;
  isActive: boolean;
};

export type CashTransaction = {
  id: string;
  cashAccountId: string;
  transactionDate: string;
  transactionType: number;
  transactionTypeName: string;
  direction: number;
  amount: number;
  currencyCode: string;
  description: string;
  documentNumber?: string | null;
  currentAccountId?: string | null;
  currentAccountTitle?: string | null;
  projectId?: string | null;
  projectCode?: string | null;
  sourceModule?: string | null;
  sourceEntityId?: string | null;
  accountingVoucherId?: string | null;
  accountingVoucherNumber?: string | null;
  runningBalance: number;
};

export type CashAccountStatement = {
  cashAccountId: string;
  code: string;
  name: string;
  currencyCode: string;
  openingBalance: number;
  periodOpeningBalance: number;
  totalIn: number;
  totalOut: number;
  closingBalance: number;
  transactions: CashTransaction[];
};

export type CreateCashAccountPayload = {
  companyId: string;
  type: number;
  code: string;
  name: string;
  bankName?: string | null;
  iban?: string | null;
  currencyCode: string;
  openingBalance: number;
  accountingAccountId: string;
};

export type CreateCashTransactionPayload = {
  transactionDate: string;
  transactionType: number;
  direction: number;
  amount: number;
  currencyCode: string;
  description: string;
  documentNumber?: string | null;
  currentAccountId?: string | null;
  projectId?: string | null;
};

export const cashAccountService = {
  getAll(params: { companyId?: string; type?: number; includeInactive?: boolean } = {}) {
    const query = new URLSearchParams();
    if (params.companyId) query.set("companyId", params.companyId);
    if (params.type !== undefined) query.set("type", String(params.type));
    if (params.includeInactive) query.set("includeInactive", "true");

    const suffix = query.toString();
    return apiClient<CashAccount[]>(`cash-accounts${suffix ? `?${suffix}` : ""}`);
  },

  create(payload: CreateCashAccountPayload) {
    return apiClient<{ id: string; code: string; name: string }>("cash-accounts", {
      method: "POST",
      body: payload,
    });
  },

  getStatement(
    id: string,
    params: { startDate?: string; endDate?: string } = {}
  ) {
    const query = new URLSearchParams();
    if (params.startDate) query.set("startDate", params.startDate);
    if (params.endDate) query.set("endDate", params.endDate);

    const suffix = query.toString();
    return apiClient<CashAccountStatement>(
      `cash-accounts/${id}/transactions${suffix ? `?${suffix}` : ""}`
    );
  },

  createTransaction(id: string, payload: CreateCashTransactionPayload) {
    return apiClient<{ id: string; accountingVoucherId: string }>(
      `cash-accounts/${id}/transactions`,
      { method: "POST", body: payload }
    );
  },
};
