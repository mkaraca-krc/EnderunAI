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
};

export type UpdateWorkTaskRequest = {
  title: string;
  description?: string | null;
  priority: WorkTaskPriority;
  assignedToUserId?: string | null;
  startDate?: string | null;
  dueDate?: string | null;
  tags?: string | null;
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

export const workTaskService = {
  getAll(filters?: WorkTaskFilters) {
    return apiClient<WorkTask[]>(
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
