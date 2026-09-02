import { apiClient } from "@/lib/api/api-client";

export enum WorkTaskPriority {
  Low = 0,
  Normal = 1,
  High = 2,
  Critical = 3,
}

/*
 * DURUM ENUM'U ARKA UÇLA BİREBİR.
 *
 * Önce burada `Draft = 0` ve `Waiting = 3` vardı — arka uçta İKİSİ DE
 * YOK. `Approved = 6` ve `Returned = 7` ise arka uçta VAR, burada
 * yoktu. Yani "tek kaynak" sanılan bu enum'un kendisi de yanlıştı:
 * liste ekranı onaylanmış bir görevi hiç tanımıyordu.
 *
 * `Waiting` yalnız etikette değil, DAVRANIŞTA da kullanılıyordu
 * (`gorevler/page.tsx` "Başlat" düğmesi koşulu) — hiçbir zaman
 * eşleşmeyen ölü bir dal.
 *
 * Kaynak: backend/EnderunAI.Api/Models/WorkTask.cs. `tests/
 * gorev-durum-etiketi.test.ts` bu hizayı C# dosyasını okuyarak ölçer.
 */
export enum WorkTaskStatus {
  Open = 1,
  InProgress = 2,
  /** Yapan bitirdi; GÖNDERENİN onayı bekleniyor. */
  Completed = 4,
  Cancelled = 5,
  /** Gönderen onayladı — görev kapandı. */
  Approved = 6,
  /** Gönderen gerekçesiyle iade etti; görev yapana geri döner. */
  Returned = 7,
}

/*
 * ETİKET VE RENK — TEK KAYNAK.
 *
 * NEDEN BURADA: liste ve detay ekranları bu eşlemeyi AYRI AYRI
 * yazmıştı. Detay ekranı yoğun 0-tabanlı numaralandırma varsaymış
 * (DURUM_OPEN = 0) ve altı gerçek değerden üçünü yanlış göstermişti.
 * Genel Müdür listede "Açık" gördüğü görevin detayında "Devam Ediyor"
 * gördü; kayıt değişmemişti, iki ekran aynı sayıyı farklı okuyordu.
 *
 * `Record<WorkTaskStatus, ...>` derleme zamanında EKSİKSİZLİK dayatır:
 * enum'a değer eklenip buraya eklenmezse derleme düşer. Çalışma
 * zamanı ölçümü ayrıca `tests/gorev-durum-etiketi.test.ts` içinde.
 */
export const DURUM_ETIKETLERI: Record<WorkTaskStatus, string> = {
  [WorkTaskStatus.Open]: "Açık",
  [WorkTaskStatus.InProgress]: "Devam Ediyor",
  [WorkTaskStatus.Completed]: "Tamamlandı, onay bekliyor",
  [WorkTaskStatus.Cancelled]: "İptal",
  [WorkTaskStatus.Approved]: "Onaylandı",
  [WorkTaskStatus.Returned]: "İade Edildi",
};

export const DURUM_RENKLERI: Record<WorkTaskStatus, string> = {
  [WorkTaskStatus.Open]: "blue",
  [WorkTaskStatus.InProgress]: "yellow",
  [WorkTaskStatus.Completed]: "yellow",
  [WorkTaskStatus.Cancelled]: "red",
  [WorkTaskStatus.Approved]: "green",
  [WorkTaskStatus.Returned]: "red",
};

/*
 * İKİ RENK SÖZLÜĞÜ, TEK KAYNAK.
 *
 * Liste `erp-status` CSS sınıfı kullanıyor (blue/green/red...), detay
 * ise `Badge` bileşeninin türlerini (success/warning/danger...). İkisi
 * ayrı sözlük — bu yüzden tek bir renk haritası ikisine de yetmez.
 * Ama eşlemenin KENDİSİ tek yerde durmalı; yoksa yine ayrışırlar.
 */
export const DURUM_ROZET_TURU: Record<
  WorkTaskStatus,
  "default" | "success" | "warning" | "danger" | "info"
> = {
  [WorkTaskStatus.Open]: "info",
  [WorkTaskStatus.InProgress]: "warning",
  [WorkTaskStatus.Completed]: "warning",
  [WorkTaskStatus.Cancelled]: "danger",
  [WorkTaskStatus.Approved]: "success",
  [WorkTaskStatus.Returned]: "danger",
};

export const ONCELIK_ETIKETLERI: Record<WorkTaskPriority, string> = {
  [WorkTaskPriority.Low]: "Düşük",
  [WorkTaskPriority.Normal]: "Normal",
  [WorkTaskPriority.High]: "Yüksek",
  [WorkTaskPriority.Critical]: "Kritik",
};

/**
 * Durumun Türkçe adı. Tanınmayan değer için sunucunun gönderdiği
 * `statusName` kullanılır — sunucu İngilizce enum adı gönderir, bu
 * yüzden yedek bir ÇÖZÜM DEĞİL, görünür bir işarettir.
 */
export function durumEtiketi(status: number, yedek?: string): string {
  return DURUM_ETIKETLERI[status as WorkTaskStatus] ?? yedek ?? String(status);
}

