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
  /** Tahsil edilebilir (onaylı) ilave iş toplamı. */
  collectibleExtraWorkAmount: number;
  /** Onay bekleyen ilave iş — erozyondan düşülmez. */
  pendingExtraWorkAmount: number;
  /** Onaylı ek iş düşüldükten sonra kalan fiili kâr erozyonu. */
  netErosionAmount: number;
  warnings: string[];
};

export const ExtraWorkApprovalStatus = {
  Pending: 0,
  Approved: 1,
  Rejected: 2,
} as const;

export type ProjectExtraWork = {
  id: string;
  projectHakedisSectionId?: string | null;
  sectionName?: string | null;
  positionCode: string;
  description: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  amount: number;
  workDate: string;
  approvalStatus: number;
  approvedAtUtc?: string | null;
  approvalDocumentId?: string | null;
  approvalDocumentName?: string | null;
  progressPaymentId?: string | null;
  progressPaymentNumber?: string | null;
  notes?: string | null;
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

/** Hakedişe aktarılmaya uygun ilave iş — uçtan süzülmüş hâli. */
export type TransferableExtraWork = {
  id: string;
  projectHakedisSectionId?: string | null;
  positionCode: string;
  description: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  amount: number;
};

export const extraWorkService = {
  list(projectId: string) {
    return apiClient<ProjectExtraWork[]>(
      `project-extra-works?projectId=${projectId}`
    );
  },

  create(payload: {
    projectId: string;
    projectHakedisSectionId?: string | null;
    positionCode: string;
    description: string;
    unit: string;
    quantity: number;
    unitPrice: number;
    workDate: string;
    notes?: string | null;
  }) {
    return apiClient<{ id: string; message: string }>("project-extra-works", {
      method: "POST",
      body: payload,
    });
  },

  /** Anahtar teslimde onay belgesi zorunlu. */
  approve(id: string, approvalDocumentId: string | null, notes?: string | null) {
    return apiClient<{ message: string }>(
      `project-extra-works/${id}/approve`,
      { method: "POST", body: { approvalDocumentId, notes: notes ?? null } }
    );
  },

  reject(id: string, notes?: string | null) {
    return apiClient<{ message: string }>(
      `project-extra-works/${id}/reject`,
      { method: "POST", body: { approvalDocumentId: null, notes: notes ?? null } }
    );
  },

  /**
   * Hakedişe aktarılabilecek ilave işler.
   *
   * KURAL BURADA TEKRARLANMIYOR: reddedilenleri ve zaten aktarılmış
   * olanları uç eliyor, anahtar teslim sözleşmede yalnızca işveren
   * onaylı olanı veriyor. Ekran "onaylı ve hakedişsiz" diye kendi
   * süzgecini kursaydı, sözleşme türü kuralını ikinci kez yazmış
   * olurdu — anahtar teslimde onaysız ek iş devredilebilir görünürdü.
   */
  transferable(projectId: string) {
    return apiClient<TransferableExtraWork[]>(
      `project-extra-works/transferable?projectId=${projectId}`
    );
  },

  /** İlave işi bir hakedişe bağlar; uç tekrar aktarımı engeller. */
  transfer(id: string, progressPaymentId: string) {
    return apiClient<{ message: string }>(
      `project-extra-works/${id}/transfer/${progressPaymentId}`,
      { method: "POST" }
    );
  },
};
