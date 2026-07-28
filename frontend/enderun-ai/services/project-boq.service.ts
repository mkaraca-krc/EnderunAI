import { apiClient } from "@/lib/api/api-client";

export enum ProjectBoqStatus {
  Draft = 0,
  Approved = 1,
  Superseded = 2,
  Archived = 3,
}

export enum ProjectBoqItemType {
  Mixed = 0,
  Material = 1,
  Labor = 2,
}

export interface ProjectBoqItemRequest {
  engineeringPositionId?: string | null;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  unitPrice: number;
  itemType: ProjectBoqItemType;
  category?: string | null;
  notes?: string | null;
}

export interface CreateProjectBoqRequest {
  companyId: string;
  projectId: string;
  boqNumber: string;
  name: string;
  revisionNumber: number;
  currencyCode: string;
  description?: string | null;
  notes?: string | null;
  items: ProjectBoqItemRequest[];
}

export interface ProjectBoqListItem {
  id: string;
  companyId: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  boqNumber: string;
  name: string;
  revisionNumber: number;
  revisionCode: string;
  status: ProjectBoqStatus;
  isCurrentRevision: boolean;
  currencyCode: string;
  totalAmount: number;
  itemCount: number;
  createdAtUtc: string;
}

export interface ProjectBoqItem {
  id: string;
  engineeringPositionId?: string | null;
  lineNumber: number;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  unitPrice: number;
  totalAmount: number;
  itemType: ProjectBoqItemType;
  category?: string | null;
  notes?: string | null;
}

export interface ProjectBoqDetail extends ProjectBoqListItem {
  description?: string | null;
  notes?: string | null;
  approvedAtUtc?: string | null;
  items: ProjectBoqItem[];
}

export const projectBoqService = {
  getAll(filters?: {
    companyId?: string;
    projectId?: string;
    status?: number;
  }) {
    const params = new URLSearchParams();

    if (filters?.companyId) {
      params.set("companyId", filters.companyId);
    }

    if (filters?.projectId) {
      params.set("projectId", filters.projectId);
    }

    if (filters?.status !== undefined) {
      params.set("status", String(filters.status));
    }

    const query = params.toString();

    return apiClient<ProjectBoqListItem[]>(
      `project-boqs${query ? `?${query}` : ""}`
    );
  },

  getById(id: string) {
    return apiClient<ProjectBoqDetail>(
      `project-boqs/${id}`
    );
  },

  create(request: CreateProjectBoqRequest) {
    return apiClient<{
      id: string;
      boqNumber: string;
      revisionNumber: number;
      status: ProjectBoqStatus;
      totalAmount: number;
    }>("project-boqs", {
      method: "POST",
      body: request,
    });
  },

  approve(id: string) {
    return apiClient<{
      id: string;
      boqNumber: string;
      revisionNumber: number;
      status: ProjectBoqStatus;
      message: string;
    }>(`project-boqs/${id}/approve`, {
      method: "POST",
    });
  },

  archive(id: string) {
    return apiClient<{
      id: string;
      boqNumber: string;
      revisionNumber: number;
      status: ProjectBoqStatus;
      message: string;
    }>(`project-boqs/${id}/archive`, {
      method: "POST",
    });
  },

  remove(id: string) {
    return apiClient<void>(
      `project-boqs/${id}`,
      {
        method: "DELETE",
      }
    );
  },
};
