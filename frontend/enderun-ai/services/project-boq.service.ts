import { apiClient, ApiError } from "@/lib/api/api-client";

export enum ProjectBoqStatus {
  Draft = 0,
  Approved = 1,
  Superseded = 2,
  Archived = 3,
}

export enum ProjectBoqItemType {
  Mixed = 0,
  Material = 1,
  Labor = 2,
}

export interface ProjectBoqItemRequest {
  engineeringPositionId?: string | null;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  /** Bileşenler verilirse sunucu bunu üçünün toplamı olarak türetir. */
  unitPrice: number;
  itemType: ProjectBoqItemType;
  category?: string | null;
  notes?: string | null;
  /** Kalemin kısmı; boş bırakılabilir. */
  projectHakedisSectionId?: string | null;
  materialUnitPrice?: number | null;
  laborUnitPrice?: number | null;
  overheadUnitPrice?: number | null;
}

export interface CreateProjectBoqRequest {
  companyId: string;
  projectId: string;
  boqNumber: string;
  name: string;
  revisionNumber: number;
  currencyCode: string;
  description?: string | null;
  notes?: string | null;
  items: ProjectBoqItemRequest[];
}

export interface ProjectBoqListItem {
  id: string;
  companyId: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  boqNumber: string;
  name: string;
  revisionNumber: number;
  revisionCode: string;
  status: ProjectBoqStatus;
  isCurrentRevision: boolean;
  currencyCode: string;
  totalAmount: number;
  itemCount: number;
  createdAtUtc: string;
}

export interface ProjectBoqItem {
  id: string;
  engineeringPositionId?: string | null;
  projectHakedisSectionId?: string | null;
  lineNumber: number;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  materialUnitPrice: number;
  laborUnitPrice: number;
  overheadUnitPrice: number;
  unitPrice: number;
  totalAmount: number;
  itemType: ProjectBoqItemType;
  category?: string | null;
  notes?: string | null;
}

/** Kısım ara toplamı. */
export interface ProjectBoqSectionSummary {
  id: string;
  order: number;
  name: string;
  code?: string | null;
  itemCount: number;
  materialAmount: number;
  laborAmount: number;
  overheadAmount: number;
  totalAmount: number;
}

export interface ProjectBoqDetail extends ProjectBoqListItem {
  description?: string | null;
  notes?: string | null;
  approvedAtUtc?: string | null;
  isContractBaseline: boolean;
  /** Onaylı icmal kilitlidir; düzenleme kapatılır. */
  isLocked: boolean;
  sections: ProjectBoqSectionSummary[];
  unsectionedItemCount: number;
  unsectionedAmount: number;
  items: ProjectBoqItem[];
}

export interface UpdateProjectBoqRequest {
  name: string;
  currencyCode: string;
  description?: string | null;
  notes?: string | null;
  items: ProjectBoqItemRequest[];
}

export interface CreateBoqRevisionRequest {
  amendmentNumber?: string | null;
  amendmentDate?: string | null;
  reason?: string | null;
}

/** Excel içe aktarma önizlemesi — hiçbir şey yazılmadan önce. */
export interface BoqImportPreview {
  sectionCount: number;
  itemCount: number;
  totalAmount: number;
  unsectionedItemCount: number;
  sections: {
    rowNumber: number;
    name: string;
    isNew: boolean;
    itemCount: number;
    totalAmount: number;
  }[];
  errors: { rowNumber: number; message: string }[];
  items: BoqImportPreviewItem[];
}

/** Önizlemede bir satır için bulunan poz adayı. */
export interface BoqImportMatchCandidate {
  positionId: string;
  code: string;
  name: string;
  unit: string;
  institution?: string | null;
  score: number;
  unitPrice?: number | null;
  materialPrice?: number | null;
  laborPrice?: number | null;
}

export interface BoqImportPreviewItem {
  rowNumber: number;
  sectionName?: string | null;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  materialUnitPrice: number;
  laborUnitPrice: number;
  overheadUnitPrice: number;
  unitPrice: number;
  totalAmount: number;
  /**
   * Poz önerisi. Kesinse aktarımda otomatik bağlanır; değilse kullanıcı
   * adaylardan seçer, özel poz açar ya da atlar.
   */
  match?: {
    isCertain: boolean;
    certaintyReason?: string | null;
    candidates: BoqImportMatchCandidate[];
  } | null;
}

/**
 * Aktarımda satır bazında poz kararı. positionId boşsa satır BİLEREK
 * atlanmış demektir; o satıra otomatik eşleştirme de uygulanmaz.
 */
