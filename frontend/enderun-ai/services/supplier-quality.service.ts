import { apiClient } from "@/lib/api/api-client";

const root = "purchasing/supplier-quality";

/**
 * Tedarikçi kalite karnesi — yalnızca okur.
 *
 * Kaynak yalnızca KESİNLEŞMİŞ mal kabuller; taslak mal kabul henüz
 * bir teslimat değil. Red oranı da miktar üzerinden hesaplanıyor,
 * teslimat sayısı üzerinden değil. Her iki kural da backend'de;
 * ekran hiçbirini yeniden hesaplamıyor.
 *
 * Yetki: `purchasing.view`.
 */

export interface SupplierQualityRow {
  supplierCurrentAccountId: string;
  supplierTitle: string;
  receiptCount: number;
  problemReceiptCount: number;
  deliveredQuantity: number;
  acceptedQuantity: number;
  rejectedQuantity: number;
  damagedQuantity: number;
  /** (Red + hasar) / gelen, yüzde. */
  rejectionRatePercent: number;
  lastProblemDate: string | null;
  /** Teslim tarihi geçmiş açık sipariş sayısı. */
  lateOrderCount: number;
}

export interface SupplierQualityReport {
  months: number;
  /** Sorunlu olan başta. */
  rows: SupplierQualityRow[];
  /** Red oranı eşiğini aşan tedarikçi sayısı. */
  problemSupplierCount: number;
}

export const supplierQualityService = {
  get(companyId?: string, months?: number) {
    const query = new URLSearchParams();
    if (companyId) query.set("companyId", companyId);
    if (months) query.set("months", String(months));

    const suffix = query.toString();
    return apiClient<SupplierQualityReport>(
      suffix ? `${root}?${suffix}` : root
    );
  },
};
