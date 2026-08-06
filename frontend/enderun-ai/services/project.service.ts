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
  isArchived: boolean;
  archivedAtUtc?: string | null;
  archiveReason?: string | null;
  warehouseCount: number;
};

/** Kalıcı silmeyi engelleyen kesinleşmiş kayıt kalemi. */
export type ProjectDeletionBlocker = {
  key: string;
  label: string;
  count: number;
  reason: string;
};

export type ProjectDeletionDependency = {
  key: string;
  label: string;
  count: number;
};

export type ProjectDeletionImpact = {
  projectId: string;
  projectCode: string;
  projectName: string;
  isArchived: boolean;
  canHardDelete: boolean;
  blockers: ProjectDeletionBlocker[];
  dependencies: ProjectDeletionDependency[];
  totalBlockingRecords: number;
  totalDependentRecords: number;
  documentCount: number;
  documentSizeBytes: number;
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
  getAll(companyId?: string, includeArchived = false) {
    const query = new URLSearchParams();
    if (companyId) query.set("companyId", companyId);
    if (includeArchived) query.set("includeArchived", "true");

    const suffix = query.toString() ? `?${query.toString()}` : "";

    return apiClient<ProjectListItem[]>(`projects${suffix}`);
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

  getDeletionImpact(id: string) {
    return apiClient<ProjectDeletionImpact>(`projects/${id}/deletion-impact`);
  },

  archive(id: string, reason?: string | null) {
    return apiClient<{ message: string }>(`projects/${id}/archive`, {
      method: "POST",
      body: { reason: reason ?? null },
    });
  },

  unarchive(id: string) {
    return apiClient<{ message: string }>(`projects/${id}/unarchive`, {
      method: "POST",
    });
  },

  /** Kalıcı silme — onay olarak proje kodu birebir yazılmalı. */
  remove(id: string, confirmationCode: string) {
    return apiClient<{ message: string }>(`projects/${id}`, {
      method: "DELETE",
      body: { confirmationCode },
    });
  },
};
