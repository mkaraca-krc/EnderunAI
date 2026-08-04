import { apiClient } from "@/lib/api/api-client";

/** Sunucudaki ProjectContractType ile aynı değerler. */
export const ProjectContractType = {
  Undetermined: 0,
  LumpSum: 1,
  UnitPrice: 2,
  Mixed: 3,
} as const;

export const CONTRACT_TYPE_LABELS: Record<number, string> = {
  [ProjectContractType.Undetermined]: "Belirlenmedi",
  [ProjectContractType.LumpSum]: "Anahtar Teslim (Götürü)",
  [ProjectContractType.UnitPrice]: "Birim Fiyatlı",
  [ProjectContractType.Mixed]: "Karma",
};

/**
 * Sapmanın anlamı — renk kodu doğrudan buradan çıkar.
 * Sunucudaki DeviationImpact ile aynı değerler.
 */
export const DeviationImpact = {
  None: 0,
  /** Birim fiyatlıda keşif üstü: ilave hakediş fırsatı. */
  Opportunity: 1,
  /** Anahtar teslimde keşif üstü: kâr erozyonu. */
  ProfitErosion: 2,
  /** Anahtar teslimde keşif altı: tasarruf. */
  Saving: 3,
  /** Birim fiyatlıda keşif altı: yalnızca bilgi. */
  Information: 4,
  Undetermined: 5,
} as const;

export type TrackingItem = {
  positionCode: string;
  description: string;
  unit: string;
  sectionId?: string | null;
  sectionName?: string | null;
  contractQuantity: number;
  realizedQuantity: number;
  remainingQuantity: number;
  deviationQuantity: number;
  deviationRate: number;
  contractAmount: number;
  realizedAmount: number;
  deviationAmount: number;
  issuedStockQuantity?: number | null;
  effectiveContractType: number;
  impact: number;
  exceedsWarningThreshold: boolean;
};

export type TrackingTotals = {
  contractAmount: number;
  realizedAmount: number;
  overrunAmount: number;
  underrunAmount: number;
  netDeviationAmount: number;
  physicalCompletionRate: number;
  itemCount: number;
  warningItemCount: number;
};

export type ProfitEstimate = {
  isReliable: boolean;
  unreliableReason?: string | null;
  contractAmount: number;
  actualCost: number;
  physicalCompletionRate: number;
  estimatedTotalCost: number;
  estimatedProfit: number;
  estimatedProfitRate: number;
};

export type ProgressTracking = {
  projectId: string;
  projectCode: string;
  projectName: string;
  contractType: number;
  contractTypeName: string;
  contractAmount?: number | null;
  deviationAlertThresholdRate: number;
  /** Sözleşme metrajının kaynağı — kullanıcı neye baktığını bilmeli. */
  baselineSource: string;
  items: TrackingItem[];
  totals: TrackingTotals;
  profitEstimate: ProfitEstimate;
  erosionAlarm: boolean;
  warnings: string[];
};

export type ProgressDeviationAlert = {
  projectId: string;
  projectCode: string;
  projectName: string;
  contractType: number;
  exceedingItemCount: number;
  netDeviationAmount: number;
  erosionAlarm: boolean;
};

export const progressTrackingService = {
  get(projectId: string) {
    return apiClient<ProgressTracking>(
      `projects/${projectId}/progress-tracking`
    );
  },

  /** Sapma uyarısı üreten projeler (dashboard bildirim merkezi). */
  getAlerts() {
    return apiClient<ProgressDeviationAlert[]>("progress-tracking/alerts");
  },
};
