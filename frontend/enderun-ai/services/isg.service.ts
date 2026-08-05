import { apiClient, ApiError } from "@/lib/api/api-client";

// --- Ortak ---

/**
 * Geçerlilik rozeti rengi backend'den geliyor (erp-status sınıfı adı).
 * Renk kuralı IsgValidityCalculator'da tek yerde duruyor; arayüz kendi
 * eşiğini uydurmuyor.
 */
export type IsgValidityColor = "green" | "yellow" | "red" | "gray";

export const ISG_HEALTH_REPORT_TYPES = [
  { value: 0, label: "İşe giriş muayenesi" },
  { value: 1, label: "Periyodik muayene" },
  { value: 2, label: "İşe dönüş muayenesi" },
  { value: 3, label: "Özel durum muayenesi" },
];

export const ISG_HEALTH_RESULTS = [
  { value: 0, label: "Çalışabilir" },
  { value: 1, label: "Şartlı çalışabilir" },
  { value: 2, label: "Çalışamaz" },
];

export const ISG_TRAINING_TYPES = [
  { value: 0, label: "Temel İSG eğitimi" },
  { value: 1, label: "İşbaşı eğitimi" },
  { value: 2, label: "Yenileme eğitimi" },
  { value: 3, label: "Özel/konu bazlı eğitim" },
];

export const ISG_CERTIFICATE_TYPES = [
  { value: 0, label: "Yüksekte çalışma" },
  { value: 1, label: "Elektrik yetki belgesi" },
  { value: 2, label: "İlk yardımcı" },
  { value: 3, label: "Yangın güvenliği" },
  { value: 4, label: "İş makinesi / forklift" },
  { value: 99, label: "Diğer" },
];

export const ISG_SITE_DOCUMENT_TYPES = [
  { value: 0, label: "Risk değerlendirmesi" },
  { value: 1, label: "Acil durum planı" },
  { value: 2, label: "İSG kurul tutanağı" },
  { value: 3, label: "Saha denetim formu" },
  { value: 4, label: "KKD zimmet formu" },
  { value: 99, label: "Diğer" },
];

export const ISG_INCIDENT_TYPES = [
  { value: 0, label: "İş kazası" },
  { value: 1, label: "Ramak kala" },
  { value: 2, label: "Meslek hastalığı" },
];

export const ISG_INCIDENT_SEVERITIES = [
  { value: 0, label: "Zarar yok" },
  { value: 1, label: "İlk yardım" },
  { value: 2, label: "Tıbbi tedavi" },
  { value: 3, label: "İş günü kaybı" },
  { value: 4, label: "Sürekli iş göremezlik" },
  { value: 5, label: "Ölümlü" },
];

export const ISG_INCIDENT_STATUSES = [
  { value: 0, label: "Açık" },
  { value: 1, label: "İnceleniyor" },
  { value: 2, label: "Kapatıldı" },
];

export const OSGB_BILLING_TYPES = [
  { value: 0, label: "Sabit aylık bedel" },
  { value: 1, label: "Kişi başı bedel" },
];

export const OSGB_EXPERT_TYPES = [
  { value: 0, label: "İş güvenliği uzmanı" },
  { value: 1, label: "İşyeri hekimi" },
  { value: 2, label: "Diğer sağlık personeli" },
];

export function labelOf(
  options: { value: number; label: string }[],
  value: number
) {
  return options.find((option) => option.value === value)?.label ?? "—";
}

// --- Panel ---

type ExpirySummary = {
  suresiDoldu: number;
  yakindaDoluyor: number;
};

export type IsgDashboard = {
  saglikRaporu: ExpirySummary & { eksikPersonel: number };
  egitim: ExpirySummary & { temelEgitimiEksikPersonel: number };
  sertifika: ExpirySummary;
  sahaBelgeleri: ExpirySummary & { riskDegerlendirmesiOlanSantiye: number };
  osgb: {
    aktifSozlesme: number;
    suresiDoluyor: number;
    suresiDoldu: number;
  };
  aktifPersonel: number;
  uyariEsigiGun: number;
  /**
   * Kaza defterine yetkisi olmayan kullanıcıda null gelir — sayı bile
   * kendi başına bilgi taşıdığı için backend hiç döndürmüyor.
   */
  kaza: {
    acikKayit: number;
    agirKayit: number;
    sgkBildirimiGecikmis: number;
    buYilKaza: number;
    buYilRamakKala: number;
    buYilKayipIsGunu: number;
  } | null;
  kazaGizli?: boolean;
};

// --- OSGB sözleşmesi ---