export interface BoqImportMatchDecision {
  rowNumber: number;
  positionId: string | null;
}

export interface HakedisSectionTemplate {
  key: string;
  name: string;
  description: string;
  sectionCount: number;
  sections: { order: number; name: string }[];
}

export interface ProjectHakedisSection {
  id: string;
  order: number;
  name: string;
  code?: string | null;
  isActive: boolean;
}

export const projectBoqService = {
  getAll(filters?: {
    companyId?: string;
    projectId?: string;
    status?: number;
  }) {
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

    const query = params.toString();

    return apiClient<ProjectBoqListItem[]>(
      `project-boqs${query ? `?${query}` : ""}`
    );
  },

  getById(id: string) {
    return apiClient<ProjectBoqDetail>(
      `project-boqs/${id}`
    );
  },

  create(request: CreateProjectBoqRequest) {
    return apiClient<{
      id: string;
      boqNumber: string;
      revisionNumber: number;
      status: ProjectBoqStatus;
      totalAmount: number;
    }>("project-boqs", {
      method: "POST",
      body: request,
    });
  },

  approve(id: string) {
    return apiClient<{
      id: string;
      boqNumber: string;
      revisionNumber: number;
      status: ProjectBoqStatus;
      message: string;
    }>(`project-boqs/${id}/approve`, {
      method: "POST",
    });
  },

  archive(id: string) {
    return apiClient<{
      id: string;
      boqNumber: string;
      revisionNumber: number;
      status: ProjectBoqStatus;
      message: string;
    }>(`project-boqs/${id}/archive`, {
      method: "POST",
    });
  },

  remove(id: string) {
    return apiClient<void>(
      `project-boqs/${id}`,
      {
        method: "DELETE",
      }
    );
  },

  update(id: string, request: UpdateProjectBoqRequest) {
    return apiClient<{
      id: string;
      totalAmount: number;
      itemCount: number;
      message: string;
    }>(`project-boqs/${id}`, {
      method: "PUT",
      body: request,
    });
  },

  createRevision(id: string, request: CreateBoqRevisionRequest) {
    return apiClient<{
      id: string;
      revisionNumber: number;
      revisionCode: string;
      totalAmount: number;
      itemCount: number;
      message: string;
    }>(`project-boqs/${id}/revizyon`, {
      method: "POST",
      body: request,
    });
  },

  templateDownloadUrl() {
    return "/api/backend/project-boqs/icmal-sablonu";
  },

  /**
   * Excel'i okur ve önizleme döner — hiçbir şey yazmaz.
   * FormData gönderildiği için apiClient yerine doğrudan fetch:
   * tarayıcının kendi content-type sınırını koruması gerekiyor.
   */
  importPreview(id: string, file: File) {
    return uploadExcel<BoqImportPreview>(
      `project-boqs/${id}/icmal-aktar/onizleme`,
      file
    );
  },

  importCommit(id: string, file: File, decisions?: BoqImportMatchDecision[]) {
    return uploadExcel<{
      message: string;
      sectionCount: number;
      itemCount: number;
      skippedRowCount: number;
      linkedCount: number;
      unlinkedCount: number;
      totalAmount: number;
    }>(
      `project-boqs/${id}/icmal-aktar`,
      file,
      decisions && decisions.length > 0
        ? { matches: JSON.stringify(decisions) }
        : undefined
    );
  },

  getSectionTemplates() {
    return apiClient<HakedisSectionTemplate[]>("hakedis-section-templates");
  },

  getSections(projectId: string) {
    return apiClient<ProjectHakedisSection[]>(
      `projects/${projectId}/hakedis-sections`
    );
  },

  replaceSections(
    projectId: string,
    sections: {
      id?: string | null;
      order: number;
      name: string;
      code?: string | null;
      isActive: boolean;
      contractType?: number | null;
    }[]
  ) {
    return apiClient<{ message: string }>(
      `projects/${projectId}/hakedis-sections`,
      { method: "PUT", body: { sections } }
    );
  },
};

async function uploadExcel<T>(
  path: string,
  file: File,
  fields?: Record<string, string>
): Promise<T> {
  const formData = new FormData();
  formData.append("file", file);

  for (const [key, value] of Object.entries(fields ?? {}))
    formData.append(key, value);

  const response = await fetch(`/api/backend/${path}`, {
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
        : `Dosya işlenemedi: ${response.status}`;

    throw new ApiError(message, response.status, payload);
  }

  return payload as T;
}
