import { apiClient } from "@/lib/api/api-client";

export enum WorkTaskPriority {
  Low = 0,
  Normal = 1,
  High = 2,
  Critical = 3,
}

export enum WorkTaskStatus {
  Draft = 0,
  Open = 1,
  InProgress = 2,
  Waiting = 3,
  Completed = 4,
  Cancelled = 5,
}

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
  startDate?: string | null;
  dueDate?: string | null;
  completedAtUtc?: string | null;
  completionNote?: string | null;
  cancellationReason?: string | null;
  sourceModule?: string | null;
  sourceEntityId?: string | null;
  sourceEventCode?: string | null;
  tags?: string | null;
  isOverdue: boolean;
  createdAtUtc: string;
};

export type WorkTaskDashboard = {
  totalOpen: number;
  assignedToMe: number;
  dueToday: number;
  overdue: number;
  critical: number;
  completedToday: number;
};

export type WorkTaskFilters = {
  companyId?: string;
  projectId?: string;
  status?: number;
  priority?: number;
  overdueOnly?: boolean;
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
};

function buildQuery(filters?: WorkTaskFilters) {
  const params = new URLSearchParams();

  if (filters?.companyId) params.set("companyId", filters.companyId);
  if (filters?.projectId) params.set("projectId", filters.projectId);
  if (filters?.status !== undefined) {
    params.set("status", String(filters.status));
  }
  if (filters?.priority !== undefined) {
    params.set("priority", String(filters.priority));
  }
  if (filters?.overdueOnly) params.set("overdueOnly", "true");

  const query = params.toString();
  return query ? `?${query}` : "";
}

export const workTaskService = {
  getAll(filters?: WorkTaskFilters) {
    return apiClient<WorkTask[]>(`work-tasks${buildQuery(filters)}`);
  },

  getDashboard() {
    return apiClient<WorkTaskDashboard>("work-tasks/dashboard");
  },

  create(request: CreateWorkTaskRequest) {
    return apiClient<WorkTask>("work-tasks", {
      method: "POST",
      body: request,
    });
  },

  start(id: string) {
    return apiClient<WorkTask>(`work-tasks/${id}/start`, {
      method: "POST",
    });
  },

  complete(id: string, note?: string | null) {
    return apiClient<WorkTask>(`work-tasks/${id}/complete`, {
      method: "POST",
      body: { note: note ?? null },
    });
  },

  cancel(id: string, reason: string) {
    return apiClient<WorkTask>(`work-tasks/${id}/cancel`, {
      method: "POST",
      body: { reason },
    });
  },
};
