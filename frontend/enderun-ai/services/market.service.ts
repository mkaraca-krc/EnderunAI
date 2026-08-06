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
