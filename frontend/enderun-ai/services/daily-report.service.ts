import { apiClient } from "@/lib/api/api-client";

export type DailyReportListItem = {
  id: string;
  reportDate: string;
  weatherCondition?: string | null;
  totalHeadcount: number;
  workItemCount: number;
  photoCount: number;
};

export type DailyReportWorkItem = {
  id?: string;
  description: string;
  quantity?: number | null;
  unit?: string | null;
};

export type DailyReportPhoto = {
  id: string;
  originalName: string;
  caption?: string | null;
  isVisibleToEmployer: boolean;
  createdAtUtc: string;
};

export type DailyReportDetail = {
  id: string;
  projectSiteId: string;
  reportDate: string;
  weatherCondition?: string | null;
  engineerCount: number;
  foremanCount: number;
  craftsmanCount: number;
  workerCount: number;
  otherCount: number;
  notes?: string | null;
  workItems: DailyReportWorkItem[];
  photos: DailyReportPhoto[];
};

export type SuggestedHeadcount = {
  engineerCount: number;
  foremanCount: number;
  craftsmanCount: number;
  workerCount: number;
  otherCount: number;
};

export type UpsertDailyReportRequest = {
  reportDate: string;
  weatherCondition?: string | null;
  engineerCount: number;
  foremanCount: number;
  craftsmanCount: number;
  workerCount: number;
  otherCount: number;
  notes?: string | null;
  workItems: DailyReportWorkItem[];
};

export const dailyReportService = {
  getAll(siteId: string, from?: string, to?: string) {
    const params = new URLSearchParams();
    if (from) params.set("from", from);
    if (to) params.set("to", to);
    const query = params.toString() ? `?${params.toString()}` : "";

    return apiClient<DailyReportListItem[]>(
      `project-sites/${siteId}/daily-reports${query}`
    );
  },

  getByDate(siteId: string, date: string) {
    return apiClient<DailyReportDetail>(
      `project-sites/${siteId}/daily-reports/by-date/${date}`
    );
  },

  getSuggestedHeadcount(siteId: string, date: string) {
    return apiClient<SuggestedHeadcount>(
      `project-sites/${siteId}/daily-reports/suggested-headcount?date=${date}`
    );
  },

  create(siteId: string, payload: UpsertDailyReportRequest) {
    return apiClient<{ message: string; id: string; existingReportId?: string }>(
      `project-sites/${siteId}/daily-reports`,
      { method: "POST", body: payload }
    );
  },

  update(siteId: string, reportId: string, payload: UpsertDailyReportRequest) {
    return apiClient<{ message: string }>(
      `project-sites/${siteId}/daily-reports/${reportId}`,
      { method: "PUT", body: payload }
    );
  },

  async uploadPhoto(
    siteId: string,
    reportId: string,
    file: File,
    isVisibleToEmployer: boolean,
    caption?: string
  ) {
    const formData = new FormData();
    formData.append("file", file);
    formData.append("isVisibleToEmployer", String(isVisibleToEmployer));
    if (caption) formData.append("caption", caption);

    const response = await fetch(
      `/api/backend/project-sites/${siteId}/daily-reports/${reportId}/photos`,
      { method: "POST", credentials: "include", body: formData }
    );

    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      throw new Error(payload?.message ?? "Fotoğraf yüklenemedi.");
    }

    return payload as { message: string; id: string; isVisibleToEmployer: boolean };
  },

  setPhotoVisibility(
    siteId: string,
    reportId: string,
    photoId: string,
    isVisibleToEmployer: boolean
  ) {
    return apiClient<{ message: string; isVisibleToEmployer: boolean }>(
      `project-sites/${siteId}/daily-reports/${reportId}/photos/${photoId}/visibility?isVisibleToEmployer=${isVisibleToEmployer}`,
      { method: "PATCH" }
    );
  },

  deletePhoto(siteId: string, reportId: string, photoId: string) {
    return apiClient<void>(
      `project-sites/${siteId}/daily-reports/${reportId}/photos/${photoId}`,
      { method: "DELETE" }
    );
  },

  photoUrl(siteId: string, reportId: string, photoId: string) {
    return `/api/backend/project-sites/${siteId}/daily-reports/${reportId}/photos/${photoId}`;
  },
};
