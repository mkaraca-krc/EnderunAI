import { apiClient } from "@/lib/api/api-client";

export type SupplierSpendCurrency = {
  currency: string;
  orderTotal: number;
};

export type SupplierPerformance = {
  supplierCurrentAccountId: string;
  companyId: string;
  supplierCode: string;
  supplierTitle: string;
  invitationCount: number;
  responseCount: number;
  responseRate: number;
  awardCount: number;
  totalOrderCount: number;
  completedOrderCount: number;
  activeOrderCount: number;
  overdueOpenOrderCount: number;
  deliveryMeasuredOrderCount: number;
  onTimeDeliveryOrderCount: number;
  onTimeDeliveryRate: number;
  receiptLineCount: number;
  exceptionLineCount: number;
  qualityRate: number;
  priceBenchmarkCount: number;
  priceScore: number;
  performanceScore: number;
  confidence: string;
  lastOrderDate?: string | null;
  spendByCurrency: SupplierSpendCurrency[];
};

export type RfqDecisionSupport = {
  rfqId: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  rfqNumber: string;
  issueDate: string;
  status: number;
  quotationCount: number;
  comparisonCurrency: string;
  lowestNormalizedTotal: number;
  highestNormalizedTotal: number;
  averageNormalizedTotal: number;
  offerSpread: number;
  recommendedSupplierCurrentAccountId: string;
  recommendedSupplierTitle: string;
  recommendedNormalizedTotal: number;
  recommendedScore: number;
  awardedSupplierCurrentAccountId?: string | null;
  awardedSupplierTitle?: string | null;
  awardedNormalizedTotal?: number | null;
};

export type ProcurementDecisionAlert = {
  severity: "danger" | "warning" | "info" | string;
  code: string;
  title: string;
  message: string;
  count: number;
  href: string;
};

export type ProcurementDecisionSupport = {
  companyId?: string | null;
  projectId?: string | null;
  periodDays: number;
  periodStartUtc: string;
  generatedAtUtc: string;
  summary: {
    supplierCount: number;
    comparedRfqCount: number;
    averageSupplierScore: number;
    responseRate: number;
    onTimeDeliveryRate: number;
    qualityRate: number;
    comparedOfferSpreadTotalTry: number;
  };
  suppliers: SupplierPerformance[];
  recentRfqComparisons: RfqDecisionSupport[];
  alerts: ProcurementDecisionAlert[];
};

export const procurementDecisionSupportService = {
  getReport(params?: {
    companyId?: string;
    projectId?: string;
    periodDays?: number;
  }) {
    const query = new URLSearchParams();

    if (params?.companyId) query.set("companyId", params.companyId);
    if (params?.projectId) query.set("projectId", params.projectId);
    if (params?.periodDays) {
      query.set("periodDays", String(params.periodDays));
    }

    const suffix = query.size > 0 ? `?${query.toString()}` : "";
    return apiClient<ProcurementDecisionSupport>(
      `procurement/decision-support${suffix}`,
    );
  },
};
