import { apiClient } from "@/lib/api/api-client";

/** Ücretin hangi tutar üzerinden anlaşıldığı. */
export const SalaryBasis = {
  Gross: 0,
  Net: 1,
} as const;

export const SALARY_BASIS_LABELS: Record<number, string> = {
  0: "Brüt esaslı",
  1: "Net esaslı",
};

export type SalaryDefinition = {
  id: string;
  companyId: string;
  personnelId: string;
  employmentStartDate?: string | null;
  effectiveStartDate: string;
  effectiveEndDate?: string | null;
  salaryBasis: number;
  salaryBasisName: string;
  /** Net esaslı kartta anlaşılan aylık resmi net. */
  targetNetSalary: number;
  grossSalary: number;
  netSalary: number;
  /** Kartın hesaplanmış resmi neti; parametre yoksa null. */
  officialNetSalary?: number | null;
  /** Elden ödeme; yetki yoksa null (sorgudan hiç çıkmaz). */
  extraPaymentMonthlyAmount?: number | null;
  /** Resmi net + elden ödeme; elden gizliyse null. */
  totalTakeHome?: number | null;
  extraPaymentHidden: boolean;
  dailyRate: number;
  hourlyRate: number;
  overtimeMultiplier: number;
  sundayMultiplier: number;
  publicHolidayMultiplier: number;
  currencyCode: string;
  description?: string | null;
  createdAtUtc: string;
};

/** Canlı brütleştirme sonucu — kayıt yazmaz. */
export type NetToGrossResult = {
  grossSalary: number;
  achievedNet: number;
  targetNet: number;
  difference: number;
  isExact: boolean;
  sgkEmployee: number;
  unemploymentEmployee: number;
  incomeTax: number;
  incomeTaxExemption: number;
  stampTax: number;
  stampTaxExemption: number;
  totalDeductions: number;
  totalEmployerCost: number;
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
  /** 0 = brüt esaslı (varsayılan), 1 = net esaslı. */
  salaryBasis?: number;
  /** Net esaslıda zorunlu. */
  targetNetSalary?: number;
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

  /**
   * Girilen nete karşılık gelen brütü ve kesinti kırılımını hesaplar.
   * Kayıt yazmaz; kullanıcı net girdikçe çağrılır.
   */
  netToGross(payload: {
    companyId: string;
    year: number;
    targetNet: number;
    month?: number;
    cumulativeIncomeTaxBaseBefore?: number;
  }) {
    return apiClient<NetToGrossResult>(
      "hr/payroll/net-to-gross",
      { method: "POST", body: payload }
    );
  },
};
