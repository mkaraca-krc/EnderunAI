import { apiClient } from "@/lib/api/api-client";

export enum ProjectCostType {
  Material = 0,
  Labor = 1,
  Equipment = 2,
  Subcontractor = 3,
  Overhead = 4,
  Other = 5,
}

export const projectCostTypeLabels: Record<ProjectCostType, string> = {
  [ProjectCostType.Material]: "Malzeme",
  [ProjectCostType.Labor]: "İşçilik",
  [ProjectCostType.Equipment]: "Ekipman",
  [ProjectCostType.Subcontractor]: "Taşeron",
  [ProjectCostType.Overhead]: "Genel Gider",
  [ProjectCostType.Other]: "Diğer",
};

export type ProjectCostTransactionItem = {
  id: string;
  projectId: string;
  projectSiteId?: string | null;
  siteCode?: string | null;
  siteName?: string | null;
  costType: ProjectCostType;
  costDate: string;
  amount: number;
  description: string;
};

export type CreateProjectCostTransactionRequest = {
  projectSiteId?: string | null;
  costType: ProjectCostType;
  costDate: string;
  amount: number;
  description: string;
};

export type ProjectCostBreakdownSite = {
  id: string;
  code: string;
  name: string;
  amount: number;
};

export type ProjectCostBreakdown = {
  projectId: string;
  sites: ProjectCostBreakdownSite[];
  sharedCost: number;
  projectTotal: number;
};

export const projectCostService = {
  getAll(projectId: string, siteId?: string) {
    const query = siteId ? `?siteId=${siteId}` : "";
    return apiClient<ProjectCostTransactionItem[]>(
      `projects/${projectId}/cost-transactions${query}`
    );
  },

  create(projectId: string, payload: CreateProjectCostTransactionRequest) {
    return apiClient<{ message: string; id: string }>(
      `projects/${projectId}/cost-transactions`,
      {
        method: "POST",
        body: payload,
      }
    );
  },

  getBreakdown(projectId: string) {
    return apiClient<ProjectCostBreakdown>(
      `projects/${projectId}/cost-breakdown`
    );
  },
};
