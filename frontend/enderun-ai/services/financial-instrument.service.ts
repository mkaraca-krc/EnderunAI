import { apiClient } from "@/lib/api/api-client";

export type BankLoanStatus = "Planned" | "Active" | "Closed" | "Cancelled";

export const BANK_LOAN_STATUS_VALUE: Record<BankLoanStatus, number> = {
  Planned: 0,
  Active: 1,
  Closed: 2,
  Cancelled: 90,
};

export const BANK_LOAN_STATUS_LABEL: Record<BankLoanStatus, string> = {
  Planned: "Planlandı",
  Active: "Aktif",
  Closed: "Kapandı",
  Cancelled: "İptal",
};

export type CreditCardOwnership = "Company" | "Personal";

export const CREDIT_CARD_OWNERSHIP_VALUE: Record<CreditCardOwnership, number> = {
  Company: 0,
  Personal: 1,
};

export interface BankLoan {
  id: string;
  name: string;
  contractNumber?: string | null;
  status: BankLoanStatus;
  principalAmount: number;
  monthlyInterestRate: number;
  installmentCount: number;
  drawdownDate: string;
  firstInstallmentDate: string;
  /** Para hesaba girdi mi — girdiyse nakit akışta tekrar giriş yazılmaz. */
  isDrawn: boolean;
  projectId?: string | null;
  projectName?: string | null;
  /** Ödenmemiş taksitlerin anaparası; faiz borç değil, gelecekteki gider. */
  remainingPrincipal: number;
  paidCount: number;
}

export interface BankLoanInstallment {
  id: string;
  number: number;
  dueDate: string;
  principalAmount: number;
  interestAmount: number;
  totalAmount: number;
  isPaid: boolean;
  paidDate?: string | null;
}

export interface CreditCard {
  id: string;
  name: string;
  bankName?: string | null;
  lastFourDigits?: string | null;
  ownership: CreditCardOwnership;
  partnerAccountId?: string | null;
  partnerName?: string | null;
  statementDay: number;
  dueDay: number;
  isActive: boolean;
}

export interface CreditCardStatement {
  creditCardId: string;
  cardName: string;
  periodStart: string;
  periodEnd: string;
  dueDate: string;
  amount: number;
  itemCount: number;
  /** Şirket kartında true; şahıs kartının ekstresini kişi öder. */
  producesCashOutflow: boolean;
}

export const financialInstrumentService = {
  listLoans(companyId: string) {
    return apiClient<BankLoan[]>(
      `finansal-araclar/krediler?companyId=${companyId}`,
    );
  },

  listInstallments(loanId: string) {
    return apiClient<BankLoanInstallment[]>(
      `finansal-araclar/krediler/${loanId}/taksitler`,
    );
  },

  createLoan(payload: {
    companyId: string;
    name: string;
    contractNumber?: string | null;
    bankCurrentAccountId?: string | null;
    cashAccountId?: string | null;
    projectId?: string | null;
    principalAmount: number;
    monthlyInterestRate: number;
    installmentCount: number;
    drawdownDate: string;
    firstInstallmentDate: string;
    notes?: string | null;
  }) {
    return apiClient<{ id: string }>("finansal-araclar/krediler", {
      method: "POST",
      body: payload,
    });
  },

  rebuildSchedule(loanId: string) {
    return apiClient<{ id: string }>(
      `finansal-araclar/krediler/${loanId}/plan-yenile`,
      { method: "POST" },
    );
  },

  updateInstallment(
    id: string,
    payload: {
      principalAmount: number;
      interestAmount: number;
      dueDate: string;
      isPaid: boolean;
      paidDate?: string | null;
    },
  ) {
    return apiClient<{ id: string }>(`finansal-araclar/taksitler/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  updateLoanStatus(loanId: string, status: number, isDrawn?: boolean) {
    const query = new URLSearchParams({ status: String(status) });

    if (isDrawn !== undefined) query.set("isDrawn", String(isDrawn));

    return apiClient<{ id: string; status: string; isDrawn: boolean }>(
      `finansal-araclar/krediler/${loanId}/durum?${query.toString()}`,
      { method: "POST" },
    );
  },

  listCards(companyId: string) {
    return apiClient<CreditCard[]>(
      `finansal-araclar/kartlar?companyId=${companyId}`,
    );
  },

  listStatements(companyId: string, from?: string, to?: string) {
    const query = new URLSearchParams({ companyId });

    if (from) query.set("from", from);
    if (to) query.set("to", to);

    return apiClient<CreditCardStatement[]>(
      `finansal-araclar/kartlar/ekstreler?${query.toString()}`,
    );
  },

  createCard(payload: {
    companyId: string;
    name: string;
    bankName?: string | null;
    lastFourDigits?: string | null;
    ownership: number;
    partnerAccountId?: string | null;
    cashAccountId?: string | null;
    statementDay: number;
    dueDay: number;
    isActive: boolean;
  }) {
    return apiClient<{ id: string }>("finansal-araclar/kartlar", {
      method: "POST",
      body: payload,
    });
  },
};

// ---------------- Barter ----------------

export interface BarterLedgerEntry {
  id: string;
  /** 0 = kesinti (alacak doğar), 1 = teslim alma (alacak düşer). */
  entryType: number;
  entryDate: string;
  amount: number;
  description: string;
  projectSiteName?: string | null;
  progressPaymentNumber?: string | null;
}

export interface BarterLedger {
  entries: BarterLedgerEntry[];
  totalDeducted: number;
  totalReceived: number;
  /** Kesilen − teslim alınan: işverenden alınacak mal/hizmet. */
  openBalance: number;
}

export const barterService = {
  get(projectId: string) {
    return apiClient<BarterLedger>(`barter-ledger?projectId=${projectId}`);
  },

  addReceipt(payload: {
    projectId: string;
    projectSiteId?: string | null;
    entryDate: string;
    amount: number;
    description: string;
    notes?: string | null;
  }) {
    return apiClient<{ id: string }>("barter-ledger/receipts", {
      method: "POST",
      body: payload,
    });
  },
};
