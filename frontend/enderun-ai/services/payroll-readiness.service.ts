import { apiClient } from "@/lib/api/api-client";

const root = "hr";

/**
 * Bordro öncesi ön kontrol ve SGK bildirim dökümü.
 *
 * İkisi de yalnızca OKUR — hiçbir kayıt yazmaz. Ön kontrol bordro
 * üretilmeden önce neyin eksik olduğunu söyler; SGK dökümü ise
 * SGK'nın kendi ekranına elle girilecek alanları verir (dosya
 * biçimi üretilmiyor, bilinçli bir karar).
 *
 * Yetki: her iki uç da `attendance-payroll.view` istiyor —
 * `payroll.view` DEĞİL. İkisi ayrı anahtar; menü ve middleware de
 * bu anahtara bağlandı, yoksa yetkisi olmayan kullanıcı ekranı
 * açıp uçtan 403 yerdi.
 */

/** Bordroyu üretmeyi engelleyen personel. */
export interface PayrollBlockedPerson {
  personnelId: string;
  employeeNumber: string | null;
  fullName: string;
}

/** Bordro üretilebilir ama resmî bildirim için alanı eksik olan. */
export interface PayrollIncompletePerson extends PayrollBlockedPerson {
  missingFields: string[];
}

export interface PayrollReadiness {
  year: number;
  month: number;
  personnelCount: number;
  payrollReadyCount: number;
  officialReadyCount: number;
  attendanceRecordCount: number;
  approvedAttendanceCount: number;
  holidayCalendarVerified: boolean;
  settingsVerified: boolean;
  mealTravelExemptionCapsDefined: boolean;
  /** Engel yoksa true; bordro bu ay hesaplanabilir. */
  canCalculate: boolean;
  /** Bordro üretimini durduran sebepler. */
  blockers: string[];
  /** Üretimi durdurmayan ama düzeltilmesi gereken durumlar. */
  warnings: string[];
  blocked: PayrollBlockedPerson[];
  incomplete: PayrollIncompletePerson[];
}

/** SGK ekranına girilecek işe giriş satırı. */
export interface SgkEntryRow {
  id: string;
  employeeNumber: string | null;
  fullName: string;
  identityNumber: string | null;
  birthDate: string | null;
  sgkRegistrationNumber: string | null;
  date: string;
  jobTitle: string | null;
  /** Boş değilse bu satır SGK'ya girilemez. */
  missingFields: string[];
  /** Bildirge özlük dosyasına yüklenmişse true. */
  noticeUploaded: boolean;
}

/** SGK ekranına girilecek işten çıkış satırı. */
export interface SgkExitRow {
  personnelId: string;
  employeeNumber: string | null;
  fullName: string;
  identityNumber: string | null;
  birthDate: string | null;
  sgkRegistrationNumber: string | null;
  date: string;
  reason: number;
  reasonName: string;
  /** Çıkış işlemi kesinleşmemişse tarih değişebilir. */
  isFinalized: boolean;
  missingFields: string[];
  noticeUploaded: boolean;
}

export interface SgkNotificationList {
  entries: SgkEntryRow[];
  exits: SgkExitRow[];
  entryCount: number;
  exitCount: number;
  /** Eksik alanı yüzünden bildirilemeyecek satır sayısı. */
  notNotifiableCount: number;
  note: string;
}

export const payrollReadinessService = {
  readiness(companyId: string, year: number, month: number) {
    const query = new URLSearchParams({
      companyId,
      year: String(year),
      month: String(month),
    });

    return apiClient<PayrollReadiness>(
      `${root}/bordro-on-kontrol?${query.toString()}`
    );
  },

  sgkNotifications(companyId: string, from: string, to: string) {
    const query = new URLSearchParams({ companyId, from, to });

    return apiClient<SgkNotificationList>(
      `${root}/sgk-bildirim?${query.toString()}`
    );
  },
};
