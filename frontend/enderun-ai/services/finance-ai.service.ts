import { apiClient } from "@/lib/api/api-client";


export interface FinanceAnalysis {
  summary: string;
  warnings: string[];
}


export const financeAIService = {

  analyze() {

    return apiClient<FinanceAnalysis>(
      "ai/finance-analysis"
    );

  }

};
