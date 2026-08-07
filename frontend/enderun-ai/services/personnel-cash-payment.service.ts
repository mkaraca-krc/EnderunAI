import { apiClient } from "@/lib/api/api-client";

/** Elden ödemenin türü. */
export const PersonnelCashPaymentKind = {
  MonthlySalary: 0,
  Advance: 1,
  Bonus: 2,
  Severance: 3,
  Other: 99,
} as const;

export const CASH_PAYMENT_KINDS: [number, string][] = [
  [PersonnelCashPaymentKind.MonthlySalary, "Aylık ücret"],
  [PersonnelCashPaymentKind.Advance, "Avans"],
  [PersonnelCashPaymentKind.Bonus, "Prim"],
  [PersonnelCashPaymentKind.Severance, "Ayrılış"],
  [PersonnelCashPaymentKind.Other, "Diğer"],
];

export type PersonnelCashPayment = {
  id: string;
  personnelId: string;
  personnelFullName: string;
  companyId: string;
  kind: number;
  kindName: string;
  paymentDate: string;
  amount: number;
  periodYear?: number | null;
  periodMonth?: number | null;
  note?: string | null;
};

export type CashPaymentSummaryRow = {
  personnelId: string;
  personnelFullName: string;
  /** Ek ödeme kartında tanımlı aylık tutar. */
  definedAmount: number;
  /** O dönem fiilen ödenen toplam. */
  paidAmount: number;
  /** Ödenen − tanımlı. Negatifse eksik ödeme var. */
  difference: number;
};

export type CashPaymentSummary = {
  companyId: string;
  year: number;
  month: number;
  personnelCount: number;
  definedTotal: number;
  paidTotal: number;
  unpaidCount: number;
  rows: CashPaymentSummaryRow[];
};

/**
 * Elden ödeme kasası: personele FİİLEN elden ödenen tutarların defteri.
 *
 * Ek ödeme kartı aylık ne ödeneceğinin TANIMIdır; burası gerçekten ne
 * zaman ne kadar ödendiğidir. Bu uçların hiçbiri muhasebe fişi, kasa
 * hareketi ya da proje maliyet kaydı üretmez; tamamı extra_payment
 * izinleriyle korunur.
 */
export const personnelCashPaymentService = {
  list(params: {
    personnelId?: string;
    companyId?: string;
    year?: number;
    month?: number;
  } = {}) {
    const query = new URLSearchParams();

    if (params.personnelId) query.set("personnelId", params.personnelId);
    if (params.companyId) query.set("companyId", params.companyId);
    if (params.year !== undefined) query.set("year", String(params.year));
    if (params.month !== undefined) query.set("month", String(params.month));

    const suffix = query.toString();

    return apiClient<PersonnelCashPayment[]>(
      `personnel-cash-payments${suffix ? `?${suffix}` : ""}`
    );
  },

  getSummary(companyId: string, year: number, month: number) {
    const query = new URLSearchParams({
      companyId,
      year: String(year),
      month: String(month),
    });

    return apiClient<CashPaymentSummary>(
      `personnel-cash-payments/summary?${query.toString()}`
    );
  },

  create(payload: {
    personnelId: string;
    kind: number;
    paymentDate: string;
    amount: number;
    periodYear?: number | null;
    periodMonth?: number | null;
    note?: string | null;
  }) {
    return apiClient<{ message: string; id: string }>(
      "personnel-cash-payments",
      { method: "POST", body: payload }
    );
  },

  remove(id: string) {
    return apiClient<{ message: string }>(`personnel-cash-payments/${id}`, {
      method: "DELETE",
    });
  },
};
