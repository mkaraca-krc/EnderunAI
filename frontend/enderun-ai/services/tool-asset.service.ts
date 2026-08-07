import { apiClient } from "@/lib/api/api-client";

/** Aletin o anki durumu. */
export const ToolAssetStatus = {
  InWarehouse: 0,
  InUse: 1,
  InService: 2,
  Scrapped: 3,
} as const;

export const TOOL_ASSET_STATUSES: [number, string][] = [
  [ToolAssetStatus.InWarehouse, "Depoda"],
  [ToolAssetStatus.InUse, "Kullanımda"],
  [ToolAssetStatus.InService, "Serviste"],
  [ToolAssetStatus.Scrapped, "Hurda"],
];

export const ToolAssetLocationType = {
  HeadOffice: 0,
  Site: 1,
} as const;

/** Servis talebinin durumu. */
export const ToolServiceStatus = {
  Requested: 0,
  Transferred: 1,
  InService: 2,
  Completed: 3,
  Scrapped: 4,
  Cancelled: 5,
} as const;

export const TOOL_SERVICE_STATUSES: [number, string][] = [
  [ToolServiceStatus.Requested, "Talep edildi"],
  [ToolServiceStatus.Transferred, "Merkezde"],
  [ToolServiceStatus.InService, "Serviste"],
  [ToolServiceStatus.Completed, "Tamamlandı"],
  [ToolServiceStatus.Scrapped, "Hurda"],
  [ToolServiceStatus.Cancelled, "İptal"],
];

/** Arızanın nasıl giderileceği kararı. */
export const ToolServiceDecision = {
  Pending: 0,
  ExternalWarranty: 1,
  ExternalPaid: 2,
  InHouse: 3,
  Scrap: 4,
} as const;

export const TOOL_SERVICE_DECISIONS: [number, string][] = [
  [ToolServiceDecision.ExternalWarranty, "Dış servis — garanti"],
  [ToolServiceDecision.ExternalPaid, "Dış servis — ücretli"],
  [ToolServiceDecision.InHouse, "Yerinde onarım"],
  [ToolServiceDecision.Scrap, "Hurda"],
];

export const TOOL_SERVICE_URGENCIES: [number, string][] = [
  [0, "Düşük"],
  [1, "Normal"],
  [2, "Yüksek"],
  [3, "Kritik"],
];

export type ToolAsset = {
  id: string;
  companyId: string;
  code: string;
  name: string;
  brand?: string | null;
  model?: string | null;
  serialNumber?: string | null;
  purchaseDate?: string | null;
  purchaseCost?: number | null;
  warrantyEndDate?: string | null;
  status: number;
  statusName: string;
  locationType: number;
  projectSiteId?: string | null;
  siteName?: string | null;
  assignedPersonnelId?: string | null;
  assignedPersonnelName?: string | null;
  notes?: string | null;
};

export type ToolServiceRow = {
  id: string;
  requestNumber: string;
  requestDate: string;
  faultDescription: string;
  status: number;
  decision: number;
  serviceCost: number;
  projectCode?: string | null;
};

/** Alet kartı + servis geçmişi özeti. */
/** Aletin üzerinde duran açık zimmet. */
export type ToolAssetAssignment = {
  id: string;
  personnelId: string;
  personnelName?: string | null;
  projectId?: string | null;
  assignmentDate: string;
  plannedReturnDate?: string | null;
};

export type ToolAssetCard = {
  asset: ToolAsset;
  /** Açık zimmet yoksa null. */
  assignment?: ToolAssetAssignment | null;
  /** Kaç kez arızalandı. */
  serviceCount: number;
  /** Toplam servis masrafı. */
  serviceTotalCost: number;
  lastServiceDate?: string | null;
  history: ToolServiceRow[];
};

export type ToolServiceRequest = {
  id: string;
  requestNumber: string;
  toolAssetId: string;
  assetCode: string;
  assetName: string;
  projectId?: string | null;
  projectCode?: string | null;
  projectSiteId?: string | null;
  siteName?: string | null;
  requestDate: string;
  faultDescription: string;
  urgency: number;
  status: number;
  decision: number;
  decisionNote?: string | null;
  serviceProviderName?: string | null;
  serviceCost: number;
  replacementPurchaseRequestId?: string | null;
  completedAtUtc?: string | null;
};

export type WarrantyExpiringAsset = {
  id: string;
  code: string;
  name: string;
  warrantyEndDate: string;
  status: number;
};

