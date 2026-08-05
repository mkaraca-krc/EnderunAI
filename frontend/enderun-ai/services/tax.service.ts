import { apiClient } from "@/lib/api/api-client";

/** Takvimdeki vergi yükümlülüğünün türü. */
export const TaxObligationKind = {
  Vat: 0,
  SocialSecurity: 1,
  Withholding: 2,
  AdvanceTax: 3,
} as const;

export type VatPeriodSummary = {
  year: number;
  month: number;
  label: string;
  outputVat: number;
  inputVat: number;
  /** Sorumlu sıfatıyla ödenecek KDV — ayrı ödenir. */
  reverseChargeVat: number;
  carryForwardIn: number;
  payableVat: number;
  carryForwardOut: number;
  isAccrued: boolean;
  accrualVoucherId?: string | null;
  accrualVoucherNumber?: string | null;
};

export type PayrollTaxPeriodSummary = {
  year: number;
  month: number;
  label: string;
  incomeTaxWithholding: number;
  stampTax: number;
  sgkEmployee: number;
  sgkEmployer: number;
  sgkTotal: number;
  totalBurden: number;
  personnelCount: number;
  /** Tahakkuk fişi kesilmiş mi; kesilmemişse rakam bordrodan gelir. */
  isAccrued: boolean;
};

export type AdvanceTaxPeriodSummary = {
  year: number;
  quarter: number;
  label: string;
  periodStart: string;
  periodEnd: string;
  revenue: number;
  expense: number;
  profitBeforeTax: number;
  taxRate: number;
  estimatedTax: number;
  dueDate: string;
};

export type TaxOverview = {
  companyId: string;
  currencyCode: string;
  vat: VatPeriodSummary[];
  payroll: PayrollTaxPeriodSummary[];
  advanceTax: AdvanceTaxPeriodSummary[];
  corporateTaxRate: number;
  estimatedAnnualCorporateTax: number;
  assumptions: string[];
};

export type TaxObligation = {
  kind: number;
  kindName: string;
  periodYear: number;
  periodNumber: number;
  periodLabel: string;
  dueDate: string;
  estimatedAmount: number;
  isPaid: boolean;
  paidAmount?: number | null;
  paidAtUtc?: string | null;
  isOverdue: boolean;
};

export type VatAccrualResult = {
  voucherId: string;
  voucherNumber: string;
  year: number;
  month: number;
  payableVat: number;
  carryForwardOut: number;
  message: string;
};

export type VatReconciliationRow = {
  year: number;
  month: number;
  label: string;
  computedPayable: number;
  computedCarryForward: number;
  isAccrued: boolean;
  voucherNumber?: string | null;
  accruedPayable: number;
  accruedCarryForward: number;
  difference: number;
};

export const taxService = {
  getOverview(companyId: string, year?: number) {
    const query = new URLSearchParams({ companyId });
    if (year) query.set("year", String(year));

    return apiClient<TaxOverview>(`tax/overview?${query.toString()}`);
  },

  getCalendar(companyId: string, from?: string, to?: string) {
    const query = new URLSearchParams({ companyId });
    if (from) query.set("from", from);
    if (to) query.set("to", to);

    return apiClient<TaxObligation[]>(`tax/calendar?${query.toString()}`);
  },

  getVatReconciliation(companyId: string, year?: number) {
    const query = new URLSearchParams({ companyId });
    if (year) query.set("year", String(year));

    return apiClient<VatReconciliationRow[]>(
      `tax/vat-reconciliation?${query.toString()}`
    );
  },

  accrueVat(companyId: string, year: number, month: number) {
    return apiClient<VatAccrualResult>("tax/vat-accrual", {
      method: "POST",
      body: { companyId, year, month },
    });
  },

  markPaid(payload: {
    companyId: string;
    kind: number;
    periodYear: number;
    periodNumber: number;
    amount?: number | null;
    note?: string | null;
  }) {
    return apiClient<TaxObligation>("tax/payments", {
      method: "POST",
      body: payload,
    });
  },

  undoPayment(
    companyId: string,
    kind: number,
    periodYear: number,
    periodNumber: number
  ) {
    const query = new URLSearchParams({
      companyId,
      kind: String(kind),
      periodYear: String(periodYear),
      periodNumber: String(periodNumber),
    });

    return apiClient<void>(`tax/payments?${query.toString()}`, {
      method: "DELETE",
    });
  },
};
