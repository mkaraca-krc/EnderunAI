import { apiClient } from "@/lib/api/api-client";

const root = "pricing/calculate-offer";

/**
 * Teklif birim fiyat hesabı — kayıt yazmaz, saf hesap.
 *
 * NEDEN GEREKLİ, EKRANDA HESAP VARKEN: teklif ekranı yazarken canlı
 * geri bildirim için maliyeti kendisi hesaplıyor ama nakliye,
 * zaiyat, finansman ve genel gideri TEK bir birim maliyete
 * katlıyor — hangisinin ne kadar yer tuttuğu görünmüyor. Bu uç
 * kalemleri ayrı ayrı döndürüyor; kırılımı istemcide yeniden
 * hesaplamak, aynı formülü üçüncü kez yazmak olurdu.
 *
 * Yetki: `engineering.manage`.
 */

export interface CalculateOfferPriceRequest {
  listPrice: number;
  discountRate: number;
  freightRate: number;
  wasteRate: number;
  financeRate: number;
  generalExpenseRate: number;
  profitRate: number;
}

export interface CalculateOfferPriceResponse {
  listPrice: number;
  discountRate: number;
  netPurchasePrice: number;
  freightAmount: number;
  wasteAmount: number;
  financeAmount: number;
  generalExpenseAmount: number;
  costPrice: number;
  profitAmount: number;
  salesPrice: number;
}

export const pricingService = {
  calculateOffer(request: CalculateOfferPriceRequest) {
    return apiClient<CalculateOfferPriceResponse>(root, {
      method: "POST",
      body: request,
    });
  },
};
