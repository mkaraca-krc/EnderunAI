import { apiClient } from "@/lib/api/api-client";

/** Sistemin arşivlediği para birimleri. TRY yerel para birimi. */
export const SUPPORTED_CURRENCIES = ["TRY", "USD", "EUR", "GBP"] as const;

export type SupportedCurrency = (typeof SUPPORTED_CURRENCIES)[number];

export type ExchangeRateLookup = {
  currencyCode: string;
  requestedDate: string;
  /** Kurun geldiği bülten tarihi; hafta sonu/tatilde istenen günden eski olur. */
  effectiveDate: string;
  forexBuying: number;
  forexSelling: number;
  source: string;
  daysBack: number;
};

export type ExchangeRateRow = {
  rateDate: string;
  currencyCode: string;
  forexBuying: number;
  forexSelling: number;
  banknoteBuying?: number | null;
  banknoteSelling?: number | null;
  bulletinNumber?: string | null;
  source: string;
};

export type ExchangeRateFreshness = {
  latestRateDate?: string | null;
  daysSinceLatest?: number | null;
  isStale: boolean;
  warning?: string | null;
};

export const marketService = {
  /**
   * Bir tarihe uygulanacak kur. Arşivde kayıt yoksa uç 404 döner —
   * çağıran taraf bunu "kur yok" diye ele almalı, 1 varsaymamalı.
   */
  lookupRate(currency: string, date: string) {
    const query = new URLSearchParams({ currency, date });

    return apiClient<ExchangeRateLookup>(
      `market/exchange-rates/lookup?${query.toString()}`
    );
  },

  getRates(currency: string, from?: string, to?: string) {
    const query = new URLSearchParams({ currency });
    if (from) query.set("from", from);
    if (to) query.set("to", to);

    return apiClient<ExchangeRateRow[]>(
      `market/exchange-rates?${query.toString()}`
    );
  },

  getFreshness() {
    return apiClient<ExchangeRateFreshness>("market/exchange-rates/freshness");
  },

  refresh(days = 7) {
    return apiClient<{
      fetchedDays: number;
      alreadyPresentDays: number;
      unavailableDays: number;
      message: string;
      errors: string[];
    }>(`market/exchange-rates/refresh?days=${days}`, { method: "POST" });
  },
};

export type CommodityPricePoint = {
  priceDate: string;
  priceUsdPerTon: number;
  priceTryPerTon?: number | null;
  usdRate?: number | null;
};

export type CommoditySummary = {
  commodity: number;
  /** Kaynak etiketi — COMEX ile LME aynı şey değil, ekranda görünmeli. */
  sourceLabel: string;
  sourceSymbol: string;
  isLme: boolean;
  latestDate?: string | null;
  latestUsdPerTon?: number | null;
  latestTryPerTon?: number | null;
  usdRate?: number | null;
  /** USD bazında yüzde değişim — yalnızca emtia hareketi. */
  changePercentUsd?: number | null;
  /** TL bazında yüzde değişim — emtia + kur hareketi birlikte. */
  changePercentTry?: number | null;
  comparedToUsdPerTon?: number | null;
  comparedToDate?: string | null;
  isStale: boolean;
  warning?: string | null;
  trend: CommodityPricePoint[];
};

/** Eşiğin hangi yönde aşıldığı. */
export const CommodityAlertDirection = {
  BuyOpportunity: 0,
  CostRisk: 1,
} as const;

export type CommodityAlertTrigger = {
  id: string;
  direction: number;
  /** Geçişin gerçekleştiği fiyat günü. */
  priceDate: string;
  priceUsdPerTon: number;
  priceTryPerTon?: number | null;
  thresholdUsdPerTon: number;
  acknowledgedAtUtc?: string | null;
};

/**
 * Şirketin bakır eşiği ve bekleyen tetiklenmeleri.
 *
 * Eşikler USD/ton'dur: TL eşiği tutmak kur hareketini emtia
 * hareketiyle karıştırıp "bakır mı pahalandı, lira mı değer kaybetti"
 * sorusunu eşiğin içine gömerdi.
 */
export type CommodityAlertStatus = {
  companyId: string;
  commodity: number;
  buyBelowUsdPerTon?: number | null;
  alertAboveUsdPerTon?: number | null;
  isEnabled: boolean;
  latestPriceUsdPerTon?: number | null;
  latestPriceDate?: string | null;
  /** Şu an hangi bölgedeyiz; hiçbirinde değilsek null. */
  currentState?: number | null;
  pendingTriggers: CommodityAlertTrigger[];
};

export const commodityService = {
  getCopper(days = 30) {
    return apiClient<CommoditySummary>(`market/commodities/copper?days=${days}`);
  },

  refresh(days = 30) {
    return apiClient<{
      storedDays: number;
      updatedDays: number;
      sourceLabel: string;
      message: string;
      errors: string[];
      newTriggers: number;
    }>(`market/commodities/refresh?days=${days}`, { method: "POST" });
  },

  getCopperAlert(companyId: string) {
    return apiClient<CommodityAlertStatus>(
      `market/commodities/copper/alert?companyId=${encodeURIComponent(companyId)}`
    );
  },

  saveCopperAlert(input: {
    companyId: string;
    buyBelowUsdPerTon: number | null;
    alertAboveUsdPerTon: number | null;
    isEnabled: boolean;
    notes: string | null;
  }) {
    return apiClient<CommodityAlertStatus>("market/commodities/copper/alert", {
      method: "PUT",
      body: input,
    });
  },

  acknowledgeAlert(triggerId: string) {
    return apiClient<{ message: string }>(
      `market/commodities/alerts/${triggerId}/acknowledge`,
      { method: "POST" }
    );
  },
};

export const CopperTonnageSource = {
  Unknown: 0,
  Manual: 1,
  BillOfQuantities: 2,
} as const;

/**
 * Bakır + kur hareketinin bir projenin kalan işine tahmini etkisi.
 *
 * Üç bileşen ayrı gelir ve toplamları TL etkisini verir: emtia hareketi
 * (taban kurla), kur hareketi (taban fiyatla) ve ikisinin çarpım artığı.
 * Artık ayrı durur; birine sessizce eklenirse "bakır mı, kur mu"
 * sorusunun cevabı bozulur.
 */
export type ProjectCopperImpact = {
  projectId: string;
  projectCode: string;
  projectName: string;
  contractType: number;
  contractTypeName: string;
  /** Anahtar teslimde etki doğrudan kâr erozyonu. */
  isCostRisk: boolean;
  tonnageSource: number;
  tonnageSourceName: string;
  remainingTons?: number | null;
  baselineDate?: string | null;
  baselineReason?: string | null;
  baselineUsdPerTon?: number | null;
  baselineUsdRate?: number | null;
  currentUsdPerTon?: number | null;
  currentUsdRate?: number | null;
  copperChangePercent?: number | null;
  fxChangePercent?: number | null;
  copperEffect?: number | null;
  fxEffect?: number | null;
  combinedEffect?: number | null;
  totalEffect?: number | null;
  assumptions: string[];
};

export const copperImpactService = {
  getPortfolio(companyId?: string) {
    const query = companyId
      ? `?companyId=${encodeURIComponent(companyId)}`
      : "";

    return apiClient<ProjectCopperImpact[]>(`market/copper-impact${query}`);
  },

  save(
    projectId: string,
    payload: {
      remainingTons?: number | null;
      baselineDate?: string | null;
      note?: string | null;
    }
  ) {
    return apiClient<ProjectCopperImpact>(`market/copper-impact/${projectId}`, {
      method: "PUT",
      body: payload,
    });
  },
};
