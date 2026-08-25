import { apiClient } from "@/lib/api/api-client";

export type HrDashboardPersonnel = {
  id: string;
  companyId: string;
  employeeNumber?: string | null;
  firstName?: string | null;
  lastName?: string | null;
  fullName?: string | null;
  jobTitle?: string | null;
  profession?: string | null;
  branchId?: string | null;
  status?: number;
  statusName?: string | null;
  birthDate?: string | null;
  employmentStartDate?: string | null;
  employmentEndDate?: string | null;
  isActive?: boolean;
  createdAtUtc?: string | null;
  /** 0 = Atanmadı, 1 = Merkez, 2 = Şantiye. */
  workLocationType?: number;
  /** Görev yeri atanmamış veya şantiye seçilip ataması olmayan. */
  isAwaitingWorkLocation?: boolean;
};

export type HrDashboardPayroll = {
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
  otherDeductionAmount: number;

  actualPayableAmount: number;
  netPayableAmount: number;
  officialNetPayableAmount: number;

  currencyCode: string;
  status: number;
  statusName?: string | null;

  approvedAtUtc?: string | null;
  paidAtUtc?: string | null;
  paymentReference?: string | null;
  createdAtUtc?: string | null;
};

export type HrDashboardSalaryDefinition = {
  id: string;
  companyId: string;
  personnelId: string;
  effectiveStartDate: string;
  effectiveEndDate?: string | null;
  grossSalary: number;
  netSalary: number;
  currencyCode: string;
};

function normalizeList<T>(
  payload: unknown
): T[] {
  if (Array.isArray(payload)) {
    return payload as T[];
  }

  if (
    payload &&
    typeof payload === "object"
  ) {
    const value =
      payload as {
        items?: unknown;
        data?: unknown;
        result?: unknown;
      };

    if (Array.isArray(value.items)) {
      return value.items as T[];
    }

    if (Array.isArray(value.data)) {
      return value.data as T[];
    }

    if (Array.isArray(value.result)) {
      return value.result as T[];
    }
  }

  return [];
}

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

export const hrDashboardService = {
  async getPersonnel(
    companyId: string
  ) {
    const response =
      await apiClient<unknown>(
        `personnel${queryString({
          companyId,
        })}`
      );

    return normalizeList<
      HrDashboardPersonnel
    >(response).filter(
      /*
       * KAPSAM ALANI EKSİKSE SATIR ELENİR.
       *
       * Önce `!item.companyId || ...` yazıyordu: şirket alanı
       * gelmeyen satır listeye GİRİYORDU. Şirket izolasyonunda
       * varsayılan KAPALI olmalı — "kapı bilinmeyen tipte kapalı
       * düşer" kuralının aynısı.
       *
       * Bugün zararsız çünkü canlıda tek şirket var. İkinci şirket
       * geldiği gün ya da kapsamı sınırlı bir kullanıcı tanımlandığı
       * gün sızdırırdı — G3 erteleme nöbetçisinin beklediği iki
       * tetikleyici tam olarak bunlar.
       */
      (item) => item.companyId === companyId
    );
  },

  async getPayrolls(
    companyId: string,
    year?: number,
    month?: number
  ) {
    const response =
      await apiClient<unknown>(
        `hr/payroll/records${queryString({
          companyId,
          year,
          month,
        })}`
      );

    return normalizeList<
      HrDashboardPayroll
    >(response);
  },

  async getSalaryDefinitions(
    companyId: string,
    effectiveDate?: string
  ) {
    const response =
      await apiClient<unknown>(
        `hr/payroll/salary-definitions${queryString({
          companyId,
          effectiveDate,
        })}`
      );

    return normalizeList<
      HrDashboardSalaryDefinition
    >(response);
  },
};
