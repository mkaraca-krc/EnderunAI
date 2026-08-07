import { apiClient } from "@/lib/api/api-client";

export type EngineeringPositionListItem = {
  id: string;
  companyId: string;
  companyName: string;
  code: string;
  name: string;
  unit: string;
  source: number;
  discipline: number;
  status: number;
  officialInstitution?: string | null;
  officialCode?: string | null;
  category?: string | null;
  revisionNumber: number;
  defaultLaborHours: number;
  defaultHelperHours: number;
  defaultMachineHours: number;
  totalLaborHours: number;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
};

/**
 * Poz kaynağı. Backend'de YALNIZCA İKİ değer var:
 * Official = 0 (resmî kurum kitabı), Enderun = 1 (şirkete özel).
 *
 * Ekranlarda bir dönem beş seçenekli bir liste vardı (Enderun/ÇŞİDB/
 * MSB/TEDAŞ/Özel) ve eşleme TERSTİ: resmî pozlar "Enderun", şirkete
 * özel poz "ÇŞİDB" görünüyordu; 2/3/4 değerleri ise backend'de hiç
 * tanımlı olmadığı için 400 dönüyordu. Kurum bilgisi kaynakta değil,
 * ayrı OfficialInstitution alanında tutuluyor.
 */
export const EngineeringPositionSource = {
  Official: 0,
  Custom: 1,
} as const;

/**
 * Pozun kaynağını kullanıcının dilinde yazar.
 *
 * Resmî pozda kaynak adı KURUMDUR (ÇŞB, TEDAŞ); "Resmî" demek
 * kullanıcıya hangi kitaptan geldiğini söylemezdi.
 */
export function positionSourceLabel(
  source: number,
  officialInstitution?: string | null
) {
  if (source === EngineeringPositionSource.Custom) return "Özel (şirket)";
  return officialInstitution?.trim() || "Resmî kurum";
}

export type EngineeringPositionFilters = {
  search?: string;
  source?: number;
  discipline?: number;
  status?: number;
  /** Kaç kayıt döneceği; uç varsayılan 100, tavan 500 uygular. */
  take?: number;
};

export const engineeringPositionService = {
  getAll(filters: EngineeringPositionFilters = {}) {
    const params = new URLSearchParams();

    if (filters.search?.trim()) {
      params.set("search", filters.search.trim());
    }

    if (filters.source !== undefined) {
      params.set("source", String(filters.source));
    }

    if (filters.discipline !== undefined) {
      params.set("discipline", String(filters.discipline));
    }

    if (filters.status !== undefined) {
      params.set("status", String(filters.status));
    }

    if (filters.take !== undefined) {
      params.set("take", String(filters.take));
    }

    const query = params.toString();

    return apiClient<EngineeringPositionListItem[]>(
      `engineering-positions${query ? `?${query}` : ""}`
    );
  },

  getById(id: string) {
    return apiClient<EngineeringPositionListItem>(
      `engineering-positions/${id}`
    );
  },
};

export type EngineeringPositionDetail = EngineeringPositionListItem & {
  description?: string | null;
  technicalSpecification?: string | null;
  searchKeywords?: string | null;
  approvedAtUtc?: string | null;
  approvedByUserId?: string | null;
};

export type UpdateEngineeringPositionRequest = {
  name: string;
  unit: string;
  discipline: number;
  status: number;
  officialInstitution?: string | null;
  officialCode?: string | null;
  category?: string | null;
  description?: string | null;
  technicalSpecification?: string | null;
  searchKeywords?: string | null;
  defaultLaborHours: number;
  defaultHelperHours: number;
  defaultMachineHours: number;
};

export const engineeringPositionStatusService = {
  /** Poz durumunu değiştirir (0 Taslak, 1 Aktif, 2 Pasif, 3 Arşiv). */
  change(positionId: string, status: number) {
    return apiClient<{ message: string }>(
      `engineering-positions/${positionId}/status`,
      { method: "PATCH", body: { status } }
    );
  },
};

