import { apiClient } from "@/lib/api/api-client";

export enum VisitorStatus {
  Expected = 0,
  CheckedIn = 1,
  CheckedOut = 2,
  Cancelled = 3,
  Rejected = 4,
}

export type VisitorItem = {
  id: string;
  companyId: string;
  projectId?: string | null;
  fullName: string;
  identityNumber?: string | null;
  phoneNumber?: string | null;
  email?: string | null;
  companyName?: string | null;
  vehiclePlate?: string | null;
  visitorCardNumber?: string | null;
  personToVisit: string;
  departmentName?: string | null;
  visitPurpose: string;
  plannedVisitAtUtc: string;
  checkInAtUtc?: string | null;
  checkOutAtUtc?: string | null;
  approvedByName?: string | null;
  receivedByName?: string | null;
  description?: string | null;
  status: VisitorStatus;
  statusName: string;
  createdAtUtc: string;
};

export type VisitorFilters = {
  companyId?: string;
  projectId?: string;
  status?: number;
  startDate?: string;
  endDate?: string;
  search?: string;
};

export type CreateVisitorRequest = {
  companyId: string;
  projectId?: string | null;
  fullName: string;
  identityNumber?: string | null;
  phoneNumber?: string | null;
  email?: string | null;
  companyName?: string | null;
  vehiclePlate?: string | null;
  visitorCardNumber?: string | null;
  personToVisit: string;
  departmentName?: string | null;
  visitPurpose: string;
  plannedVisitAtUtc: string;
  approvedByName?: string | null;
  description?: string | null;
};

function buildQuery(filters?: VisitorFilters) {
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

  if (filters?.startDate) {
    params.set("startDate", filters.startDate);
  }

  if (filters?.endDate) {
    params.set("endDate", filters.endDate);
  }

  if (filters?.search?.trim()) {
    params.set("search", filters.search.trim());
  }

  const query = params.toString();

  return query ? `?${query}` : "";
}

export const visitorService = {
  getAll(filters?: VisitorFilters) {
    return apiClient<VisitorItem[]>(
      `secretariat/visitors${buildQuery(filters)}`
    );
  },

  create(request: CreateVisitorRequest) {
    return apiClient<VisitorItem>(
      "secretariat/visitors",
      {
        method: "POST",
        body: request,
      }
    );
  },

  checkIn(id: string, receivedByName?: string | null) {
    return apiClient<VisitorItem>(
      `secretariat/visitors/${id}/check-in`,
      {
        method: "POST",
        body: {
          receivedByName: receivedByName || null,
        },
      }
    );
  },

  checkOut(id: string) {
    return apiClient<VisitorItem>(
      `secretariat/visitors/${id}/check-out`,
      {
        method: "POST",
      }
    );
  },

  delete(id: string) {
    return apiClient<{ message: string }>(
      `secretariat/visitors/${id}`,
      {
        method: "DELETE",
      }
    );
  },
};
