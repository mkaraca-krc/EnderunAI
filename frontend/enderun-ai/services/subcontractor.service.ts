import { apiClient } from "@/lib/api/api-client";

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
