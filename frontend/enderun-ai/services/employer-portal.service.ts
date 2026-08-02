import { apiClient } from "@/lib/api/api-client";

export type EmployerPortalLink = {
  id: string;
  token: string;
  isActive: boolean;
  createdAtUtc: string;
  revokedAtUtc?: string | null;
} | null;

export const employerPortalService = {
  get(projectId: string) {
    return apiClient<EmployerPortalLink>(
      `projects/${projectId}/employer-portal-link`
    );
  },

  create(projectId: string) {
    return apiClient<{ message: string; id: string; token: string }>(
      `projects/${projectId}/employer-portal-link`,
      { method: "POST" }
    );
  },

  revoke(projectId: string) {
    return apiClient<{ message: string }>(
      `projects/${projectId}/employer-portal-link/revoke`,
      { method: "POST" }
    );
  },
};

export type PortalSite = {
  id: string;
  name: string;
  location?: string | null;
};

export type PortalProject = {
  projectName: string;
  projectCode: string;
  sites: PortalSite[];
};

export type PortalWorkItem = {
  description: string;
  quantity?: number | null;
  unit?: string | null;
};

export type PortalPhoto = {
  id: string;
  caption?: string | null;
};

export type PortalReport = {
  id: string;
  projectSiteId: string;
  siteName: string;
  reportDate: string;
  weatherCondition?: string | null;
  engineerCount: number;
  foremanCount: number;
  craftsmanCount: number;
  workerCount: number;
  otherCount: number;
  notes?: string | null;
  workItems: PortalWorkItem[];
  photos: PortalPhoto[];
};

async function publicGet<T>(path: string): Promise<T> {
  const response = await fetch(`/api/backend/${path}`, { cache: "no-store" });

  if (!response.ok) {
    throw new Error(
      response.status === 429
        ? "Çok fazla istek gönderildi. Lütfen biraz sonra tekrar deneyin."
        : "Portal bilgisi bulunamadı veya erişim iptal edilmiş."
    );
  }

  return (await response.json()) as T;
}

export const publicPortalService = {
  getProject(token: string) {
    return publicGet<PortalProject>(`portal/${encodeURIComponent(token)}`);
  },

  getReports(token: string, from?: string, to?: string, siteId?: string) {
    const params = new URLSearchParams();
    if (from) params.set("from", from);
    if (to) params.set("to", to);
    if (siteId) params.set("siteId", siteId);
    const query = params.toString() ? `?${params.toString()}` : "";

    return publicGet<PortalReport[]>(
      `portal/${encodeURIComponent(token)}/reports${query}`
    );
  },

  photoUrl(token: string, photoId: string) {
    return `/api/backend/portal/${encodeURIComponent(token)}/photos/${photoId}`;
  },
};
