import { apiClient } from "@/lib/api/api-client";

export type InventoryItemType = 0 | 1 | 2;

export interface InventoryItemListItem {
  /** Konum — açık bölgede raf ve kat null kalır. */
  zoneName?: string | null;
  shelfCode?: string | null;
  levelCode?: string | null;
  inventoryCategoryId?: string | null;
  categoryLabel?: string | null;

  id: string;
  companyId: string;
  companyName: string;
  code: string;
  name: string;
  category?: string | null;
  brand?: string | null;
  model?: string | null;
  unit: string;
  barcode?: string | null;
  minimumStock: number;
  maximumStock: number;
  type: InventoryItemType;
  isActive: boolean;
  totalStock: number;
  /** Ağırlıklı ortalama birim maliyet (TRY). */
  averageUnitCost: number;
  /** Toplam stok × ortalama maliyet. */
  stockValue: number;
  lastPurchasePrice?: number | null;
  lastPurchaseDate?: string | null;
  vatRate?: number | null;
  preferredSupplierCurrentAccountId?: string | null;
  preferredSupplierTitle?: string | null;
}

export interface InventoryItemWarehouseStock {
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  quantity: number;
}

export interface InventoryItemDetail {
  id: string;
  companyId: string;
  companyName: string;
  code: string;
  name: string;
  category?: string | null;
  brand?: string | null;
  model?: string | null;
  unit: string;
  barcode?: string | null;
  minimumStock: number;
  maximumStock?: number | null;
  type: InventoryItemType;
  isActive: boolean;
  averageUnitCost: number;
  lastPurchasePrice?: number | null;
  lastPurchaseDate?: string | null;
  preferredSupplierCurrentAccountId?: string | null;
  preferredSupplierTitle?: string | null;
  vatRate?: number | null;
  description?: string | null;
  imagePath?: string | null;
  totalStock: number;
  stockValue: number;
  warehouses: InventoryItemWarehouseStock[];
  /**
   * Birim başına bakır (kg). Bakır maruziyeti raporunun tek kaynağı;
   * girilmediği sürece emtia riski boş çalışır.
   */
  copperKgPerUnit?: number | null;
}

export interface CreateInventoryItemRequest {
  companyId: string;

  /**
   * KATEGORİ ZORUNLU (S2). Kartın adı, birimi ve mükerrer imzası
   * buradan türer.
   */
  categoryId: string;

  /**
   * Kategorinin İZİN VERDİĞİ birimlerden biri. Kart açıldıktan sonra
   * DEĞİŞMEZ; hareket girişi bunu kullanır.
   */
  unit: string;

  /**
   * STANDART kategoride seçilen özellik değerleri. Ad ve mükerrer
   * imzası bunlardan üretilir.
   */
  optionIds?: string[];

  /**
   * YALNIZ SERBEST kategoride (dekoratif, özel imalat) zorunlu.
   * STANDART kategoride yok sayılır — ad özelliklerden üretilir.
   */
  name?: string;

  brand?: string;
  model?: string;
  barcode?: string;
  minimumStock: number;
  maximumStock?: number | null;
  type: InventoryItemType;
  preferredSupplierCurrentAccountId?: string | null;
  vatRate?: number | null;
  description?: string | null;
  copperKgPerUnit?: number | null;
}

export interface UpdateInventoryItemRequest {
  name: string;
  category?: string | null;
  brand?: string | null;
  model?: string | null;
  unit: string;
  barcode?: string | null;
  minimumStock: number;
  maximumStock?: number | null;
  type: InventoryItemType;
  isActive: boolean;
  preferredSupplierCurrentAccountId?: string | null;
  vatRate?: number | null;
  /**
   * Birim başına bakır (kg). Bakır maruziyeti raporunun tek kaynağı;
   * girilmediği sürece emtia riski boş çalışır.
   */
  copperKgPerUnit?: number | null;
  description?: string | null;
}

export interface CompanyOption {
  id: string;
  name: string;
}


/** Kategorinin tipi: 0 STANDART (özellikten ad üretilir), 1 SERBEST. */
export type InventoryCategoryKind = 0 | 1;

export type InventoryAttributeOption = {
  id: string;
  value: string;
  /** Ada giren metin ("200mm"); yoksa `value` kullanılır. */
  display: string;
  sortOrder: number;
};

export type InventoryAttribute = {
  id: string;
  code: string;
  name: string;
  isRequired: boolean;
  sortOrder: number;
  options: InventoryAttributeOption[];
};

