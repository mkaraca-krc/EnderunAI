import { apiClient } from "@/lib/api/api-client";

const root = "hakedis-deduction-accounts";

/**
 * Hakediş kesinti türlerinin muhasebe hesap eşlemesi.
 *
 * Eşleme ZORUNLU DEĞİL: boş bırakılan tür, finans ayarındaki genel
 * kesinti hesabına düşer. Ekran bunu açıkça söylemeli, yoksa
 * kullanıcı boş satırı "eksik" sanıp gereksiz hesap açar.
 *
 * Yetki: okuma `accounting.view`, kaydetme `accounting.manage`.
 */

export interface DeductionAccountMapping {
  /** `HakedisDeductionType` sayısal değeri. */
  deductionType: number;
  /** Türün Türkçe adı; backend'den geliyor, ekranda tekrar yazılmıyor. */
  name: string;
  accountingAccountId: string | null;
  accountCode: string | null;
  accountName: string | null;
  notes: string | null;
}

export interface DeductionAccountMappingInput {
  deductionType: number;
  accountingAccountId: string | null;
  notes: string | null;
}

export const hakedisDeductionAccountService = {
  get(companyId: string) {
    return apiClient<DeductionAccountMapping[]>(
      `${root}?companyId=${encodeURIComponent(companyId)}`
    );
  },

  /**
   * Eşlemeleri TOPLUCA kaydeder. Hesabı boşaltılan tür eşlemesi
   * silinir; kısmi gönderim yok, gönderilen liste son durumdur.
   */
  replace(companyId: string, mappings: DeductionAccountMappingInput[]) {
    return apiClient<{ message: string }>(
      `${root}?companyId=${encodeURIComponent(companyId)}`,
      { method: "PUT", body: { mappings } }
    );
  },
};
