import { apiClient } from "@/lib/api/api-client";

export type HrShiftItem = {
  id: string;
  companyId: string;
  code: string;
  name: string;
  startTime: string;
  endTime: string;
  breakHours: number;
  dailyWorkingHours: number;
  isNightShift: boolean;
  description?: string | null;
  createdAtUtc: string;
};

export type HrShiftAssignmentItem = {
  id: string;
  companyId: string;
  personnelId: string;
  shiftDefinitionId: string;
  projectId?: string | null;
  startDate: string;
  endDate?: string | null;
  teamName?: string | null;
  description?: string | null;
  createdAtUtc: string;
};

export type CreateShiftRequest = {
  companyId: string;
  code: string;
  name: string;
  startTime: string;
  endTime: string;
  breakHours: number;
  dailyWorkingHours: number;
  isNightShift: boolean;
  description?: string | null;
};

export type UpdateShiftRequest = {
  code: string;
  name: string;
  startTime: string;
  endTime: string;
  breakHours: number;
  dailyWorkingHours: number;
  isNightShift: boolean;
  description?: string | null;
};

export type CreateShiftAssignmentRequest = {
  companyId: string;
  personnelId: string;
  shiftDefinitionId: string;
  projectId?: string | null;
  startDate: string;
  endDate?: string | null;
  teamName?: string | null;
  description?: string | null;
};

function buildQuery(params?: Record<string, string | undefined>) {
  const query = new URLSearchParams();

  Object.entries(params ?? {}).forEach(([key, value]) => {
    if (value) {
      query.set(key, value);
    }
  });

  const result = query.toString();
  return result ? `?${result}` : "";
}

export const hrShiftService = {
  getShifts(params?: {
    companyId?: string;
    search?: string;
  }) {
    return apiClient<HrShiftItem[]>(
      `hr/workforce/shifts${buildQuery(params)}`
    );
  },

  createShift(payload: CreateShiftRequest) {
    return apiClient<HrShiftItem>("hr/workforce/shifts", {
      method: "POST",
      body: payload,
    });
  },

  updateShift(id: string, payload: UpdateShiftRequest) {
    return apiClient<HrShiftItem>(
      `hr/workforce/shifts/${id}`,
      {
        method: "PUT",
        body: payload,
      }
    );
  },

  deleteShift(id: string) {
    return apiClient<{ message: string }>(
      `hr/workforce/shifts/${id}`,
      {
        method: "DELETE",
      }
    );
  },

  getAssignments(params?: {
    companyId?: string;
    personnelId?: string;
    projectId?: string;
    startDate?: string;
    endDate?: string;
  }) {
    return apiClient<HrShiftAssignmentItem[]>(
      `hr/workforce/shift-assignments${buildQuery(params)}`
    );
  },

  createAssignment(payload: CreateShiftAssignmentRequest) {
    return apiClient<HrShiftAssignmentItem>(
      "hr/workforce/shift-assignments",
      {
        method: "POST",
        body: payload,
      }
    );
  },

  deleteAssignment(id: string) {
    return apiClient<{ message: string }>(
      `hr/workforce/shift-assignments/${id}`,
      {
        method: "DELETE",
      }
    );
  },
};
