import { apiClient } from "@/lib/api/api-client";

export type ManufacturerPriceProduct = {
  id: string;
  priceListId: string;
  manufacturer: string;
  listName: string;
  listDate: string;
  validUntil?: string | null;
  currency: string;
  productCode: string;
  productDescription: string;
  unit: string;
  listPrice: number;
  category?: string | null;
  brand?: string | null;
  model?: string | null;
};

export const manufacturerPriceListService = {
  searchProducts(params: {
    companyId: string;
    search: string;
    take?: number;
  }) {
    const query = new URLSearchParams({
      companyId: params.companyId,
      search: params.search,
      take: String(params.take ?? 100),
    });

    return apiClient<ManufacturerPriceProduct[]>(
      `manufacturer-price-lists/search-products?${query.toString()}`
    );
  },
};
