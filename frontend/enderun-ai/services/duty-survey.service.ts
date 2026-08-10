import { apiClient } from "@/lib/api/api-client";

export type SurveyMeasurement = {
  id?: string;
  description: string;
  quantity?: number | null;
  unit?: string | null;
  note?: string | null;
};

export type SurveyReport = {
  id: string;
  dutyId: string;
  projectId: string;
  reportDate: string;
  summary: string;
  siteConditions?: string | null;
  accessNotes?: string | null;
  risks?: string | null;
  recommendBid?: boolean | null;
  measurements: SurveyMeasurement[];
  photos: {
    id: string;
    originalName: string;
    contentType: string;
    caption?: string | null;
  }[];
};

export type SaveSurveyReportRequest = {
  reportDate?: string | null;
  summary: string;
  siteConditions?: string | null;
  accessNotes?: string | null;
  risks?: string | null;
  recommendBid?: boolean | null;
  measurements: SurveyMeasurement[];
};

/** 1 kazanıldı · 2 kaybedildi */
export type SurveyOutcome = 1 | 2;

export type SurveyDossier = {
  project: {
    id: string;
    code: string;
    name: string;
    status: number;
    surveyOutcome: number;
    surveyOutcomeAtUtc?: string | null;
    surveyOutcomeNote?: string | null;
  };
  surveyOutcomeName: string;
  reports: {
    id: string;
    dutyId: string;
    reportDate: string;
    summary: string;
    recommendBid?: boolean | null;
    measurementCount: number;
    photoCount: number;
  }[];
};

export const dutySurveyService = {
  /** Rapor yoksa uç 404 döner; ekran bunu "henüz yazılmamış" olarak okur. */
  async getReport(dutyId: string): Promise<SurveyReport | null> {
    try {
      return await apiClient<SurveyReport>(
        `hr/gorevlendirmeler/${dutyId}/saha-raporu`
      );
    } catch {
      return null;
    }
  },

  saveReport(dutyId: string, payload: SaveSurveyReportRequest) {
    return apiClient<{ message: string; id: string }>(
      `hr/gorevlendirmeler/${dutyId}/saha-raporu`,
      { method: "PUT", body: payload }
    );
  },

  /**
   * Fotoğraf yüklemesi multipart olduğu için apiClient kullanılmıyor:
   * apiClient gövdeyi JSON'a çeviriyor.
   */
  async uploadPhoto(dutyId: string, file: File, caption?: string) {
    const form = new FormData();

    form.append("file", file);
    if (caption) form.append("caption", caption);

    const response = await fetch(
      `/api/backend/hr/gorevlendirmeler/${dutyId}/saha-raporu/fotograf`,
      { method: "POST", body: form, cache: "no-store" }
    );

    if (response.status === 401) {
      if (typeof window !== "undefined") window.location.href = "/login";
      throw new Error("Oturum süresi doldu.");
    }

    if (!response.ok) {
      const payload = await response.json().catch(() => null);

      throw new Error(
        payload && typeof payload === "object" && "message" in payload
          ? String((payload as { message?: unknown }).message)
          : "Fotoğraf yüklenemedi."
      );
    }

    return response.json() as Promise<{ id: string; message: string }>;
  },

  photoUrl(dutyId: string, photoId: string) {
    return `/api/backend/hr/gorevlendirmeler/${dutyId}/saha-raporu/fotograf/${photoId}`;
  },

  deletePhoto(dutyId: string, photoId: string) {
    return apiClient<{ message: string }>(
      `hr/gorevlendirmeler/${dutyId}/saha-raporu/fotograf/${photoId}`,
      { method: "DELETE" }
    );
  },

  getDossier(projectId: string) {
    return apiClient<SurveyDossier>(`projects/${projectId}/kesif-dosyasi`);
  },

  setOutcome(projectId: string, outcome: SurveyOutcome, note: string) {
    return apiClient<{ message: string }>(
      `projects/${projectId}/kesif-sonucu`,
      { method: "POST", body: { outcome, note } }
    );
  },
};
