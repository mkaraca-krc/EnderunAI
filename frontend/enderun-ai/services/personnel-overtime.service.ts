import { apiClient } from "@/lib/api/api-client";

/** Fazla mesai türü — çarpanlar ücret kartından gelir. */
export type OvertimeKind = 0 | 1 | 2;

export type PersonnelOvertimeLine = {
  id: string;
  workDate: string;
  hours: number;
  kind: OvertimeKind;
  kindName: string;
  multiplier: number;

  /**
   * Onaylı saat puantaja düştü mü. Düşmediyse (ör. o günün puantajı
   * onaylıydı) saat bordroya girmez — kartta görünmesi gerekir.
   */
  landedOnAttendance: boolean;
  attendanceMonth?: string | null;

  reason?: string | null;
  approvedAtUtc?: string | null;

  /** Yalnızca payroll.view ile dolu; yoksa null gelir. */
  amount?: number | null;
};

export type PersonnelOvertimeSummary = {
  personnelId: string;
  personnelName: string;
  year: number;

  /** Yıllık azami saat. null = o yıl için girilmedi. */
  annualLimit?: number | null;

  /** Sınır sayımına giren tek kalem: fazla çalışma. */
  overtimeHours: number;
  sundayHours: number;
  publicHolidayHours: number;

  limitStatus: "undefined" | "ok" | "near" | "exceeded";
  limitStatusName: string;
  limitCountsOvertimeOnly: boolean;

  consent: {
    year?: number | null;
    date?: string | null;
    isValid: boolean;
  };

  amountsHidden: boolean;
  totalAmount?: number | null;

  notLandedCount: number;
  lines: PersonnelOvertimeLine[];
};

export const personnelOvertimeService = {
  get(personnelId: string, year?: number) {
    const query = year ? `?year=${year}` : "";

    return apiClient<PersonnelOvertimeSummary>(
      `hr/personel/${personnelId}/fazla-mesai${query}`
    );
  },
};
