import { apiClient } from "@/lib/api/api-client";

export enum ProjectMeasurementStatus {
  Draft = 0,
  PendingApproval = 1,
  Approved = 2,
  TransferredToProgressPayment = 3,
  Cancelled = 4,
}

export interface ProjectMeasurementItemRequest {
  projectBoqItemId: string;
  currentQuantity: number;
  measurementReference?: string | null;
  location?: string | null;
  block?: string | null;
  floor?: string | null;
  room?: string | null;
  notes?: string | null;
}

export interface CreateProjectMeasurementRequest {
  companyId: string;
  projectId: string;
  projectBoqId: string;
  measurementNumber: string;
  measurementDate: string;
  description?: string | null;
  notes?: string | null;
  items: ProjectMeasurementItemRequest[];
}

export interface UpdateProjectMeasurementRequest {
  measurementDate: string;
  description?: string | null;
  notes?: string | null;
  items: ProjectMeasurementItemRequest[];
}

export interface ProjectMeasurementListItem {
  id: string;
  companyId: string;
  projectId: string;
  projectBoqId: string;
  projectCode: string;
  projectName: string;
  boqNumber: string;
  measurementNumber: string;
  measurementDate: string;
  status: ProjectMeasurementStatus;
  currencyCode: string;
  totalAmount: number;
  itemCount: number;
  createdAtUtc: string;
}

export interface ProjectMeasurementItem {
  id: string;
  projectBoqItemId: string;
  engineeringPositionId?: string | null;
  lineNumber: number;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  previousQuantity: number;
  currentQuantity: number;
  cumulativeQuantity: number;
  remainingQuantity: number;
  unitPrice: number;
  currentAmount: number;
  cumulativeAmount: number;
  completionRate: number;
  measurementReference?: string | null;
  location?: string | null;
  block?: string | null;
  floor?: string | null;
  room?: string | null;
  notes?: string | null;
}

export interface ProjectMeasurementDetail {
  id: string;
  companyId: string;
  projectId: string;
  projectBoqId: string;
  projectCode: string;
  projectName: string;
  boqNumber: string;
  measurementNumber: string;
  measurementDate: string;
  status: ProjectMeasurementStatus;
  currencyCode: string;
  totalAmount: number;
  description?: string | null;
  notes?: string | null;
  cancellationReason?: string | null;
  submittedAtUtc?: string | null;
  approvedAtUtc?: string | null;
  transferredAtUtc?: string | null;
  progressPaymentId?: string | null;
  items: ProjectMeasurementItem[];
}

export interface ProjectMeasurementActionResponse {
  id: string;
  measurementNumber: string;
  status: ProjectMeasurementStatus;
  message: string;
}

export const projectMeasurementService = {
  getAll(filters?: {
    companyId?: string;
    projectId?: string;
    projectBoqId?: string;
    status?: number;
  }) {
    const params = new URLSearchParams();

    if (filters?.companyId) {
      params.set("companyId", filters.companyId);
    }

    if (filters?.projectId) {
      params.set("projectId", filters.projectId);
    }

    if (filters?.projectBoqId) {
      params.set("projectBoqId", filters.projectBoqId);
    }

    if (filters?.status !== undefined) {
      params.set("status", String(filters.status));
    }

    const query = params.toString();

    return apiClient<ProjectMeasurementListItem[]>(
      `project-measurements${query ? `?${query}` : ""}`
    );
  },

  getById(id: string) {
    return apiClient<ProjectMeasurementDetail>(
      `project-measurements/${id}`
    );
  },

  create(request: CreateProjectMeasurementRequest) {
    return apiClient<{
      id: string;
      measurementNumber: string;
      status: ProjectMeasurementStatus;
      totalAmount: number;
    }>("project-measurements", {
      method: "POST",
      body: request,
    });
  },

  update(
    id: string,
    request: UpdateProjectMeasurementRequest
  ) {
    return apiClient<ProjectMeasurementDetail>(
      `project-measurements/${id}`,
      {
        method: "PUT",
        body: request,
      }
    );
  },

  remove(id: string) {
    return apiClient<void>(
      `project-measurements/${id}`,
      {
        method: "DELETE",
      }
    );
  },

  submit(id: string) {
    return apiClient<ProjectMeasurementActionResponse>(
      `project-measurements/${id}/submit`,
      {
        method: "POST",
      }
    );
  },

  approve(id: string) {
    return apiClient<ProjectMeasurementActionResponse>(
      `project-measurements/${id}/approve`,
      {
        method: "POST",
      }
    );
  },

  cancel(id: string, reason: string) {
    return apiClient<ProjectMeasurementActionResponse>(
      `project-measurements/${id}/cancel`,
      {
        method: "POST",
        body: {
          reason,
        },
      }
    );
  },
};
