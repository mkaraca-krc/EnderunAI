import { apiClient } from "@/lib/api/api-client";


export interface SupplierBalanceSummary {

  supplierId: string;

  supplierName: string;

  totalDebt: number;

  totalPaid: number;

  balance: number;

}


export const supplierBalanceService = {

  getSummary() {

    return apiClient<SupplierBalanceSummary[]>(
      "finance/suppliers-summary"
    );

  }

};
