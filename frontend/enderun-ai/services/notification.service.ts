import { apiClient } from "@/lib/api/api-client";

export type NotificationStatus =
  | "Open"
  | "Read"
  | "Snoozed"
  | "Dismissed"
  | "Closed";

export interface NotificationItem {
  id: string;
  type: string;
  title: string;
  /**
   * Gövde. Tutar içeren metin YALNIZ yetkili kullanıcıya gelir;
   * yetkisiz kullanıcıda tutarsız metin döner. Maskeleme uçta
   * yapılıyor, burada değil.
   */
  detail?: string | null;
  severity: number;
  severityName: string;
  targetPath?: string | null;
  dueDate?: string | null;
  status: NotificationStatus;
  snoozedUntil?: string | null;
  firstSeenAtUtc: string;
}

export interface NotificationList {
  /** Yalnız Açık durumdakiler; okunanlar sayılmaz. */
  unreadCount: number;
  items: NotificationItem[];
}

export const notificationService = {
  list(companyId: string, includeHandled = false) {
    const query = new URLSearchParams({ companyId });

    if (includeHandled) query.set("includeHandled", "true");

    return apiClient<NotificationList>(`bildirimler?${query.toString()}`);
  },

  markRead(id: string) {
    return apiClient<{ id: string; status: string }>(`bildirimler/${id}/okundu`, {
      method: "POST",
    });
  },

  dismiss(id: string) {
    return apiClient<{ id: string; status: string }>(`bildirimler/${id}/kapat`, {
      method: "POST",
    });
  },

  /** Erteleme tarihi GELECEKTE olmalı; uç geçmişi reddediyor. */
  snooze(id: string, until: string) {
    return apiClient<{ id: string; status: string }>(`bildirimler/${id}/ertele`, {
      method: "POST",
      body: { until },
    });
  },
};
