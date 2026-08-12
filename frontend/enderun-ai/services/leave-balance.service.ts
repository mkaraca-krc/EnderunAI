import { apiClient } from "@/lib/api/api-client";

const root = "hr/izin-bakiye";

/**
 * Yıllık izin bakiyesi — yalnızca okur.
 *
 * Hak ediş kuralı backend'de TEK yerde: kademe tablosu (1 yıl 14,
 * 5 yıl üstü 20, 15 yıl üstü 26) çıkış tazminatıyla aynı kaynaktan
 * geliyor. Bu yüzden ekranda hiçbir hesap tekrarlanmıyor — ikinci
 * bir kural, aynı personel için ekranda ve çıkışta farklı iki rakam
 * üretirdi.
 *
 * Yetki: `attendance-payroll.view`.
 */

export interface LeaveBalance {
  personnelId: string;
  employeeNumber: string;
  fullName: string;
  serviceDays: number;
  serviceYears: number;
  /** Bugüne kadar hak edilen toplam gün; devir dahil. */
  entitlementDays: number;
  usedDays: number;
  /** Onay bekleyen; bakiyeden düşülmez ama kullanılabilirden düşer. */
  pendingDays: number;
  /** Hak ediş − kullanılan. */
  remainingDays: number;
  /** Kalan − onay bekleyen. Yeni talep bu rakamla karşılaştırılır. */
  availableDays: number;
  /** Kıdemine göre şu anki yıllık hak ediş kademesi. */
  currentTierDays: number;
  nextAccrualDate: string | null;
  nextAccrualDays: number;
  note: string | null;
}

export interface LeaveBalanceSummary {
  asOf: string;
  personnelCount: number;
  totalEntitlementDays: number;
  totalRemainingDays: number;
  /** Hak edişini aşmış olanlar: avans izin ya da eksik veri. */
  overdraftCount: number;
  /** İşe giriş tarihi olmadığı için hesaplanamayanlar. */
  withoutStartDateCount: number;
  items: LeaveBalance[];
}

export const leaveBalanceService = {
  get(companyId: string, personnelId?: string) {
    const query = new URLSearchParams({ companyId });
    if (personnelId) query.set("personnelId", personnelId);

    return apiClient<LeaveBalanceSummary>(`${root}?${query.toString()}`);
  },
};
