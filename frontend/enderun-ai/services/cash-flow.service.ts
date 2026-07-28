import { apiClient } from "@/lib/api/api-client";
import {
  financeScopeQuery,
  type FinanceScopeFilter,
} from "@/services/finance.service";

export interface CashFlowSummary {
  totalIncome: number;
  totalExpense: number;
  netCash: number;
}


export const cashFlowService = {

  getSummary(filter: FinanceScopeFilter = {}) {
    return apiClient<CashFlowSummary>(
      `finance/cash-flow${financeScopeQuery(filter)}`
    );
  }

};
