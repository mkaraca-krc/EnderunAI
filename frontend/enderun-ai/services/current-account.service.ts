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
export interface CurrentAccountCurrencyBalance {
  currencyCode: string;
  /** İşlem para biriminde borç toplamı. */
  totalDebit: number;
  totalCredit: number;
  /** İşlem para biriminde bakiye (örn. 12.500 USD). */
  balance: number;
  /** Aynı bakiyenin defterdeki TL karşılığı (işlem günü kurlarıyla). */
  balanceLocal: number;
  movementCount: number;
  lastMovementDate?: string | null;
}

export interface CurrentAccountBalance {
  currentAccountId: string;
  totalDebit: number;
  totalCredit: number;
  /** Borç − Alacak, TL. Pozitif: bize borçlu, negatif: biz borçluyuz. */
  balance: number;
  movementCount: number;
  lastMovementDate?: string | null;
  hasForeignCurrency?: boolean;
  currencyBalances?: CurrentAccountCurrencyBalance[];
}

export interface CurrentAccountCurrencyValuation {
  currencyCode: string;
  balance: number;
  /** Defter değeri: hareketlerin kendi günündeki kurla TL toplamı. */
  bookValueLocal: number;
  rateAvailable: boolean;
  valuationRate?: number | null;
  rateSource?: string | null;
  valuedLocal?: number | null;
  /** Değerlenmiş − defter. Gerçekleşmemiş kur farkı. */
  difference?: number | null;
  message?: string | null;
}

export interface CurrentAccountValuation {
  currentAccountId: string;
  valuationDate: string;
  currencies: CurrentAccountCurrencyValuation[];
  totalDifference: number;
  /** Kuru bulunamayan döviz varsa toplam eksiktir. */
  hasMissingRate: boolean;
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
  /** TL (defter) tutarı. */
  debit: number;
  credit: number;
  /** TL yürüyen bakiye. */
  runningBalance: number;
  currencyCode?: string;
  exchangeRate?: number;
  /** İşlem para birimindeki tutar; TL satırda debit ile aynıdır. */
  debitOriginal?: number;
  creditOriginal?: number;
  /** Aynı para biriminin kendi içinde yürüyen bakiyesi. */
  runningBalanceOriginal?: number;
}

export interface CurrentAccountStatementCurrencySummary {
  currencyCode: string;
  openingBalance: number;
  openingBalanceLocal: number;
  periodDebit: number;
  periodCredit: number;
  periodDebitLocal: number;
  periodCreditLocal: number;
  closingBalance: number;
  closingBalanceLocal: number;
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
  /** Uygulanan para birimi filtresi; yoksa null. */
  currency?: string | null;
  hasForeignCurrency?: boolean;
  currencySummary?: CurrentAccountStatementCurrencySummary[];
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
    range: { startDate?: string; endDate?: string; currency?: string } = {}
  ) {
    const params = new URLSearchParams();
    if (range.startDate) params.set("startDate", range.startDate);
    if (range.endDate) params.set("endDate", range.endDate);
    if (range.currency) params.set("currency", range.currency);
    const query = params.toString();

    return apiClient<CurrentAccountStatement>(
      `current-accounts/${id}/statement${query ? `?${query}` : ""}`
    );
  },

  /**
   * Döviz bakiyesinin verilen tarihteki kurla değerlemesi. Defter
   * değeriyle arasındaki fark gerçekleşmemiş kur farkıdır; bu uç
   * yalnızca raporlar, fiş kesmez.
   */
  getCurrencyValuation(id: string, valuationDate?: string) {
    const query = valuationDate
      ? `?valuationDate=${encodeURIComponent(valuationDate)}`
      : "";

    return apiClient<CurrentAccountValuation>(
      `current-accounts/${id}/currency-valuation${query}`
    );
  }

};
