import { apiClient } from "@/lib/api/api-client";


export interface ProjectSiteAnalysisItem {

  level: string;

  title: string;

  message: string;

  module: string;

}


export interface ProjectSiteAnalysisResponse {

  generatedAtUtc: string;

  items: ProjectSiteAnalysisItem[];

}


export const projectSiteAnalysisService = {

  getById(id:string){

    return apiClient<ProjectSiteAnalysisResponse>(
      `projects/${id}/site-analysis`
    );

  }

};
