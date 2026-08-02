import { apiClient } from "@/lib/api/api-client";


export interface SupplierBalanceSummary {

  supplierId: string;

  supplierName: string;

  totalDebt: number;

  totalPaid: number;

  balance: number;

}

export interface SupplierBalanceResponse {
  items: SupplierBalanceSummary[];
  // Tedarikçi fatura + ödeme defteri henüz uygulamaya bağlı değil -
  // false ise items her zaman boştur, arayüz "veri yok" göstermeli.
  available: boolean;
  message?: string | null;
}


export const supplierBalanceService = {

  getSummary() {

    return apiClient<SupplierBalanceResponse>(
      "finance/suppliers-summary"
    );

  }

};
