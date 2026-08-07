import { apiClient, ApiError } from "@/lib/api/api-client";

/**
 * Bir işin taşeronda mı bizde mi olduğu. Bizdeyse masrafı biz
 * yaptığımız için taşeron hakedişinden kesilir; taşerondaysa hakedişte
 * hiç görünmez.
 */
export enum SubcontractorResponsibility {
  Us = 0,
  Subcontractor = 1,
}

export enum SubcontractorContractStatus {
  Draft = 0,
  Active = 1,
  Completed = 2,
  Cancelled = 3,
}

/** Projeyle aynı enum; taşeronda Karma ve Belirsiz kabul edilmez. */
export enum SubcontractorContractType {
  LumpSum = 1,
  UnitPrice = 2,
}

export interface SubcontractorContractSection {
  id?: string;
  projectHakedisSectionId: string;
  sectionName?: string | null;
  sectionAmount: number;
  order: number;
}

export interface SubcontractorContractListItem {
  id: string;
  companyId: string;
  currentAccountId: string;
  subcontractorTitle: string;
  projectId: string;
  projectName: string;
  projectSiteId?: string | null;
  projectSiteName?: string | null;
  contractNumber: string;
  workDescription: string;
  contractType: number;
  contractTypeName: string;
  contractAmount: number;
  currencyCode: string;
  startDate: string;
  endDate?: string | null;
  status: number;
  statusName: string;
  retentionRate: number;
  withholdingNumerator: number;
  withholdingDenominator: number;
  mealResponsibility: number;
  accommodationResponsibility: number;
  socialSecurityResponsibility: number;
  materialResponsibility: number;
  ohsResponsibility: number;
  notes?: string | null;
  sectionCount: number;
}

export interface SubcontractorContractDetail
  extends Omit<
    SubcontractorContractListItem,
    "subcontractorTitle" | "projectName" | "projectSiteName" | "sectionCount"
  > {
  sections: SubcontractorContractSection[];
}

export interface SaveSubcontractorContractRequest {
  companyId: string;
  currentAccountId: string;
  projectId: string;
  projectSiteId?: string | null;
  contractNumber: string;
  workDescription: string;
  contractType: number;
  contractAmount: number;
  currencyCode?: string | null;
  startDate: string;
  endDate?: string | null;
  retentionRate: number;
  withholdingNumerator: number;
  withholdingDenominator: number;
  mealResponsibility: number;
  accommodationResponsibility: number;
  socialSecurityResponsibility: number;
  materialResponsibility: number;
  ohsResponsibility: number;
  notes?: string | null;
  sections: Array<{
    projectHakedisSectionId: string;
    sectionAmount: number;
    order: number;
  }>;
}

export const subcontractorService = {
  list(filters?: {
    companyId?: string;
    projectId?: string;
    currentAccountId?: string;
  }) {
    const query = new URLSearchParams();
    if (filters?.companyId) query.set("companyId", filters.companyId);
    if (filters?.projectId) query.set("projectId", filters.projectId);
    if (filters?.currentAccountId) {
      query.set("currentAccountId", filters.currentAccountId);
    }

    const suffix = query.toString() ? `?${query.toString()}` : "";
    return apiClient<SubcontractorContractListItem[]>(
      `subcontractor-contracts${suffix}`
    );
  },

  getById(id: string) {
    return apiClient<SubcontractorContractDetail>(
      `subcontractor-contracts/${id}`
    );
  },

  create(payload: SaveSubcontractorContractRequest) {
    return apiClient<{ id: string; message: string }>(
      "subcontractor-contracts",
      { method: "POST", body: payload }
    );
  },

  update(id: string, payload: SaveSubcontractorContractRequest) {
    return apiClient<{ message: string }>(`subcontractor-contracts/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  remove(id: string) {
    return apiClient<{ message: string }>(`subcontractor-contracts/${id}`, {
      method: "DELETE",
    });
  },
};

// --- Evraklar (T7) ---

export enum SubcontractorDocumentType {
  Contract = 0,
  SignatureCircular = 1,
  TaxCertificate = 2,
  SocialSecurityClearance = 3,
  TaxClearance = 4,
  OccupationalSafety = 5,
  TradeRegistry = 6,
  InsurancePolicy = 7,
  Other = 99,
}

/** İSG belgeleriyle aynı geçerlilik motorundan gelir. */
export enum SubcontractorDocumentStatus {
  Valid = 0,
  ExpiringSoon = 1,
  Expired = 2,
  NoExpiry = 3,
}

export interface SubcontractorDocument {
  id: string;
  subcontractorContractId: string;
  contractNumber: string;
  subcontractorTitle: string;
  documentType: number;
  documentTypeName: string;
  title: string;
  issueDate: string;
  validUntil?: string | null;
  /** SGK borcu yoktur yazısında bitiş girilmemişse üç aylık kural. */
  effectiveValidUntil?: string | null;
  validUntilIsImplied: boolean;
  status: number;
  statusName: string;
  daysRemaining?: number | null;
  originalFileName: string;
  sizeBytes: number;
  notes?: string | null;
}

export const subcontractorDocumentService = {
  list(subcontractorContractId?: string, onlyProblems = false) {
    const query = new URLSearchParams();
    if (subcontractorContractId) {
      query.set("subcontractorContractId", subcontractorContractId);
    }
    if (onlyProblems) query.set("onlyProblems", "true");

    const suffix = query.toString() ? `?${query.toString()}` : "";
    return apiClient<SubcontractorDocument[]>(
      `subcontractor-documents${suffix}`
    );
  },

  /**
   * Evrak yükleme. FormData gönderildiği için apiClient yerine doğrudan
   * fetch: tarayıcının kendi content-type sınırını (boundary) koruması
   * gerekiyor.
   */
  async upload(input: {
    subcontractorContractId: string;
    documentType: number;
    title: string;
    issueDate: string;
    validUntil?: string | null;
    notes?: string | null;
    file: File;
  }): Promise<{
    id: string;
    effectiveValidUntil?: string | null;
    message: string;
  }> {
    const formData = new FormData();
    formData.append("subcontractorContractId", input.subcontractorContractId);
    formData.append("documentType", String(input.documentType));
    formData.append("title", input.title);
    formData.append("issueDate", input.issueDate);
    if (input.validUntil) formData.append("validUntil", input.validUntil);
    if (input.notes) formData.append("notes", input.notes);
    formData.append("file", input.file);

    const response = await fetch("/api/backend/subcontractor-documents", {
      method: "POST",
      body: formData,
      cache: "no-store",
    });

    if (response.status === 401) {
      if (typeof window !== "undefined") window.location.href = "/login";
      throw new ApiError("Oturum süresi doldu.", 401);
    }

    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      const message =
        payload && typeof payload === "object" && "message" in payload
          ? String((payload as { message?: unknown }).message)
          : `Evrak yüklenemedi: ${response.status}`;

      throw new ApiError(message, response.status, payload);
    }

    return payload as {
      id: string;
      effectiveValidUntil?: string | null;
      message: string;
    };
  },

  downloadUrl(id: string) {
    return `/api/backend/subcontractor-documents/${id}/dosya`;
  },

  remove(id: string) {
    return apiClient<{ message: string }>(`subcontractor-documents/${id}`, {
      method: "DELETE",
    });
  },
};
