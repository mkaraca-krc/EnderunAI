import { apiClient } from "@/lib/api/api-client";

export type AttendanceStatus =
  | 0
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6
  | 7
  | 8
  | 9;

export type AttendanceItem = {
  id: string;
  companyId: string;
  projectId?: string | null;
  projectSiteId?: string | null;
  personnelId: string;
  workDate: string;
  status: AttendanceStatus;
  statusName: string;
  checkInTime?: string | null;
  checkOutTime?: string | null;
  normalHours: number;
  overtimeHours: number;
  sundayHours: number;
  publicHolidayHours: number;
  totalHours: number;
  teamName?: string | null;
  roleName?: string | null;
  workItemCode?: string | null;
  workItemName?: string | null;
  /** İcmal kısmı — işçiliğin hangi imalata yazılacağı. Opsiyonel. */
  projectHakedisSectionId?: string | null;
  locationName?: string | null;
  isApproved: boolean;
  approvedByUserId?: string | null;
  approvedAtUtc?: string | null;
  description?: string | null;
  createdAtUtc: string;
};

export type AttendanceFilters = {
  companyId?: string;
  projectId?: string;
  projectSiteId?: string;
  personnelId?: string;
  status?: number;
  startDate?: string;
  endDate?: string;
  search?: string;
};

export type CreateAttendanceRequest = {
  companyId: string;
  projectId?: string | null;
  projectSiteId?: string | null;
  personnelId: string;
  workDate: string;
  status: number;
  checkInTime?: string | null;
  checkOutTime?: string | null;
  normalHours: number;
  overtimeHours: number;
  sundayHours: number;
  publicHolidayHours: number;
  teamName?: string | null;
  roleName?: string | null;
  workItemCode?: string | null;
  workItemName?: string | null;
  /** İcmal kısmı — işçiliğin hangi imalata yazılacağı. Opsiyonel. */
  projectHakedisSectionId?: string | null;
  locationName?: string | null;
  description?: string | null;
};

export type UpdateAttendanceRequest = Omit<
  CreateAttendanceRequest,
  "companyId" | "personnelId" | "workDate"
>;

export type AttendanceSummary = {
  personnelId: string;
  startDate: string;
  endDate: string;
  presentDays: number;
  leaveDays: number;
  absenceDays: number;
  normalHours: number;
  overtimeHours: number;
  sundayHours: number;
  publicHolidayHours: number;
  totalHours: number;
};

function buildQuery(values?: Record<string, string | number | undefined>) {
  const query = new URLSearchParams();

  Object.entries(values ?? {}).forEach(([key, value]) => {
    if (value !== undefined && value !== "") {
      query.set(key, String(value));
    }
  });

  const result = query.toString();
  return result ? `?${result}` : "";
}

export const hrAttendanceService = {
  getAll(filters?: AttendanceFilters) {
    return apiClient<AttendanceItem[]>(
      `hr/attendance${buildQuery(filters)}`
    );
  },

  getById(id: string) {
    return apiClient<AttendanceItem>(`hr/attendance/${id}`);
  },

  create(payload: CreateAttendanceRequest) {
    return apiClient<AttendanceItem>("hr/attendance", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: UpdateAttendanceRequest) {
    return apiClient<AttendanceItem>(`hr/attendance/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  approve(id: string) {
    return apiClient<AttendanceItem>(
      `hr/attendance/${id}/approve`,
      {
        method: "POST",
      }
    );
  },

  delete(id: string) {
    return apiClient<{ message: string }>(
      `hr/attendance/${id}`,
      {
        method: "DELETE",
      }
    );
  },

  getSummary(
    personnelId: string,
    startDate: string,
    endDate: string
  ) {
    return apiClient<AttendanceSummary>(
      `hr/attendance/summary${buildQuery({
        personnelId,
        startDate,
        endDate,
      })}`
    );
  },
  /**
   * Personelin GERÇEK yevmiyesi: resmî günlük/saatlik ücret ve
   * üzerine elden ödemenin günlük payı.
   *
   * Elden kısım extra_payment.view ister; yoksa yalnızca resmî
   * rakamlar döner ve `extraPaymentHidden` ile eksiklik bildirilir.
   * Bu rakam SALT GÖSTERİMdir; bordroya ve muhasebeye girmez.
   */
  getDailyWage(personnelId: string, asOf?: string) {
    return apiClient<ActualDailyWage>(
      `hr/attendance/daily-wage${buildQuery({ personnelId, asOf })}`
    );
  },
};

export type ActualDailyWage = {
  personnelId: string;
  asOf: string;
  monthlyGross: number;
  officialDailyRate: number;
  officialHourlyRate: number;
  dailyWorkHours: number;
  extraMonthlyAmount?: number | null;
  extraDailyRate?: number | null;
  actualDailyRate?: number | null;
  actualHourlyRate?: number | null;
  extraPaymentHidden: boolean;
};