export const engineeringPositionDetailService = {
  getById(id: string) {
    return apiClient<EngineeringPositionDetail>(
      `engineering-positions/${id}`
    );
  },

  update(id: string, payload: UpdateEngineeringPositionRequest) {
    return apiClient<{
      message: string;
      id: string;
      code: string;
      revisionNumber: number;
    }>(`engineering-positions/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
};

export type CreateEngineeringPositionRequest = {
  companyId: string;
  code?: string | null;
  name: string;
  unit: string;
  source: number;
  discipline: number;
  officialInstitution?: string | null;
  officialCode?: string | null;
  category?: string | null;
  description?: string | null;
  technicalSpecification?: string | null;
  searchKeywords?: string | null;
  defaultLaborHours: number;
  defaultHelperHours: number;
  defaultMachineHours: number;
};

export const engineeringPositionCreateService = {
  create(payload: CreateEngineeringPositionRequest) {
    return apiClient<{
      message: string;
      id: string;
      code: string;
      name: string;
      revisionNumber: number;
      status: number;
    }>("engineering-positions", {
      method: "POST",
      body: payload,
    });
  },
};

/**
 * Kütüphanede karşılığı olmayan kalem için şirkete özel poz.
 * Kod verilmezse ENDERUN-xxxx üretilir; girilen fiyat "Şirket" kurumu
 * altına yazılır, resmi kitap fiyatıymış gibi görünmez.
 */
export type CreateCustomPositionRequest = {
  companyId: string;
  name: string;
  unit?: string | null;
  discipline: number;
  code?: string | null;
  category?: string | null;
  notes?: string | null;
  unitPrice?: number | null;
  year?: number | null;
};

export const customPositionService = {
  create(payload: CreateCustomPositionRequest) {
    return apiClient<{
      message: string;
      id: string;
      code: string;
      name: string;
      unit: string;
      institution: string;
      unitPrice?: number | null;
    }>("engineering-positions/custom", {
      method: "POST",
      body: payload,
    });
  },
};

/** Birim fiyatı yayımlayan kurum. */
export const PositionPriceInstitution = {
  Csb: 0,
  Tedas: 1,
  Company: 2,
  Other: 3,
} as const;

export const POSITION_PRICE_INSTITUTION_LABELS: Record<number, string> = {
  [PositionPriceInstitution.Csb]: "ÇŞB",
  [PositionPriceInstitution.Tedas]: "TEDAŞ",
  [PositionPriceInstitution.Company]: "Şirket",
  [PositionPriceInstitution.Other]: "Diğer",
};

export type PositionPriceRow = {
  id: string;
  year: number;
  institution: number;
  institutionName: string;
  unitPrice: number;
  currencyCode: string;
  effectiveFrom?: string | null;
  sourceNote?: string | null;
  createdAtUtc: string;
};

/** Bulunamadıysa found=false döner; sıfır fiyat üretilmez. */
export type PositionPriceResolution = {
  found: boolean;
  unitPrice?: number | null;
  /** Kitap bileşen verdiyse malzeme payı. */
  materialPrice?: number | null;
  /** Kitap bileşen verdiyse montaj payı. */
  laborPrice?: number | null;
  currencyCode?: string | null;
  year?: number | null;
  institution?: number | null;
  institutionName?: string | null;
  sourceNote?: string | null;
  explanation: string;
};

export type UpsertPositionPriceInput = {
  year: number;
  institution: number;
  unitPrice: number;
  currencyCode?: string | null;
  effectiveFrom?: string | null;
  sourceNote?: string | null;
};

export type PositionReferencePrice = {
  institution: number;
  institutionName: string;
  found: boolean;
  unitPrice?: number | null;
  materialPrice?: number | null;
  laborPrice?: number | null;
  year?: number | null;
  explanation: string;
};

export const positionPriceService = {
  /** ÇŞB, TEDAŞ ve şirket fiyatları yan yana. */
  getReferencePrices(positionId: string, year?: number) {
    const query = year ? `?year=${year}` : "";

    return apiClient<PositionReferencePrice[]>(
      `engineering-positions/${positionId}/reference-prices${query}`
    );
  },

  getHistory(positionId: string) {
    return apiClient<PositionPriceRow[]>(
      `engineering-positions/${positionId}/prices`
    );
  },

  resolve(positionId: string, year?: number, institution?: number) {
    const query = new URLSearchParams();
    if (year !== undefined) query.set("year", String(year));
    if (institution !== undefined) query.set("institution", String(institution));

    const suffix = query.toString() ? `?${query.toString()}` : "";

    return apiClient<PositionPriceResolution>(
      `engineering-positions/${positionId}/prices/resolve${suffix}`
    );
  },

  upsert(positionId: string, payload: UpsertPositionPriceInput) {
    return apiClient<PositionPriceRow>(
      `engineering-positions/${positionId}/prices`,
      { method: "PUT", body: payload }
    );
  },

  remove(priceId: string) {
    return apiClient<{ message: string }>(
      `engineering-positions/prices/${priceId}`,
      { method: "DELETE" }
    );
  },
};

export type PositionSuggestion = {
  positionId: string;
  code: string;
  officialCode?: string | null;
  name: string;
  unit: string;
  institution?: string | null;
  category?: string | null;
  score: number;
  matchedTerms: string[];
  unitPrice?: number | null;
  materialPrice?: number | null;
  laborPrice?: number | null;
  priceExplanation?: string | null;
  aiRank?: number | null;
  aiReason?: string | null;
};

export type PositionMatchResult = {
  query: string;
  aiUsed: boolean;
  aiSkippedReason?: string | null;
  suggestions: PositionSuggestion[];
  explanation: string;
  /** Kesinse otomatik seçilebilir; değilse kullanıcı seçmeli. */
  isCertain: boolean;
  certaintyReason?: string | null;
};

export const positionMatchService = {
  suggest(
    companyId: string,
    query: string,
    options: { year?: number; limit?: number; useAi?: boolean } = {}
  ) {
    const params = new URLSearchParams({ companyId, query });

    if (options.year !== undefined) params.set("year", String(options.year));
    if (options.limit !== undefined) params.set("limit", String(options.limit));
    if (options.useAi !== undefined) params.set("useAi", String(options.useAi));

    return apiClient<PositionMatchResult>(
      `engineering-positions/suggest?${params.toString()}`
    );
  },
};
