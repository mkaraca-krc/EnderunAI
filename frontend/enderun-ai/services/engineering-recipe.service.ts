import { apiClient } from "@/lib/api/api-client";

export type RecipeMaterial = {
  id?: string;
  inventoryItemId?: string | null;
  materialCode: string;
  materialName: string;
  quantity: number;
  unit: string;
  wastePercent: number;
  effectiveQuantity?: number;
  notes?: string | null;
};

export type RecipeLabor = {
  id?: string;
  laborType: number;
  personCount: number;
  hours: number;
  totalHours?: number;
  notes?: string | null;
};

export type RecipeMachine = {
  id?: string;
  machineName: string;
  quantity: number;
  hours: number;
  totalHours?: number;
  notes?: string | null;
};

export type EngineeringRecipeListItem = {
  id: string;
  engineeringPositionId: string;
  positionCode: string;
  positionName: string;
  unit?: string;
  discipline?: number;
  version: number;
  description?: string | null;
  isDefault: boolean;
  materialCount: number;
  laborCount: number;
  machineCount: number;
  totalLaborHours: number;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
};

export type EngineeringRecipeDetail = {
  id: string;
  engineeringPositionId: string;
  positionCode: string;
  positionName: string;
  version: number;
  description?: string | null;
  isDefault: boolean;
  materials: RecipeMaterial[];
  labors: RecipeLabor[];
  machines: RecipeMachine[];
};

export type SaveEngineeringRecipeRequest = {
  description?: string | null;
  isDefault: boolean;
  materials: RecipeMaterial[];
  labors: RecipeLabor[];
  machines: RecipeMachine[];
};

/** Poz kitabının ne kadarının reçetesi var — sayı veritabanından gelir. */
export type EngineeringRecipeCoverage = {
  positionCount: number;
  positionsWithRecipe: number;
  positionsWithoutRecipe: number;
};

export type EngineeringRecipeFilters = {
  search?: string;
  discipline?: number;
  onlyDefault?: boolean;
  take?: number;
};

export const engineeringRecipeService = {
  /**
   * Reçete listesi — TEK çağrı.
   *
   * Liste ekranı eskiden pozları çekip her poz için ayrı reçete isteği
   * atıyordu; poz ucu 100 kayıt döndürdüğü için 23.500 pozun ancak ilk
   * yüzünü tarayabiliyordu. Liste artık reçeteden başlıyor.
   */
  getAll(filters: EngineeringRecipeFilters = {}) {
    const params = new URLSearchParams();

    if (filters.search?.trim()) params.set("search", filters.search.trim());
    if (filters.discipline !== undefined)
      params.set("discipline", String(filters.discipline));
    if (filters.onlyDefault) params.set("onlyDefault", "true");
    if (filters.take) params.set("take", String(filters.take));

    const query = params.toString();

    return apiClient<EngineeringRecipeListItem[]>(
      `engineering-recipes${query ? `?${query}` : ""}`
    );
  },

  getCoverage(companyId?: string) {
    return apiClient<EngineeringRecipeCoverage>(
      `engineering-recipes/coverage${companyId ? `?companyId=${companyId}` : ""}`
    );
  },

  getByPosition(positionId: string) {
    return apiClient<EngineeringRecipeListItem[]>(
      `engineering-recipes/position/${positionId}`
    );
  },

  getById(id: string) {
    return apiClient<EngineeringRecipeDetail>(
      `engineering-recipes/${id}`
    );
  },

  create(
    engineeringPositionId: string,
    payload: SaveEngineeringRecipeRequest
  ) {
    return apiClient<{
      message: string;
      id: string;
      version: number;
      isDefault: boolean;
    }>("engineering-recipes", {
      method: "POST",
      body: {
        engineeringPositionId,
        ...payload,
      },
    });
  },

  update(id: string, payload: SaveEngineeringRecipeRequest) {
    return apiClient<{ message: string; id: string }>(
      `engineering-recipes/${id}`,
      {
        method: "PUT",
        body: payload,
      }
    );
  },
};