export function durumRengi(status: number): string {
  return DURUM_RENKLERI[status as WorkTaskStatus] ?? "gray";
}

export function durumRozetTuru(
  status: number,
): "default" | "success" | "warning" | "danger" | "info" {
  return DURUM_ROZET_TURU[status as WorkTaskStatus] ?? "default";
}

export const ONCELIK_RENKLERI: Record<WorkTaskPriority, string> = {
  [WorkTaskPriority.Low]: "gray",
  [WorkTaskPriority.Normal]: "blue",
  [WorkTaskPriority.High]: "yellow",
  [WorkTaskPriority.Critical]: "red",
};

export function oncelikRengi(priority: number): string {
  return ONCELIK_RENKLERI[priority as WorkTaskPriority] ?? "gray";
}

export function oncelikEtiketi(priority: number, yedek?: string): string {
  return (
    ONCELIK_ETIKETLERI[priority as WorkTaskPriority] ?? yedek ?? String(priority)
  );
}


/*
 * MASRAF MERKEZİ — ÜÇ ALAN, TEK SEÇİM.
 *
 * Arka uç bu üçünü DTO'da hep gönderiyordu; ön yüz tipi onları
 * tanımıyordu bile. Genel Müdür'ün "iş emrinde merkez çıkmıyor"
 * demesinin sebebi buydu: veri geliyordu, ekran okumuyordu.
 *
 * `centerType` YAZILMAZ, OKUNUR. Sunucu onu seçimden türetiyor;
 * gönderilen değer yalnız çelişki kontrolünde kullanılıyor.
 */
export const MERKEZ_TURU = {
  Sube: 0,
  Proje: 1,
  Santiye: 2,
} as const;

export type MerkezTuru = (typeof MERKEZ_TURU)[keyof typeof MERKEZ_TURU];

export type WorkTask = {
  id: string;
  companyId: string;
  projectId?: string | null;
  taskNumber: string;
  title: string;
  description?: string | null;
  priority: WorkTaskPriority;
  priorityName: string;
  status: WorkTaskStatus;
  statusName: string;
  assignedToUserId?: string | null;
  assignedByUserId?: string | null;
  startDate?: string | null;
  dueDate?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  completionNote?: string | null;
  sourceModule?: string | null;
  sourceEntityId?: string | null;
  sourceEventCode?: string | null;
  tags?: string | null;
  isOverdue: boolean;
  createdAtUtc: string;

  /** Masraf merkezi — üçünden yalnız biri dolu (şantiyede projesi de gelir). */
  branchId?: string | null;
  projectSiteId?: string | null;
  centerType?: MerkezTuru | null;

  /*
   * MERKEZ ADLARI SUNUCUDAN GELİR.
   *
   * Liste ekranı adları kendi çektiği listelerden çözebiliyordu ama
   * DETAY ekranı hiçbir liste çekmiyor. Aynı bilgi iki ayrı yoldan
   * üretilseydi ikisi bir gün ayrışırdı — tek kaynak sunucu.
   */
  projectName?: string | null;
  branchName?: string | null;
  projectSiteName?: string | null;

  /*
   * ÇİFT ADIMLI KAPANIŞ İZİ.
   *
   * Yapanın "bitti" demesi görevi kapatmaz; gönderen onaylayınca
   * kapanır ya da gerekçeyle iade eder. Bu alanlar olmadan ekran
   * "tamamlandı" ile "onaylandı"yı ayırt edemez.
   */
  approvedAtUtc?: string | null;
  approvedByUserId?: string | null;
  returnedAtUtc?: string | null;
  returnReason?: string | null;

  /** Üçüncü kez iade edilen iş, tek seferde bitenle aynı görünmemeli. */
  returnCount: number;

  delegatedFromUserId?: string | null;
  delegatedAtUtc?: string | null;
  delegationCount: number;

  /*
   * ADLAR SUNUCUDAN GELİYOR, TEK SORGUDA.
   *
   * Ekran kimlikten ada kendi çevirseydi satır başına bir istek
   * atardı. Ad çözülemezse "(bilinmeyen kullanıcı)" gelir — boş
   * değil: yazarsız görünen bir kayıt, arızayı gizler.
   */
  assignedToName?: string | null;
  assignedByName?: string | null;
  approvedByName?: string | null;
  delegatedFromName?: string | null;
};

export type WorkTaskDashboard = {
  totalOpen: number;
  assignedToMe: number;
  dueToday: number;
  overdue: number;
  critical: number;
  completedToday: number;
};

export type CreateWorkTaskRequest = {
  companyId: string;
  projectId?: string | null;
  title: string;
  description?: string | null;
  priority: WorkTaskPriority;
  assignedToUserId?: string | null;
  startDate?: string | null;
  dueDate?: string | null;
  sourceModule?: string | null;
  sourceEntityId?: string | null;
  sourceEventCode?: string | null;
  tags?: string | null;

  /** Masraf merkezi. `centerType` sunucuda seçimden türetilir. */
  branchId?: string | null;
  projectSiteId?: string | null;
  centerType?: MerkezTuru | null;
};

