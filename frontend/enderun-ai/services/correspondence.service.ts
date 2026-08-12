import { apiClient } from "@/lib/api/api-client";

export enum CorrespondenceDirection {
  Incoming = 0,
  Outgoing = 1,
}

export enum CorrespondenceStatus {
  Draft = 0,
  Registered = 1,
  Assigned = 2,
  InProgress = 3,
  Answered = 4,
  Completed = 5,
  Archived = 6,
  Cancelled = 7,
}

export enum CorrespondencePriority {
  Low = 0,
  Normal = 1,
  High = 2,
  Urgent = 3,
}

export type CorrespondenceItem = {
  id: string;
  companyId: string;
  projectId?: string | null;
  categoryId?: string | null;
  direction: CorrespondenceDirection;
  directionName: string;
  documentNumber: string;
  externalDocumentNumber?: string | null;
  documentDate: string;
  registrationDate: string;
  subject: string;
  senderName?: string | null;
  recipientName?: string | null;
  institutionName?: string | null;
  deliveryMethod?: string | null;
  referenceNumber?: string | null;
  description?: string | null;
  priority: CorrespondencePriority;
  priorityName: string;
  status: CorrespondenceStatus;
  statusName: string;
  assignedToName?: string | null;
  dueDate?: string | null;
  attachmentCount: number;
  createdAtUtc: string;
};

export type CreateCorrespondenceRequest = {
  companyId: string;
  projectId?: string | null;
  direction: CorrespondenceDirection;
  documentNumber?: string | null;
  documentDate: string;
  registrationDate?: string | null;
  subject: string;
  senderName?: string | null;
  recipientName?: string | null;
  institutionName?: string | null;
  deliveryMethod?: string | null;
  referenceNumber?: string | null;
  description?: string | null;
  categoryId?: string | null;
  priority?: CorrespondencePriority;
  assignedToUserId?: string | null;
  assignedToName?: string | null;
  dueDate?: string | null;
  signedByName?: string | null;
  notes?: string | null;
};

export type CorrespondenceFilters = {
  companyId?: string;
  projectId?: string;
  direction?: number;
  status?: number;
  search?: string;
  startDate?: string;
  endDate?: string;
};

function buildQuery(filters?: CorrespondenceFilters) {
  const params = new URLSearchParams();
  if (filters?.companyId) params.set("companyId", filters.companyId);
  if (filters?.projectId) params.set("projectId", filters.projectId);
  if (filters?.direction !== undefined) params.set("direction", String(filters.direction));
  if (filters?.status !== undefined) params.set("status", String(filters.status));
  if (filters?.search?.trim()) params.set("search", filters.search.trim());
  if (filters?.startDate) params.set("startDate", filters.startDate);
  if (filters?.endDate) params.set("endDate", filters.endDate);
  const query = params.toString();
  return query ? `?${query}` : "";
}

function directionQuery(direction: CorrespondenceDirection) {
  return `?direction=${direction}`;
}

/** Evrak akışındaki bir adım. */
export type CorrespondenceWorkflowStep = {
  id: string;
  action: number;
  actionName: string;
  fromUserName?: string | null;
  toUserName?: string | null;
  description?: string | null;
  actionAtUtc: string;
};

export type CorrespondenceAttachment = {
  id: string;
  direction: CorrespondenceDirection;
  documentId: string;
  fileName: string;
  storedFileName: string;
  filePath: string;
  contentType?: string | null;
  fileSize: number;
  description?: string | null;
  createdAtUtc: string;
};

/** Evrakın kendisi + ekleri + akış geçmişi. */
export type CorrespondenceDetail = {
  document: CorrespondenceItem;
  attachments: CorrespondenceAttachment[];
  workflow: CorrespondenceWorkflowStep[];
};

/**
 * Akış eylemleri — `SecretariatWorkflowAction` ile birebir.
 *
 * Created ve Archived ELLE seçilemez: birincisi kayıt açılırken,
 * ikincisi arşivleme ucundan otomatik yazılıyor. Listeye konsaydı
 * kullanıcı elle "oluşturuldu" adımı ekleyip geçmişi bozabilirdi.
 */
