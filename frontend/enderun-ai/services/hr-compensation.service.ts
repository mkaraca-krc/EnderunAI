import { apiClient } from "@/lib/api/api-client";

export type CompensationComponentType =
  | 0
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6
  | 7
  | 8;

export type CompensationCalculationType =
  | 0
  | 1
  | 2
  | 3
  | 4;

export type CompensationPaymentMethod =
  | 0
  | 1
  | 2
  | 3;

export type CompensationComponent = {
  id: string;
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  code: string;
  name: string;
  componentType: CompensationComponentType;
  componentTypeName: string;
  calculationType: CompensationCalculationType;
  calculationTypeName: string;
  paymentMethod: CompensationPaymentMethod;
  paymentMethodName: string;
  amount: number;
  currencyCode: string;
  effectiveStartDate: string;
  effectiveEndDate?: string | null;
  isAttendanceBased: boolean;
  isInKindBenefit: boolean;
  includeInPayroll: boolean;
  includeInSgkBase: boolean;
  includeInIncomeTaxBase: boolean;
  includeInStampTaxBase: boolean;
  includeInProjectCost: boolean;
  includeInProgressPaymentCost: boolean;
  isActive: boolean;
  description?: string | null;
  createdAtUtc: string;
};

export type CompensationFilters = {
  companyId?: string;
  personnelId?: string;
  projectId?: string;
  isActive?: boolean;
  effectiveDate?: string;
};

export type CreateCompensationComponentRequest = {
  companyId: string;
  personnelId: string;
  projectId?: string | null;
  code: string;
  name: string;
  componentType: number;
  calculationType: number;
  paymentMethod: number;
  amount: number;
  currencyCode: string;
  effectiveStartDate: string;
  effectiveEndDate?: string | null;
  isAttendanceBased: boolean;
  isInKindBenefit: boolean;
  includeInPayroll: boolean;
  includeInSgkBase: boolean;
  includeInIncomeTaxBase: boolean;
  includeInStampTaxBase: boolean;
  includeInProjectCost: boolean;
  includeInProgressPaymentCost: boolean;
  isActive: boolean;
  description?: string | null;
};

export type UpdateCompensationComponentRequest = Omit<
  CreateCompensationComponentRequest,
  "companyId" | "personnelId"
>;

export type CompensationSummary = {
  personnelId: string;
  effectiveDate: string;
  componentCount: number;
  monthlyFixedAmount: number;
  dailyAmount: number;
  hourlyAmount: number;
  payrollIncludedAmount: number;
  projectCostIncludedAmount: number;
  currencyCode: string;
};

function buildQuery(
  values?: Record<
    string,
    string | number | boolean | undefined
  >
) {
  const query = new URLSearchParams();

  Object.entries(values ?? {}).forEach(([key, value]) => {
    if (value !== undefined && value !== "") {
      query.set(key, String(value));
    }
  });

  const result = query.toString();

  return result ? `?${result}` : "";
}

export const hrCompensationService = {
  getAll(filters?: CompensationFilters) {
    return apiClient<CompensationComponent[]>(
      `hr/compensation-components${buildQuery(filters)}`
    );
  },

  getById(id: string) {
    return apiClient<CompensationComponent>(
      `hr/compensation-components/${id}`
    );
  },

  create(payload: CreateCompensationComponentRequest) {
    return apiClient<CompensationComponent>(
      "hr/compensation-components",
      {
        method: "POST",
        body: payload,
      }
    );
  },

  update(
    id: string,
    payload: UpdateCompensationComponentRequest
  ) {
    return apiClient<CompensationComponent>(
      `hr/compensation-components/${id}`,
      {
        method: "PUT",
        body: payload,
      }
    );
  },

  delete(id: string) {
    return apiClient<{ message: string }>(
      `hr/compensation-components/${id}`,
      {
        method: "DELETE",
      }
    );
  },

  getSummary(
    personnelId: string,
    effectiveDate: string
  ) {
    return apiClient<CompensationSummary>(
      `hr/compensation-components/summary${buildQuery({
        personnelId,
        effectiveDate,
      })}`
    );
  },
};
