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

export const engineeringRecipeService = {
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
