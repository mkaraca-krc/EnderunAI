import { apiClient } from "@/lib/api/api-client";

/*
 * TEK SÖZLEŞME — UCUN DÖNDÜRDÜĞÜ ADLAR.
 *
 * Önce burada `accountName`, `accountingAccountId` ve `isActive`
 * vardı; üçü de sunucuda YOKTU. Sonuç: `.filter` her satırı eliyordu
 * ve liste SESSİZCE boş kalıyordu — 404'ten beter, çünkü hata bile
 * görünmüyordu.
 *
 * Eşleme katmanı yazılmadı. Modeldeki doğru ad `AccountHolder`
 * (hesap sahibi) olduğu için uç `accountHolder` döndürüyor ve tip
 * ona uyduruldu. `accountingAccountId` yalnız KASA hesabında var,
 * bankada yok. `isActive` modelde hiç yok; uç zaten silinmemişleri
 * döndürüyor.
 *
 * IBAN MASKELİ: son dört hane. Tam IBAN ayrı uçtan, denetim kaydıyla.
 */
export type PayrollBankAccount = {
  id: string;
  companyId: string;
  bankName: string;
  accountHolder?: string | null;
  ibanMasked: string;
  currencyCode?: string | null;
};

export type PayrollCashAccount = {
  id: string;
  companyId: string;
  code: string;
  name: string;
  currencyCode: string;
  accountingAccountId: string;
  isActive?: boolean;
};

function normalizeList<T>(
  payload: unknown
): T[] {
  if (Array.isArray(payload)) {
    return payload as T[];
  }

  if (
    payload &&
    typeof payload === "object"
  ) {
    const value =
      payload as {
        items?: unknown;
        data?: unknown;
        result?: unknown;
      };

    if (Array.isArray(value.items)) {
      return value.items as T[];
    }

    if (Array.isArray(value.data)) {
      return value.data as T[];
    }

    if (Array.isArray(value.result)) {
      return value.result as T[];
    }
  }

  return [];
}

export const payrollPaymentAccountService = {
  /*
   * SÜZME SUNUCUDA. Şirket parametresi uca gidiyor; ön yüz artık
   * çekip elemiyor. Elemek, alan adı bir gün değişirse listeyi
   * sessizce boşaltan desendi.
   */
  async getBankAccounts(companyId: string) {
    return apiClient<PayrollBankAccount[]>(
      `company-settings/bank-accounts?companyId=${encodeURIComponent(companyId)}`
    );
  },

  /** Tam IBAN — "Göster/Kopyala". Her çağrı kayda düşer. */
  async revealIban(id: string) {
    return apiClient<{ iban: string }>(
      `company-settings/bank-accounts/${id}/iban`
    );
  },

  async getCashAccounts(
    companyId: string
  ) {
    const query =
      new URLSearchParams({
        companyId,
      });

    const response =
      await apiClient<unknown>(
        `cash-accounts?${query.toString()}`
      );

    return normalizeList<
      PayrollCashAccount
    >(response).filter(
      (item) =>
        item.companyId === companyId &&
        item.isActive !== false
    );
  },
};
