import type { Paged } from "@/lib/api/paged";
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

/** Fiyat listesi başlığı — ürünleri değil, listenin kendisini anlatır. */
export type ManufacturerPriceList = {
  id: string;
  companyId: string;
  companyName: string;
  manufacturerName: string;
  listName: string;
  listDate: string;
  validUntil?: string | null;
  currency: string;
  isActive: boolean;
  itemCount: number;
};

export type CreateManufacturerPriceListItem = {
  productCode: string;
  productDescription: string;
  unit: string;
  listPrice: number;
  category?: string | null;
  brand?: string | null;
  model?: string | null;
};

export type CreateManufacturerPriceListRequest = {
  companyId: string;
  manufacturerName: string;
  listName: string;
  listDate: string;
  validUntil?: string | null;
  currency: string;
  /** Uç en az bir ürün istiyor; boş liste kabul edilmiyor. */
  items: CreateManufacturerPriceListItem[];
};

export const manufacturerPriceListService = {
  /**
   * Fiyat listeleri.
   *
   * `activeOnly` varsayılan olarak AÇIK ve süresi geçmiş listeyi
   * uç eliyor. Bu süzme istemcide tekrarlanmıyor: "geçerli liste"
   * tanımı (aktif + son geçerlilik tarihi bugünden küçük değil)
   * backend'de tek yerde duruyor.
   */
  getAll(params?: {
    companyId?: string;
    manufacturer?: string;
    activeOnly?: boolean;
  }) {
    const query = new URLSearchParams();
    if (params?.companyId) query.set("companyId", params.companyId);
    if (params?.manufacturer) query.set("manufacturer", params.manufacturer);
    if (params?.activeOnly === false) query.set("activeOnly", "false");

    const suffix = query.toString();
    return apiClient<ManufacturerPriceList[]>(
      suffix
        ? `manufacturer-price-lists?${suffix}`
        : "manufacturer-price-lists"
    );
  },

  create(request: CreateManufacturerPriceListRequest) {
    return apiClient<{
      message: string;
      id: string;
      manufacturerName: string;
      listName: string;
    }>("manufacturer-price-lists", { method: "POST", body: request });
  },

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

    return apiClient<Paged<ManufacturerPriceProduct>>(
      `manufacturer-price-lists/search-products?${query.toString()}`
    );
  },
};
