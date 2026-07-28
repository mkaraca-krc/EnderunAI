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

export type FinanceScopeFilter = {
  companyId?: string;
  projectId?: string;
  hierarchyNodeId?: string;
};

export function financeScopeQuery(
  filter: FinanceScopeFilter = {}
) {
  const query = new URLSearchParams();

  if (filter.companyId) {
    query.set("companyId", filter.companyId);
  }
  if (filter.projectId) {
    query.set("projectId", filter.projectId);
  }
  if (filter.hierarchyNodeId) {
    query.set("hierarchyNodeId", filter.hierarchyNodeId);
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const financeService = {
  getDashboard(filter: FinanceScopeFilter = {}) {
    return apiClient<FinanceDashboard>(
      `finance/dashboard${financeScopeQuery(filter)}`
    );
  }
};
