import { apiClient } from "@/lib/api/api-client";

export enum CargoDirection {
  Incoming = 0,
  Outgoing = 1,
}

export enum CargoStatus {
  Registered = 0,
  InTransit = 1,
  Delivered = 2,
  Returned = 3,
  Cancelled = 4,
}

export type CargoItem = {
  id: string;
  companyId: string;
  projectId?: string | null;

  direction: CargoDirection;
  directionName: string;

  trackingNumber: string;
  cargoCompany: string;

  senderName?: string | null;
  recipientName?: string | null;
  institutionName?: string |null;

  cargoDate: string;
  expectedDeliveryDate?: string | null;
  deliveredAtUtc?: string | null;
  deliveredToName?: string | null;

  status: CargoStatus;
  statusName: string;

  createdAtUtc: string;
};

export type CargoFilters = {
  companyId?: string;
  projectId?: string;
  direction?: number;
  status?: number;
  search?: string;
};

export type CreateCargoRequest = {
  companyId: string;
  projectId?: string | null;

  direction: CargoDirection;

  trackingNumber: string;
  cargoCompany: string;

  senderName?: string | null;
  recipientName?: string | null;
  institutionName?: string | null;

  cargoDate: string;
  expectedDeliveryDate?: string | null;

  description?: string | null;
};


export type UpdateCargoRequest = {
  projectId?: string | null;
  cargoCompany: string;
  senderName?: string | null;
  recipientName?: string | null;
  institutionName?: string | null;
  cargoDate: string;
  expectedDeliveryDate?: string | null;
  deliveredAtUtc?: string | null;
  deliveredToName?: string | null;
  description?: string | null;
  status: CargoStatus;
};

function buildQuery(filters?: CargoFilters) {

  const params = new URLSearchParams();

  if (filters?.companyId)
    params.set("companyId", filters.companyId);

  if (filters?.projectId)
    params.set("projectId", filters.projectId);

  if (filters?.direction !== undefined)
    params.set("direction", String(filters.direction));

  if (filters?.status !== undefined)
    params.set("status", String(filters.status));

  if (filters?.search?.trim())
    params.set("search", filters.search.trim());

  const query = params.toString();

  return query ? `?${query}` : "";
}

export const cargoService = {

  getAll(filters?: CargoFilters) {
    return apiClient<CargoItem[]>(
      `secretariat/cargo${buildQuery(filters)}`
    );
  },

  get(id: string) {
    return apiClient<CargoItem>(
      `secretariat/cargo/${id}`
    );
  },

  create(request: CreateCargoRequest) {

    return apiClient<CargoItem>(
      "secretariat/cargo",
      {
        method: "POST",
        body: request,
      }
    );
  },

  update(id: string, request: UpdateCargoRequest) {
    return apiClient<CargoItem>(
      `secretariat/cargo/${id}`,
      {
        method: "PUT",
        body: request,
      }
    );
  },

  delete(id: string) {

    return apiClient<{message:string}>(
      `secretariat/cargo/${id}`,
      {
        method:"DELETE",
      }
    );
  }

};
