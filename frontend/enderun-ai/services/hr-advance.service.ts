import { apiClient } from "@/lib/api/api-client";

export type HrAdvanceItem = {
  id: string;
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  requestDate: string;
  requestedAmount: number;
  approvedAmount: number;
  currencyCode: string;
  deductionInstallmentCount: number;
  firstDeductionDate?: string | null;
  reason: string;
  status: number;
  statusName: string;
  approvedByUserId?: string | null;
  approvedAtUtc?: string | null;
  paidAtUtc?: string | null;
  paymentReference?: string | null;
  createdAtUtc: string;
};

export type HrAdvanceFilters = {
  companyId?: string;
  personnelId?: string;
  projectId?: string;
  status?: number;
  startDate?: string;
  endDate?: string;
};

export type CreateHrAdvanceRequest = {
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  requestDate: string;
  requestedAmount: number;
  currencyCode: string;
  deductionInstallmentCount: number;
  firstDeductionDate?: string | null;
  reason: string;
};

export type UpdateHrAdvanceRequest = {
  projectId?: string | null;
  requestDate: string;
  requestedAmount: number;
  approvedAmount: number;
  currencyCode: string;
  deductionInstallmentCount: number;
  firstDeductionDate?: string | null;
  reason: string;
  status: number;
  paymentReference?: string | null;
};

function buildQuery(filters?: HrAdvanceFilters) {
  const query = new URLSearchParams();
  if (filters?.companyId) query.set("companyId", filters.companyId);
  if (filters?.personnelId) query.set("personnelId", filters.personnelId);
  if (filters?.projectId) query.set("projectId", filters.projectId);
  if (filters?.status !== undefined) {
    query.set("status", String(filters.status));
  }
  if (filters?.startDate) query.set("startDate", filters.startDate);
  if (filters?.endDate) query.set("endDate", filters.endDate);
  const value = query.toString();
  return value ? `?${value}` : "";
}

export const hrAdvanceService = {
  getAll(filters?: HrAdvanceFilters) {
    return apiClient<HrAdvanceItem[]>(
      `hr/workforce/advances${buildQuery(filters)}`
    );
  },

  create(payload: CreateHrAdvanceRequest) {
    return apiClient<HrAdvanceItem>("hr/workforce/advances", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: UpdateHrAdvanceRequest) {
    return apiClient<HrAdvanceItem>(`hr/workforce/advances/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  approve(id: string) {
    return apiClient<HrAdvanceItem>(
      `hr/workforce/advances/${id}/approve`,
      { method: "POST" }
    );
  },

  reject(id: string, reason: string) {
    return apiClient<HrAdvanceItem>(
      `hr/workforce/advances/${id}/reject`,
      {
        method: "POST",
        body: { reason: reason.trim() },
      }
    );
  },

  markPaid(id: string, paymentReference?: string | null) {
    return apiClient<HrAdvanceItem>(
      `hr/workforce/advances/${id}/paid`,
      {
        method: "POST",
        body: { paymentReference: paymentReference || null },
      }
    );
  },

  delete(id: string) {
    return apiClient<{ message: string }>(`hr/workforce/advances/${id}`, {
      method: "DELETE",
    });
  },
};
