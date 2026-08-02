import { apiClient } from "@/lib/api/api-client";

export interface FinanceDashboard {
  totalContractAmount: number;
  totalProgressPaymentAmount: number;
  totalPriceDifferenceAmount: number;
  totalDeductionAmount: number;
  totalNetPayableAmount: number;
  activeProjectCount: number;
  progressPaymentCount: number;
}


export const financeService = {

  getDashboard() {

    return apiClient<FinanceDashboard>(
      "finance/dashboard"
    );

  }

};
