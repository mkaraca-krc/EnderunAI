import { apiClient } from "@/lib/api/api-client";

export interface CurrencyValuationPreviewLine {
  currentAccountId: string;
  currentAccountCode: string;
  currentAccountTitle: string;
  currencyCode: string;
  /** Döviz bakiyesi (işaretli: pozitif alacak, negatif borç). */
  balance: number;
  /** Defterdeki TL karşılığı (değerleme düzeltmeleri hariç). */
  bookValueLocal: number;
  rateAvailable: boolean;
  valuationRate?: number | null;
  rateSource?: string | null;
  valuedLocal?: number | null;
  /** Toplam fark; tamamı bu turda yazılmaz. */
  totalDifference?: number | null;
  /** Önceki turlarda yazılmış düzeltme. */
  previouslyPosted: number;
  /** Bu turda deftere yazılacak kısım. */
  postableDifference: number;
  message?: string | null;
}

export interface CurrencyValuationPreview {
  companyId: string;
  valuationDate: string;
  lines: CurrencyValuationPreviewLine[];
  totalGain: number;
  totalLoss: number;
  netDifference: number;
  hasMissingRate: boolean;
  /** Aynı tarihte iptal edilmemiş tur varsa yeni fiş kesilemez. */
  alreadyPostedRunId?: string | null;
}

export interface CurrencyValuationRunResult {
  id: string;
  valuationDate: string;
  postedDifference: number;
  accountingVoucherId?: string | null;
  lineCount: number;
}

export const currencyValuationService = {
  preview(companyId: string, valuationDate?: string) {
    const params = new URLSearchParams({ companyId });
    if (valuationDate) params.set("valuationDate", valuationDate);

    return apiClient<CurrencyValuationPreview>(
      `accounting/currency-valuation/preview?${params.toString()}`
    );
  },

  post(companyId: string, valuationDate: string) {
    return apiClient<CurrencyValuationRunResult>(
      "accounting/currency-valuation",
      {
        method: "POST",
        body: { companyId, valuationDate },
      }
    );
  },

  reverse(runId: string, reason: string) {
    return apiClient<{ id: string; reversalVoucherId: string }>(
      `accounting/currency-valuation/${runId}/reverse`,
      {
        method: "POST",
        body: { reason },
      }
    );
  },
};
