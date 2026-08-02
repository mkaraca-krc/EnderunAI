import { apiClient } from "@/lib/api/api-client";

export type OfferStatus = 0 | 1 | 2 | 3 | 4 | 5 | 6;

export type OfferListItem = {
  id: string;
  companyId: string;
  companyName: string;
  projectId?: string | null;
  projectName?: string | null;
  customerId?: string | null;
  offerNumber: string;
  title: string;
  offerDate: string;
  validUntil?: string | null;
  currency: string;
  exchangeRate: number;
  status: OfferStatus;
  subtotal: number;
  discountTotal: number;
  costTotal: number;
  profitTotal: number;
  grandTotal: number;
  itemCount: number;
};

export type OfferItem = {
  id: string;
  lineNumber: number;
  positionNumber?: string | null;
  engineeringPositionId?: string | null;
  engineeringRecipeId?: string | null;
  recipeVersion?: number | null;
  description: string;
  manufacturerPriceListItemId?: string | null;
  manufacturerName?: string | null;
  productCode?: string | null;
  brand?: string | null;
  model?: string | null;
  quantity: number;
  unit: string;
  listPrice: number;
  discountRate: number;
  netPurchasePrice: number;
  freightRate: number;
  wasteRate: number;
  financeRate: number;
  generalExpenseRate: number;
  profitRate: number;
  unitCost: number;
  unitSalesPrice: number;
  costTotal: number;
  salesTotal: number;
  notes?: string | null;
};

export type OfferDetail = Omit<OfferListItem, "itemCount"> & {
  description?: string | null;
  notes?: string | null;
  items: OfferItem[];
};

export type OfferItemPayload = {
  positionNumber?: string | null;
  engineeringPositionId?: string | null;
  engineeringRecipeId?: string | null;
  recipeVersion?: number | null;
  description: string;
  manufacturerPriceListItemId?: string | null;
  manufacturerName?: string | null;
  productCode?: string | null;
  brand?: string | null;
  model?: string | null;
  quantity: number;
  unit: string;
  listPrice: number;
  discountRate: number;
  freightRate: number;
  wasteRate: number;
  financeRate: number;
  generalExpenseRate: number;
  profitRate: number;
  notes?: string | null;
};

export type CreateOfferPayload = {
  companyId: string;
  projectId?: string | null;
  customerId?: string | null;
  title: string;
  offerDate: string;
  validUntil?: string | null;
  currency: string;
  exchangeRate: number;
  description?: string | null;
  notes?: string | null;
  items: OfferItemPayload[];
};

export type OfferItemCalculation = {
  netPurchasePrice: number;
  unitCost: number;
  unitSalesPrice: number;
  costTotal: number;
  salesTotal: number;
  profitTotal: number;
};

function buildQuery(params?: {
  companyId?: string;
  projectId?: string;
  status?: number;
  search?: string;
}) {
  const query = new URLSearchParams();

  if (params?.companyId) query.set("companyId", params.companyId);
  if (params?.projectId) query.set("projectId", params.projectId);
  if (params?.status !== undefined) query.set("status", String(params.status));
  if (params?.search) query.set("search", params.search);

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const offerService = {
  getAll(params?: {
    companyId?: string;
    projectId?: string;
    status?: number;
    search?: string;
  }) {
    return apiClient<OfferListItem[]>(`offers${buildQuery(params)}`);
  },

  getById(id: string) {
    return apiClient<OfferDetail>(`offers/${id}`);
  },

  calculateItem(payload: {
    quantity: number;
    listPrice: number;
    discountRate: number;
    freightRate: number;
    wasteRate: number;
    financeRate: number;
    generalExpenseRate: number;
    profitRate: number;
  }) {
    return apiClient<OfferItemCalculation>("offers/calculate-item", {
      method: "POST",
      body: payload,
    });
  },

  create(payload: CreateOfferPayload) {
    return apiClient<{
      message: string;
      id: string;
      offerNumber: string;
      grandTotal: number;
      status: OfferStatus;
    }>("offers", {
      method: "POST",
      body: payload,
    });
  },
};
