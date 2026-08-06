import { apiClient } from "@/lib/api/api-client";

/** Kütüphaneden gelen kurum referansı (ÇŞB / TEDAŞ). */
export type ReferencePrice = {
  institutionName: string;
  unitPrice?: number | null;
  materialPrice?: number | null;
  laborPrice?: number | null;
  year?: number | null;
};

/**
 * Şirketin bu poz için geçmiş projelerde gerçekleşen birim maliyeti.
 * Yalnızca poza etiketlenmiş maliyetten hesaplanır; veri yetersizse
 * rakam değil gerekçe döner.
 */
export type CompanyActualAverage = {
  hasEnoughData: boolean;
  projectCount: number;
  averageUnitCost?: number | null;
  minUnitCost?: number | null;
  maxUnitCost?: number | null;
  explanation: string;
};

export type BoqLineProfit = {
  boqItemId: string;
  positionCode: string;
  description: string;
  unit: string;
  sectionId?: string | null;
  contractQuantity: number;
  /** 1 — Sözleşme (gelir). */
  contractUnitPrice: number;
  contractMaterialUnitPrice: number;
  contractLaborUnitPrice: number;
  contractTotal: number;
  /** 2 — Referans. */
  references: ReferencePrice[];
  /** 3 — Şirket gerçekleşmesi. */
  companyAverage: CompanyActualAverage;
  /** 4 — Bu projedeki anlık maliyet. */
  measuredCost: number;
  allocatedCost: number;
  actualCost: number;
  /** Maliyetin ne kadarı ölçüme dayanıyor (0-1). */
  measuredRatio: number;
  profit: number;
  profitMarginPercent?: number | null;
};

export type ProjectProfitBreakdown = {
  projectId: string;
  includesExtraPayments: boolean;
  contractTotal: number;
  actualCostTotal: number;
  measuredCostTotal: number;
  allocatedCostTotal: number;
  unassignedCost: number;
  profit: number;
  profitMarginPercent?: number | null;
  lines: BoqLineProfit[];
  assumptions: string[];
};

export const projectProfitService = {
  get(projectId: string, referenceYear?: number) {
    const query = referenceYear ? `?referenceYear=${referenceYear}` : "";
    return apiClient<ProjectProfitBreakdown>(
      `projects/${projectId}/kar-analizi${query}`
    );
  },
};
