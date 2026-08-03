import { apiClient } from "@/lib/api/api-client";


export interface CurrentAccountListItem {
  id: string;
  companyId: string;
  companyName: string;

  code: string;
  title: string;
  shortName?: string | null;

  roles: number;
  status: number;

  taxOffice?: string | null;
  taxNumber?: string | null;

  authorizedPerson?: string | null;
  phone?: string | null;
  email?: string | null;

  paymentTerm?: string | null;
  creditLimit?: number | null;

  isActive: boolean;

  payableAccountingAccountId?: string | null;
  payableAccountCode?: string | null;
  receivableAccountingAccountId?: string | null;
  receivableAccountCode?: string | null;
}

/**
 * Cari bakiyesi — ayrı bir hareket defterinden değil, doğrudan muhasebe
 * defterinden (kesinleşmiş fişlerin cari boyutu) hesaplanır.
 */
export interface CurrentAccountBalance {
  currentAccountId: string;
  totalDebit: number;
  totalCredit: number;
  /** Borç − Alacak. Pozitif: bize borçlu, negatif: biz borçluyuz. */
  balance: number;
  movementCount: number;
  lastMovementDate?: string | null;
}

export interface CurrentAccountStatementLine {
  id: string;
  voucherId: string;
  voucherNumber: string;
  voucherDate: string;
  sourceModule?: string | null;
  accountCode: string;
  accountName: string;
  description?: string | null;
  documentNumber?: string | null;
  dueDate?: string | null;
  projectCode?: string | null;
  debit: number;
  credit: number;
  runningBalance: number;
}

export interface CurrentAccountStatement {
  currentAccount: {
    id: string;
    code: string;
    title: string;
    creditLimit?: number | null;
  };
  openingBalance: number;
  periodDebit: number;
  periodCredit: number;
  closingBalance: number;
  lineCount: number;
  lines: CurrentAccountStatementLine[];
}



export interface CurrentAccountSummary {
  totalReceivable: number;
  totalPayable: number;
  netBalance: number;
  accountCount: number;
  // Cari hareket/bakiye defteri (fatura + tahsilat) henüz uygulamaya
  // bağlı değil - false ise yukarıdaki tutar alanları gerçek değil,
  // arayüz bunları "veri yok" olarak göstermeli. accountCount her
  // zaman gerçektir (kayıtlı cari kart sayısı).
  balancesAvailable: boolean;
  message?: string | null;
}



export const currentAccountService = {


  getAll(companyId?: string) {

    const query = companyId
      ? `?companyId=${encodeURIComponent(companyId)}`
      : "";


    return apiClient<CurrentAccountListItem[]>(
      `current-accounts${query}`
    );

  },


  getSummary() {

    return apiClient<CurrentAccountSummary>(
      "finance/cari-summary"
    );

  },

  getBalances(companyId?: string) {
    const query = companyId
      ? `?companyId=${encodeURIComponent(companyId)}`
      : "";

    return apiClient<CurrentAccountBalance[]>(
      `current-accounts/balances${query}`
    );
  },

  getStatement(
    id: string,
    range: { startDate?: string; endDate?: string } = {}
  ) {
    const params = new URLSearchParams();
    if (range.startDate) params.set("startDate", range.startDate);
    if (range.endDate) params.set("endDate", range.endDate);
    const query = params.toString();

    return apiClient<CurrentAccountStatement>(
      `current-accounts/${id}/statement${query ? `?${query}` : ""}`
    );
  }

};
