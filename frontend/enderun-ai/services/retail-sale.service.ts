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
}

export interface RetailSaleListResponse {
  items: RetailSaleRow[];
  /** Elden tutarı gizlenen kayıt sayısı. */
  hiddenCount: number;
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

  reject(id: string, reason: string) {
    return request<{ status: number }>(`/api/perakende/${id}/reddet`, {
      method: "POST",
      body: JSON.stringify({ reason }),
    });
  },
};