export type IsgOsgbExpert = {
  id: string;
  expertType: number;
  expertTypeName: string;
  fullName: string;
  certificateNumber?: string | null;
  expertClass?: string | null;
  phone?: string | null;
  email?: string | null;
  startDate: string;
  endDate?: string | null;
  isCurrentlyAssigned: boolean;
};

export type IsgOsgbExpertPayload = {
  expertType: number;
  fullName: string;
  certificateNumber?: string | null;
  expertClass?: string | null;
  phone?: string | null;
  email?: string | null;
  startDate: string;
  endDate?: string | null;
};

export type IsgOsgbContractListItem = {
  id: string;
  contractNumber: string;
  currentAccountId: string;
  osgbTitle: string;
  startDate: string;
  endDate?: string | null;
  billingType: number;
  billingTypeName: string;
  monthlyFee: number;
  perPersonFee: number;
  currencyCode: string;
  statusName: string;
  daysUntilExpiry?: number | null;
  expertCount: number;
};

export type IsgOsgbInvoice = {
  id: string;
  internalNumber: string;
  invoiceNumber: string;
  invoiceDate: string;
  grandTotal: number;
  currencyCode: string;
  status: number;
  statusName: string;
};

export type IsgOsgbContractDetail = {
  id: string;
  companyId: string;
  contractNumber: string;
  currentAccountId: string;
  osgbTitle: string;
  osgbTaxNumber?: string | null;
  startDate: string;
  endDate?: string | null;
  billingType: number;
  billingTypeName: string;
  monthlyFee: number;
  perPersonFee: number;
  currencyCode: string;
  notes?: string | null;
  statusName: string;
  daysUntilExpiry?: number | null;
  experts: IsgOsgbExpert[];
  invoices: IsgOsgbInvoice[];
};

export type IsgOsgbContractPayload = {
  companyId?: string;
  currentAccountId: string;
  contractNumber: string;
  startDate: string;
  endDate?: string | null;
  billingType: number;
  monthlyFee: number;
  perPersonFee: number;
  currencyCode: string;
  notes?: string | null;
  experts: IsgOsgbExpertPayload[];
};

// --- Personel kayıtları ---

export type IsgHealthReport = {
  id: string;
  personnelId: string;
  personnelName: string;
  employeeNumber?: string | null;
  reportType: number;
  reportTypeName: string;
  examDate: string;
  validUntil?: string | null;
  result: number;
  resultName: string;
  doctorName?: string | null;
  validityStatus: string;
  validityStatusName: string;
  validityColor: IsgValidityColor;
  daysRemaining?: number | null;
  /** Tıbbi detay: yalnızca isg.health.view iznindekilere dolu gelir. */
  restrictions?: string | null;
  doctorNotes?: string | null;
  hasDocument?: boolean | null;
  /** true ise kısıtlama/hekim notu gizlendi — ekran bunu yazar. */
  healthDetailHidden: boolean;
};

export type IsgTraining = {
  id: string;
  personnelId: string;
  personnelName: string;
  employeeNumber?: string | null;
  trainingType: number;
  trainingTypeName: string;
  topic: string;
  trainingDate: string;
  durationHours: number;
  validUntil?: string | null;
  trainerName?: string | null;
  validityStatus: string;
  validityStatusName: string;
  validityColor: IsgValidityColor;
  daysRemaining?: number | null;
  hasDocument: boolean;
  notes?: string | null;
};

export type IsgCertificate = {
  id: string;
  personnelId: string;
  personnelName: string;
  employeeNumber?: string | null;
  certificateType: number;
  certificateTypeName: string;
  certificateNumber?: string | null;
  issuedBy?: string | null;
  issueDate: string;
  expiryDate?: string | null;
  validityStatus: string;
  validityStatusName: string;
  validityColor: IsgValidityColor;
  daysRemaining?: number | null;
  hasDocument: boolean;
  notes?: string | null;
};

export type IsgPersonnelCard = {
  personnelId: string;
  personnelName: string;
  employeeNumber?: string | null;
  jobTitle?: string | null;
  healthReports: IsgHealthReport[];
  trainings: IsgTraining[];
  certificates: IsgCertificate[];
  expiredCount: number;
  expiringSoonCount: number;
};

export type IsgPersonnelSummary = {
  personnelId: string;
  personnelName: string;
  employeeNumber?: string | null;
  jobTitle?: string | null;
  hasValidHealthReport: boolean;
  healthReportValidUntil?: string | null;
  hasValidBasicTraining: boolean;
  certificateCount: number;
  expiredCount: number;
  expiringSoonCount: number;
  hasMissingRecords: boolean;
};

