import { apiClient } from "@/lib/api/api-client";

export type ProcurementApprovalPolicy = {
  versionId: string;
  companyId: string;
  purchasingApprovalLimitTry: number;
  financeApprovalLimitTry: number;
  requireBudget: boolean;
  note?: string | null;
  updatedBy?: string | null;
  updatedAtUtc: string;
};

export type ProcurementBudget = {
  budgetId: string;
  versionId: string;
  companyId: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  name: string;
  periodStart: string;
  periodEnd: string;
  amountTry: number;
  warningThresholdPercent: number;
  isActive: boolean;
  note?: string | null;
  updatedBy?: string | null;
  updatedAtUtc: string;
  committedAmountTry: number;
  remainingAmountTry: number;
  utilizationPercent: number;
  isWarning: boolean;
  isExceeded: boolean;
};

export type PurchaseOrderApprovalStep = {
  sequence: number;
  code: string;
  name: string;
  requiredAuthority: string;
  status: "Pending" | "Approved" | "Rejected";
  decidedByUserId?: string | null;
  decidedByUsername?: string | null;
  decidedAtUtc?: string | null;
  note?: string | null;
};

export type PurchaseOrderApprovalContext = {
  purchaseOrderId: string;
  orderNumber: string;
  companyId: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  orderStatus: number;
  orderAmountTry: number;
  policyConfigured: boolean;
  policy?: ProcurementApprovalPolicy | null;
  budget?: ProcurementBudget | null;
  budgetAmountAfterOrderTry?: number | null;
  budgetRemainingAfterOrderTry?: number | null;
  budgetAllowsOrder: boolean;
  planId?: string | null;
  currentStageSequence?: number | null;
  currentStageName?: string | null;
  canCurrentUserApprove: boolean;
  steps: PurchaseOrderApprovalStep[];
  warnings: string[];
};

export type ProcurementPendingApproval = {
  purchaseOrderId: string;
  orderNumber: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  supplierTitle: string;
  orderDate: string;
  orderAmountTry: number;
  currentStageSequence: number;
  currentStageName: string;
  requiredAuthority: string;
  canCurrentUserApprove: boolean;
  budgetWarning: boolean;
  budgetRemainingAfterOrderTry?: number | null;
};

export type ProcurementApprovalDashboard = {
  companyId: string;
  companyCode: string;
  companyName: string;
  policy?: ProcurementApprovalPolicy | null;
  budgets: ProcurementBudget[];
  pendingApprovals: ProcurementPendingApproval[];
  pendingApprovalCount: number;
  approvalsCurrentUserCanActOn: number;
  pendingApprovalAmountTry: number;
  budgetWarningCount: number;
  warnings: string[];
};

export type ConfigureProcurementApprovalPolicyRequest = {
  purchasingApprovalLimitTry: number;
  financeApprovalLimitTry: number;
  requireBudget: boolean;
  note?: string | null;
};

export type UpsertProcurementBudgetRequest = {
  name: string;
  periodStart: string;
  periodEnd: string;
  amountTry: number;
  warningThresholdPercent: number;
  isActive: boolean;
  note?: string | null;
};

export const procurementApprovalService = {
  getDashboard(companyId: string, projectId?: string) {
    const query = new URLSearchParams({ companyId });
    if (projectId) query.set("projectId", projectId);
    return apiClient<ProcurementApprovalDashboard>(
      `procurement/approval-control/dashboard?${query.toString()}`,
    );
  },

  getOrderContext(purchaseOrderId: string) {
    return apiClient<PurchaseOrderApprovalContext>(
      `procurement/approval-control/orders/${purchaseOrderId}`,
    );
  },

  configurePolicy(
    companyId: string,
    payload: ConfigureProcurementApprovalPolicyRequest,
  ) {
    return apiClient<ProcurementApprovalPolicy>(
      `procurement/approval-control/companies/${companyId}/policy`,
      { method: "PUT", body: payload },
    );
  },

  createBudget(projectId: string, payload: UpsertProcurementBudgetRequest) {
    return apiClient<ProcurementBudget>(
      `procurement/approval-control/projects/${projectId}/budgets`,
      { method: "POST", body: payload },
    );
  },

  updateBudget(
    projectId: string,
    budgetId: string,
    payload: UpsertProcurementBudgetRequest,
  ) {
    return apiClient<ProcurementBudget>(
      `procurement/approval-control/projects/${projectId}/budgets/${budgetId}`,
      { method: "PUT", body: payload },
    );
  },
};

