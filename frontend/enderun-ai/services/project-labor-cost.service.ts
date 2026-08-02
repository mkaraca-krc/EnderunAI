import { apiClient } from "@/lib/api/api-client";

export type ProjectLaborCostItem = {
  id: string;
  projectId: string;
  personnelId: string;
  personnelName: string;
  projectSiteId?: string | null;
  siteCode?: string | null;
  siteName?: string | null;
  workDate: string;
  normalHours: number;
  overtimeHours: number;
  normalCost: number;
  overtimeCost: number;
  otherCost: number;
  totalLaborCost: number;
  currencyCode: string;
};

export type CreateProjectLaborCostRequest = {
  personnelId: string;
  projectSiteId?: string | null;
  workDate: string;
  normalHours: number;
  overtimeHours: number;
  normalCost: number;
  overtimeCost: number;
  otherCost: number;
  currencyCode?: string;
};

export type ProjectLaborCostBreakdownSite = {
  id: string;
  code: string;
  name: string;
  amount: number;
};

export type ProjectLaborCostBreakdown = {
  projectId: string;
  sites: ProjectLaborCostBreakdownSite[];
  sharedCost: number;
  projectTotal: number;
};

export const projectLaborCostService = {
  getAll(projectId: string, siteId?: string) {
    const query = siteId ? `?siteId=${siteId}` : "";
    return apiClient<ProjectLaborCostItem[]>(
      `projects/${projectId}/labor-costs${query}`
    );
  },

  create(projectId: string, payload: CreateProjectLaborCostRequest) {
    return apiClient<{ message: string; id: string; totalLaborCost: number }>(
      `projects/${projectId}/labor-costs`,
      {
        method: "POST",
        body: payload,
      }
    );
  },

  getBreakdown(projectId: string) {
    return apiClient<ProjectLaborCostBreakdown>(
      `projects/${projectId}/labor-cost-breakdown`
    );
  },
};
