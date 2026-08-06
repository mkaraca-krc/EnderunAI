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
    }>(`market/commodities/refresh?days=${days}`, { method: "POST" });
  },
};
