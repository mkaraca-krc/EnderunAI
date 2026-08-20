import { apiClient } from "@/lib/api/api-client";

/**
 * DÖNEMSEL SAYIM.
 *
 * Sayım oturumu bir belge: açılır, sayılır, onaya gider, yetkili
 * onaylar ve ancak o zaman stok ile muhasebe değişir. Ara adımların
 * hiçbiri stoğa dokunmaz — bu yüzden yarım kalan sayım zarar vermez.
 */

export const STOCK_COUNT_STATUS: Record<number, string> = {
  0: "Sayımda",
  1: "Onay bekliyor",
  2: "Onaylandı",
  3: "Reddedildi",
  4: "İptal",
};

/**
 * Fark gerekçeleri. Serbest metin DEĞİL: "hangi depoda ne kadar fire
 * var" sorusu ancak gerekçe sayılabilirse cevaplanır.
 */
export const VARIANCE_REASON: Record<number, string> = {
  0: "Fire",
  1: "Kayıp",
  2: "Sayım hatası",
  3: "Kırılma",
};

export interface StockCountRow {
  id: string;
  documentNumber: string;
  name: string;
  countDate: string;
  status: number;
  warehouseId: string;
  warehouseName: string;
  warehouseZoneId?: string | null;
  zoneName?: string | null;
  lineCount: number;
  countedCount: number;
  varianceCount: number;
  accountingVoucherId?: string | null;
  decisionReason?: string | null;
}

export interface StockCountLineRow {
  id: string;
  inventoryItemId: string;
  code: string;
  name: string;
  unit: string;
  barcode?: string | null;
  categoryName?: string | null;
  zoneName?: string | null;
  systemQuantity: number;
  countedQuantity?: number | null;
  unitCostAtCount: number;
  varianceReason?: number | null;
  note?: string | null;
}

export interface StockCountDetail extends StockCountRow {
  submittedAtUtc?: string | null;
  decidedAtUtc?: string | null;
  lines: StockCountLineRow[];
}

export interface VarianceGroup {
  lines: number;
  value: number;
}

export interface StockCountVarianceReport {
  documentNumber: string;
  name: string;
  countDate: string;
  warehouseName: string;
  totalLines: number;
  countedLines: number;
  /** Sayılmayan satır sayısı — atlandığı SESSİZ kalmasın diye. */
  uncountedLines: number;
  varianceLines: number;
  shortageValue: number;
  surplusValue: number;
  netValue: number;
  byZone: (VarianceGroup & { zone: string })[];
  byCategory: (VarianceGroup & { category: string })[];
  byReason: (VarianceGroup & { reason?: number | null; reasonLabel: string })[];
}

export const stockCountService = {
  getAll(params?: { warehouseId?: string; status?: number }) {
    const query = new URLSearchParams();
    if (params?.warehouseId) query.set("warehouseId", params.warehouseId);
    if (params?.status !== undefined) query.set("status", String(params.status));
    const suffix = query.size > 0 ? `?${query.toString()}` : "";
    return apiClient<StockCountRow[]>(`stock-counts${suffix}`);
  },

  getById(id: string) {
    return apiClient<StockCountDetail>(`stock-counts/${id}`);
  },

  getVarianceReport(id: string) {
    return apiClient<StockCountVarianceReport>(`stock-counts/${id}/fark-raporu`);
  },

  start(payload: {
    companyId: string;
    warehouseId: string;
    warehouseZoneId?: string | null;
    name: string;
    countDate: string;
  }) {
    return apiClient<{ id: string; documentNumber: string; lineCount: number; message: string }>(
      "stock-counts",
      { method: "POST", body: JSON.stringify(payload) }
    );
  },

  saveCounts(
    id: string,
    lines: {
      lineId: string;
      countedQuantity?: number | null;
      varianceReason?: number | null;
      note?: string | null;
    }[]
  ) {
    return apiClient<{ message: string }>(`stock-counts/${id}/miktarlar`, {
      method: "PUT",
      body: JSON.stringify({ lines }),
    });
  },

  submit(id: string) {
    return apiClient<{ message: string }>(`stock-counts/${id}/onaya-gonder`, { method: "POST" });
  },

  approve(id: string) {
    return apiClient<{ accountingVoucherId?: string | null; message: string }>(
      `stock-counts/${id}/onayla`,
      { method: "POST" }
    );
  },

  reject(id: string, reason: string) {
    return apiClient<{ message: string }>(`stock-counts/${id}/reddet`, {
      method: "POST",
      body: JSON.stringify({ reason }),
    });
  },

  cancel(id: string, reason: string) {
    return apiClient<{ message: string }>(`stock-counts/${id}/iptal`, {
      method: "POST",
      body: JSON.stringify({ reason }),
    });
  },
};
