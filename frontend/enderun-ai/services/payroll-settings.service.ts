import { apiClient } from "@/lib/api/api-client";

export type PayrollTaxBracket = {
  id: string;
  order: number;
  lowerBound: number;
  upperBound: number | null;
  rate: number;
};

export type PayrollSettings = {
  id: string;
  companyId: string;
  year: number;
  minimumWageGross: number;
  minimumWageNet: number;
  sgkBaseFloor: number;
  sgkBaseCeiling: number;
  sgkEmployeeRate: number;
  unemploymentEmployeeRate: number;
  sgkEmployerRate: number;
  unemploymentEmployerRate: number;
  sgkEmployerDiscountEnabled: boolean;
  sgkEmployerDiscountPoints: number;
  stampTaxPerMille: number;
  minimumWageIncomeTaxExemptionEnabled: boolean;
  minimumWageStampTaxExemptionEnabled: boolean;
  /** Kıdem tazminatı tavanı: bir hizmet yılı için ödenecek üst sınır. */
  severanceCeiling: number;
  severanceCeilingPeriodNote: string | null;
  /** Günlük normal çalışma süresi (saat); saatlik ücret bundan türer. */
  dailyWorkHours: number;
  /**
   * Nakdî yemek/yol yardımının günlük istisna tavanları. null =
   * o yıl için tanımlanmadı; istisna uygulanmaz ve bordro ön kontrolü
   * uyarır.
   */
  mealSgkExemptionDailyCap: number | null;
  mealIncomeTaxExemptionDailyCap: number | null;
  travelSgkExemptionDailyCap: number | null;
  travelIncomeTaxExemptionDailyCap: number | null;
  verifiedAtUtc: string | null;
  verificationNote: string | null;
  isVerified: boolean;
  taxBrackets: PayrollTaxBracket[];
};

export type UpdatePayrollSettingsPayload = Omit<
  PayrollSettings,
  "id" | "companyId" | "year" | "verifiedAtUtc" | "verificationNote" | "isVerified" | "taxBrackets"
> & {
  taxBrackets: Array<Omit<PayrollTaxBracket, "id">>;
};

function query(companyId: string, year?: number) {
  const params = new URLSearchParams({ companyId });
  if (year !== undefined) params.set("year", String(year));
  return params.toString();
}

export const payrollSettingsService = {
  get(companyId: string, year?: number) {
    return apiClient<PayrollSettings>(`payroll-settings?${query(companyId, year)}`);
  },

  update(companyId: string, payload: UpdatePayrollSettingsPayload, year?: number) {
    return apiClient<PayrollSettings>(`payroll-settings?${query(companyId, year)}`, {
      method: "PUT",
      body: payload,
    });
  },

  /** Parametrelerin mevzuatla karşılaştırıldığını onaylar. */
  verify(companyId: string, verificationNote: string | null, year?: number) {
    return apiClient<PayrollSettings>(
      `payroll-settings/verify?${query(companyId, year)}`,
      { method: "POST", body: { verificationNote } }
    );
  },
};
