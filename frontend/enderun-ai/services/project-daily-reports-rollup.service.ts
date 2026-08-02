import { apiClient } from "@/lib/api/api-client";

export type ProjectDailyReportRollupItem = {
  id: string;
  projectSiteId: string;
  siteName: string;
  reportDate: string;
  weatherCondition?: string | null;
  totalHeadcount: number;
  notes?: string | null;
};

export const projectDailyReportsRollupService = {
  getRecent(projectId: string, take = 10) {
    return apiClient<ProjectDailyReportRollupItem[]>(
      `projects/${projectId}/daily-reports?take=${take}`
    );
  },
};
