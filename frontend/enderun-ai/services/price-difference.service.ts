import { apiClient } from "@/lib/api/api-client";

export enum PriceDifferenceCalculationType {
  PublicContractFormula = 0,
  FixedRate = 1,
  Manual = 2,
}

export interface PriceDifferenceCoefficient {
  id: string;
  a: number;
  b1: number;
  b2: number;
  b3: number;
  b4: number;
  b5: number;
  c: number;
  total: number;
}

export interface PriceDifferenceProfile {
  id: string;
  companyId: string;
  projectId: string;
  profileName: string;
  calculationType: PriceDifferenceCalculationType;
  baseYear: number;
  baseMonth: number;
  currencyCode: string;
  isDefault: boolean;
  isVatIncluded: boolean;
  formulaName?: string | null;
  notes?: string | null;
  coefficient: PriceDifferenceCoefficient;
}

export interface CreatePriceDifferenceProfileRequest {
  companyId: string;
  projectId: string;
  profileName: string;
  calculationType: PriceDifferenceCalculationType;
  baseYear: number;
  baseMonth: number;
  currencyCode: string;
  isDefault: boolean;
  isVatIncluded: boolean;
  formulaName?: string | null;
  notes?: string | null;
  a: number;
  b1: number;
  b2: number;
  b3: number;
  b4: number;
  b5: number;
  c: number;
}

export interface UpdatePriceDifferenceProfileRequest {
  profileName: string;
  calculationType: PriceDifferenceCalculationType;
  baseYear: number;
  baseMonth: number;
  currencyCode: string;
  isDefault: boolean;
  isVatIncluded: boolean;
  formulaName?: string | null;
  notes?: string | null;
  a: number;
  b1: number;
  b2: number;
  b3: number;
  b4: number;
  b5: number;
  c: number;
}

export interface PriceDifferenceIndexPeriod {
  id: string;
  year: number;
  month: number;
  sourceName: string;
  periodLabel?: string | null;
  laborIndex: number;
  fuelIndex: number;
  materialIndex: number;
  machineryIndex: number;
  cementIndex: number;
  otherIndex: number;
  copperIndex: number;
  steelIndex: number;
  electricityIndex: number;
  usdRate: number;
  eurRate: number;
  notes?: string | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface CreatePriceDifferenceIndexRequest {
  year: number;
  month: number;
  sourceName: string;
  periodLabel?: string | null;
  laborIndex: number;
  fuelIndex: number;
  materialIndex: number;
  machineryIndex: number;
  cementIndex: number;
  otherIndex: number;
  copperIndex: number;
  steelIndex: number;
  electricityIndex: number;
  usdRate: number;
  eurRate: number;
  notes?: string | null;
}

export type UpdatePriceDifferenceIndexRequest =
  CreatePriceDifferenceIndexRequest;

export interface PriceDifferenceComponent {
  component: string;
  coefficient: number;
  baseIndex: number;
  currentIndex: number;
  ratio: number;
  weightedValue: number;
}

export interface CalculatePriceDifferenceRequest {
  progressPaymentId: string;
  priceDifferenceProfileId: string;
  baseIndexPeriodId: string;
  currentIndexPeriodId: string;
  baseAmount?: number | null;
  notes?: string | null;
}

export interface PriceDifferenceCalculation {
  id: string;
  progressPaymentId: string;
  priceDifferenceProfileId: string;
  baseIndexPeriodId: string;
  currentIndexPeriodId: string;
  baseAmount: number;
  pn: number;
  delta: number;
  priceDifferenceAmount: number;
  components: PriceDifferenceComponent[];
  notes?: string | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

function buildProfileQuery(filters?: {
  companyId?: string;
  projectId?: string;
}) {
  const query = new URLSearchParams();

  if (filters?.companyId) {
    query.set("companyId", filters.companyId);
  }

  if (filters?.projectId) {
    query.set("projectId", filters.projectId);
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

function buildIndexQuery(filters?: {
  year?: number;
  sourceName?: string;
}) {
  const query = new URLSearchParams();

  if (filters?.year !== undefined) {
    query.set("year", String(filters.year));
  }

  if (filters?.sourceName) {
    query.set("sourceName", filters.sourceName);
  }

  const value = query.toString();
  return value ? `?${value}` : "";
}

export const priceDifferenceService = {
  getProfiles(filters?: {
    companyId?: string;
    projectId?: string;
  }) {
    return apiClient<PriceDifferenceProfile[]>(
      `price-difference-profiles${buildProfileQuery(filters)}`
    );
  },

  getProfile(id: string) {
    return apiClient<PriceDifferenceProfile>(
      `price-difference-profiles/${id}`
    );
  },

  createProfile(
    request: CreatePriceDifferenceProfileRequest
  ) {
    return apiClient<PriceDifferenceProfile>(
      "price-difference-profiles",
      {
        method: "POST",
        body: request,
      }
    );
  },

  updateProfile(
    id: string,
    request: UpdatePriceDifferenceProfileRequest
  ) {
    return apiClient<PriceDifferenceProfile>(
      `price-difference-profiles/${id}`,
      {
        method: "PUT",
        body: request,
      }
    );
  },

  getIndexes(filters?: {
    year?: number;
    sourceName?: string;
  }) {
    return apiClient<PriceDifferenceIndexPeriod[]>(
      `price-difference-indexes${buildIndexQuery(filters)}`
    );
  },

  getIndex(id: string) {
    return apiClient<PriceDifferenceIndexPeriod>(
      `price-difference-indexes/${id}`
    );
  },

  createIndex(
    request: CreatePriceDifferenceIndexRequest
  ) {
    return apiClient<PriceDifferenceIndexPeriod>(
      "price-difference-indexes",
      {
        method: "POST",
        body: request,
      }
    );
  },

  updateIndex(
    id: string,
    request: UpdatePriceDifferenceIndexRequest
  ) {
    return apiClient<PriceDifferenceIndexPeriod>(
      `price-difference-indexes/${id}`,
      {
        method: "PUT",
        body: request,
      }
    );
  },

  calculate(
    request: CalculatePriceDifferenceRequest
  ) {
    return apiClient<PriceDifferenceCalculation>(
      "price-difference-calculations/calculate",
      {
        method: "POST",
        body: request,
      }
    );
  },

  getCalculationByProgressPayment(
    progressPaymentId: string
  ) {
    return apiClient<PriceDifferenceCalculation>(
      `price-difference-calculations/progress-payment/${progressPaymentId}`
    );
  },
};
