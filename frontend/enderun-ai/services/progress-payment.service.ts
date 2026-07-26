import { apiClient } from "@/lib/api/api-client";

export enum ProgressPaymentStatus {
  Draft = 0,
  PendingApproval = 1,
  Approved = 2,
  Posted = 3,
  Cancelled = 4,
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
}

export interface ProgressPaymentDeductionRequest {
  deductionType: number;
  description: string;
  rate: number;
  baseAmount: number;
  manualAmount?: number | null;
  notes?: string | null;
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
  lineNumber: number;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  previousQuantity: number;
  currentQuantity: number;
  cumulativeQuantity: number;
  unitPrice: number;
  previousAmount: number;
  currentAmount: number;
  cumulativeAmount: number;
  completionRate: number;
  measurementReference?: string | null;
  notes?: string | null;
}

export interface ProgressPaymentDeduction {
  id: string;
  lineNumber: number;
  deductionType: number;
  description: string;
  rate: number;
  baseAmount: number;
  amount: number;
  isManualAmount: boolean;
  notes?: string | null;
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
  priceDifferenceAmount: number;
  vatRate: number;
  vatAmount: number;
  withholdingNumerator: number;
  withholdingDenominator: number;
  withholdingAmount: number;
  totalDeductionAmount: number;
  grossPayableAmount: number;
  netPayableAmount: number;
  description?: string | null;
  notes?: string | null;
  submittedAtUtc?: string | null;
  approvedAtUtc?: string | null;
  postedAtUtc?: string | null;
  items: ProgressPaymentItem[];
  deductions: ProgressPaymentDeduction[];
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
