import { apiClient } from "@/lib/api/api-client";

/**
 * ORTAK YORUM VE EK DOSYA SERVİSİ.
 *
 * Bu servis HİÇBİR MODÜLÜ BİLMEZ. Yalnızca `(entityType, entityId)`
 * çiftiyle çalışır — görev, hakediş, çek, teklif ya da mal kabul
 * arasında hiçbir fark yoktur. Modüle özel bir dal eklenirse
 * ortaklık biter ve her ekran kendi kopyasını taşımaya başlar; bu
 * dosyanın tek işi o dalın hiç açılmamasıdır.
 *
 * Varlık tipleri sunucudaki `EntityContextResolver` ile aynı olmak
 * ZORUNDA: eşleşmeyen bir tip 404 döner ve kullanıcı sebebini
 * anlamaz. `CommentEntityTypeGuardTests` bu eşleşmeyi kaynak
 * taramasıyla koruyor.
 */

export type CollaborationEntityType =
  | "WorkTask"
  | "Project"
  | "Cheque"
  | "ProgressPayment"
  | "Offer"
  | "PurchaseRequest"
  | "GoodsReceipt";

export type CommentItem = {
  id: string;
  entityType: string;
  entityId: string;

  /** Gizlenmiş yorumda `null` — sunucu gövdeyi hiç göndermiyor. */
  body: string | null;
  isHidden: boolean;

  createdAtUtc: string;
  createdByUserId: string | null;

  /** Ekranda gösterilecek ad; sunucu tek sorguda çözüyor. */
  createdByName: string;

  editedAtUtc?: string | null;
  editCount: number;

  hiddenAtUtc?: string | null;
  hiddenByUserId?: string | null;
  hiddenByName?: string | null;

  mentionedUserIds?: string | null;
};

export type CommentPage = {
  items: CommentItem[];
  hasMore: boolean;
  nextCursor: { createdAtUtc: string; id: string } | null;
};

export type AttachmentItem = {
  id: string;
  entityType: string;
  entityId: string;
  originalName: string;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
  uploadedByUserId: string | null;
  uploadedByName: string;

  /**
   * Tarayıcı bu dosyayı gösterebilir mi. HEIC için `false` gelir:
   * yükleme kabul ediliyor (iPhone varsayılanı) ama Chrome ve
   * Firefox gösteremiyor. Ekran buna bakıp "indirin" der — bozuk
   * resim simgesi göstermez.
   */
  isBrowserViewable: boolean;

  downloadUrl: string;
};

function sorgu(entityType: CollaborationEntityType, entityId: string) {
  return `entityType=${encodeURIComponent(entityType)}&entityId=${encodeURIComponent(entityId)}`;
}

export const collaborationService = {
  async listComments(
    entityType: CollaborationEntityType,
    entityId: string,
    cursor?: { createdAtUtc: string; id: string } | null,
    pageSize = 50
  ): Promise<CommentPage> {
    const parcalar = [sorgu(entityType, entityId), `pageSize=${pageSize}`];

    // KEYSET: yorum sayısı görevden de hızlı büyür; sayfa numarası
    // değil imleç kullanılıyor.
    if (cursor) {
      parcalar.push(
        `cursorCreatedAtUtc=${encodeURIComponent(cursor.createdAtUtc)}`,
        `cursorId=${encodeURIComponent(cursor.id)}`
      );
    }

    return apiClient<CommentPage>(`collaboration/comments?${parcalar.join("&")}`);
  },

  async addComment(
    entityType: CollaborationEntityType,
    entityId: string,
    body: string,
    mentionedUserIds?: string[]
  ): Promise<CommentItem> {
    return apiClient<CommentItem>("collaboration/comments", {
      method: "POST",
      body: {
        entityType,
        entityId,
        body,
        mentionedUserIds: mentionedUserIds?.length ? mentionedUserIds : undefined,
      },
    });
  },

  async editComment(id: string, body: string): Promise<CommentItem> {
    return apiClient<CommentItem>(`collaboration/comments/${id}`, {
      method: "PUT",
      body: { body },
    });
  },

  /**
   * YORUM SİLİNMEZ, GİZLENİR. Silme, cevap verilmiş bir cümleyi
   * konuşmadan çıkarır ve kalan cevapları anlamsızlaştırır.
   */
  async hideComment(id: string): Promise<CommentItem> {
    return apiClient<CommentItem>(`collaboration/comments/${id}/hide`, {
      method: "POST",
    });
  },

  async listAttachments(
    entityType: CollaborationEntityType,
    entityId: string
  ): Promise<AttachmentItem[]> {
    return apiClient<AttachmentItem[]>(
      `collaboration/attachments?${sorgu(entityType, entityId)}`
    );
  },

  /**
   * apiClient KULLANILMIYOR: gövdeyi JSON'a çeviriyor ve
   * Content-Type'ı kendisi koyuyor. Dosya yüklemede gövde FormData
   * olmalı ve sınırı (boundary) tarayıcı yazmalı — elle
   * Content-Type verilirse sunucu form alanlarını ayrıştıramaz.
   */
  async uploadAttachment(
    entityType: CollaborationEntityType,
    entityId: string,
    file: File
  ): Promise<AttachmentItem> {
    const form = new FormData();
    form.append("entityType", entityType);
    form.append("entityId", entityId);
    form.append("file", file);

    const response = await fetch("/api/backend/collaboration/attachments", {
      method: "POST",
      body: form,
      cache: "no-store",
    });

    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      throw new Error(
        (payload as { message?: string } | null)?.message ??
          `Dosya yüklenemedi: ${response.status}`
      );
    }

    return payload as AttachmentItem;
  },
};

/** Bayt sayısını okunur boyuta çevirir. */
export function dosyaBoyutu(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
