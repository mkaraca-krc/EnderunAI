import { apiClient } from "@/lib/api/api-client";

export type EngineeringPositionListItem = {
  id: string;
  companyId: string;
  companyName: string;
  code: string;
  name: string;
  unit: string;
  source: number;
  discipline: number;
  status: number;
  officialInstitution?: string | null;
  officialCode?: string | null;
  category?: string | null;
  revisionNumber: number;
  defaultLaborHours: number;
  defaultHelperHours: number;
  defaultMachineHours: number;
  totalLaborHours: number;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
};

export type EngineeringPositionFilters = {
  search?: string;
  source?: number;
  discipline?: number;
  status?: number;
};

export const engineeringPositionService = {
  getAll(filters: EngineeringPositionFilters = {}) {
    const params = new URLSearchParams();

    if (filters.search?.trim()) {
      params.set("search", filters.search.trim());
    }

    if (filters.source !== undefined) {
      params.set("source", String(filters.source));
    }

    if (filters.discipline !== undefined) {
      params.set("discipline", String(filters.discipline));
    }

    if (filters.status !== undefined) {
      params.set("status", String(filters.status));
    }

    const query = params.toString();

    return apiClient<EngineeringPositionListItem[]>(
      `engineering-positions${query ? `?${query}` : ""}`
    );
  },

  getById(id: string) {
    return apiClient<EngineeringPositionListItem>(
      `engineering-positions/${id}`
    );
  },
};

export type EngineeringPositionDetail = EngineeringPositionListItem & {
  description?: string | null;
  technicalSpecification?: string | null;
  searchKeywords?: string | null;
  approvedAtUtc?: string | null;
  approvedByUserId?: string | null;
};

export type UpdateEngineeringPositionRequest = {
  name: string;
  unit: string;
  discipline: number;
  status: number;
  officialInstitution?: string | null;
  officialCode?: string | null;
  category?: string | null;
  description?: string | null;
  technicalSpecification?: string | null;
  searchKeywords?: string | null;
  defaultLaborHours: number;
  defaultHelperHours: number;
  defaultMachineHours: number;
};

export const engineeringPositionDetailService = {
  getById(id: string) {
    return apiClient<EngineeringPositionDetail>(
      `engineering-positions/${id}`
    );
  },

  update(id: string, payload: UpdateEngineeringPositionRequest) {
    return apiClient<{
      message: string;
      id: string;
      code: string;
      revisionNumber: number;
    }>(`engineering-positions/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
};

export type CreateEngineeringPositionRequest = {
  companyId: string;
  code?: string | null;
  name: string;
  unit: string;
  source: number;
  discipline: number;
  officialInstitution?: string | null;
  officialCode?: string | null;
  category?: string | null;
  description?: string | null;
  technicalSpecification?: string | null;
  searchKeywords?: string | null;
  defaultLaborHours: number;
  defaultHelperHours: number;
  defaultMachineHours: number;
};

export const engineeringPositionCreateService = {
  create(payload: CreateEngineeringPositionRequest) {
    return apiClient<{
      message: string;
      id: string;
      code: string;
      name: string;
      revisionNumber: number;
      status: number;
    }>("engineering-positions", {
      method: "POST",
      body: payload,
    });
  },
};
