import { apiClient } from "@/lib/api/api-client";

export type PersonnelAssignmentItem = {
  id: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  role?: string | null;
  startDate: string;
  endDate?: string | null;
  isPrimaryAssignment: boolean;
  isActive?: boolean;
};

export type PersonnelListItem = {
  id: string;
  companyId: string;
  companyName: string;
  branchId?: string | null;
  branchName?: string | null;
  employeeNumber: string;
  firstName: string;
  lastName: string;
  fullName: string;
  identityNumber?: string | null;
  phone?: string | null;
  email?: string | null;
  jobTitle?: string | null;
  profession?: string | null;
  employmentStartDate?: string | null;
  employmentEndDate?: string | null;
  monthlySalary?: number | null;
  status: number;
  isActive: boolean;
  activeAssignments: PersonnelAssignmentItem[];
};

export type PersonnelDetail = PersonnelListItem & {
  birthDate?: string | null;
  address?: string | null;
  sgkRegistrationNumber?: string | null;
  assignments: PersonnelAssignmentItem[];
};

export type CreatePersonnelRequest = {
  companyId: string;
  branchId?: string | null;
  employeeNumber: string;
  firstName: string;
  lastName: string;
  identityNumber?: string | null;
  birthDate?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  jobTitle?: string | null;
  profession?: string | null;
  sgkRegistrationNumber?: string | null;
  employmentStartDate?: string | null;
  monthlySalary?: number | null;
};

export type UpdatePersonnelRequest = {
  branchId?: string | null;
  firstName: string;
  lastName: string;
  identityNumber?: string | null;
  birthDate?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  jobTitle?: string | null;
  profession?: string | null;
  sgkRegistrationNumber?: string | null;
  employmentStartDate?: string | null;
  employmentEndDate?: string | null;
  monthlySalary?: number | null;
  status: number;
  isActive: boolean;
};

export type AssignPersonnelRequest = {
  projectId: string;
  startDate: string;
  endDate?: string | null;
  role?: string | null;
  notes?: string | null;
  isPrimaryAssignment: boolean;
};

export const personnelService = {
  getAll(params?: {
    companyId?: string;
    projectId?: string;
    search?: string;
  }) {
    const query = new URLSearchParams();

    if (params?.companyId) {
      query.set("companyId", params.companyId);
    }

    if (params?.projectId) {
      query.set("projectId", params.projectId);
    }

    if (params?.search) {
      query.set("search", params.search);
    }

    const suffix = query.toString() ? `?${query.toString()}` : "";

    return apiClient<PersonnelListItem[]>(`personnel${suffix}`);
  },

  getById(id: string) {
    return apiClient<PersonnelDetail>(`personnel/${id}`);
  },

  create(payload: CreatePersonnelRequest) {
    return apiClient<{
      message: string;
      id: string;
      employeeNumber: string;
      firstName: string;
      lastName: string;
    }>("personnel", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: UpdatePersonnelRequest) {
    return apiClient<{ message: string }>(`personnel/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  assignToProject(id: string, payload: AssignPersonnelRequest) {
    return apiClient<{ message: string; id: string }>(
      `personnel/${id}/assignments`,
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
      `personnel/assignments/${assignmentId}/close${query}`,
      {
        method: "PUT",
      }
    );
  },
};
