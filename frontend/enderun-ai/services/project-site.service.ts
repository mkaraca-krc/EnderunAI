import { apiClient } from "@/lib/api/api-client";

export type ProjectSiteListItem = {
  id: string;
  projectId: string;
  code: string;
  name: string;
  location?: string | null;
  notes?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  assignmentCount: number;
  warehouseCount: number;
};

export type ProjectSiteAssignmentItem = {
  id: string;
  personnelId: string;
  employeeNumber: string;
  fullName: string;
  jobTitle?: string | null;
  role?: string | null;
  notes?: string | null;
  startDate: string;
  endDate?: string | null;
  isActive: boolean;
};

export type ProjectSiteWarehouseItem = {
  id: string;
  code: string;
  name: string;
};

export type ProjectSiteDetail = {
  id: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  code: string;
  name: string;
  location?: string | null;
  notes?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  assignments: ProjectSiteAssignmentItem[];
  warehouses: ProjectSiteWarehouseItem[];
};

export type AssignablePersonnelItem = {
  id: string;
  employeeNumber: string;
  fullName: string;
  jobTitle?: string | null;
  profession?: string | null;
};

export type CreateProjectSiteRequest = {
  code: string;
  name: string;
  location?: string | null;
  notes?: string | null;
};

export type UpdateProjectSiteRequest = {
  code: string;
  name: string;
  location?: string | null;
  notes?: string | null;
  isActive: boolean;
};

export type AssignPersonnelToSiteRequest = {
  personnelId: string;
  startDate: string;
  endDate?: string | null;
  role?: string | null;
  notes?: string | null;
};

export const projectSiteService = {
  getAll(projectId: string) {
    return apiClient<ProjectSiteListItem[]>(
      `projects/${projectId}/sites`
    );
  },

  create(projectId: string, payload: CreateProjectSiteRequest) {
    return apiClient<{ message: string; id: string; code: string; name: string }>(
      `projects/${projectId}/sites`,
      {
        method: "POST",
        body: payload,
      }
    );
  },

  getById(id: string) {
    return apiClient<ProjectSiteDetail>(`project-sites/${id}`);
  },

  update(id: string, payload: UpdateProjectSiteRequest) {
    return apiClient<{ message: string }>(`project-sites/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  remove(id: string) {
    return apiClient<void>(`project-sites/${id}`, {
      method: "DELETE",
    });
  },

  getAssignablePersonnel(id: string) {
    return apiClient<AssignablePersonnelItem[]>(
      `project-sites/${id}/assignable-personnel`
    );
  },

  assignPersonnel(id: string, payload: AssignPersonnelToSiteRequest) {
    return apiClient<{ message: string; id: string }>(
      `project-sites/${id}/assignments`,
      {
        method: "POST",
        body: payload,
      }
    );
  },

  closeAssignment(assignmentId: string, endDate?: string) {
    const query = endDate
      ? `?endDate=${encodeURIComponent(endDate)}`
      : "";

    return apiClient<{ message: string }>(
      `project-sites/assignments/${assignmentId}/close${query}`,
      {
        method: "PUT",
      }
    );
  },
};
