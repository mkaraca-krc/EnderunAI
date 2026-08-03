import { apiClient } from "@/lib/api/api-client";

export const ChequeDirection = {
  Received: 0,
  Issued: 1,
} as const;

export const ChequeStatus = {
  Portfolio: 0,
  AtBank: 1,
  AtFactoring: 2,
  Collected: 3,
  Bounced: 4,
  Issued: 10,
  Paid: 11,
  Returned: 12,
} as const;

export const CHEQUE_STATUS_LABELS: Record<number, string> = {
  0: "Portföyde",
  1: "Bankada (tahsilde)",
  2: "Faktoringde",
  3: "Tahsil edildi",
  4: "Karşılıksız",
  10: "Verildi",
  11: "Ödendi",
  12: "İade alındı",
};

/** erp-status.{renk} sınıfıyla eşleşir. */
export const CHEQUE_STATUS_COLORS: Record<number, string> = {
  0: "blue",
  1: "yellow",
  2: "yellow",
  3: "green",
  4: "red",
  10: "blue",
  11: "green",
  12: "gray",
};

/** Bu geçişler için kasa/banka hesabı seçimi zorunlu. */
export const CHEQUE_TRANSITIONS_REQUIRING_CASH_ACCOUNT: Record<string, boolean> = {
  "0-1": true,
  "0-3": true,
  "1-3": true,
  "2-4": true,
  "10-11": true,
};

export function requiresCashAccount(from: number, to: number) {
  return CHEQUE_TRANSITIONS_REQUIRING_CASH_ACCOUNT[`${from}-${to}`] === true;
}

export type ChequeMovement = {
  id: string;
  movementDate: string;
  fromStatus?: number | null;
  fromStatusName?: string | null;
  toStatus: number;
  toStatusName: string;
  description: string;
  cashAccountId?: string | null;
  cashAccountName?: string | null;
  accountingVoucherId?: string | null;
  accountingVoucherNumber?: string | null;
};

export type ChequeListItem = {
  id: string;
  companyId: string;
  direction: number;
  directionName: string;
  status: number;
  statusName: string;
  internalNumber: string;
  chequeNumber: string;
  bankName: string;
  drawer?: string | null;
  currentAccountId?: string | null;
  currentAccountTitle?: string | null;
  projectId?: string | null;
  projectCode?: string | null;
  amount: number;
  currencyCode: string;
  issueDate: string;
  dueDate: string;
  daysToDue: number;
  isOverdue: boolean;
};

export type ChequeDetail = ChequeListItem & {
  bankBranch?: string | null;
  projectName?: string | null;
  progressPaymentId?: string | null;
  progressPaymentNumber?: string | null;
  supplierInvoiceId?: string | null;
  supplierInvoiceNumber?: string | null;
  cashAccountId?: string | null;
  cashAccountName?: string | null;
  description?: string | null;
  allowedNextStatuses: number[];
  movements: ChequeMovement[];
};

export type ChequeSummary = {
  receivedPortfolioAmount: number;
  receivedAtBankAmount: number;
  receivedAtFactoringAmount: number;
  receivedCollectedAmount: number;
  receivedBouncedAmount: number;
  issuedOpenAmount: number;
  issuedPaidAmount: number;
  receivedOpenCount: number;
  issuedOpenCount: number;
};

export type CreateChequePayload = {
  companyId: string;
  direction: number;
  chequeNumber: string;
  bankName: string;
  bankBranch?: string | null;
  drawer?: string | null;
  currentAccountId?: string | null;
  projectId?: string | null;
  amount: number;
  currencyCode: string;
  issueDate: string;
  dueDate: string;
  progressPaymentId?: string | null;
  supplierInvoiceId?: string | null;
  description?: string | null;
};

export type ChequeStatusChangePayload = {
  toStatus: number;
  movementDate: string;
  cashAccountId?: string | null;
  description?: string | null;
};

export const chequeService = {
  getAll(
    params: {
      companyId?: string;
      direction?: number;
      status?: number;
      currentAccountId?: string;
      projectId?: string;
      search?: string;
    } = {}
  ) {
    const query = new URLSearchParams();
    if (params.companyId) query.set("companyId", params.companyId);
    if (params.direction !== undefined) query.set("direction", String(params.direction));
    if (params.status !== undefined) query.set("status", String(params.status));
    if (params.currentAccountId) query.set("currentAccountId", params.currentAccountId);
    if (params.projectId) query.set("projectId", params.projectId);
    if (params.search) query.set("search", params.search);

    const suffix = query.toString();
    return apiClient<ChequeListItem[]>(`cheques${suffix ? `?${suffix}` : ""}`);
  },

  getSummary(companyId?: string) {
    const suffix = companyId ? `?companyId=${companyId}` : "";
    return apiClient<ChequeSummary>(`cheques/summary${suffix}`);
  },

  getById(id: string) {
    return apiClient<ChequeDetail>(`cheques/${id}`);
  },

  create(payload: CreateChequePayload) {
    return apiClient<ChequeDetail>("cheques", { method: "POST", body: payload });
  },

  changeStatus(id: string, payload: ChequeStatusChangePayload) {
    return apiClient<ChequeDetail>(`cheques/${id}/status`, {
      method: "POST",
      body: payload,
    });
  },
};
