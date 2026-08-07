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
  /** Resmî işçilik (geriye uyum için korunan alan). */
  amount: number;
  officialAmount: number;
  /**
   * Elden ödemenin bu şantiyeye düşen payı; yetki yoksa null.
   *
   * Pay puantaj gününe ORANLA hesaplanır ve işçilik defterine
   * YAZILMAZ — defter personnel.view ile okunuyor, elden tutar oradan
   * sızardı. Okuma anında, yetki doğrulanarak ekleniyor.
   */
  extraPaymentAmount?: number | null;
  /** Resmî + elden; yetki yoksa null. */
  actualAmount?: number | null;
};

export type ProjectLaborCostBreakdown = {
  projectId: string;
  sites: ProjectLaborCostBreakdownSite[];
  /** Şantiyesi girilmemiş puantaj günlerinin resmî maliyeti. */
  sharedCost: number;
  sharedOfficialCost: number;
  sharedExtraPaymentCost?: number | null;
  projectTotal: number;
  projectOfficialTotal: number;
  projectExtraPaymentTotal?: number | null;
  projectActualTotal?: number | null;
  /** Yetki yoksa true; elden alanları null gelir. */
  extraPaymentHidden?: boolean;
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
