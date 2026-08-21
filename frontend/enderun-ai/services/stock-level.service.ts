import { apiClient } from "@/lib/api/api-client";

/**
 * DEPO BAZLI ASGARİ/AZAMİ STOK SEVİYESİ.
 *
 * Seviye stok kartında DEĞİL, depo satırında durur: merkez deposunda
 * bulundurulacak asgari ile biten bir şantiye deposununki aynı sayı
 * olamaz. Satırın varlığı takibin kendisidir — takibi bırakmak için
 * satır silinir, "asgarisi sıfır" diye bir takip yoktur.
 */

export interface StockLevelRow {
  id: string;
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  inventoryItemId: string;
  itemCode: string;
  itemName: string;
  unit: string;
  minimumQuantity: number;
  maximumQuantity?: number | null;
  note?: string | null;
  currentQuantity: number;
  isBelowMinimum: boolean;
  isDepleted: boolean;
  /** Azami − mevcut. Azami tanımsızsa null: miktar tahmin edilmez. */
  suggestedQuantity?: number | null;
  averageUnitCost: number;
  suggestedCost?: number | null;
  preferredSupplierCurrentAccountId?: string | null;
  preferredSupplierTitle?: string | null;
}

export interface SaveStockLevelRequest {
  warehouseId: string;
  inventoryItemId: string;
  minimumQuantity: number;
  maximumQuantity?: number | null;
  note?: string | null;
}

export interface StockLevelPurchaseLine {
  inventoryItemId: string;
  quantity: number;
}

export interface CreatePurchaseRequestFromLevelsRequest {
  warehouseId: string;
  projectId: string;
  requestedByName: string;
  priority: number;
  neededByDate?: string | null;
  description?: string | null;
  lines: StockLevelPurchaseLine[];
}

export const stockLevelService = {
  list(params?: {
    companyId?: string;
    warehouseId?: string;
    belowMinimumOnly?: boolean;
  }) {
    const query = new URLSearchParams();
    if (params?.companyId) query.set("companyId", params.companyId);
    if (params?.warehouseId) query.set("warehouseId", params.warehouseId);
    if (params?.belowMinimumOnly) query.set("belowMinimumOnly", "true");

    const suffix = query.toString();
    return apiClient<StockLevelRow[]>(
      `stock-levels${suffix ? `?${suffix}` : ""}`
    );
  },

  save(payload: SaveStockLevelRequest) {
    return apiClient<{ message: string }>("stock-levels", {
      method: "POST",
      body: payload,
    });
  },

  remove(id: string) {
    return apiClient<{ message: string }>(`stock-levels/${id}`, {
      method: "DELETE",
    });
  },

  createPurchaseRequest(payload: CreatePurchaseRequestFromLevelsRequest) {
    return apiClient<{
      message: string;
      purchaseRequestId: string;
      requestNumber: string;
      lineCount: number;
      totalQuantity: number;
    }>("stock-levels/satin-alma-talebi", {
      method: "POST",
      body: payload,
    });
  },
};
