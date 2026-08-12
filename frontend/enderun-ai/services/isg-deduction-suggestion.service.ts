import { apiClient } from "@/lib/api/api-client";

const root = "isg/hakedis-kesinti-onerisi";

/**
 * OSGB sözleşmesinden hakediş kesintisi önerisi — kayıt YAZMAZ.
 *
 * Sözleşme yoksa, dönem sözleşme dışındaysa veya kişi başı bedelde
 * çalışan yoksa öneri ÜRETİLMEZ ve sebebi döner. Ekran o durumda
 * uydurma bir tutar koymuyor: sebebi gösterip kullanıcıyı kendi
 * girmeye bırakıyor. Backend'in bu ilkesini istemcide "0 yaz geç"
 * diye ezmek, ön muhasebenin fark etmediği yanlış bir kesinti
 * satırı üretirdi.
 *
 * Yanıt alanları bilinçli olarak hakediş kesinti satırıyla birebir
 * eşleşiyor; ekran dönüşüm yapmıyor.
 *
 * Yetki: `isg.view` VEYA `hakedis.view` — biri yeterli. Kesintiyi
 * hakedişi hazırlayan giriyor, İSG yetkisi şart koşulmuyor.
 */
export interface IsgDeductionSuggestion {
  hasSuggestion: boolean;
  /** HakedisDeductionType.OhsContribution = 8. */
  deductionType: number;
  description: string;
  manualAmount: number;
  personCount: number | null;
  osgbContractId: string | null;
  contractNumber: string | null;
  /** Öneri üretilemediyse sebebi; üretildiyse null. */
  reason: string | null;
}

export const isgDeductionSuggestionService = {
  get(companyId: string, projectId: string, donem?: string) {
    const query = new URLSearchParams({ companyId, projectId });
    if (donem) query.set("donem", donem);

    return apiClient<IsgDeductionSuggestion>(`${root}?${query.toString()}`);
  },
};
