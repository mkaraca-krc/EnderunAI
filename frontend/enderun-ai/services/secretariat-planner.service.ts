import { apiClient } from "@/lib/api/api-client";

export enum PhoneNoteStatus {
  New = 0,
  Informed = 1,
  Returned = 2,
  Closed = 3,
  Cancelled = 4,
}

export enum ScheduleStatus {
  Planned = 0,
  Confirmed = 1,
  Completed = 2,
  Cancelled = 3,
}

export type SecretariatDashboard = {
  todayIncoming: number;
  todayOutgoing: number;
  pendingDocuments: number;
  overdueDocuments: number;
  cargoInTransit: number;
  visitorsInside: number;
  openPhoneNotes: number;
  todayMeetings: number;
  todayAppointments: number;
  recentActivities: Array<{
    module: string;
    recordId: string;
    title: string;
    action: string;
    userName?: string | null;
    actionAtUtc: string;
  }>;
};

export type PhoneNoteItem = {
  id: string;
  companyId: string;
  projectId?: string | null;
  callerName: string;
  phoneNumber?: string | null;
  institutionName?: string | null;
  subject: string;
  message: string;
  responsibleName: string;
  receivedAtUtc: string;
  informedAtUtc?: string | null;
  returnedAtUtc?: string | null;
  status: PhoneNoteStatus;
  statusName: string;
  notes?: string | null;
  createdAtUtc: string;
};

export type ScheduleItem = {
  id: string;
  companyId: string;
  projectId?: string | null;
  type: number;
  typeName: string;
  title: string;
  contactName?: string | null;
  companyName?: string | null;
  location?: string | null;
  startAtUtc: string;
  endAtUtc?: string | null;
  ownerName?: string | null;
  participants?: string | null;
  description?: string | null;
  reminderAtUtc?: string | null;
  completedAtUtc?: string | null;
  status: ScheduleStatus;
  statusName: string;
  notes?: string | null;
  createdAtUtc: string;
};

export type RegistryFilters = {
  companyId?: string;
  projectId?: string;
  status?: number;
  startDate?: string;
  endDate?: string;
  search?: string;
};

export type CreatePhoneNoteRequest = {
  companyId: string;
  projectId?: string | null;
  callerName: string;
  phoneNumber?: string | null;
  institutionName?: string | null;
  subject: string;
  message: string;
  responsibleName: string;
  receivedAtUtc?: string | null;
  notes?: string | null;
};

export type CreateScheduleRequest = {
  companyId: string;
  projectId?: string | null;
  title: string;
  contactName?: string | null;
  companyName?: string | null;
  location?: string | null;
  startAtUtc: string;
  endAtUtc?: string | null;
  ownerName?: string | null;
  participants?: string | null;
  description?: string | null;
  reminderAtUtc?: string | null;
  notes?: string | null;
};

function query(filters?: RegistryFilters) {
  const params = new URLSearchParams();
  if (filters?.companyId) params.set("companyId", filters.companyId);
  if (filters?.projectId) params.set("projectId", filters.projectId);
  if (filters?.status !== undefined) params.set("status", String(filters.status));
  if (filters?.startDate) params.set("startDate", filters.startDate);
  if (filters?.endDate) params.set("endDate", filters.endDate);
  if (filters?.search?.trim()) params.set("search", filters.search.trim());
  const value = params.toString();
  return value ? `?${value}` : "";
}

function scheduleApi(path: "meetings" | "appointments") {
  return {
    getAll(filters?: RegistryFilters) {
      return apiClient<ScheduleItem[]>(`secretariat/${path}${query(filters)}`);
    },
    create(request: CreateScheduleRequest) {
      return apiClient<ScheduleItem>(`secretariat/${path}`, {
        method: "POST",
        body: request,
      });
    },
    setStatus(id: string, status: ScheduleStatus) {
      return apiClient<ScheduleItem>(`secretariat/${path}/${id}/status`, {
        method: "POST",
        body: { status },
      });
    },
    delete(id: string) {
      return apiClient<{ message: string }>(`secretariat/${path}/${id}`, {
        method: "DELETE",
      });
    },
  };
}

export const secretariatPlannerService = {
  dashboard(companyId?: string) {
    const suffix = companyId
      ? `?companyId=${encodeURIComponent(companyId)}`
      : "";
    return apiClient<SecretariatDashboard>(`secretariat/dashboard${suffix}`);
  },

  phoneNotes: {
    getAll(filters?: RegistryFilters) {
      return apiClient<PhoneNoteItem[]>(
        `secretariat/phone-notes${query(filters)}`
      );
    },
    create(request: CreatePhoneNoteRequest) {
      return apiClient<PhoneNoteItem>("secretariat/phone-notes", {
        method: "POST",
        body: request,
      });
    },
    setStatus(id: string, status: PhoneNoteStatus) {
      return apiClient<PhoneNoteItem>(
        `secretariat/phone-notes/${id}/status`,
        {
          method: "POST",
          body: { status },
        }
      );
    },
    delete(id: string) {
      return apiClient<{ message: string }>(
        `secretariat/phone-notes/${id}`,
        { method: "DELETE" }
      );
    },
  },

  meetings: scheduleApi("meetings"),
  appointments: scheduleApi("appointments"),
};
