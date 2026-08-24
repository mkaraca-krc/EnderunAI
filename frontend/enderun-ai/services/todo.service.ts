import { apiClient } from "@/lib/api/api-client";

/**
 * YAPILACAKLAR — TEK EKRAN, ÜÇ BÖLÜM.
 *
 * Kullanıcı iki ayrı "bekleyen iş" listesine bakmak zorunda kalmasın
 * diye onay merkezinin dört kuyruğu ile görev onayları TEK LİSTEDE
 * derleniyor. Onay/ret hâlâ kendi uçlarına gidiyor — kurallı akış bu
 * pakete girmiyor, yalnız GÖRÜNÜM birleşiyor.
 */

export type TodoKind =
  | "task"
  | "progressPayment"
  | "purchaseOrder"
  | "purchaseRequest"
  | "siteReport";

export const TODO_KIND_LABELS: Record<TodoKind, string> = {
  task: "Görev",
  progressPayment: "Hakediş",
  purchaseOrder: "Satın Alma Siparişi",
  purchaseRequest: "Satın Alma Talebi",
  siteReport: "Saha Raporu",
};

export type TodoItem = {
  id: string;
  kind: TodoKind;
  title: string;
  subtitle?: string | null;

  /**
   * Tutar taşıyan satırlarda görünür: 2.000 TL'lik onayla 200.000
   * TL'lik onay aynı ağırlıkta görünmemeli.
   */
  amount?: number | null;
  currencyCode?: string | null;

  /** Termin — gecikme hesabı buradan. */
  dueDate?: string | null;

  /** Satırın ne zamandır beklediği: sıralamanın üçüncü ölçütü. */
  waitingSince?: string | null;

  href: string;
  isOverdue: boolean;
  isDueToday: boolean;

  /** Görevlerde iade sayısı; üçüncü kez iade edilen iş öne çıkmalı. */
  returnCount?: number | null;
  priority?: number | null;
};

/**
 * Bir kaynağın sonucu. HATA SESSİZ KALMAZ: bölüm "yüklenemedi" der,
 * diğer kaynaklar görünmeye devam eder ve sayaç eksik olduğunu belli
 * eder ("3+").
 */
export type TodoSource = {
  kind: TodoKind;
  items: TodoItem[];
  failed: boolean;
  /** İzin yoksa kaynak HİÇ ÇAĞRILMAZ — boş dönmez, istenmez. */
  skipped: boolean;
};

export type TodoBoard = {
  awaitingMyApproval: TodoSource[];
  assignedToMe: TodoItem[];
  sentByMe: TodoItem[];
  assignedFailed: boolean;
  sentFailed: boolean;
};

/**
 * ACİLİYET SIRASI — TARİH DEĞİL:
 *   1) Termini geçmiş (en üstte)
 *   2) Bugün biten
 *   3) Kalanlar: BEKLEME SÜRESİ UZUN OLAN ÜSTTE
 *
 * Üçüncü ölçüt bilerek "en eski önce": en kolay unutulan iş, uzun
 * süredir bekleyendir. Yeni gelenler zaten göz önünde.
 */
export function sortByUrgency(items: TodoItem[]): TodoItem[] {
  return [...items].sort((a, b) => {
    if (a.isOverdue !== b.isOverdue) return a.isOverdue ? -1 : 1;
    if (a.isDueToday !== b.isDueToday) return a.isDueToday ? -1 : 1;

    const aTime = a.waitingSince ? Date.parse(a.waitingSince) : Number.MAX_SAFE_INTEGER;
    const bTime = b.waitingSince ? Date.parse(b.waitingSince) : Number.MAX_SAFE_INTEGER;

    return aTime - bTime;
  });
}

function gunBasi(value?: string | null) {
  if (!value) return null;
  const tarih = new Date(value);
  return Number.isNaN(tarih.getTime()) ? null : tarih;
}

export function terminDurumu(dueDate?: string | null) {
  const termin = gunBasi(dueDate);

  if (!termin) return { isOverdue: false, isDueToday: false };

  const simdi = new Date();
  const bugun = new Date(simdi.getFullYear(), simdi.getMonth(), simdi.getDate());
  const terminGunu = new Date(
    termin.getFullYear(),
    termin.getMonth(),
    termin.getDate(),
  );

  return {
    isOverdue: terminGunu.getTime() < bugun.getTime(),
    isDueToday: terminGunu.getTime() === bugun.getTime(),
  };
}

export const todoService = {
  /** Bana atanan açık görevler. */
  assignedToMe(userId: string) {
    return apiClient<{ items: unknown[] }>(
      `tasks?assignedToUserId=${encodeURIComponent(userId)}&pageSize=50`,
    );
  },

  /** Gönderdiğim açık görevler. */
  sentByMe() {
    return apiClient<{ items: unknown[] }>(`tasks?pageSize=50`);
  },

  /** Onayımı bekleyen görevler: tamamlandı, gönderenin onayında. */
  awaitingTaskApproval() {
    return apiClient<{ items: unknown[] }>(`tasks?status=4&pageSize=50`);
  },
};
