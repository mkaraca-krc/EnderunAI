import { apiClient } from "@/lib/api/api-client";

export const ProjectStatus = {
  Kesif: 0,
  Active: 2,
  Completed: 4,
  Cancelled: 5,
} as const;

export const PROJECT_STATUS_LABELS: Record<number, string> = {
  [ProjectStatus.Kesif]: "Keşif/Teklif",
  [ProjectStatus.Active]: "Aktif",
  [ProjectStatus.Completed]: "Tamamlandı",
  [ProjectStatus.Cancelled]: "İptal",
};

/** erp-status.{renk} sınıfıyla eşleşir — mevcut renkler: green, gray, blue, yellow. */
export const PROJECT_STATUS_BADGE_COLOR: Record<number, string> = {
  [ProjectStatus.Kesif]: "yellow",
  [ProjectStatus.Active]: "blue",
  [ProjectStatus.Completed]: "green",
  [ProjectStatus.Cancelled]: "gray",
};

export type ProjectListItem = {
  id: string;
  companyId: string;
  companyName: string;
  branchId: string;
  branchName: string;
  employerCurrentAccountId?: string | null;
  employerName?: string | null;
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
  employerCurrentAccountId?: string | null;
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
  status: number;
};

export type UpdateProjectRequest = {
  name: string;
  employerCurrentAccountId?: string | null;
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
  status: number;
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
