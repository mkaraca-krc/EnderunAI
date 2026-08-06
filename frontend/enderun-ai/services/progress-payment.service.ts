import { apiClient } from "@/lib/api/api-client";

export enum ProgressPaymentStatus {
  Draft = 0,
  PendingApproval = 1,
  Approved = 2,
  Posted = 3,
  Cancelled = 4,
}

/** Projenin imalat bölümü şablonu (NATURA'da 12 bölüm). */
export interface ProjectHakedisSection {
  id: string;
  order: number;
  name: string;
  code?: string | null;
  isActive: boolean;
}

/** Hakediş hazırlanırken gereken önceki dönem bilgileri. */
export interface PreviousContext {
  previousTotalAmount: number;
  previousQuantities: Array<{ positionCode: string; quantity: number }>;
  previousDeductions: Array<{ deductionType: number; amount: number }>;
}

/** Sonraki hakedişte mahsup edilebilecek açık ihzarat kalemi. */
export interface OpenAdvanceMaterial {
  id: string;
  positionCode: string;
  description: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  valuationRate: number;
  amount: number;
  offsetAmount: number;
  openAmount: number;
  sourceProgressPaymentNumber: string;
  sourcePeriodNumber: number;
}

export interface ProgressPaymentItemRequest {
  engineeringPositionId?: string | null;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  currentQuantity: number;
  unitPrice: number;
  measurementReference?: string | null;
  notes?: string | null;
  /** Birim fiyat bileşenleri; verilirse UnitPrice bunların toplamıdır. */
  materialUnitPrice?: number | null;
  laborUnitPrice?: number | null;
  overheadUnitPrice?: number | null;
  /** Pozun ait olduğu proje bölümü. */
  sectionId?: string | null;
  /** Satırın geldiği sözleşme icmali kalemi; elle satırda boş. */
  projectBoqItemId?: string | null;
}

export interface ProgressPaymentAdvanceMaterialRequest {
  positionCode: string;
  description: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  valuationRate: number;
  notes?: string | null;
}

export interface ProgressPaymentAdvanceOffsetRequest {
  advanceMaterialId: string;
  amount: number;
  notes?: string | null;
}

export interface ProgressPaymentDeductionLineRequest {
  name: string;
  unitPrice: number;
  quantity: number;
  vatRate: number;
  notes?: string | null;
}

export interface ProgressPaymentDeductionRequest {
  deductionType: number;
  description: string;
  rate: number;
  baseAmount: number;
  manualAmount?: number | null;
  notes?: string | null;
  accountingAccountId?: string | null;
  /** Verilmezse hakedişin kümülatif toplamı kullanılır. */
  cumulativeBaseAmount?: number | null;
  lines?: ProgressPaymentDeductionLineRequest[] | null;
}

export interface ProgressPaymentPaymentPlanRequest {
  paymentType: number;
  rate: number;
  maturityDays?: number | null;
  description?: string | null;
}

export interface CreateProgressPaymentRequest {
  companyId: string;
  projectId: string;
  projectMeasurementId?: string | null;
  progressPaymentNumber: string;
  periodNumber: number;
  periodStartDate?: string | null;
  periodEndDate?: string | null;
  progressPaymentDate: string;
  priceDifferenceAmount: number;
  vatRate: number;
  withholdingNumerator: number;
  withholdingDenominator: number;
  description?: string | null;
  notes?: string | null;
  items: ProgressPaymentItemRequest[];
  deductions: ProgressPaymentDeductionRequest[];
  advanceMaterials?: ProgressPaymentAdvanceMaterialRequest[] | null;
  advanceOffsets?: ProgressPaymentAdvanceOffsetRequest[] | null;
  incomeTaxWithholdingRate?: number;
  paymentPlans?: ProgressPaymentPaymentPlanRequest[] | null;
}

export interface UpdateProgressPaymentRequest {
  periodStartDate?: string | null;
  periodEndDate?: string | null;
  progressPaymentDate: string;
  priceDifferenceAmount: number;
  vatRate: number;
  withholdingNumerator: number;
  withholdingDenominator: number;
  description?: string | null;
  notes?: string | null;
  items: ProgressPaymentItemRequest[];
  deductions: ProgressPaymentDeductionRequest[];
  advanceMaterials?: ProgressPaymentAdvanceMaterialRequest[] | null;
  advanceOffsets?: ProgressPaymentAdvanceOffsetRequest[] | null;
  incomeTaxWithholdingRate?: number;
  paymentPlans?: ProgressPaymentPaymentPlanRequest[] | null;
}