export type CreateHealthReportPayload = {
  companyId: string;
  personnelId: string;
  isgOsgbContractId?: string | null;
  reportType: number;
  examDate: string;
  validUntil?: string | null;
  result: number;
  doctorName?: string | null;
  restrictions?: string | null;
  doctorNotes?: string | null;
};

export type CreateTrainingPayload = {
  companyId: string;
  personnelId: string;
  isgOsgbContractId?: string | null;
  trainingType: number;
  topic: string;
  trainingDate: string;
  durationHours: number;
  validUntil?: string | null;
  trainerName?: string | null;
  notes?: string | null;
};

export type CreateCertificatePayload = {
  companyId: string;
  personnelId: string;
  certificateType: number;
  customTypeName?: string | null;
  certificateNumber?: string | null;
  issuedBy?: string | null;
  issueDate: string;
  expiryDate?: string | null;
  notes?: string | null;
};

// --- Kaza / ramak kala ---

export type IsgIncidentListItem = {
  id: string;
  incidentDateTime: string;
  incidentType: number;
  incidentTypeName: string;
  severity: number;
  severityName: string;
  severityColor: IsgValidityColor;
  projectId?: string | null;
  projectCode?: string | null;
  projectSiteId?: string | null;
  siteName?: string | null;
  personnelId?: string | null;
  personnelName?: string | null;
  lostWorkDays: number;
  sgkNotified: boolean;
  sgkNotificationOverdue: boolean;
  status: number;
  statusName: string;
};

export type IsgIncidentDetail = IsgIncidentListItem & {
  companyId: string;
  projectName?: string | null;
  description: string;
  rootCause?: string | null;
  actionTaken?: string | null;
  sgkNotificationDate?: string | null;
  sgkNotificationNumber?: string | null;
  closedAtUtc?: string | null;
  closureNote?: string | null;
  createdAtUtc: string;
};

export type IsgIncidentPayload = {
  companyId?: string;
  projectId?: string | null;
  projectSiteId?: string | null;
  personnelId?: string | null;
  incidentDateTime: string;
  incidentType: number;
  severity: number;
  description: string;
  rootCause?: string | null;
  actionTaken?: string | null;
  lostWorkDays: number;
  sgkNotified: boolean;
  sgkNotificationDate?: string | null;
  sgkNotificationNumber?: string | null;
  status?: number;
  closureNote?: string | null;
};

// --- Saha belgeleri ---

export type IsgSiteDocument = {
  id: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  projectSiteId?: string | null;
  siteName?: string | null;
  documentType: number;
  documentTypeName: string;
  title: string;
  issueDate: string;
  validUntil?: string | null;
  validityStatus: string;
  validityStatusName: string;
  validityColor: IsgValidityColor;
  daysRemaining?: number | null;
  originalFileName: string;
  sizeBytes: number;
  notes?: string | null;
  createdAtUtc: string;
};

export type UpdateSiteDocumentPayload = {
  documentType: number;
  title: string;
  issueDate: string;
  validUntil?: string | null;
  projectSiteId?: string | null;
  notes?: string | null;
};

function query(params: Record<string, string | number | undefined | null>) {
  const search = new URLSearchParams();

  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") {
      search.append(key, String(value));
    }
  });

  const text = search.toString();
  return text ? `?${text}` : "";
}

