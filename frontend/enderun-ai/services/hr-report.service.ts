import { apiClient } from "@/lib/api/api-client";

export type HrReportPersonnel = {
  id: string;
  companyId: string;
  companyName?: string | null;
  branchId?: string | null;
  branchName?: string | null;
  employeeNumber: string;
  firstName: string;
  lastName: string;
  fullName: string;
  identityNumber?: string | null;
  phone?: string | null;
  email?: string | null;
  jobTitle?: string | null;
  profession?: string | null;
  employmentStartDate?: string | null;
  employmentEndDate?: string | null;
  status: number;
  isActive: boolean;
};

export type HrReportPayroll = {
  id: string;
  companyId: string;
  personnelId: string;
  year: number;
  month: number;
  grossSalary: number;
  totalEarnings: number;
  totalDeductions: number;
  sgkEmployeeDeduction: number;
  incomeTaxDeduction: number;
  stampTaxDeduction: number;
  advanceDeduction: number;
  actualPayableAmount: number;
  netPayableAmount: number;
  currencyCode: string;
  status: number;
  statusName?: string | null;
  paidAtUtc?: string | null;
  paymentReference?: string | null;
};

export type HrReportLeave = {
  id: string;
  companyId: string;
  personnelId: string;
  leaveTypeName: string;
  startDate: string;
  endDate: string;
  totalDays: number;
  reason: string;
  status: number;
  statusName: string;
};

export type HrReportOvertime = {
  id: string;
  companyId: string;
  personnelId: string;
  workDate: string;
  requestedHours: number;
  approvedHours: number;
  isSundayWork: boolean;
  isPublicHolidayWork: boolean;
  reason: string;
  status: number;
  statusName: string;
};

export type HrReportAdvance = {
  id: string;
  companyId: string;
  personnelId: string;
  requestDate: string;
  requestedAmount: number;
  approvedAmount: number;
  currencyCode: string;
  deductionInstallmentCount: number;
  reason: string;
  status: number;
  statusName: string;
  paidAtUtc?: string | null;
  paymentReference?: string | null;
};

function queryString(
  values: Record<
    string,
    string | number | undefined
  >
) {
  const query =
    new URLSearchParams();

  Object.entries(values).forEach(
    ([key, value]) => {
      if (
        value !== undefined &&
        value !== ""
      ) {
        query.set(
          key,
          String(value)
        );
      }
    }
  );

  const result =
    query.toString();

  return result
    ? `?${result}`
    : "";
}

export const hrReportService = {
  getPersonnel(
    companyId: string
  ) {
    return apiClient<
      HrReportPersonnel[]
    >(
      `personnel${queryString({
        companyId,
      })}`
    );
  },

  getPayrolls(
    companyId: string,
    year?: number,
    month?: number
  ) {
    return apiClient<
      HrReportPayroll[]
    >(
      `hr/payroll/records${queryString({
        companyId,
        year,
        month,
      })}`
    );
  },

  getLeaves(
    companyId: string,
    startDate?: string,
    endDate?: string
  ) {
    return apiClient<
      HrReportLeave[]
    >(
      `hr/workforce/leaves${queryString({
        companyId,
        startDate,
        endDate,
      })}`
    );
  },

  getOvertimes(
    companyId: string,
    startDate?: string,
    endDate?: string
  ) {
    return apiClient<
      HrReportOvertime[]
    >(
      `hr/workforce/overtimes${queryString({
        companyId,
        startDate,
        endDate,
      })}`
    );
  },

  getAdvances(
    companyId: string,
    startDate?: string,
    endDate?: string
  ) {
    return apiClient<
      HrReportAdvance[]
    >(
      `hr/workforce/advances${queryString({
        companyId,
        startDate,
        endDate,
      })}`
    );
  },
};
