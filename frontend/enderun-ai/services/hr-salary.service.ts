import { apiClient } from "@/lib/api/api-client";

export type SalaryDefinition = {
  id: string;
  companyId: string;
  personnelId: string;
  effectiveStartDate: string;
  effectiveEndDate?: string | null;
  grossSalary: number;
  netSalary: number;
  dailyRate: number;
  hourlyRate: number;
  overtimeMultiplier: number;
  sundayMultiplier: number;
  publicHolidayMultiplier: number;
  currencyCode: string;
  description?: string | null;
  createdAtUtc: string;
};

export type CreateSalaryDefinitionRequest = {
  companyId: string;
  personnelId: string;
  effectiveStartDate: string;
  effectiveEndDate?: string | null;
  grossSalary: number;
  netSalary: number;
  dailyRate: number;
  hourlyRate: number;
  overtimeMultiplier: number;
  sundayMultiplier: number;
  publicHolidayMultiplier: number;
  currencyCode: string;
  description?: string | null;
};

export type UpdateSalaryDefinitionRequest =
  Omit<
    CreateSalaryDefinitionRequest,
    "companyId" | "personnelId"
  >;

export type SalaryFilters = {
  companyId?: string;
  personnelId?: string;
  effectiveDate?: string;
};

function buildQuery(
  filters: SalaryFilters
) {
  const query = new URLSearchParams();

  if (filters.companyId) {
    query.set(
      "companyId",
      filters.companyId
    );
  }

  if (filters.personnelId) {
    query.set(
      "personnelId",
      filters.personnelId
    );
  }

  if (filters.effectiveDate) {
    query.set(
      "effectiveDate",
      filters.effectiveDate
    );
  }

  const value = query.toString();

  return value ? `?${value}` : "";
}

export const hrSalaryService = {
  getAll(
    filters: SalaryFilters = {}
  ) {
    return apiClient<
      SalaryDefinition[]
    >(
      `hr/payroll/salary-definitions${buildQuery(
        filters
      )}`
    );
  },

  create(
    payload: CreateSalaryDefinitionRequest
  ) {
    return apiClient<SalaryDefinition>(
      "hr/payroll/salary-definitions",
      {
        method: "POST",
        body: payload,
      }
    );
  },

  update(
    id: string,
    payload: UpdateSalaryDefinitionRequest
  ) {
    return apiClient<SalaryDefinition>(
      `hr/payroll/salary-definitions/${id}`,
      {
        method: "PUT",
        body: payload,
      }
    );
  },

  delete(id: string) {
    return apiClient<{
      message: string;
    }>(
      `hr/payroll/salary-definitions/${id}`,
      {
        method: "DELETE",
      }
    );
  },
};
