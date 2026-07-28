import { apiClient } from "@/lib/api/api-client";
import {
  financeScopeQuery,
  type FinanceScopeFilter,
} from "@/services/finance.service";

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

  getSummary(filter: FinanceScopeFilter = {}) {
    return apiClient<ProjectFinanceSummary[]>(
      `finance/projects-summary${financeScopeQuery(filter)}`
    );
  }

};
