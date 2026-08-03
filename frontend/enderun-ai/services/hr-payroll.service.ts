import { apiClient } from "@/lib/api/api-client";

export enum PayrollStatus {
  Draft = 0,
  Calculated = 1,
  Approved = 2,
  Paid = 3,
}

export type PayrollRecord = {
  id: string;
  companyId: string;
  personnelId: string;
  year: number;
  month: number;
  grossSalary: number;
  normalWorkAmount: number;
  overtimeAmount: number;
  sundayWorkAmount: number;
  publicHolidayAmount: number;
  bonusAmount: number;
  mealAmount: number;
  travelAmount: number;
  otherEarningAmount: number;
  compensationAmount: number;
  totalEarnings: number;
  sgkEmployeeDeduction: number;
  incomeTaxDeduction: number;
  stampTaxDeduction: number;
  advanceDeduction: number;
  otherDeductionAmount: number;
  totalDeductions: number;
  officialNetPayableAmount: number;
  actualPayableAmount: number;
  netPayableAmount: number;
  currencyCode: string;
  status: PayrollStatus;
  statusName: string;
  approvedAtUtc?: string | null;
  approvedByUserId?: string | null;
  paidAtUtc?: string | null;
  paymentReference?: string | null;
  description?: string | null;
  createdAtUtc: string;
};

export type PayrollSummary = {
  companyId: string;
  year: number;
  month: number;
  payrollCount: number;
  draftCount: number;
  calculatedCount: number;
  approvedCount: number;
  paidCount: number;
  totalGrossSalary: number;
  totalEarnings: number;
  totalDeductions: number;
  totalCompensationAmount: number;
  totalOfficialNetPayableAmount: number;
  totalNetPayableAmount: number;
  currencyCode: string;
};

export type CompanyPayrollCalculationResult = {
  companyId: string;
  year: number;
  month: number;
  personnelCount: number;
  createdCount: number;
  updatedCount: number;
  skippedCount: number;
  totalNetPayableAmount: number;
};

export type MarkPayrollPaidRequest = {
  paymentReference?: string | null;
  paymentMethod: 0 | 1;
  bankAccountId?: string | null;
  cashAccountId?: string | null;
  paymentDate: string;
};

export type PayrollFilters = {
  companyId?: string;
  personnelId?: string;
  year?: number;
  month?: number;
  status?: number;
};

/**
 * Kesinti tutarları artık istemciden gönderilmiyor: SGK, gelir ve damga
 * vergisi şirketin bordro parametrelerinden hesaplanıyor.
 */
export type CalculateCompanyPayrollRequest = {
  companyId: string;
  year: number;
  month: number;
  recalculateExisting?: boolean;
};

function buildQuery(
  values: Record<string, string | number | boolean | undefined>
): string {
  const query = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== "") {
      query.set(key, String(value));
    }
  });
  const result = query.toString();
  return result ? `?${result}` : "";
}

export const hrPayrollService = {
  getAll(filters: PayrollFilters) {
    return apiClient<PayrollRecord[]>(
      `hr/payroll/records${buildQuery(filters)}`
    );
  },

  getById(id: string) {
    return apiClient<PayrollRecord>(`hr/payroll/records/${id}`);
  },

  getSummary(companyId: string, year: number, month: number) {
    return apiClient<PayrollSummary>(
      `hr/payroll/summary${buildQuery({ companyId, year, month })}`
    );
  },

  calculateCompany(payload: CalculateCompanyPayrollRequest) {
    return apiClient<CompanyPayrollCalculationResult>(
      "hr/payroll/records/calculate-company",
      { method: "POST", body: payload }
    );
  },

  approve(id: string) {
    return apiClient<PayrollRecord>(
      `hr/payroll/records/${id}/approve`,
      { method: "POST" }
    );
  },

  cancel(id: string, reason: string) {
    return apiClient<PayrollRecord>(
      `hr/payroll/records/${id}/cancel`,
      {
        method: "POST",
        body: { reason: reason.trim() },
      }
    );
  },

  markPaid(id: string, payload: MarkPayrollPaidRequest) {
    return apiClient<PayrollRecord>(
      `hr/payroll/records/${id}/paid`,
      { method: "POST", body: payload }
    );
  },

  delete(id: string) {
    return apiClient<{ message: string }>(`hr/payroll/records/${id}`, {
      method: "DELETE",
    });
  },
};
