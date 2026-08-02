import { apiClient } from "@/lib/api/api-client";

export type ProjectListItem = {
  id: string;
  companyId: string;
  companyName: string;
  branchId: string;
  branchName: string;
  employerCurrentAccountId: string;
  employerName: string;
  code: string;
  name: string;
  contractNumber?: string | null;
  contractAmount?: number | null;
  currencyCode: string;
  vatRate: number;
  withholdingRate?: string | null;
  increaseRate: number;
  cashRetentionRate: number;
  withholdingTaxRate: number;
  materialDeductionRate: number;
  status: number;
  healthStatus: number;
  warehouseCount: number;
};

export type CreateProjectRequest = {
  companyId: string;
  branchId: string;
  employerCurrentAccountId: string;
  code: string;
  name: string;
  contractNumber?: string | null;
  contractDate?: string | null;
  contractAmount?: number | null;
  currencyCode: string;
  vatRate: number;
  withholdingRate?: string | null;
  increaseRate: number;
  cashRetentionRate: number;
  withholdingTaxRate: number;
  materialDeductionRate: number;
  plannedStartDate?: string | null;
  plannedEndDate?: string | null;
  city?: string | null;
  district?: string | null;
  address?: string | null;
};

export type UpdateProjectRequest = {
  name: string;
  contractNumber?: string | null;
  contractDate?: string | null;
  contractAmount?: number | null;
  currencyCode: string;
  vatRate: number;
  withholdingRate?: string | null;
  increaseRate: number;
  cashRetentionRate: number;
  withholdingTaxRate: number;
  materialDeductionRate: number;
  plannedStartDate?: string | null;
  plannedEndDate?: string | null;
  city?: string | null;
  district?: string | null;
  address?: string | null;
};

export const projectService = {
  getAll(companyId?: string) {
    const query = companyId
      ? `?companyId=${encodeURIComponent(companyId)}`
      : "";

    return apiClient<ProjectListItem[]>(`projects${query}`);
  },

  getById(id: string) {
    return apiClient(`projects/${id}`);
  },

  create(payload: CreateProjectRequest) {
    return apiClient("projects", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: UpdateProjectRequest) {
    return apiClient(`projects/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
};
