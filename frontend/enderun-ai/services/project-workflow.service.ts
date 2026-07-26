import { apiClient } from "@/lib/api/api-client";

export type WorkflowCountSummary = {
  total: number;
  draft: number;
  pending: number;
  approved: number;
  completed: number;
  cancelled: number;
};

export type WorkflowPurchaseOrderSummary = {
  total: number;
  draft: number;
  pendingApproval: number;
  approved: number;
  partiallyReceived: number;
  completed: number;
  cancelled: number;
  rejected: number;
  overdueDelivery: number;
};

export type WorkflowGoodsReceiptSummary = {
  total: number;
  draft: number;
  posted: number;
  cancelled: number;
};

export type WorkflowInventorySummary = {
  activeReservations: number;
  expiredReservations: number;
  reservedQuantity: number;
  consumedQuantity: number;
  remainingReservedQuantity: number;
  receiptMovementCount: number;
  issueMovementCount: number;
  receivedQuantity: number;
  issuedQuantity: number;
};

export type WorkflowProgressPaymentSummary = {
  total: number;
  draft: number;
  pendingApproval: number;
  approved: number;
  posted: number;
  cancelled: number;
  currentAmount: number;
  priceDifferenceAmount: number;
  grossPayableAmount: number;
  netPayableAmount: number;
};

export type WorkflowPaymentSummary = {
  total: number;
  draft: number;
  pendingApproval: number;
  approved: number;
  paid: number;
  cancelled: number;
  pendingAmount: number;
  approvedAmount: number;
  paidAmount: number;
};

export type WorkflowAccountingSummary = {
  voucherCount: number;
  draftVoucherCount: number;
  postedVoucherCount: number;
  cancelledVoucherCount: number;
  debitTotal: number;
  creditTotal: number;
};

export type ProjectWorkflowAlert = {
  severity: "danger" | "warning" | "info" | string;
  code: string;
  title: string;
  message: string;
  count: number;
};

export type ProjectWorkflowSummary = {
  projectId: string;
  companyId: string;
  projectCode: string;
  projectName: string;
  generatedAtUtc: string;
  purchaseRequests: WorkflowCountSummary;
  rfqs: WorkflowCountSummary;
  purchaseOrders: WorkflowPurchaseOrderSummary;
  goodsReceipts: WorkflowGoodsReceiptSummary;
  inventory: WorkflowInventorySummary;
  progressPayments: WorkflowProgressPaymentSummary;
  payments: WorkflowPaymentSummary;
  accounting: WorkflowAccountingSummary;
  alerts: ProjectWorkflowAlert[];
};

export const projectWorkflowService = {
  getSummary(projectId: string) {
    return apiClient<ProjectWorkflowSummary>(
      `projects/${projectId}/workflow-summary`
    );
  },
};
