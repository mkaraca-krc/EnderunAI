import { apiClient } from "@/lib/api/api-client";


export interface AIAnalysisItem {

  level: string;

  title: string;

  message: string;

  module: string;

  suggestion?: string | null;

}


export interface AIAnalysisDashboardResponse {

  generatedAtUtc: string;

  items: AIAnalysisItem[];

}


export const aiAnalysisService = {

  getDashboard() {

    return apiClient<AIAnalysisDashboardResponse>(
      "ai/dashboard"
    );

  }

};