export type InventoryCategory = {
  id: string;
  code: string;
  name: string;
  kind: InventoryCategoryKind;
  /**
   * MUHASEBE KARŞILIĞI: 0 sarf (150 / 740), 1 ticari mal (153 / 621).
   *
   * Varsayılan sarftır — ağırlıklı taahhüt işi yapıldığı için yeni
   * kategori kendiliğinden "satılabilir mal" sayılmaz. Ticari mal
   * işareti mali müşavir izniyle (accounting.manage) verilir.
   */
  accountingKind: 0 | 1;
  isActive: boolean;
  sortOrder: number;
  /**
   * İZİN VERİLEN birimler. Kart açılırken biri seçilir ve BİR DAHA
   * DEĞİŞMEZ; hareket girişi kartın birimini kullanır.
   */
  units: string[];
  attributes: InventoryAttribute[];
};

export const inventoryService = {
  async getItems(params?: {
    companyId?: string;
    search?: string;
    category?: string;
    warehouseId?: string;
    criticalOnly?: boolean;
    /**
     * ARŞİVLENMİŞ kartları da getirir. Varsayılan KAPALI: seçiciler
     * arşivi görmemeli. Yalnız stok kartı YÖNETİM ekranı açar —
     * orada arşiv görülüp geri açılabilmeli.
     */
    includeInactive?: boolean;
  }): Promise<InventoryItemListItem[]> {
    const query = new URLSearchParams();

    if (params?.companyId) query.set("companyId", params.companyId);
    if (params?.search) query.set("search", params.search);
    if (params?.category) query.set("category", params.category);
    if (params?.warehouseId) query.set("warehouseId", params.warehouseId);
    if (params?.criticalOnly) query.set("criticalOnly", "true");
    if (params?.includeInactive) query.set("includeInactive", "true");

    const suffix = query.size > 0 ? `?${query.toString()}` : "";
    return apiClient<InventoryItemListItem[]>(`inventory/items${suffix}`);
  },

  getItem(id: string) {
    return apiClient<InventoryItemDetail>(`inventory/items/${id}`);
  },

  /**
   * STOK KATEGORİLERİ — özellik şablonu, izin verilen birimler ve tip.
   *
   * SİSTEM GENELİ: kategori şirkete bağlı değil, o yüzden `companyId`
   * almıyor. Eski uç serbest metin `Category` alanından DISTINCT
   * çekiyordu; o alan kategori değildi (canlıda bir kartta "TURAN"
   * yazıyordu, tedarikçi adı).
   */
  getCategories() {
    return apiClient<InventoryCategory[]>("inventory/categories");
  },

  /**
   * Kategorinin muhasebe karşılığını değiştirir. Ayrı uç, ayrı izin:
   * kategori açmak depo sorumlusunun, hangi hesaba yazılacağına karar
   * vermek mali müşavirin işi.
   */
  async setCategoryAccountingKind(
    categoryId: string,
    accountingKind: 0 | 1
  ): Promise<{ message: string; accountingKind: number }> {
    return apiClient(`inventory/categories/${categoryId}/accounting-kind`, {
      method: "PUT",
      body: JSON.stringify({ accountingKind }),
    });
  },

  async createItem(
    payload: CreateInventoryItemRequest
  ): Promise<{ id: string; code: string; name: string; message: string }> {
    return apiClient("inventory/items", {
      method: "POST",
      body: payload,
    });
  },

  updateItem(id: string, payload: UpdateInventoryItemRequest) {
    return apiClient<{ message: string }>(`inventory/items/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  async getCompanies(): Promise<CompanyOption[]> {
    const result = await apiClient<
      CompanyOption[] | { items?: CompanyOption[]; data?: CompanyOption[] }
    >("companies");

    if (Array.isArray(result)) return result;
    return result.items ?? result.data ?? [];
  },

  /**
   * Listedeki satır içi minimum stok düzenlemesi.
   *
   * PUT tüm kartı yazdığı için gönderilmeyen alanlar null'a düşer;
   * tercih edilen tedarikçi, KDV oranı ve açıklama bu yüzden önce
   * karttan okunup aynen geri gönderiliyor. Aksi halde minimum stoğu
   * değiştiren kişi farkında olmadan o üç alanı silerdi.
   */
  async updateMinimumStock(
    item: InventoryItemListItem,
    minimumStock: number
  ): Promise<void> {
    const detail = await inventoryService.getItem(item.id);

    await inventoryService.updateItem(item.id, {
      name: detail.name,
      category: detail.category ?? null,
      brand: detail.brand ?? null,
      model: detail.model ?? null,
      unit: detail.unit,
      barcode: detail.barcode ?? null,
      minimumStock,
      maximumStock: detail.maximumStock ?? null,
      type: detail.type,
      isActive: detail.isActive,
      preferredSupplierCurrentAccountId:
        detail.preferredSupplierCurrentAccountId ?? null,
      vatRate: detail.vatRate ?? null,
      description: detail.description ?? null,
      copperKgPerUnit: detail.copperKgPerUnit ?? null,
    });
  },
};
