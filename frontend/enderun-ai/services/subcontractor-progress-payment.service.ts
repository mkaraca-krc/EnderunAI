import { apiClient } from "@/lib/api/api-client";

export enum SubcontractorProgressPaymentStatus {
  Draft = 0,
  Submitted = 1,
  Approved = 2,
  Paid = 3,
  Cancelled = 4,
}

export interface SubcontractorProgressPaymentListItem {
  id: string;
  subcontractorContractId: string;
  contractNumber: string;
  subcontractorTitle: string;
  projectName: string;
  progressPaymentNumber: string;
  periodNumber: number;
  periodStartDate: string;
  periodEndDate: string;
  progressPaymentDate: string;
  status: number;
  statusName: string;
  currencyCode: string;
  contractAmount: number;
  previousAmount: number;
  currentAmount: number;
  cumulativeAmount: number;
  totalDeductionAmount: number;
  grossPayableAmount: number;
  netPayableAmount: number;
}

/** Öneri ve mutabakat ayrı sütunlar; hesap mutabakatla yapılır. */
export interface SubcontractorProgressPaymentItem {
  id: string;
  projectHakedisSectionId?: string | null;
  sectionName?: string | null;
  projectBoqItemId?: string | null;
  lineNumber: number;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  previousQuantity: number;
  suggestedQuantity: number;
  agreedQuantity: number;
  currentQuantity: number;
  unitPrice: number;
  previousAmount: number;
  currentAmount: number;
  cumulativeAmount: number;
  notes?: string | null;
}

export interface SubcontractorProgressPaymentSection {
  id: string;
  projectHakedisSectionId: string;
  sectionName?: string | null;
  order: number;
  sectionAmount: number;
  previousProgressRate: number;
  suggestedProgressRate: number;
  agreedProgressRate: number;
  previousAmount: number;
  currentAmount: number;
  cumulativeAmount: number;
  notes?: string | null;
}

export interface SubcontractorProgressPaymentDeduction {
  id: string;
  lineNumber: number;
  deductionType: number;
  description: string;
  rate: number;
  cumulativeBaseAmount: number;
  previousAmount: number;
  cumulativeAmount: number;
  amount: number;
  isManualAmount: boolean;
  /** Önerinin nasıl hesaplandığı; üretilemediyse sebebi. */
  suggestionBasis?: string | null;
}

export interface SubcontractorProgressPaymentDetail {
  id: string;
  subcontractorContractId: string;
  progressPaymentNumber: string;
  periodNumber: number;
  periodStartDate: string;
  periodEndDate: string;
  progressPaymentDate: string;
  year: number;
  month: number;
  status: number;
  statusName: string;
  currencyCode: string;
  contractType: number;
  contractAmount: number;
  previousAmount: number;
  currentAmount: number;
  cumulativeAmount: number;
  totalDeductionAmount: number;
  grossPayableAmount: number;
  netPayableAmount: number;
  notes?: string | null;
  items: SubcontractorProgressPaymentItem[];
  sections: SubcontractorProgressPaymentSection[];
  deductions: SubcontractorProgressPaymentDeduction[];
}

export interface SaveSubcontractorProgressPaymentRequest {
  items: Array<{
    id?: string | null;
    projectHakedisSectionId?: string | null;
    projectBoqItemId?: string | null;
    positionCode: string;
    description: string;
    unit: string;
    contractQuantity: number;
    suggestedQuantity: number;
    agreedQuantity: number;
    unitPrice: number;
    notes?: string | null;
  }>;
  sections: Array<{
    projectHakedisSectionId: string;
    sectionAmount: number;
    suggestedProgressRate: number;
    agreedProgressRate: number;
    notes?: string | null;
  }>;
  deductions: Array<{
    deductionType: number;
    description: string;
    rate: number;
    manualAmount?: number | null;
    suggestionBasis?: string | null;
    lines?: Array<{
      name: string;
      unitPrice: number;
      quantity: number;
      vatRate: number;
    }>;
  }>;
  notes?: string | null;
}

export const subcontractorProgressPaymentService = {
  list(filters?: { subcontractorContractId?: string; projectId?: string }) {
    const query = new URLSearchParams();
    if (filters?.subcontractorContractId) {
      query.set("subcontractorContractId", filters.subcontractorContractId);
    }
    if (filters?.projectId) query.set("projectId", filters.projectId);

    const suffix = query.toString() ? `?${query.toString()}` : "";
    return apiClient<SubcontractorProgressPaymentListItem[]>(
      `subcontractor-progress-payments${suffix}`
    );
  },

  getById(id: string) {
    return apiClient<SubcontractorProgressPaymentDetail>(
      `subcontractor-progress-payments/${id}`
    );
  },

  create(payload: {
    subcontractorContractId: string;
    progressPaymentNumber?: string | null;
    periodStartDate: string;
    periodEndDate: string;
    progressPaymentDate: string;
    notes?: string | null;
  }) {
    return apiClient<{
      id: string;
      progressPaymentNumber: string;
      message: string;
    }>("subcontractor-progress-payments", { method: "POST", body: payload });
  },

  update(id: string, payload: SaveSubcontractorProgressPaymentRequest) {
    return apiClient<{
      message: string;
      currentAmount: number;
      totalDeductionAmount: number;
      netPayableAmount: number;
    }>(`subcontractor-progress-payments/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  approve(id: string) {
    return apiClient<{ message: string }>(
      `subcontractor-progress-payments/${id}/approve`,
      { method: "POST" }
    );
  },

  remove(id: string) {
    return apiClient<{ message: string }>(
      `subcontractor-progress-payments/${id}`,
      { method: "DELETE" }
    );
  },
};

/** Taşeron ekibi (SGK bizde). Ücret rakamı dönmez. */
export interface SubcontractorTeamMember {
  id: string;
  employeeNumber: string;
  fullName: string;
  jobTitle?: string | null;
  isActive: boolean;
}

export const subcontractorTeamService = {
  get(contractId: string) {
    return apiClient<{
      socialSecurityWithUs: boolean;
      members: SubcontractorTeamMember[];
    }>(`subcontractor-contracts/${contractId}/team`);
  },

  replace(contractId: string, personnelIds: string[]) {
    return apiClient<{ message: string }>(
      `subcontractor-contracts/${contractId}/team`,
      { method: "PUT", body: { personnelIds } }
    );
  },
};
