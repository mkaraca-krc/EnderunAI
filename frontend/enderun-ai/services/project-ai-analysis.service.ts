import { apiClient } from "@/lib/api/api-client";


export interface ProjectAIAnalysisItem {

  level: string;

  title: string;

  message: string;

  module: string;

  suggestion?: string | null;

}


export interface ProjectAIAnalysisResponse {

  generatedAtUtc: string;

  items: ProjectAIAnalysisItem[];

}


export const projectAIAnalysisService = {

  getById(id:string){

    return apiClient<ProjectAIAnalysisResponse>(
      `projects/${id}/ai-analysis`
    );

  }

};
