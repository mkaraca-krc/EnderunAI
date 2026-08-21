import { apiClient } from "@/lib/api/api-client";

export type InventoryItemType = 0 | 1 | 2;

/**
 * TEDARİK TİPİ (S9) — üçü birbirini dışlar.
 *
 * Asgari/azami seviye takibi (S8) yalnız STOKLU kartlarda anlamlı:
 * siparişe göre üretilen bir üründe stok bulundurMAMAK bilinçli
 * karardır, uyarı her gün "eksik" diye bağırırdı.
 */
export type InventorySupplyKind = 0 | 1 | 2;

export const SUPPLY_KIND_LABELS: Record<InventorySupplyKind, string> = {
  0: "Stoklu",
  1: "Özel imalat",
  2: "Sipariş üzerine",
};

export interface InventoryItemPhoto {
  id: string;
  originalName: string;
  contentType: string;
  size: number;
  /** Listede ve etikette gösterilen görsel. Galeri doluysa tam bir tane. */
  isCover: boolean;
  caption?: string | null;
  uploadedAtUtc: string;
}

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
  type: InventoryItemType;
  isActive: boolean;
  /** Kartın açıldığı proje — bağlayıcıdır (S9). */
  projectId?: string | null;
  projectName?: string | null;
  /** 0 Stoklu, 1 Özel imalat, 2 Sipariş üzerine. */
  supplyKind: InventorySupplyKind;
  coverPhotoId?: string | null;
  photoCount: number;
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
  type: InventoryItemType;
  isActive: boolean;
  projectId?: string | null;
  projectName?: string | null;
  supplyKind: InventorySupplyKind;
  coverPhotoId?: string | null;
  photoCount: number;
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
  type: InventoryItemType;
  projectId?: string | null;
  supplyKind?: InventorySupplyKind;
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
  type: InventoryItemType;
  isActive: boolean;
  projectId?: string | null;
  supplyKind?: InventorySupplyKind;
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

export type StockAccountingLine = {
  kind: string;
  stockAccountCode: string;
  /** Depodaki değer: miktar × ağırlıklı ortalama maliyet. */
  stockValue: number;
  /** Mizandaki bakiye: yalnız KESİNLEŞMİŞ fişlerden. */
  accountBalance: number;
  difference: number;
};

export type StockAccountingConsistencyReport = {
  asOfUtc: string;
  lines: StockAccountingLine[];
  /**
   * 379.01 bakiyesi — TUTARSIZLIK DEĞİL: "malı aldık, faturası
   * gelmedi" demek. Kalıcı bakiye eksik fatura takibidir.
   */
  pendingInvoiceBalance: number;
  isConsistent: boolean;
  summary: string;
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
    projectId?: string;
    supplyKind?: number;
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
    if (params?.projectId) query.set("projectId", params.projectId);
    if (params?.supplyKind !== undefined)
      query.set("supplyKind", String(params.supplyKind));
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
   * STOK ↔ MUHASEBE TUTARLILIK RAPORU.
   *
   * Depodaki değer (miktar × ağırlıklı ortalama) ile 150/153
   * hesaplarının mizan bakiyesini karşılaştırır.
   */
  getAccountingConsistency() {
    return apiClient<StockAccountingConsistencyReport>(
      "inventory/accounting-consistency"
    );
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

  getPhotos(itemId: string) {
    return apiClient<InventoryItemPhoto[]>(`inventory/items/${itemId}/fotograflar`);
  },

  /** Görselin ham dosyası — <img src> bunu kullanır. */
  photoUrl(photoId: string) {
    return `/api/backend/inventory/fotograflar/${photoId}/dosya`;
  },

  async addPhoto(itemId: string, file: File, caption?: string) {
    const form = new FormData();
    form.append("file", file);
    if (caption) form.append("caption", caption);

    // FormData gönderiliyor: Content-Type'ı TARAYICI koymalı ki
    // multipart sınırı (boundary) doğru yazılsın. Elle "application/json"
    // yazılsaydı sunucu dosyayı hiç göremezdi.
    const response = await fetch(`/api/backend/inventory/items/${itemId}/fotograflar`, {
      method: "POST",
      credentials: "include",
      body: form,
    });

    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      throw new Error(body.message ?? "Görsel yüklenemedi.");
    }

    return (await response.json()) as InventoryItemPhoto;
  },

  setCoverPhoto(photoId: string) {
    return apiClient<{ message: string }>(`inventory/fotograflar/${photoId}/kapak`, {
      method: "PUT",
    });
  },

  deletePhoto(photoId: string) {
    return apiClient<{ message: string }>(`inventory/fotograflar/${photoId}`, {
      method: "DELETE",
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
};
