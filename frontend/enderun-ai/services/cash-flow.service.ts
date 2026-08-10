import { apiClient } from "@/lib/api/api-client";

export interface CashFlowSummary {
  totalIncome: number;
  totalExpense: number;
  netCash: number;
  // Kasa/banka hareket modülü henüz uygulamaya bağlı değil - false ise
  // yukarıdaki tutarlar gerçek değil, arayüz "veri yok" göstermeli.
  available: boolean;
  message?: string | null;
}

/** Beklenen tek bir giriş/çıkış kalemi (çek, hakediş, tedarikçi faturası). */
export interface CashFlowItem {
  kind: string;
  kindName: string;
  sourceId: string;
  reference: string;
  title: string;
  currentAccountId?: string | null;
  currentAccountTitle?: string | null;
  projectId?: string | null;
  projectCode?: string | null;
  expectedDate: string;
  daysToDue: number;
  isOverdue: boolean;
  amount: number;
  currencyCode: string;
}

export interface CashFlowBucket {
  days: number;
  label: string;
  inflowAmount: number;
  outflowAmount: number;
  netAmount: number;
  projectedBalance: number;
}

export interface CashFlowForecast {
  companyId: string;
  asOfDate: string;
  currentCashBalance: number;
  overdueInflowAmount: number;
  overdueOutflowAmount: number;
  buckets: CashFlowBucket[];
  inflows: CashFlowItem[];
  outflows: CashFlowItem[];
}


/** Bir kalemin tarihi ne kadar güvenilir. */
export const CashFlowCertainty = { Confirmed: 0, Estimated: 1 } as const;

export interface CashFlowProjectionItem {
  date: string;
  kind: string;
  kindName: string;
  title: string;
  reference?: string | null;
  projectId?: string | null;
  projectCode?: string | null;
  amount: number;
  isInflow: boolean;
  certainty: number;
  certaintyName: string;
}

export interface CashFlowProjectionDay {
  date: string;
  inflow: number;
  outflow: number;
  net: number;
  runningBalance: number;
  items: CashFlowProjectionItem[];
}

export interface CashFlowProjectionMonth {
  year: number;
  month: number;
  label: string;
  inflow: number;
  outflow: number;
  net: number;
  closingBalance: number;
  lowestBalance: number;
  lowestBalanceDate?: string | null;
}

/**
 * Finansman açığı: ilk negatif gün ve EN DERİN nokta.
 *
 * İkisi ayrı sorudur — ilk gün "ne zaman para bitiyor", en derin
 * nokta "ne kadar bulmam lazım". Kredi pazarlığı ikincisine göre
 * yapılır.
 */
export interface CashFlowShortfall {
  firstNegativeDate: string;
  firstNegativeBalance: number;
  peakDate: string;
  peakBalance: number;
  requiredFinancing: number;
}

export interface CashFlowTargetSummary {
  targetDate: string;
  inflow: number;
  outflow: number;
  closingBalance: number;
  requiredFinancing: number;
}

export interface CashFlowProjection {
  companyId: string;
  fromDate: string;
  toDate: string;
  months: number;
  openingBalance: number;
  closingBalance: number;
  monthlySummary: CashFlowProjectionMonth[];
  days: CashFlowProjectionDay[];
  shortfall?: CashFlowShortfall | null;
  target?: CashFlowTargetSummary | null;
  /** Tablonun neyi göstermediğini söyleyen uyarılar. */
  notes: string[];
}

export interface EstimatedExpense {
  id: string;
  description: string;
  amount: number;
  startYear: number;
  startMonth: number;
  recurrenceCount: number;
  paymentDay: number;
  projectId?: string | null;
  projectCode?: string | null;
}

export interface SaveEstimatedExpenseRequest {
  companyId: string;
  description: string;
  amount: number;
  startYear: number;
  startMonth: number;
  recurrenceCount: number;
  paymentDay: number;
  projectId?: string | null;
}

export const cashFlowService = {
  getSummary() {
    return apiClient<CashFlowSummary>("finance/cash-flow");
  },

  /** Vade bazlı 30/60/90 gün nakit akışı. */
  getForecast(params: { companyId: string; projectId?: string }) {
    const query = new URLSearchParams({ companyId: params.companyId });
    if (params.projectId) query.set("projectId", params.projectId);

    return apiClient<CashFlowForecast>(`cash-flow?${query.toString()}`);
  },

  /**
   * Likidite takvimi: tarih bazlı yürüyen bakiye.
   *
   * AYRI İZİN (cashflow.view): tablo bordroyu elden dahil tam tutarla
   * taşıyor, o yüzden finance.view'den dar bir kapıda.
   */
  getProjection(params: {
    companyId: string;
    months?: number;
    targetDate?: string;
  }) {
    const query = new URLSearchParams({ companyId: params.companyId });

    if (params.months) query.set("months", String(params.months));
    if (params.targetDate) query.set("targetDate", params.targetDate);

    return apiClient<CashFlowProjection>(
      `cash-flow/projeksiyon?${query.toString()}`
    );
  },

  listEstimatedExpenses(companyId: string) {
    return apiClient<EstimatedExpense[]>(
      `cash-flow/tahmini-giderler?companyId=${companyId}`
    );
  },

  createEstimatedExpense(payload: SaveEstimatedExpenseRequest) {
    return apiClient<{ id: string; message: string }>(
      "cash-flow/tahmini-giderler",
      { method: "POST", body: payload }
    );
  },

  deleteEstimatedExpense(id: string) {
    return apiClient<{ message: string }>(
      `cash-flow/tahmini-giderler/${id}`,
      { method: "DELETE" }
    );
  },
};
