import { apiClient } from "@/lib/api/api-client";
import {
  financeScopeQuery,
  type FinanceScopeFilter,
} from "@/services/finance.service";


export interface SupplierBalanceSummary {

  supplierId: string;

  supplierName: string;

  totalDebt: number;

  totalPaid: number;

  balance: number;

}


export const supplierBalanceService = {

  getSummary(filter: FinanceScopeFilter = {}) {

    return apiClient<SupplierBalanceSummary[]>(
      `finance/suppliers-summary${financeScopeQuery(filter)}`
    );

  }

};