export interface ProgressPaymentListItem {
  id: string;
  companyId: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  progressPaymentNumber: string;
  periodNumber: number;
  progressPaymentDate: string;
  status: ProgressPaymentStatus;
  currencyCode: string;
  previousAmount: number;
  currentAmount: number;
  cumulativeAmount: number;
  priceDifferenceAmount: number;
  vatAmount: number;
  withholdingAmount: number;
  totalDeductionAmount: number;
  netPayableAmount: number;
  itemCount: number;
}

export interface ProgressPaymentItem {
  id: string;
  engineeringPositionId?: string | null;
  /** Pozun ait olduğu imalat bölümü (hakedişin kendi kopyası). */
  progressPaymentSectionId?: string | null;
  lineNumber: number;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  previousQuantity: number;
  currentQuantity: number;
  cumulativeQuantity: number;
  /** Satırın geldiği icmal kalemi. */
  projectBoqItemId?: string | null;
  /** Hakediş hazırlanırken dondurulan saha miktarı (bu dönem). */
  fieldQuantity: number;
  cumulativeFieldQuantity: number;
  /** Bu dönem − saha: eksi ise devreden iş. */
  fieldDifference: number;
  materialUnitPrice: number;
  laborUnitPrice: number;
  overheadUnitPrice: number;
  unitPrice: number;
  materialAmount: number;
  laborAmount: number;
  overheadAmount: number;
  previousAmount: number;
  currentAmount: number;
  cumulativeAmount: number;
  completionRate: number;
  measurementReference?: string | null;
  notes?: string | null;
}

/** Hakedişin imalat bölümü icmali (NATURA'da 12 bölüm). */
export interface ProgressPaymentSection {
  id: string;
  projectHakedisSectionId?: string | null;
  order: number;
  name: string;
  code?: string | null;
  materialAmount: number;
  laborAmount: number;
  overheadAmount: number;
  previousAmount: number;
  currentAmount: number;
  cumulativeAmount: number;
}

/** Alt kalemli kesinti satırı (yemek, konaklama, İSG). */
export interface ProgressPaymentDeductionLine {
  id: string;
  lineNumber: number;
  name: string;
  unitPrice: number;
  quantity: number;
  vatRate: number;
  netAmount: number;
  vatAmount: number;
  grossAmount: number;
}

export interface ProgressPaymentDeduction {
  id: string;
  lineNumber: number;
  deductionType: number;
  description: string;
  rate: number;
  baseAmount: number;
  /** Kesintinin uygulandığı kümülatif taban. */
  cumulativeBaseAmount: number;
  /** Önceki hakedişlerde bu türden kesilen toplam. */
  previousAmount: number;
  cumulativeAmount: number;
  amount: number;
  isManualAmount: boolean;
  notes?: string | null;
  lines: ProgressPaymentDeductionLine[];
}

/** Sahaya gelmiş ama monte edilmemiş malzeme. */
export interface ProgressPaymentAdvanceMaterial {
  id: string;
  lineNumber: number;
  positionCode: string;
  description: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  valuationRate: number;
  amount: number;
  offsetAmount: number;
  openAmount: number;
  notes?: string | null;
}

export interface ProgressPaymentAdvanceOffset {
  id: string;
  advanceMaterialId: string;
  positionCode: string;
  advanceDescription: string;
  amount: number;
  notes?: string | null;
}

export const ProgressPaymentPaymentType = {
  Cash: 0,
  Cheque: 1,
} as const;

/** Ödeme dağılımı parçası: nakit veya vadeli çek. */
export interface ProgressPaymentPaymentPlan {
  id: string;
  lineNumber: number;
  paymentType: number;
  rate: number;
  amount: number;
  maturityDays?: number | null;
  dueDate?: string | null;
  chequeId?: string | null;
  chequeNumber?: string | null;
  description?: string | null;
}

