import { apiClient } from "@/lib/api/api-client";

/**
 * MASRAF MERKEZİ — TEK KAYNAK.
 *
 * Bu sistemde masraf merkezi ayrı bir varlık değil; iki alana
 * bölünmüş: proje (şantiye) ve şubenin muhasebe kodu (merkez ofis).
 * Her ekran ikisini ayrı ayrı topluyordu ve kullanıcı çek ekranında
 * proje seçicisine bakıp "Merkez"i orada arıyor, bulamıyordu.
 *
 * Burada tek liste: Merkez en üstte, projeler altında.
 */

export type CostCenterKind = 0 | 1;

export const COST_CENTER_KIND = {
  Center: 0 as CostCenterKind,
  Project: 1 as CostCenterKind,
};

export interface CostCenterOption {
  /** 0 Merkez, 1 Proje. */
  kind: CostCenterKind;
  /** Muhasebe masraf merkezi kodu (merkezde şube kodu, projede proje kodu). */
  code: string;
  label: string;
  /** Proje seçenekleriyle dolu, merkezde null. */
  projectId?: string | null;
  /** Kapalı/tamamlanmış proje — yalnız mevcut kayıtta seçiliyse listede. */
  isClosed: boolean;
}

export const costCenterService = {
  getOptions(params?: { companyId?: string; includeProjectId?: string }) {
    const query = new URLSearchParams();
    if (params?.companyId) query.set("companyId", params.companyId);
    if (params?.includeProjectId)
      query.set("includeProjectId", params.includeProjectId);

    const suffix = query.toString();
    return apiClient<CostCenterOption[]>(
      `masraf-merkezleri${suffix ? `?${suffix}` : ""}`
    );
  },
};

/**
 * Seçimi sunucunun beklediği ikiliye çözer.
 *
 * TEK YER: her ekran kendi çözümünü yazsaydı biri projeyi, diğeri
 * kodu gönderir ve aynı seçim iki farklı kayıt üretirdi.
 */
export function resolveCostCenter(option: CostCenterOption | undefined): {
  projectId: string | null;
  costCenterCode: string | null;
} {
  if (!option) return { projectId: null, costCenterCode: null };

  return option.kind === COST_CENTER_KIND.Project
    ? { projectId: option.projectId ?? null, costCenterCode: null }
    : { projectId: null, costCenterCode: option.code };
}

/** Kayıttaki değerden seçeneği bulur — düzenleme ekranı bunu kullanır. */
export function findCostCenter(
  options: CostCenterOption[],
  projectId?: string | null,
  costCenterCode?: string | null
): CostCenterOption | undefined {
  if (projectId)
    return options.find((x) => x.projectId === projectId);

  if (costCenterCode)
    return options.find(
      (x) => x.kind === COST_CENTER_KIND.Center && x.code === costCenterCode
    );

  return undefined;
}
