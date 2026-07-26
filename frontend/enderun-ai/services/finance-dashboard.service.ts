import { apiClient } from "@/lib/api/api-client";

type CompanyListItem = {
  id: string;
  code: string;
  name: string;
  isActive?: boolean;
};

type FinancialDashboardSummary = {
  cashBalance: number;
  bankBalance: number;
  totalLiquidAssets: number;
  receivables: number;
  payables: number;
  todayCollections: number;
  todayPayments: number;
  periodRevenue: number;
  periodExpense: number;
  netProfit: number;
  netLoss: number;
  cashInflow: number;
  cashOutflow: number;
  netCashChange: number;
};

type FinancialDashboardApiResponse = {
  companyId: string;
  startDate: string;
  endDate: string;
  generatedAtUtc: string;
  summary: FinancialDashboardSummary;
};

export interface FinanceDashboard {
  companyId: string;
  companyName: string;
  startDate: string;
  endDate: string;
  generatedAtUtc: string;

  cashBalance: number;
  bankBalance: number;
  totalLiquidAssets: number;

  receivables: number;
  payables: number;

  todayCollections: number;
  todayPayments: number;

  periodRevenue: number;
  periodExpense: number;

  netProfit: number;
  netLoss: number;

  cashInflow: number;
  cashOutflow: number;
  netCashChange: number;

  // Eski dashboard alanlarıyla geriye uyumluluk
  supplierDebt: number;
  pendingPayments: number;
  netCash: number;

  totalContractAmount: number;
  totalProgressPaymentAmount: number;
  totalPriceDifferenceAmount: number;
  totalDeductionAmount: number;
  totalNetPayableAmount: number;
  activeProjectCount: number;
  progressPaymentCount: number;
}

function toIsoDate(value: Date): string {
  return value.toISOString().slice(0, 10);
}

export const financeDashboardService = {
  async getDashboard(): Promise<FinanceDashboard> {
    const companies =
      await apiClient<CompanyListItem[]>("companies");

    const company =
      companies.find((item) => item.isActive !== false) ??
      companies[0];

    if (!company) {
      throw new Error(
        "Finans dashboard için aktif şirket bulunamadı."
      );
    }

    const now = new Date();

    const startDate = toIsoDate(
      new Date(now.getFullYear(), 0, 1)
    );

    const endDate = toIsoDate(now);

    const query = new URLSearchParams({
      companyId: company.id,
      startDate,
      endDate,
    });

    const result =
      await apiClient<FinancialDashboardApiResponse>(
        `finance/financial-dashboard?${query.toString()}`
      );

    const summary = result.summary;

    return {
      companyId: result.companyId,
      companyName: company.name,
      startDate: result.startDate,
      endDate: result.endDate,
      generatedAtUtc: result.generatedAtUtc,

      cashBalance: summary.cashBalance,
      bankBalance: summary.bankBalance,
      totalLiquidAssets: summary.totalLiquidAssets,

      receivables: summary.receivables,
      payables: summary.payables,

      todayCollections: summary.todayCollections,
      todayPayments: summary.todayPayments,

      periodRevenue: summary.periodRevenue,
      periodExpense: summary.periodExpense,

      netProfit: summary.netProfit,
      netLoss: summary.netLoss,

      cashInflow: summary.cashInflow,
      cashOutflow: summary.cashOutflow,
      netCashChange: summary.netCashChange,

      supplierDebt: summary.payables,
      pendingPayments: summary.todayPayments,
      netCash: summary.netCashChange,

      totalContractAmount: 0,
      totalProgressPaymentAmount: 0,
      totalPriceDifferenceAmount: 0,
      totalDeductionAmount: 0,
      totalNetPayableAmount: 0,
      activeProjectCount: 0,
      progressPaymentCount: 0,
    };
  },
};
