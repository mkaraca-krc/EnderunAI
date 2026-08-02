import { apiClient } from "@/lib/api/api-client";

export interface DashboardInventoryMovement {
  id: string;
  warehouseName: string;
  itemCode: string;
  itemName: string;
  projectName?: string | null;
  type: number;
  quantity: number;
  referenceNumber: string;
  movementDate: string;
}

export const dashboardInventoryMovementService = {
  getAll() {
    return apiClient<DashboardInventoryMovement[]>(
      "inventory/movements"
    );
  },
};
