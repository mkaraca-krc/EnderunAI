import { apiClient } from "@/lib/api/api-client";

export type EstimatedMaterialCost = {
  recipeMaterialId: string;
  materialCode: string;
  materialName: string;
  recipeQuantity: number;
  wastePercent: number;
  effectiveQuantity: number;
  priceFound: boolean;
  manufacturerPriceListItemId?: string | null;
  manufacturer?: string | null;
  productCode?: string | null;
  brand?: string | null;
  model?: string | null;
  unitPrice: number;
  totalPrice: number;
  currency: string;
};

export type PositionCostEstimate = {
  engineeringPositionId: string;
  positionCode: string;
  positionName: string;
  positionUnit: string;
  engineeringRecipeId: string;
  recipeVersion: number;
  materialCost: number;
  laborHours: number;
  laborCost: number;
  machineHours: number;
  machineCost: number;
  unitCost: number;
  pricedMaterialCount: number;
  unpricedMaterialCount: number;
  materials: EstimatedMaterialCost[];
};

export const offerCostingService = {
  estimatePosition(payload: {
    companyId: string;
    engineeringPositionId: string;
    currency: string;
    laborHourRate: number;
    machineHourRate: number;
  }) {
    return apiClient<PositionCostEstimate>(
      "offer-costing/estimate-position",
      {
        method: "POST",
        body: payload,
      }
    );
  },
};
