import { apiClient } from "@/lib/api/api-client";

export enum CorrespondenceDirection {
  Incoming = 0,
  Outgoing = 1,
}

export enum CorrespondenceStatus {
  Draft = 0,
  Registered = 1,
  Delivered = 2,
  Archived = 3,
  Cancelled = 4,
}

export type CorrespondenceItem = {
  id: string;
  companyId: string;
  projectId?: string | null;
  direction: CorrespondenceDirection;
  directionName: string;
  documentNumber: string;
  documentDate: string;
  registrationDate: string;
  subject: string;
  senderName?: string | null;
  recipientName?: string | null;
  institutionName?: string | null;
  status: CorrespondenceStatus;
  statusName: string;
  createdAtUtc: string;
};

export type CreateCorrespondenceRequest = {
  companyId: string;
  projectId?: string | null;
  direction: CorrespondenceDirection;
  documentNumber: string;
  documentDate: string;
  registrationDate: string;
  subject: string;
  senderName?: string | null;
  recipientName?: string | null;
  institutionName?: string | null;
  deliveryMethod?: string | null;
  referenceNumber?: string | null;
  description?: string | null;
  attachmentPath?: string | null;
};

export type CorrespondenceFilters = {
  companyId?: string;
  projectId?: string;
  direction?: number;
  status?: number;
  search?: string;
};

function buildQuery(filters?: CorrespondenceFilters) {
  const params = new URLSearchParams();

  if (filters?.companyId) {
    params.set("companyId", filters.companyId);
  }

  if (filters?.projectId) {
    params.set("projectId", filters.projectId);
  }

  if (filters?.direction !== undefined) {
    params.set("direction", String(filters.direction));
  }

  if (filters?.status !== undefined) {
    params.set("status", String(filters.status));
  }

  if (filters?.search?.trim()) {
    params.set("search", filters.search.trim());
  }

  const query = params.toString();
  return query ? `?${query}` : "";
}

export const correspondenceService = {
  getAll(filters?: CorrespondenceFilters) {
    return apiClient<CorrespondenceItem[]>(
      `secretariat/correspondence${buildQuery(filters)}`
    );
  },

  create(request: CreateCorrespondenceRequest) {
    return apiClient<CorrespondenceItem>(
      "secretariat/correspondence",
      {
        method: "POST",
        body: request,
      }
    );
  },

  delete(id: string) {
    return apiClient<{ message: string }>(
      `secretariat/correspondence/${id}`,
      {
        method: "DELETE",
      }
    );
  },
};
