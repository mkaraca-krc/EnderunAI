import { apiClient } from "@/lib/api/api-client";

export enum DeductionType {
  WithholdingTax = 0,
  StampTax = 1,
  Accommodation = 2,
  Meal = 3,
  AdvanceDeduction = 4,
  GuaranteeRetention = 5,
  SocialSecurityAndTaxDebt = 6,
  DelayPenalty = 7,
  MaterialDeduction = 8,
  LaborDeduction = 9,
  Other = 99,
}

export enum DeductionCalculationBase {
  CurrentAmount = 0,
  CurrentAmountWithPriceDifference = 1,
  GrossPayable = 2,
  Manual = 3,
}

export interface DeductionRule {
  id: string;
  companyId: string;
  projectId?: string | null;
  deductionType: number;
  description: string;
  rate: number;
  calculationBase: number;
  isAutomatic: boolean;
  notes?: string | null;
}

export interface CreateDeductionRuleRequest {
  companyId: string;
  projectId?: string | null;
  deductionType: number;
  description: string;
  rate: number;
  calculationBase: number;
  isAutomatic: boolean;
  notes?: string | null;
}

export const deductionRuleService = {

  getAll(params?: {
    companyId?: string;
    projectId?: string;
  }) {

    const query =
      new URLSearchParams();

    if (params?.companyId) {
      query.set(
        "companyId",
        params.companyId
      );
    }

    if (params?.projectId) {
      query.set(
        "projectId",
        params.projectId
      );
    }

    const suffix =
      query.toString()
        ? `?${query.toString()}`
        : "";

    return apiClient<DeductionRule[]>(
      `progress-payment-deduction-rules${suffix}`
    );
  },


  create(
    payload: CreateDeductionRuleRequest
  ) {

    return apiClient<DeductionRule>(
      "progress-payment-deduction-rules",
      {
        method: "POST",
        body: payload,
      }
    );

  },


  update(
    id:string,
    payload: Partial<CreateDeductionRuleRequest>
  ){

    return apiClient<DeductionRule>(
      `progress-payment-deduction-rules/${id}`,
      {
        method:"PUT",
        body:payload,
      }
    );

  }

};
