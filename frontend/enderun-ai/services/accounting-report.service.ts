import { apiClient } from "@/lib/api/api-client";

export type JournalReportLine = {
  voucherId: string;
  voucherDate: string;
  voucherNumber: string;
  voucherType: number;
  voucherDescription?: string | null;
  referenceNumber?: string | null;
  sourceModule?: string | null;

  lineNumber: number;
  accountingAccountId: string;
  accountCode: string;
  accountName: string;
  lineDescription?: string | null;

  currentAccountId?: string | null;
  currentAccountCode?: string | null;
  currentAccountTitle?: string | null;

  projectId?: string | null;
  projectCode?: string | null;
  projectName?: string | null;

  costCenterCode?: string | null;
  documentNumber?: string | null;
  documentDate?: string | null;
  dueDate?: string | null;

  currencyCode: string;
  exchangeRate: number;
  debitAmount: number;
  creditAmount: number;
  debitAmountLocal: number;
  creditAmountLocal: number;
};

export type JournalReportSummary = {
  voucherCount: number;
  lineCount: number;
  totalDebit: number;
  totalCredit: number;
  difference: number;
};

export type JournalReportResponse = {
  startDate?: string | null;
  endDate?: string | null;
  summary: JournalReportSummary;
  lines: JournalReportLine[];
};

export type JournalReportFilters = {
  companyId: string;
  startDate?: string;
  endDate?: string;
  accountingAccountId?: string;
  currentAccountId?: string;
  projectId?: string;
  accountCode?: string;
  search?: string;
};

function buildQuery(filters: JournalReportFilters) {
  const query = new URLSearchParams();

  query.set("companyId", filters.companyId);

  if (filters.startDate) {
    query.set("startDate", filters.startDate);
  }

  if (filters.endDate) {
    query.set("endDate", filters.endDate);
  }

  if (filters.accountingAccountId) {
    query.set(
      "accountingAccountId",
      filters.accountingAccountId
    );
  }

  if (filters.currentAccountId) {
    query.set(
      "currentAccountId",
      filters.currentAccountId
    );
  }

  if (filters.projectId) {
    query.set("projectId", filters.projectId);
  }

  if (filters.accountCode?.trim()) {
    query.set(
      "accountCode",
      filters.accountCode.trim()
    );
  }

  if (filters.search?.trim()) {
    query.set("search", filters.search.trim());
  }

  return `?${query.toString()}`;
}

export const accountingReportService = {
  getJournal(filters: JournalReportFilters) {
    return apiClient<JournalReportResponse>(
      `accounting-reports/journal${buildQuery(filters)}`
    );
  },

  getGeneralLedger(filters: GeneralLedgerFilters) {
    return apiClient<GeneralLedgerReportResponse>(
      `accounting-reports/general-ledger${buildGeneralLedgerQuery(
        filters
      )}`
    );
  },
};

export type GeneralLedgerLine = {
  voucherId: string;
  voucherDate: string;
  voucherNumber: string;
  voucherType: number;
  lineNumber: number;

  accountingAccountId: string;
  accountCode: string;
  accountName: string;

  description?: string | null;
  referenceNumber?: string | null;
  sourceModule?: string | null;

  currentAccountId?: string | null;
  currentAccountCode?: string | null;
  currentAccountTitle?: string | null;

  projectId?: string | null;
  projectCode?: string | null;
  projectName?: string | null;

  costCenterCode?: string | null;
  documentNumber?: string | null;
  documentDate?: string | null;
  dueDate?: string | null;

  debitAmount: number;
  creditAmount: number;
  balance: number;
};

export type GeneralLedgerAccountSummary = {
  accountingAccountId: string;
  accountCode: string;
  accountName: string;
  openingBalance: number;
  periodDebit: number;
  periodCredit: number;
  closingBalance: number;
  lines: GeneralLedgerLine[];
};

export type GeneralLedgerSummary = {
  accountCount: number;
  voucherCount: number;
  lineCount: number;
  totalDebit: number;
  totalCredit: number;
  difference: number;
};

export type GeneralLedgerReportResponse = {
  startDate?: string | null;
  endDate?: string | null;
  summary: GeneralLedgerSummary;
  accounts: GeneralLedgerAccountSummary[];
};

export type GeneralLedgerFilters = {
  companyId: string;
  startDate?: string;
  endDate?: string;
  accountingAccountId?: string;
  currentAccountId?: string;
  projectId?: string;
  accountCode?: string;
  search?: string;
};

function buildGeneralLedgerQuery(
  filters: GeneralLedgerFilters
) {
  const query = new URLSearchParams();

  query.set("companyId", filters.companyId);

  if (filters.startDate) {
    query.set("startDate", filters.startDate);
  }

  if (filters.endDate) {
    query.set("endDate", filters.endDate);
  }

  if (filters.accountingAccountId) {
    query.set(
      "accountingAccountId",
      filters.accountingAccountId
    );
  }

  if (filters.currentAccountId) {
    query.set(
      "currentAccountId",
      filters.currentAccountId
    );
  }

  if (filters.projectId) {
    query.set("projectId", filters.projectId);
  }

  if (filters.accountCode?.trim()) {
    query.set(
      "accountCode",
      filters.accountCode.trim()
    );
  }

  if (filters.search?.trim()) {
    query.set("search", filters.search.trim());
  }

  return `?${query.toString()}`;
}

