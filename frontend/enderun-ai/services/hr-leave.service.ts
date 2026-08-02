import { apiClient } from "@/lib/api/api-client";

export type HrLeaveListItem = {
  id: string;
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  leaveType: number;
  leaveTypeName: string;
  startDate: string;
  endDate: string;
  totalDays: number;
  reason: string;
  documentPath?: string | null;
  status: number;
  statusName: string;
  approvedByUserId?: string | null;
  approvedAtUtc?: string | null;
  approvalNote?: string | null;
  createdAtUtc: string;
};

export type HrLeaveFilters = {
  companyId?: string;
  personnelId?: string;
  projectId?: string;
  leaveType?: number;
  status?: number;
  startDate?: string;
  endDate?: string;
};

export type CreateHrLeaveRequest = {
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  leaveType: number;
  startDate: string;
  endDate: string;
  totalDays: number;
  reason: string;
  documentPath?: string | null;
};

export type UpdateHrLeaveRequest = {
  projectId?: string | null;
  leaveType: number;
  startDate: string;
  endDate: string;
  totalDays: number;
  reason: string;
  documentPath?: string | null;
  status: number;
  approvalNote?: string | null;
};

function buildQuery(filters?: HrLeaveFilters) {
  const query = new URLSearchParams();
  if (filters?.companyId) query.set("companyId", filters.companyId);
  if (filters?.personnelId) query.set("personnelId", filters.personnelId);
  if (filters?.projectId) query.set("projectId", filters.projectId);
  if (filters?.leaveType !== undefined) {
    query.set("leaveType", String(filters.leaveType));
  }
  if (filters?.status !== undefined) {
    query.set("status", String(filters.status));
  }
  if (filters?.startDate) query.set("startDate", filters.startDate);
  if (filters?.endDate) query.set("endDate", filters.endDate);
  const value = query.toString();
  return value ? `?${value}` : "";
}

export const hrLeaveService = {
  getAll(filters?: HrLeaveFilters) {
    return apiClient<HrLeaveListItem[]>(
      `hr/workforce/leaves${buildQuery(filters)}`
    );
  },

  create(payload: CreateHrLeaveRequest) {
    return apiClient<HrLeaveListItem>("hr/workforce/leaves", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: UpdateHrLeaveRequest) {
    return apiClient<HrLeaveListItem>(`hr/workforce/leaves/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  approve(id: string) {
    return apiClient<HrLeaveListItem>(
      `hr/workforce/leaves/${id}/approve`,
      { method: "POST" }
    );
  },

  reject(id: string, reason: string) {
    return apiClient<HrLeaveListItem>(
      `hr/workforce/leaves/${id}/reject`,
      {
        method: "POST",
        body: { reason: reason.trim() },
      }
    );
  },

  delete(id: string) {
    return apiClient<{ message: string }>(`hr/workforce/leaves/${id}`, {
      method: "DELETE",
    });
  },
};
