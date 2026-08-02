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


export const cashFlowService = {

  getSummary() {
    return apiClient<CashFlowSummary>(
      "finance/cash-flow"
    );
  }

};
