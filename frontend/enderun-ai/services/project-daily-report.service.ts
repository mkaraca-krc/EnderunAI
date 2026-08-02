import { apiClient } from "@/lib/api/api-client";


export interface ProjectDailyReport {

  id: string;

  projectId: string;

  reportDate: string;

  weather?: string | null;

  workerCount: number;

  summary?: string | null;

  completedWorks?: string | null;

  problems?: string | null;

  notes?: string | null;
}


export const projectDailyReportService = {

  getByProject(projectId: string) {

    return apiClient<ProjectDailyReport[]>(
      `project-daily-reports/${projectId}`
    );

  },


  create(data: Partial<ProjectDailyReport>) {

    return apiClient<ProjectDailyReport>(
      "project-daily-reports",
      {
        method: "POST",
        body: JSON.stringify(data),
      }
    );

  }

};