export const CORRESPONDENCE_WORKFLOW_ACTIONS: {
  value: number;
  label: string;
}[] = [
  { value: 1, label: "Kaydedildi" },
  { value: 2, label: "Havale edildi" },
  { value: 3, label: "Okundu" },
  { value: 4, label: "Görüş yazıldı" },
  { value: 5, label: "Cevaplandı" },
  { value: 6, label: "Tamamlandı" },
  { value: 8, label: "Yeniden açıldı" },
  { value: 9, label: "İptal edildi" },
];

/** Ek dosya indirme adresi — tarayıcı doğrudan açar. */
export function correspondenceAttachmentUrl(attachmentId: string) {
  return `/api/backend/secretariat/attachments/${attachmentId}/download`;
}

export const correspondenceService = {
  getAll(filters?: CorrespondenceFilters) {
    return apiClient<CorrespondenceItem[]>(
      `secretariat/correspondence${buildQuery(filters)}`
    );
  },

  create(request: CreateCorrespondenceRequest) {
    return apiClient<CorrespondenceItem>("secretariat/correspondence", {
      method: "POST",
      body: {
        ...request,
        priority: request.priority ?? CorrespondencePriority.Normal,
      },
    });
  },

  archive(id: string, direction: CorrespondenceDirection) {
    return apiClient(
      `secretariat/correspondence/${id}/archive${directionQuery(direction)}`,
      { method: "POST" }
    );
  },

  delete(id: string, direction: CorrespondenceDirection) {
    return apiClient<{ message: string }>(
      `secretariat/correspondence/${id}${directionQuery(direction)}`,
      { method: "DELETE" }
    );
  },

  /** Evrak detayı: ekler ve akış geçmişi birlikte gelir. */
  getById(id: string, direction: CorrespondenceDirection) {
    return apiClient<CorrespondenceDetail>(
      `secretariat/correspondence/${id}${directionQuery(direction)}`
    );
  },

  /** Akışa bir adım ekler; güncel detayı geri döner. */
  addWorkflow(
    id: string,
    direction: CorrespondenceDirection,
    request: {
      action: number;
      toUserId?: string | null;
      toUserName?: string | null;
      description?: string | null;
    }
  ) {
    return apiClient<CorrespondenceDetail>(
      `secretariat/correspondence/${id}/workflow${directionQuery(direction)}`,
      {
        method: "POST",
        body: {
          action: request.action,
          toUserId: request.toUserId ?? null,
          toUserName: request.toUserName?.trim() || null,
          description: request.description?.trim() || null,
        },
      }
    );
  },

  /**
   * Ek dosya yükler.
   *
   * apiClient KULLANILMIYOR: gövdeyi JSON'a çeviriyor ve
   * Content-Type'ı kendisi koyuyor. Dosya yüklemede gövde FormData
   * olmalı ve sınırı (boundary) tarayıcı yazmalı — elle
   * Content-Type verilirse sunucu form alanlarını ayrıştıramaz.
   */
  async addAttachment(
    id: string,
    direction: CorrespondenceDirection,
    file: File,
    description?: string | null
  ) {
    const form = new FormData();
    form.append("file", file);
    if (description?.trim()) form.append("description", description.trim());

    const response = await fetch(
      `/api/backend/secretariat/correspondence/${id}/attachments${directionQuery(direction)}`,
      { method: "POST", body: form, cache: "no-store" }
    );

    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      throw new Error(
        (payload as { message?: string } | null)?.message ??
          `Ek dosya yüklenemedi: ${response.status}`
      );
    }

    return payload as CorrespondenceAttachment;
  },

  deleteAttachment(attachmentId: string) {
    return apiClient<{ message: string }>(
      `secretariat/attachments/${attachmentId}`,
      { method: "DELETE" }
    );
  },
};
