import { apiClient } from "@/lib/api/api-client";

/**
 * Bağlantı durumu SUNUCUDAN gelir, burada hesaplanmaz.
 *
 * "Süresi geçti mi" kararı tarayıcının saatine bırakılsaydı, saati
 * geri alınmış bir makinede bağlantı geçerli görünürdü. Ekranda
 * görünen durum ile ucun uyguladığı kural aynı yerden gelmeli.
 */
export type EmployerPortalLinkStatusCode =
  | "aktif"
  | "yaklasiyor"
  | "suresi_gecti"
  | "iptal";

export type EmployerPortalLink = {
  id: string;
  token: string;
  isActive: boolean;
  createdAtUtc: string;
  revokedAtUtc?: string | null;
  employerName?: string | null;
  employerEmail?: string | null;
  expiresAtUtc: string;
  lastAccessedAtUtc?: string | null;
  accessCount: number;
  lastExtendedAtUtc?: string | null;
  extensionCount: number;
  durum: EmployerPortalLinkStatusCode;
  kalanGun: number;
} | null;

export type EmployerPortalLinkStatus = {
  link: EmployerPortalLink;
  emailConfigured: boolean;
};

export type EmployerPortalEmailLogItem = {
  id: string;
  recipientEmail: string;
  recipientName?: string | null;
  sentAtUtc: string;
  isSuccess: boolean;
  errorMessage?: string | null;
};

export const employerPortalService = {
  get(projectId: string) {
    return apiClient<EmployerPortalLinkStatus>(
      `projects/${projectId}/employer-portal-link`
    );
  },

  create(projectId: string) {
    return apiClient<{ message: string; id: string; token: string }>(
      `projects/${projectId}/employer-portal-link`,
      { method: "POST" }
    );
  },

  revoke(projectId: string, reason?: string) {
    return apiClient<{ message: string }>(
      `projects/${projectId}/employer-portal-link/revoke`,
      { method: "POST", body: { reason } }
    );
  },

  /**
   * Uzatma YENİ TOKEN ÜRETMEZ: üretseydi işverene gönderilmiş
   * bağlantı ölür ve e-postanın yeniden gönderilmesi gerekirdi —
   * "uzatma" adı altında sessizce bir iptal olurdu.
   */
  extend(projectId: string, months: number, reason?: string) {
    return apiClient<{ message: string; expiresAtUtc: string }>(
      `projects/${projectId}/employer-portal-link/extend`,
      { method: "POST", body: { months, reason } }
    );
  },

  sendEmail(
    projectId: string,
    payload: { employerName?: string; employerEmail: string; portalUrl: string }
  ) {
    return apiClient<{ message: string }>(
      `projects/${projectId}/employer-portal-link/send-email`,
      { method: "POST", body: payload }
    );
  },

  getEmailLog(projectId: string) {
    return apiClient<EmployerPortalEmailLogItem[]>(
      `projects/${projectId}/employer-portal-link/email-log`
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
  companyLogoUrl?: string | null;
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

  /**
   * Fiziksel ilerleme yüzdesi. Yanıtta TUTAR YOKTUR — yüzde sunucuda
   * sözleşme tutarıyla ağırlıklandırılır ama ağırlığın kendisi dışarı
   * çıkmaz.
   */
  getProgress(token: string) {
    return publicGet<PortalProgress>(
      `portal/${encodeURIComponent(token)}/ilerleme`
    );
  },

  photoUrl(token: string, photoId: string) {
    return `/api/backend/portal/${encodeURIComponent(token)}/photos/${photoId}`;
  },
};

export type PortalProgress =
  | { hasProgress: false; message: string }
  | {
      hasProgress: true;
      completionRate: number;
      sections: {
        name: string;
        completionRate: number;
        itemCount: number;
        completedItemCount: number;
      }[];
    };
