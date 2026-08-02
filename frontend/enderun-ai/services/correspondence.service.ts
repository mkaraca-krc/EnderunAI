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
};
