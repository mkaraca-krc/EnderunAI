import { apiClient } from "@/lib/api/api-client";

/** Maliyet sınıfı — kaynaktan türetilir, kullanıcı seçmez. */
export const ProjectCostClass = {
  Material: 0,
  Labor: 1,
  SubcontractorLabor: 2,
  Overhead: 3,
} as const;

export type CostComponentComparison = {
  costClass: number;
  costClassName: string;
  /** İcmalin tamamındaki öngörü. */
  forecastContract: number;
  /** İlerlemeye göre düzeltilmiş öngörü — asıl karşılaştırma tabanı. */
  forecastEarned: number;
  actual: number;
  variance: number;
  variancePercent?: number | null;
};

export type CostSectionBreakdown = {
  sectionId?: string | null;
  sectionName: string;
  materialAmount: number;
  laborAmount: number;
  subcontractorLaborAmount: number;
  overheadAmount: number;
  totalAmount: number;
};

export type CostMonthlyPoint = {
  year: number;
  month: number;
  label: string;
  materialAmount: number;
  laborAmount: number;
  subcontractorLaborAmount: number;
  overheadAmount: number;
  totalAmount: number;
  revenueAmount: number;
};

export type ProjectCostAnalysis = {
  projectId: string;
  projectCode: string;
  projectName: string;
  currencyCode: string;
  hasContractBaseline: boolean;
  contractForecastTotal: number;
  revenueAmount: number;
  progressRatio: number;
  totalCost: number;
  profit: number;
  profitMarginPercent?: number | null;
  /** Kâr üzerinden TAHMİNİ vergi yükü. */
  estimatedTax: number;
  netProfitAfterTax: number;
  taxRate: number;
  components: CostComponentComparison[];
  sections: CostSectionBreakdown[];
  monthly: CostMonthlyPoint[];
  employerCostFactor: number;
  /** Yalnızca elden ödeme yetkisi olan kullanıcıda dolu. */
  extraPaymentLaborCost?: number | null;
  includesExtraPayments: boolean;
  assumptions: string[];
};

export const projectCostAnalysisService = {
  get(projectId: string) {
    return apiClient<ProjectCostAnalysis>(`projects/${projectId}/cost-analysis`);
  },
};