export type UpdateWorkTaskRequest = {
  title: string;
  description?: string | null;
  priority: WorkTaskPriority;
  assignedToUserId?: string | null;
  startDate?: string | null;
  dueDate?: string | null;
  tags?: string | null;

  /*
   * MERKEZ PUT'TA DA VAR.
   *
   * Önce yoktu: merkez yalnız oluşturmada konabiliyor, yanlış konmuşsa
   * bir daha düzeltilemiyordu. Doğrulama POST ile aynı metotta.
   */
  projectId?: string | null;
  branchId?: string | null;
  projectSiteId?: string | null;
  centerType?: MerkezTuru | null;
};

export type WorkTaskFilters = {
  companyId?: string;
  projectId?: string;
  assignedToUserId?: string;
  status?: number;
  priority?: number;
  overdueOnly?: boolean;
};

function buildQuery(filters?: WorkTaskFilters) {
  const params = new URLSearchParams();

  if (filters?.companyId) {
    params.set("companyId", filters.companyId);
  }

  if (filters?.projectId) {
    params.set("projectId", filters.projectId);
  }

  if (filters?.assignedToUserId) {
    params.set(
      "assignedToUserId",
      filters.assignedToUserId
    );
  }

  if (filters?.status !== undefined) {
    params.set("status", String(filters.status));
  }

  if (filters?.priority !== undefined) {
    params.set("priority", String(filters.priority));
  }

  if (filters?.overdueOnly !== undefined) {
    params.set(
      "overdueOnly",
      String(filters.overdueOnly)
    );
  }

  const query = params.toString();

  return query ? `?${query}` : "";
}

/**
 * GÖREV LİSTESİ ZARFI — sunucunun döndürdüğü şekil.
 *
 * `nextCursor` imleç tabanlı sayfalama için; bugün ekran onu
 * kullanmıyor ama sözleşmede duruyor çünkü sunucu gönderiyor ve
 * tipin gerçeği yansıtması gerekiyor.
 */
export type WorkTaskSayfasi = {
  items: WorkTask[];
  hasMore: boolean;
  nextCursor: { createdAtUtc: string; id: string } | null;
};

export const workTaskService = {
  /**
   * GÖREV LİSTESİ — ZARF DÖNER, DÜZ DİZİ DEĞİL.
   *
   * Uç `{ items, hasMore, nextCursor }` döndürüyor. Burada
   * `WorkTask[]` bekleniyordu ve ekran ilk `.slice` çağrısında
   * `TypeError: M.slice is not a function` ile ÇÖKÜYORDU —
   * `/gorevler` hiç açılmıyordu.
   *
   * "WorkTasks 1 kayıt" tablosunun sebebi buydu: bulunabilirlik
   * değil, ekranın açılmaması.
   */
  getAll(filters?: WorkTaskFilters) {
    return apiClient<WorkTaskSayfasi>(
      `tasks${buildQuery(filters)}`
    );
  },

  getById(id: string) {
    return apiClient<WorkTask>(`tasks/${id}`);
  },

  getDashboard() {
    return apiClient<WorkTaskDashboard>(
      "tasks/dashboard"
    );
  },

  create(request: CreateWorkTaskRequest) {
    return apiClient<WorkTask>("tasks", {
      method: "POST",
      body: request,
    });
  },

  update(
    id: string,
    request: UpdateWorkTaskRequest
  ) {
    return apiClient<WorkTask>(
      `tasks/${id}`,
      {
        method: "PUT",
        body: request,
      }
    );
  },

  start(id: string) {
    return apiClient<WorkTask>(
      `tasks/${id}/start`,
      {
        method: "POST",
      }
    );
  },

  complete(
    id: string,
    completionNote?: string | null
  ) {
    return apiClient<WorkTask>(
      `tasks/${id}/complete`,
      {
        method: "POST",
        body: {
          completionNote:
            completionNote?.trim() || null,
        },
      }
    );
  },

  /**
   * ONAY — YALNIZ GÖNDEREN.
   *
   * Başkası onaylasaydı çift adımlı kapanış tören olurdu: işi
   * isteyen kişi sonucu görmeden görev kapanırdı. Kural uçta;
   * ekran yalnızca düğmeyi doğru kişiye gösteriyor.
   */
  approve(id: string) {
    return apiClient<WorkTask>(`tasks/${id}/approve`, {
      method: "POST",
    });
  },

  /**
   * İADE — GEREKÇE ZORUNLU.
   *
   * Gerekçesiz iade, yapan kişiye neyi düzelteceğini söylemez ve
   * aynı işin ikinci kez aynı eksikle gelmesine yol açar.
   * Termin KORUNUR: iade edilen görev yeniden açılır, terminini
   * geçmişse hemen gecikmiş görünür.
   */
  returnTask(id: string, reason: string) {
    return apiClient<WorkTask>(`tasks/${id}/return`, {
      method: "POST",
      body: { reason },
    });
  },

  cancel(id: string, reason: string) {
    return apiClient<WorkTask>(
      `tasks/${id}/cancel`,
      {
        method: "POST",
        body: {
          reason,
        },
      }
    );
  },
};