export interface ProgressPaymentDetail {
  id: string;
  companyId: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  progressPaymentNumber: string;
  periodNumber: number;
  periodStartDate?: string | null;
  periodEndDate?: string | null;
  progressPaymentDate: string;
  status: ProgressPaymentStatus;
  currencyCode: string;
  contractAmount: number;
  previousAmount: number;
  currentAmount: number;
  cumulativeAmount: number;
  /** Kümülatif imalat (ihzarat hariç). */
  cumulativeWorkAmount: number;
  /** Kümülatif AÇIK ihzarat — mahsup edilenler düşülmüş. */
  cumulativeAdvanceMaterialAmount: number;
  priceDifferenceAmount: number;
  vatRate: number;
  vatAmount: number;
  withholdingNumerator: number;
  withholdingDenominator: number;
  withholdingAmount: number;
  incomeTaxWithholdingRate: number;
  incomeTaxWithholdingAmount: number;
  totalDeductionAmount: number;
  grossPayableAmount: number;
  netPayableAmount: number;
  description?: string | null;
  notes?: string | null;
  submittedAtUtc?: string | null;
  approvedAtUtc?: string | null;
  postedAtUtc?: string | null;
  /** Kesinleştirmede otomatik üretilen gelir fişi. */
  accountingVoucherId?: string | null;
  accountingVoucherNumber?: string | null;
  sections: ProgressPaymentSection[];
  items: ProgressPaymentItem[];
  advanceMaterials: ProgressPaymentAdvanceMaterial[];
  advanceOffsets: ProgressPaymentAdvanceOffset[];
  paymentPlans: ProgressPaymentPaymentPlan[];
  deductions: ProgressPaymentDeduction[];
}

/** NATURA'daki Hak.Takip sayfasının karşılığı. */
export interface HakedisTracking {
  project: {
    id: string;
    code: string;
    name: string;
    contractAmount?: number | null;
    currencyCode: string;
  };
  periods: Array<{
    id: string;
    progressPaymentNumber: string;
    periodNumber: number;
    progressPaymentDate: string;
    status: number;
    cumulativeWorkAmount: number;
    cumulativeAdvanceMaterialAmount: number;
    previousAmount: number;
    currentAmount: number;
    cumulativeAmount: number;
    priceDifferenceAmount: number;
    vatAmount: number;
    withholdingAmount: number;
    incomeTaxWithholdingAmount: number;
    totalDeductionAmount: number;
    grossPayableAmount: number;
    netPayableAmount: number;
    deductions: Array<{
      deductionType: number;
      description: string;
      rate: number;
      amount: number;
      previousAmount: number;
      cumulativeAmount: number;
    }>;
    paymentPlans: Array<{
      paymentType: number;
      amount: number;
      dueDate?: string | null;
    }>;
  }>;
  deductionTypes: Array<{
    deductionType: number;
    name: string;
    totalAmount: number;
  }>;
  totals: {
    cumulativeWorkAmount: number;
    cumulativeTotalAmount: number;
    openAdvanceMaterialAmount: number;
    totalVat: number;
    totalWithholding: number;
    totalIncomeTaxWithholding: number;
    totalDeduction: number;
    totalNetPayable: number;
    completionRate: number;
  };
  barter: {
    totalDeducted: number;
    totalReceived: number;
    openBalance: number;
  };
}

export interface ProgressPaymentCreateResponse {
  id: string;
  progressPaymentNumber: string;
  periodNumber: number;
  status: ProgressPaymentStatus;
  currentAmount: number;
  netPayableAmount: number;
}

export interface ProgressPaymentActionResponse {
  id: string;
  progressPaymentNumber: string;
  status: ProgressPaymentStatus;
  message: string;
}

