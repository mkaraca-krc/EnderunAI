import { apiClient } from "@/lib/api/api-client";

export type HrOvertimeItem = {
  id: string;
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  workDate: string;
  requestedHours: number;
  approvedHours: number;
  isSundayWork: boolean;
  isPublicHolidayWork: boolean;
  isNightWork: boolean;
  reason: string;
  status: number;
  statusName: string;
  approvedByUserId?: string | null;
  approvedAtUtc?: string | null;
  approvalNote?: string | null;
  createdAtUtc: string;
};

export type HrOvertimeFilters = {
  companyId?: string;
  personnelId?: string;
  projectId?: string;
  status?: number;
  startDate?: string;
  endDate?: string;
};

export type CreateHrOvertimeRequest = {
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  workDate: string;
  requestedHours: number;
  isSundayWork: boolean;
  isPublicHolidayWork: boolean;
  isNightWork: boolean;
  reason: string;
};

export type UpdateHrOvertimeRequest = {
  projectId?: string | null;
  workDate: string;
  requestedHours: number;
  approvedHours: number;
  isSundayWork: boolean;
  isPublicHolidayWork: boolean;
  isNightWork: boolean;
  reason: string;
  status: number;
  approvalNote?: string | null;
};

function buildQuery(filters?: HrOvertimeFilters) {
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

export const hrOvertimeService = {
  getAll(filters?: HrOvertimeFilters) {
    return apiClient<HrOvertimeItem[]>(
      `hr/workforce/overtimes${buildQuery(filters)}`
    );
  },

  create(payload: CreateHrOvertimeRequest) {
    return apiClient<HrOvertimeItem>("hr/workforce/overtimes", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: UpdateHrOvertimeRequest) {
    return apiClient<HrOvertimeItem>(
      `hr/workforce/overtimes/${id}`,
      {
        method: "PUT",
        body: payload,
      }
    );
  },

  approve(id: string) {
    return apiClient<HrOvertimeItem>(
      `hr/workforce/overtimes/${id}/approve`,
      {
        method: "POST",
      }
    );
  },

  reject(id: string, reason: string) {
    return apiClient<HrOvertimeItem>(
      `hr/workforce/overtimes/${id}/reject`,
      {
        method: "POST",
        body: JSON.stringify({
          reason: reason.trim(),
        }),
      }
    );
  },

  delete(id: string) {
    return apiClient<{ message: string }>(
      `hr/workforce/overtimes/${id}`,
      {
        method: "DELETE",
      }
    );
  },
};
