import { apiClient } from "@/lib/api/api-client";


export interface ProjectProfitability {

  projectId:string;

  projectName:string;

  revenue:number;

  materialCost:number;

  laborCost:number;

  subcontractorCost:number;

  generalExpenseCost:number;

  otherCost:number;

  totalCost:number;

  profit:number;

  profitMargin:number;

}


export const projectProfitabilityService = {

  getSummary(){

    return apiClient<ProjectProfitability[]>(
      "projects/profitability-summary"
    );

  },


  getById(id:string){

    return apiClient<ProjectProfitability>(
      `projects/${id}/profitability`
    );

  }

};
