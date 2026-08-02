import { apiClient } from "@/lib/api/api-client";

export type AccessRequestStatus = 0 | 1 | 2;

export type AccessRequestListItem = {
  id: string;
  userId: string;
  username: string;
  fullName: string;
  reason: string;
  status: AccessRequestStatus;
  createdAtUtc: string;
  decidedAtUtc?: string | null;
  grantedDurationMinutes?: number | null;
  rejectionReason?: string | null;
};

const root = "access-requests";

export const accessRequestService = {
  getAll(includeDecided = false) {
    return apiClient<AccessRequestListItem[]>(
      `${root}?includeDecided=${includeDecided ? "true" : "false"}`
    );
  },
  approve(id: string, durationMinutes?: number) {
    return apiClient<{ message: string }>(`${root}/${id}/approve`, {
      method: "POST",
      body: { durationMinutes: durationMinutes ?? null },
    });
  },
  reject(id: string, rejectionReason?: string) {
    return apiClient<{ message: string }>(`${root}/${id}/reject`, {
      method: "POST",
      body: { rejectionReason: rejectionReason ?? null },
    });
  },
};
