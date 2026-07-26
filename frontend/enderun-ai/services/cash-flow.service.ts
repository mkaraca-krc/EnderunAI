import { apiClient } from "@/lib/api/api-client";

export interface CashFlowSummary {
  totalIncome: number;
  totalExpense: number;
  netCash: number;
}


export const cashFlowService = {

  getSummary() {
    return apiClient<CashFlowSummary>(
      "finance/cash-flow"
    );
  }

};
