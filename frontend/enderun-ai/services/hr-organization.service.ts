import { apiClient } from "@/lib/api/api-client";

export type HrDepartment = {
  id: string;
  companyId: string;
  companyName?: string | null;
  code: string;
  name: string;
  parentDepartmentId?: string | null;
  parentDepartmentName?: string | null;
  managerPersonnelId?: string | null;
  managerPersonnelName?: string | null;
  managerName?: string | null;
  isActive: boolean;
  positionCount?: number;
  personnelCount?: number;
  createdAt?: string;
  createdAtUtc?: string;
};

export type HrPosition = {
  id: string;
  companyId?: string;
  companyName?: string | null;
  departmentId: string;
  departmentName?: string | null;
  code: string;
  title?: string;
  name?: string;
  description?: string | null;
  isManagerial?: boolean;
  isActive: boolean;
  personnelCount?: number;
  createdAt?: string;
  createdAtUtc?: string;
};

export type OrganizationPayload = Record<string, unknown>;

const root = "hr";

function queryString(params?: Record<string, string | undefined>) {
  if (!params) {
    return "";
  }

  const query = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value) {
      query.set(key, value);
    }
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const hrOrganizationService = {
  getDepartments(params?: { companyId?: string }) {
    return apiClient<HrDepartment[]>(
      `${root}/departments${queryString(params)}`
    );
  },
  getDepartment(id: string) {
    return apiClient<HrDepartment>(`${root}/departments/${id}`);
  },
  createDepartment(payload: OrganizationPayload) {
    return apiClient<HrDepartment>(`${root}/departments`, {
      method: "POST",
      body: payload,
    });
  },
  updateDepartment(id: string, payload: OrganizationPayload) {
    return apiClient<HrDepartment>(`${root}/departments/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
  deleteDepartment(id: string) {
    return apiClient<void>(`${root}/departments/${id}`, {
      method: "DELETE",
    });
  },

  getPositions(params?: { companyId?: string; departmentId?: string }) {
    return apiClient<HrPosition[]>(
      `${root}/positions${queryString(params)}`
    );
  },
  getPosition(id: string) {
    return apiClient<HrPosition>(`${root}/positions/${id}`);
  },
  createPosition(payload: OrganizationPayload) {
    return apiClient<HrPosition>(`${root}/positions`, {
      method: "POST",
      body: payload,
    });
  },
  updatePosition(id: string, payload: OrganizationPayload) {
    return apiClient<HrPosition>(`${root}/positions/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
  deletePosition(id: string) {
    return apiClient<void>(`${root}/positions/${id}`, {
      method: "DELETE",
    });
  },
};
