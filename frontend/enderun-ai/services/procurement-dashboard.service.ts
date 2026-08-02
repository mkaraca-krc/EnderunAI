import { apiClient } from "@/lib/api/api-client";

export type PurchaseRequestDashboardSummary = {
  total: number;
  draft: number;
  submitted: number;
  approved: number;
  quotation: number;
  ordered: number;
  completed: number;
  cancelled: number;
  rejected: number;
  open: number;
  criticalOpen: number;
};

export type RfqDashboardSummary = {
  total: number;
  draft: number;
  sent: number;
  responsesReceived: number;
  awarded: number;
  closed: number;
  cancelled: number;
  responseOverdue: number;
};

export type PurchaseOrderDashboardSummary = {
  total: number;
  draft: number;
  pendingApproval: number;
  approved: number;
  partiallyReceived: number;
  completed: number;
  cancelled: number;
  rejected: number;
  open: number;
  overdueDelivery: number;
};

export type GoodsReceiptDashboardSummary = {
  total: number;
  draft: number;
  posted: number;
  cancelled: number;
  exceptionLineCount: number;
};

export type PurchaseOrderCurrencySummary = {
  currency: string;
  totalAmount: number;
  activeAmount: number;
  completedAmount: number;
};

export type GoodsReceiptUnitSummary = {
  unit: string;
  acceptedQuantity: number;
  rejectedQuantity: number;
  damagedQuantity: number;
  exceptionLineCount: number;
};

export type RecentPurchaseOrderDashboardItem = {
  id: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  orderNumber: string;
  orderDate: string;
  expectedDeliveryDate?: string | null;
  status: number;
  supplierTitle: string;
  currency: string;
  grandTotal: number;
  itemCount: number;
};

export type RecentGoodsReceiptDashboardItem = {
  id: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  receiptNumber: string;
  receiptDate: string;
  status: number;
  purchaseOrderNumber: string;
  supplierTitle: string;
  warehouseName: string;
  itemCount: number;
  exceptionLineCount: number;
};

export type ProcurementDashboardAlert = {
  severity: "danger" | "warning" | "info" | string;
  code: string;
  title: string;
  message: string;
  count: number;
  href: string;
};

export type ProcurementDashboard = {
  companyId?: string | null;
  projectId?: string | null;
  generatedAtUtc: string;
  purchaseRequests: PurchaseRequestDashboardSummary;
  rfqs: RfqDashboardSummary;
  purchaseOrders: PurchaseOrderDashboardSummary;
  goodsReceipts: GoodsReceiptDashboardSummary;
  orderValues: PurchaseOrderCurrencySummary[];
  receiptQuantities: GoodsReceiptUnitSummary[];
  recentPurchaseOrders: RecentPurchaseOrderDashboardItem[];
  recentGoodsReceipts: RecentGoodsReceiptDashboardItem[];
  alerts: ProcurementDashboardAlert[];
};

export const procurementDashboardService = {
  getDashboard(params?: {
    companyId?: string;
    projectId?: string;
  }) {
    const query = new URLSearchParams();

    if (params?.companyId) {
      query.set("companyId", params.companyId);
    }

    if (params?.projectId) {
      query.set("projectId", params.projectId);
    }

    const suffix = query.size > 0 ? `?${query.toString()}` : "";
    return apiClient<ProcurementDashboard>(
      `procurement/dashboard${suffix}`,
    );
  },
};
