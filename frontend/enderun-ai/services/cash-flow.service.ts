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
};
