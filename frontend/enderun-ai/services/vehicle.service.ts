import { apiClient } from "@/lib/api/api-client";

/**
 * Filo: araç kartları, atamalar ve araç masraf dökümü.
 *
 * MASRAF UCU YOK: araç masrafı gider kaydının kendi ucundan girilir,
 * yalnızca aracı işaretlenir. Buradaki döküm o kayıtların filtrelenmiş
 * görünümüdür — ikinci bir toplama kaynağı değil.
 */

export const VehicleType = {
  Car: 0,
  Pickup: 1,
  Van: 2,
  Truck: 3,
  Bus: 4,
  ConstructionMachine: 5,
  Other: 99,
} as const;

export const VEHICLE_TYPE_LABELS: Record<number, string> = {
  0: "Otomobil",
  1: "Pikap",
  2: "Panelvan",
  3: "Kamyon",
  4: "Otobüs / minibüs",
  5: "İş makinesi",
  99: "Diğer",
};

export const VehicleOwnership = { Owned: 0, Rented: 1 } as const;

export const VEHICLE_OWNERSHIP_LABELS: Record<number, string> = {
  0: "Öz mal",
  1: "Kiralık",
};

export const VEHICLE_FUEL_LABELS: Record<number, string> = {
  0: "Dizel",
  1: "Benzin",
  2: "LPG",
  3: "Elektrik",
  4: "Hibrit",
  99: "Diğer",
};

export const VEHICLE_RENT_PERIOD_LABELS: Record<number, string> = {
  0: "Aylık",
  1: "3 aylık",
  2: "Yıllık",
};

export type VehicleAssignmentSummary = {
  id: string;
  projectId?: string | null;
  projectCode?: string | null;
  projectName?: string | null;
  projectSiteId?: string | null;
  driverPersonnelId?: string | null;
  driverName?: string | null;
  startDate: string;
  endDate?: string | null;
  notes?: string | null;
};

export type VehicleListItem = {
  id: string;
  companyId: string;
  plateNumber: string;
  type: number;
  ownership: number;
  brand?: string | null;
  model?: string | null;
  modelYear?: number | null;
  inspectionDueDate?: string | null;
  insuranceRenewalDate?: string | null;
  cascoRenewalDate?: string | null;
  motorTaxDueDate?: string | null;
  nextMaintenanceDate?: string | null;

  /**
   * Yaklaşan/geçmiş yenileme sayısı — SUNUCUDA, bildirim motorunun
   * kendi eşiğiyle hesaplanır. İstemcide hesaplansaydı ikinci bir eşik
   * doğar ve liste "yaklaşıyor" derken bildirim merkezi susardı.
   */
  dueSoonCount: number;

  /** Açık atama; yoksa araç merkez havuzunda ya da hiç atanmamış. */
  currentAssignment?: VehicleAssignmentSummary | null;
};

export type VehicleDetail = VehicleListItem & {
  chassisNumber?: string | null;
  fuelType?: number | null;
  lessorCurrentAccountId?: string | null;
  lessorTitle?: string | null;
  rentAmount?: number | null;
  rentPeriod?: number | null;
  rentDueDay?: number | null;
  purchaseDate?: string | null;
  purchaseCost?: number | null;
  notes?: string | null;
  assignments: VehicleAssignmentSummary[];
};

export type SaveVehiclePayload = {
  companyId: string;
  plateNumber: string;
  type: number;
  ownership: number;
  brand?: string | null;
  model?: string | null;
  chassisNumber?: string | null;
  modelYear?: number | null;
  fuelType?: number | null;
  lessorCurrentAccountId?: string | null;
  rentAmount?: number | null;
  rentPeriod?: number | null;
  rentDueDay?: number | null;
  purchaseDate?: string | null;
  purchaseCost?: number | null;
  inspectionDueDate?: string | null;
  insuranceRenewalDate?: string | null;
  cascoRenewalDate?: string | null;
  motorTaxDueDate?: string | null;
  nextMaintenanceDate?: string | null;
  notes?: string | null;
};

export type AssignVehiclePayload = {
  /** Boşsa araç MERKEZ HAVUZUNA alınır. */
  projectId?: string | null;
  projectSiteId?: string | null;
  driverPersonnelId?: string | null;
  startDate: string;
  notes?: string | null;
  referenceKey?: string | null;
};

export type VehicleExpenseItem = {
  id: string;
  expenseDate: string;
  amount: number;
  description: string;
  categoryName: string;
  centerType: number;
  projectId?: string | null;
  projectCode?: string | null;
  branchId?: string | null;
  branchName?: string | null;
  paymentMethod: number;
};

export type VehicleExpenseList = {
  items: VehicleExpenseItem[];
  total: number;
  /** Elden maskesi nedeniyle gizlenen kalem SAYISI (tutar değil). */
  hiddenCount: number;
};

export const vehicleService = {
  getAll(filters: { companyId?: string; ownership?: number; search?: string } = {}) {
    const params = new URLSearchParams();

    if (filters.companyId) params.set("companyId", filters.companyId);
    if (filters.ownership !== undefined)
      params.set("ownership", String(filters.ownership));
    if (filters.search?.trim()) params.set("search", filters.search.trim());

    const query = params.toString();

    return apiClient<VehicleListItem[]>(`vehicles${query ? `?${query}` : ""}`);
  },

  getById(id: string) {
    return apiClient<VehicleDetail>(`vehicles/${id}`);
  },

  create(payload: SaveVehiclePayload) {
    return apiClient<{ id: string; plateNumber: string }>("vehicles", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: SaveVehiclePayload) {
    return apiClient<{ id: string; plateNumber: string }>(`vehicles/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  assign(id: string, payload: AssignVehiclePayload) {
    return apiClient<{ id: string; projectId?: string | null }>(
      `vehicles/${id}/assignments`,
      { method: "POST", body: payload }
    );
  },

  getExpenses(id: string) {
    return apiClient<VehicleExpenseList>(`vehicles/${id}/expenses`);
  },
};