function buildQuery(filters?: {
  companyId?: string;
  projectId?: string;
  status?: number;
}) {
  const query = new URLSearchParams();

  if (filters?.companyId) {
    query.set("companyId", filters.companyId);
  }

  if (filters?.projectId) {
    query.set("projectId", filters.projectId);
  }

  if (filters?.status !== undefined) {
    query.set("status", String(filters.status));
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

export type HakedisLineProfit = {
  itemId: string;
  positionCode: string;
  description: string;
  unit: string;
  currentQuantity: number;
  unitPrice: number;
  currentAmount: number;
  /** Hesaplanamadıysa boş — sıfır maliyet varsayılmaz. */
  unitCost?: number | null;
  periodCost?: number | null;
  profit?: number | null;
  profitMarginPercent?: number | null;
  measuredRatio: number;
  costBasis: string;
};

export type HakedisProfit = {
  progressPaymentId: string;
  progressPaymentNumber: string;
  periodNumber: number;
  status: number;
  periodStartDate?: string | null;
  periodEndDate?: string | null;
  includesExtraPayments: boolean;

  /** Kâr hesabının tabanı: bu dönem yapılan imalat. */
  productionRevenue: number;
  priceDifferenceAmount: number;
  /** İhzarat hareketi — kâra girmez. */
  advanceMaterialMovement: number;
  hakedisAmount: number;

  /** Dönem tarihleri arasında deftere işlenen maliyet (ölçüm). */
  costByDate?: number | null;
  profitByDate?: number | null;
  marginByDatePercent?: number | null;
  costByDateBasis: string;

  /** Bu dönem imalatına düşen maliyet (dağıtım). */
  costByProduction: number;
  profitByProduction: number;
  marginByProductionPercent?: number | null;
  revenueWithoutCost: number;

  cumulativeRevenue: number;
  cumulativeCost: number;
  cumulativeProfit: number;
  cumulativeMarginPercent?: number | null;

  lines: HakedisLineProfit[];
  assumptions: string[];
};

export const progressPaymentService = {
  getAll(filters?: {
    companyId?: string;
    projectId?: string;
    status?: number;
  }) {
    return apiClient<ProgressPaymentListItem[]>(
      `progress-payments${buildQuery(filters)}`
    );
  },

  getById(id: string) {
    return apiClient<ProgressPaymentDetail>(
      `progress-payments/${id}`
    );
  },

  /** Dönem ve kümülatif kâr marjı. */
  getProfit(id: string) {
    return apiClient<HakedisProfit>(`progress-payments/${id}/kar-marji`);
  },

  /**
   * Hazırlanan hakedişin önceki dönem bağlamı: poz bazında önceki
   * miktarlar, kesinti türü bazında önceden kesilen ve minha toplamı.
   */
  getPreviousContext(
    projectId: string,
    periodNumber: number,
    excludeProgressPaymentId?: string
  ) {
    const query = new URLSearchParams({
      projectId,
      periodNumber: String(periodNumber),
    });

    if (excludeProgressPaymentId) {
      query.set("excludeProgressPaymentId", excludeProgressPaymentId);
    }

    return apiClient<PreviousContext>(
      `progress-payments/previous-context?${query.toString()}`
    );
  },

  /** Projenin imalat bölümleri (hakediş satırları bunlara bağlanır). */
  getProjectSections(projectId: string) {
    return apiClient<ProjectHakedisSection[]>(
      `projects/${projectId}/hakedis-sections`
    );
  },

  /** NATURA'nın 12 bölümü — yeni projede başlangıç listesi. */
  getSectionTemplate() {
    return apiClient<Array<{ order: number; name: string }>>(
      "hakedis-section-template"
    );
  },

  replaceProjectSections(
    projectId: string,
    sections: Array<{
      id?: string | null;
      order: number;
      name: string;
      code?: string | null;
      isActive: boolean;
    }>
  ) {
    return apiClient<{ message: string }>(
      `projects/${projectId}/hakedis-sections`,
      { method: "PUT", body: { sections } }
    );
  },

  /**
   * Projenin açık ihzarat kalemleri — bu hakedişte mahsup
   * edilebilecekler.
   */
  getOpenAdvanceMaterials(projectId: string, excludeProgressPaymentId?: string) {
    const query = new URLSearchParams({ projectId });

    if (excludeProgressPaymentId) {
      query.set("excludeProgressPaymentId", excludeProgressPaymentId);
    }

    return apiClient<OpenAdvanceMaterial[]>(
      `progress-payments/open-advance-materials?${query.toString()}`
    );
  },

  /** Projenin tüm hakedişlerinin kümülatif takip tablosu. */
  getTracking(projectId: string) {
    return apiClient<HakedisTracking>(
      `hakedis-tracking?projectId=${projectId}`
    );
  },

  /** Excel çıktısının indirme adresi. */
  excelUrl(id: string) {
    return `hakedis-export/${id}/excel`;
  },

  create(request: CreateProgressPaymentRequest) {
    return apiClient<ProgressPaymentCreateResponse>(
      "progress-payments",
      {
        method: "POST",
        body: request,
      }
    );
  },

  update(
    id: string,
    request: UpdateProgressPaymentRequest
  ) {
    return apiClient<ProgressPaymentDetail>(
      `progress-payments/${id}`,
      {
        method: "PUT",
        body: request,
      }
    );
  },

  remove(id: string) {
    return apiClient<void>(
      `progress-payments/${id}`,
      {
        method: "DELETE",
      }
    );
  },

  submit(id: string) {
    return apiClient<ProgressPaymentActionResponse>(
      `progress-payments/${id}/submit`,
      {
        method: "POST",
      }
    );
  },

  approve(id: string) {
    return apiClient<ProgressPaymentActionResponse>(
      `progress-payments/${id}/approve`,
      {
        method: "POST",
      }
    );
  },

  post(id: string) {
    return apiClient<ProgressPaymentActionResponse>(
      `progress-payments/${id}/post`,
      {
        method: "POST",
      }
    );
  },

  cancel(id: string, reason?: string | null) {
    return apiClient<ProgressPaymentActionResponse>(
      `progress-payments/${id}/cancel`,
      {
        method: "POST",
        body: {
          reason: reason ?? null,
        },
      }
    );
  },
};
