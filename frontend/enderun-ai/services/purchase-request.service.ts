import { apiClient } from "@/lib/api/api-client";

export type PurchaseRequestListItem = {
  id: string; companyId: string; companyName: string; projectId: string;
  projectCode: string; projectName: string; requestNumber: string;
  requestDate: string; neededByDate?: string | null; requestedByName: string;
  description?: string | null; priority: number; status: number; isActive: boolean;
  itemCount: number; totalQuantity: number;
};

export type PurchaseRequestItemPayload = {
  materialDescription: string; quantity: number; unit: string;
  requestedDeliveryDate?: string | null; notes?: string | null;
};

export type CreatePurchaseRequestPayload = {
  companyId: string; projectId: string; requestNumber: string;
  requestDate: string; neededByDate?: string | null; requestedByName: string;
  description?: string | null; priority: number; items: PurchaseRequestItemPayload[];
};

export const purchaseRequestService = {
  getAll(params?: { companyId?: string; status?: number; search?: string }) {
    const q = new URLSearchParams();
    if (params?.companyId) q.set("companyId", params.companyId);
    if (params?.status !== undefined) q.set("status", String(params.status));
    if (params?.search) q.set("search", params.search);
    const suffix = q.toString() ? `?${q.toString()}` : "";
    return apiClient<PurchaseRequestListItem[]>(`purchase-requests${suffix}`);
  },
  create(payload: CreatePurchaseRequestPayload) {
    return apiClient<{ message: string; id: string }>("purchase-requests", {
      method: "POST", body: payload,
    });
  },
};
