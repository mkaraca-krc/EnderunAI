/**
 * Perakende satış servisi.
 *
 * ÜRÜN ARAMASI DAR BİR UÇTAN GELİYOR: maliyet alanı hiç dönmüyor.
 * Stok ekranlarının kullandığı genel uç (`/api/inventory`) maliyeti ve
 * stok değerini döndürmeye devam ediyor — orayı satın alma ve muhasebe
 * okuyor. Satış personeli o uca yetkili değil.
 */

export interface RetailProduct {
  id: string;
  code: string;
  name: string;
  unit: string;
  barcode?: string | null;
  salesPrice: number;
  maxDiscountRate: number;
  vatRate: number;
  /** Fiili stok eksi onay bekleyen fişlerdeki miktar. */
  available: number;
}

export interface RetailSaleRow {
  id: string;
  documentNumber: string;
  saleDate: string;
  status: number;
  paymentMethod: number;
  dueDate?: string | null;
  customerTitle?: string | null;
  grandTotal: number;
  recordedAmount: number;
  /** Yetkisiz kullanıcıda null gelir — maskelenmiştir, sıfır değildir. */
  cashAmount?: number | null;
  approvalReason?: string | null;
  decisionReason?: string | null;
  salesInvoiceId?: string | null;
  /**
   * Fiş kârı (matrah − dondurulmuş maliyet). İki hâlde null gelir:
   * maliyet görme yetkisi yoksa MASKELENMİŞTİR, S5 öncesi fişlerde ise
   * maliyet hiç yazılmamıştır. İkisini `profitHidden` ayırır.
   */
  profit?: number | null;
}

export interface RetailSaleListResponse {
  items: RetailSaleRow[];
  /** Elden tutarı gizlenen kayıt sayısı. */
  hiddenCount: number;
  /** Kâr sütunu yetki nedeniyle tamamen gizlendi mi. */
  profitHidden?: boolean;
}

export interface RetailSaleLineRequest {
  inventoryItemId: string;
  quantity: number;
  discountRate: number;
}

export interface CreateRetailSaleRequest {
  companyId: string;
  warehouseId: string;
  saleDate: string;
  customerCurrentAccountId?: string | null;
  walkInCustomerName?: string | null;
  paymentMethod: number;
  dueDate?: string | null;
  overallDiscountRate: number;
  cashAmount: number;
  cashAccountId?: string | null;
  items: RetailSaleLineRequest[];
}

export const RETAIL_STATUS: Record<number, string> = {
  0: "Taslak",
  1: "Finans onayı bekliyor",
  2: "Tamamlandı",
  3: "Reddedildi",
  4: "İptal",
};

export const RETAIL_PAYMENT: Record<number, string> = {
  0: "Nakit",
  1: "Kredi kartı",
  2: "Çek",
  3: "Vadeli",
};

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(`/api/backend/${path.replace(/^\/api\//, "")}`, {
    ...options,
    credentials: "include",
    headers: { "Content-Type": "application/json", ...(options.headers ?? {}) },
  });

  if (!response.ok) {
    let message = "İşlem sırasında hata oluştu.";
    try {
      message = ((await response.json()) as { message?: string }).message ?? message;
    } catch {
      // gövde okunamadıysa genel mesaj kalır
    }
    throw new Error(message);
  }

  return (await response.json()) as T;
}