export const isgService = {
  // --- Panel ---

  getDashboard(companyId?: string) {
    return apiClient<IsgDashboard>(`isg/dashboard${query({ companyId })}`);
  },

  // --- OSGB ---

  getContracts(companyId?: string) {
    return apiClient<IsgOsgbContractListItem[]>(
      `isg/osgb-sozlesmeleri${query({ companyId })}`
    );
  },

  getContract(id: string) {
    return apiClient<IsgOsgbContractDetail>(`isg/osgb-sozlesmeleri/${id}`);
  },

  createContract(payload: IsgOsgbContractPayload) {
    return apiClient<IsgOsgbContractDetail>("isg/osgb-sozlesmeleri", {
      method: "POST",
      body: payload,
    });
  },

  updateContract(id: string, payload: IsgOsgbContractPayload) {
    return apiClient<IsgOsgbContractDetail>(`isg/osgb-sozlesmeleri/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  deleteContract(id: string) {
    return apiClient<{ message: string }>(`isg/osgb-sozlesmeleri/${id}`, {
      method: "DELETE",
    });
  },

  // --- Personel kayıtları ---

  getPersonnelSummary(companyId?: string, search?: string) {
    return apiClient<IsgPersonnelSummary[]>(
      `isg/personel${query({ companyId, search })}`
    );
  },

  getPersonnelCard(personnelId: string) {
    return apiClient<IsgPersonnelCard>(`isg/personel/${personnelId}`);
  },

  /** Oturumdaki kullanıcının kendi kartı; personel bağı yoksa 404 döner. */
  getOwnCard() {
    return apiClient<IsgPersonnelCard>("isg/benim");
  },

  createHealthReport(payload: CreateHealthReportPayload) {
    return apiClient<IsgHealthReport>("isg/saglik-raporlari", {
      method: "POST",
      body: payload,
    });
  },

  deleteHealthReport(id: string) {
    return apiClient<{ message: string }>(`isg/saglik-raporlari/${id}`, {
      method: "DELETE",
    });
  },

  createTraining(payload: CreateTrainingPayload) {
    return apiClient<IsgTraining>("isg/egitimler", {
      method: "POST",
      body: payload,
    });
  },

  deleteTraining(id: string) {
    return apiClient<{ message: string }>(`isg/egitimler/${id}`, {
      method: "DELETE",
    });
  },

  createCertificate(payload: CreateCertificatePayload) {
    return apiClient<IsgCertificate>("isg/sertifikalar", {
      method: "POST",
      body: payload,
    });
  },

  deleteCertificate(id: string) {
    return apiClient<{ message: string }>(`isg/sertifikalar/${id}`, {
      method: "DELETE",
    });
  },

  // --- Kaza / ramak kala ---

  getIncidents(filters: {
    companyId?: string;
    projectId?: string;
    status?: number;
    incidentType?: number;
  }) {
    return apiClient<IsgIncidentListItem[]>(`isg/kazalar${query(filters)}`);
  },

  getIncident(id: string) {
    return apiClient<IsgIncidentDetail>(`isg/kazalar/${id}`);
  },

  createIncident(payload: IsgIncidentPayload) {
    return apiClient<IsgIncidentDetail>("isg/kazalar", {
      method: "POST",
      body: payload,
    });
  },

  updateIncident(id: string, payload: IsgIncidentPayload) {
    return apiClient<IsgIncidentDetail>(`isg/kazalar/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  deleteIncident(id: string) {
    return apiClient<{ message: string }>(`isg/kazalar/${id}`, {
      method: "DELETE",
    });
  },

  // --- Saha belgeleri ---

  getSiteDocuments(filters: {
    companyId?: string;
    projectId?: string;
    projectSiteId?: string;
    documentType?: number;
  }) {
    return apiClient<IsgSiteDocument[]>(`isg/saha-belgeleri${query(filters)}`);
  },

  /**
   * Belge yükleme. FormData gönderildiği için apiClient yerine doğrudan
   * fetch: tarayıcının kendi content-type sınırını (boundary) koruması
   * gerekiyor.
   */
  async uploadSiteDocument(input: {
    companyId: string;
    projectId: string;
    projectSiteId?: string | null;
    documentType: number;
    title: string;
    issueDate: string;
    validUntil?: string | null;
    notes?: string | null;
    file: File;
  }): Promise<IsgSiteDocument> {
    const formData = new FormData();
    formData.append("companyId", input.companyId);
    formData.append("projectId", input.projectId);
    if (input.projectSiteId) {
      formData.append("projectSiteId", input.projectSiteId);
    }
    formData.append("documentType", String(input.documentType));
    formData.append("title", input.title);
    formData.append("issueDate", input.issueDate);
    if (input.validUntil) formData.append("validUntil", input.validUntil);
    if (input.notes) formData.append("notes", input.notes);
    formData.append("file", input.file);

    const response = await fetch("/api/backend/isg/saha-belgeleri", {
      method: "POST",
      body: formData,
      cache: "no-store",
    });

    if (response.status === 401) {
      if (typeof window !== "undefined") {
        window.location.href = "/login";
      }
      throw new ApiError("Oturum süresi doldu.", 401);
    }

    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      const message =
        payload && typeof payload === "object" && "message" in payload
          ? String((payload as { message?: unknown }).message)
          : `Belge yüklenemedi: ${response.status}`;

      throw new ApiError(message, response.status, payload);
    }

    return payload as IsgSiteDocument;
  },

  updateSiteDocument(id: string, payload: UpdateSiteDocumentPayload) {
    return apiClient<IsgSiteDocument>(`isg/saha-belgeleri/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  deleteSiteDocument(id: string) {
    return apiClient<{ message: string }>(`isg/saha-belgeleri/${id}`, {
      method: "DELETE",
    });
  },

  siteDocumentDownloadUrl(id: string) {
    return `/api/backend/isg/saha-belgeleri/${id}/dosya`;
  },
};
