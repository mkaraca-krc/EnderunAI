import { apiClient } from "@/lib/api/api-client";

/** Puantajda bir günün durumu — backend AttendanceStatus ile birebir. */
export const ATTENDANCE_STATUS = {
  Absent: 0,
  Worked: 1,
  PaidLeave: 2,
  SickReport: 3,
  PublicHoliday: 4,
  WeeklyHoliday: 5,
  UnpaidLeave: 6,
  ExcusedAbsence: 7,
  HalfDay: 8,
  RemoteWork: 9,
} as const;

export const ATTENDANCE_STATUS_LABELS: Record<number, string> = {
  0: "Devamsız",
  1: "Çalıştı",
  2: "Ücretli izin",
  3: "Raporlu",
  4: "Resmi tatil",
  5: "Hafta tatili",
  6: "Ücretsiz izin",
  7: "Mazeretli devamsızlık",
  8: "Yarım gün",
  9: "Uzaktan çalışma",
};

/** Izgarada tek harfle gösterilen kısaltmalar. */
export const ATTENDANCE_STATUS_SHORT: Record<number, string> = {
  0: "D",
  1: "✓",
  2: "İ",
  3: "R",
  4: "T",
  5: "—",
  6: "Ü",
  7: "M",
  8: "½",
  9: "U",
};

/** Haftanın çalışılan günleri — bayrak toplamı. */
export const WORK_WEEK = {
  MondayToFriday: 31,
  MondayToSaturday: 63,
  AllDays: 127,
} as const;

export const RELIGIOUS_HOLIDAY = {
  Ramazan: 0,
  Kurban: 1,
} as const;

export type AttendanceCell = {
  date: string;
  isWorkDay: boolean;
  isHoliday: boolean;
  isHalfDayHoliday: boolean;
  holidayName?: string | null;
  suggestedStatus: number;
  suggestedStatusName: string;
  suggestedNormalHours: number;
  recordId?: string | null;
  status?: number | null;
  normalHours?: number | null;
  overtimeHours?: number | null;
  sundayHours?: number | null;
  publicHolidayHours?: number | null;
  isApproved: boolean;
};

export type AttendanceRow = {
  personnelId: string;
  employeeNumber: string;
  fullName: string;
  workWeek: number;
  workWeekName: string;
  workWeekSource: string;
  cells: AttendanceCell[];
};

export type AttendanceSheet = {
  year: number;
  month: number;
  holidayCalendarVerified: boolean;
  dailyWorkHours: number;
  companyWorkWeek: number;
  companyWorkWeekName: string;
  holidayCount: number;
  recordCount: number;
  approvedCount: number;
  personnelCount: number;
  message?: string | null;
  rows: AttendanceRow[];
};

export type AttendanceSheetEntry = {
  personnelId: string;
  workDate: string;
  status: number;
  normalHours: number;
  overtimeHours: number;
  sundayHours: number;
  publicHolidayHours: number;
  description?: string | null;
};

export type HolidayDay = {
  id: string;
  date: string;
  name: string;
  isHalfDay: boolean;
};

export type HolidayCalendar = {
  year: number;
  exists: boolean;
  isVerified: boolean;
  workWeek: number;
  workWeekName: string;
  headOfficeWorkWeek?: number | null;
  headOfficeWorkWeekName?: string | null;
  message?: string | null;
  calendar?: {
    id: string;
    year: number;
    verifiedAtUtc?: string | null;
    verificationNote?: string | null;
    days: HolidayDay[];
  } | null;
};

export const attendanceSheetService = {
  get: (companyId: string, year: number, month: number) =>
    apiClient<AttendanceSheet>(
      `hr/attendance/cetvel?companyId=${companyId}&year=${year}&month=${month}`
    ),

  generate: (body: {
    companyId: string;
    year: number;
    month: number;
    personnelIds?: string[] | null;
    overwrite?: boolean;
  }) =>
    apiClient<{
      createdCount: number;
      updatedCount: number;
      skippedApprovedCount: number;
      personnelCount: number;
      message: string;
    }>("hr/attendance/cetvel/olustur", { method: "POST", body }),

  save: (companyId: string, entries: AttendanceSheetEntry[]) =>
    apiClient<{
      savedCount: number;
      skippedApprovedCount: number;
      message: string;
    }>("hr/attendance/cetvel/kaydet", {
      method: "POST",
      body: { companyId, entries },
    }),

  approve: (body: {
    companyId: string;
    year: number;
    month: number;
    personnelIds?: string[] | null;
  }) =>
    apiClient<{ approvedCount: number; message: string }>(
      "hr/attendance/cetvel/onayla",
      { method: "POST", body }
    ),
};

export const holidayCalendarService = {
  get: (companyId: string, year: number) =>
    apiClient<HolidayCalendar>(
      `hr/tatil-takvimi?companyId=${companyId}&year=${year}`
    ),

  seedFixed: (companyId: string, year: number) =>
    apiClient<{ addedCount: number; message: string }>(
      `hr/tatil-takvimi/${year}/sabit-tatiller?companyId=${companyId}`,
      { method: "POST", body: {} }
    ),

  addReligious: (
    companyId: string,
    year: number,
    kind: number,
    firstDay: string
  ) =>
    apiClient<{ addedCount: number; message: string }>(
      `hr/tatil-takvimi/${year}/dini-bayram?companyId=${companyId}`,
      { method: "POST", body: { kind, firstDay } }
    ),

  addDay: (
    companyId: string,
    year: number,
    body: { date: string; name: string; isHalfDay: boolean }
  ) =>
    apiClient<{ message: string }>(
      `hr/tatil-takvimi/${year}/gun?companyId=${companyId}`,
      { method: "POST", body }
    ),

  removeDay: (id: string) =>
    apiClient<{ message: string }>(`hr/tatil-takvimi/gun/${id}`, {
      method: "DELETE",
    }),

  verify: (companyId: string, year: number, note: string | null) =>
    apiClient<{ dayCount: number; message: string }>(
      `hr/tatil-takvimi/${year}/dogrula?companyId=${companyId}`,
      { method: "POST", body: { note } }
    ),

  updateWorkWeek: (
    companyId: string,
    year: number,
    body: { workWeek?: number | null; headOfficeWorkWeek?: number | null }
  ) =>
    apiClient<{ message: string }>(
      `hr/tatil-takvimi/${year}/calisma-haftasi?companyId=${companyId}`,
      { method: "PUT", body }
    ),
};