export const retailSaleService = {
  /**
   * QR ETİKETİNDEN TEK KART. Etikette kart sayfasının URL'i var;
   * okutulunca oradan çıkan kimlik buraya geliyor. Kimlik metin olarak
   * aratılsaydı kod/ad/barkodun hiçbiriyle eşleşmez, etiket sessizce
   * çalışmazdı.
   */
  productById(warehouseId: string, itemId: string) {
    const query = new URLSearchParams({ warehouseId, itemId }).toString();
    return request<RetailProduct[]>(`/api/perakende/urunler?${query}`);
  },

  products(warehouseId: string, search: string) {
    const query = new URLSearchParams({ warehouseId });
    if (search.trim()) query.set("search", search.trim());

    return request<RetailProduct[]>(`/api/perakende/urunler?${query}`);
  },

  list(status?: number) {
    const query = status === undefined ? "" : `?status=${status}`;
    return request<RetailSaleListResponse>(`/api/perakende${query}`);
  },

  create(payload: CreateRetailSaleRequest) {
    return request<{ id: string; documentNumber: string; grandTotal: number }>(
      "/api/perakende",
      { method: "POST", body: JSON.stringify(payload) },
    );
  },

  submit(id: string) {
    return request<{ status: number; approvalReason?: string | null }>(
      `/api/perakende/${id}/gonder`,
      { method: "POST" },
    );
  },

  approve(id: string) {
    return request<{ status: number }>(`/api/perakende/${id}/onayla`, { method: "POST" });
  },

  items(id: string) {
    return request<RetailSaleItemRow[]>(`/api/perakende/${id}/kalemler`);
  },

  cancel(id: string, reason: string) {
    return request<{ status: number }>(`/api/perakende/${id}/iptal`, {
      method: "POST",
      body: JSON.stringify({ reason }),
    });
  },

  createReturn(id: string, reason: string, items: { retailSaleItemId: string; quantity: number }[]) {
    return request<{ id: string; documentNumber: string }>(`/api/perakende/${id}/iade`, {
      method: "POST",
      body: JSON.stringify({ reason, items }),
    });
  },

  reject(id: string, reason: string) {
    return request<{ status: number }>(`/api/perakende/${id}/reddet`, {
      method: "POST",
      body: JSON.stringify({ reason }),
    });
  },
};

export interface RetailSaleItemRow {
  id: string;
  description: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  discountRate: number;
  lineTotal: number;
  /** Dondurulmuş satır maliyeti. Yetki yoksa null. */
  lineCost?: number | null;
  /** Matrah − maliyet. Yetki yoksa null. */
  lineProfit?: number | null;
  /** Daha önce iade edilen miktar — kalan iade edilebilir buradan çıkar. */
  alreadyReturned: number;
}

export interface RetailPricingRow {
  id: string;
  code: string;
  name: string;
  unit: string;
  salesPrice?: number | null;
  maxDiscountRate: number;
  /** `inventory.view` yoksa null gelir — maskelenmiştir, sıfır değildir. */
  averageUnitCost?: number | null;
}

export interface RetailPricingResponse {
  items: RetailPricingRow[];
  /** Maliyeti gizlenen kalem sayısı. */
  hiddenCount: number;
}

export const retailPricingService = {
  list(search: string) {
    const query = search.trim() ? `?search=${encodeURIComponent(search.trim())}` : "";
    return request<RetailPricingResponse>(`/api/perakende/fiyatlar${query}`);
  },

  save(rows: { inventoryItemId: string; salesPrice: number | null; maxDiscountRate: number }[]) {
    return request<{ updated: number }>("/api/perakende/fiyatlar", {
      method: "PUT",
      body: JSON.stringify(rows),
    });
  },
};

export interface DayEndReport {
  date: string;
  cash: number;
  card: number;
  cheque: number;
  term: number;
  recordedTotal: number;
  /** Yetkisiz kullanıcıda null — maskelenmiştir. */
  cashAmount?: number | null;
  hiddenCount: number;
  saleCount: number;
  returnCount: number;
}

export interface StaffSalesRow {
  userId?: string | null;
  fullName: string;
  saleCount: number;
  total: number;
  discountTotal: number;
  discountRate: number;
  approvalCount: number;
}

export interface OpenReceivableRow {
  id: string;
  documentNumber: string;
  saleDate: string;
  dueDate?: string | null;
  paymentMethod: number;
  customerTitle?: string | null;
  remaining: number;
  isOverdue: boolean;
}

export const retailReportService = {
  dayEnd(companyId: string, date: string) {
    return request<DayEndReport>(
      `/api/perakende/raporlar/gun-sonu?companyId=${companyId}&date=${date}`,
    );
  },

  byStaff(companyId: string, from: string, to: string) {
    return request<StaffSalesRow[]>(
      `/api/perakende/raporlar/personel?companyId=${companyId}&from=${from}&to=${to}`,
    );
  },

  openReceivables(companyId: string) {
    return request<OpenReceivableRow[]>(
      `/api/perakende/raporlar/acik-vade?companyId=${companyId}`,
    );
  },
};
