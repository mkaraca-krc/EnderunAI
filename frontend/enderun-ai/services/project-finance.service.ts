import { apiClient } from "@/lib/api/api-client";

export interface ProjectFinanceSummary {
  projectId: string;
  projectCode: string;
  projectName: string;
  contractAmount: number;
  progressPaymentAmount: number;
  netPayableAmount: number;
  remainingAmount: number;
}


export const projectFinanceService = {

  getSummary() {
    return apiClient<ProjectFinanceSummary[]>(
      "finance/projects-summary"
    );
  }

};