export type SaveToolAssetPayload = {
  companyId: string;
  code: string;
  name: string;
  brand?: string | null;
  model?: string | null;
  serialNumber?: string | null;
  purchaseDate?: string | null;
  purchaseCost?: number | null;
  warrantyEndDate?: string | null;
  locationType: number;
  projectSiteId?: string | null;
  notes?: string | null;
};

function buildQuery(params: Record<string, unknown>) {
  const query = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === "") continue;
    query.set(key, String(value));
  }

  const suffix = query.toString();
  return suffix ? `?${suffix}` : "";
}

/**
 * Demirbaş / el aleti kartları.
 *
 * Sarftan ayrıdır: alet tüketilmez, kullanılır ve geri gelir. Kart
 * sayesinde servis geçmişi ve garanti takibi alet bazında birikir.
 */
export const toolAssetService = {
  getAll(params: {
    companyId?: string;
    status?: number | "";
    projectSiteId?: string;
    search?: string;
  } = {}) {
    return apiClient<ToolAsset[]>(`tool-assets${buildQuery(params)}`);
  },

  /** Kart + servis geçmişi (kaç kez arızalandı, toplam maliyet). */
  getCard(id: string) {
    return apiClient<ToolAssetCard>(`tool-assets/${id}`);
  },

  create(payload: SaveToolAssetPayload) {
    return apiClient<{ message: string; id: string; code: string }>(
      "tool-assets",
      { method: "POST", body: payload }
    );
  },

  /**
   * Zimmet ver / devret. Alet başkasının üzerindeyse arka uç eski
   * zimmeti iade olarak kapatır ve yenisini açar.
   */
  assign(
    id: string,
    payload: {
      personnelId: string;
      projectId?: string | null;
      assignmentDate: string;
      plannedReturnDate?: string | null;
      conditionAtAssignment?: string | null;
      notes?: string | null;
    }
  ) {
    return apiClient<{
      message: string;
      assignmentId: string;
      transferred: boolean;
    }>(`tool-assets/${id}/assign`, { method: "POST", body: payload });
  },

  returnAsset(
    id: string,
    payload: { returnDate: string; conditionAtReturn?: string | null }
  ) {
    return apiClient<{ message: string }>(`tool-assets/${id}/return`, {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: SaveToolAssetPayload) {
    return apiClient<{ message: string }>(`tool-assets/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  /** Garantisi yaklaşan aletler — uyarı kartının kaynağı. */
  getWarrantyExpiring(params: { companyId?: string; days?: number } = {}) {
    return apiClient<WarrantyExpiringAsset[]>(
      `tool-assets/warranty-expiring${buildQuery(params)}`
    );
  },
};

/**
 * Alet servis talepleri.
 *
 * Maliyet talebi AÇAN şantiyenin projesine yazılır; garanti
 * kapsamında sıfırdır ve hiçbir maliyet kaydı oluşmaz.
 */
export const toolServiceRequestService = {
  getAll(params: {
    companyId?: string;
    toolAssetId?: string;
    projectId?: string;
    status?: number | "";
    openOnly?: boolean;
  } = {}) {
    return apiClient<ToolServiceRequest[]>(
      `tool-service-requests${buildQuery(params)}`
    );
  },

  create(payload: {
    toolAssetId: string;
    projectId?: string | null;
    projectSiteId?: string | null;
    faultDescription: string;
    urgency: number;
  }) {
    return apiClient<{ message: string; id: string; requestNumber: string }>(
      "tool-service-requests",
      { method: "POST", body: payload }
    );
  },

  decide(
    id: string,
    payload: {
      decision: number;
      decisionNote: string;
      serviceProviderName?: string | null;
      serviceCost: number;
    }
  ) {
    return apiClient<{ message: string }>(
      `tool-service-requests/${id}/decide`,
      { method: "POST", body: payload }
    );
  },

  advance(id: string, status: number) {
    return apiClient<{ message: string; status: number; costWritten: boolean }>(
      `tool-service-requests/${id}/advance`,
      { method: "POST", body: { status } }
    );
  },

  /** Hurda sonrası yerine alım talebi taslağı. */
  createReplacement(id: string) {
    return apiClient<{
      message: string;
      purchaseRequestId: string;
      requestNumber: string;
    }>(`tool-service-requests/${id}/replacement-request`, { method: "POST" });
  },
};
