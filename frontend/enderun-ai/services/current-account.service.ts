import { apiClient } from "@/lib/api/api-client";
import {
  financeScopeQuery,
  type FinanceScopeFilter,
} from "@/services/finance.service";


export interface CurrentAccountListItem {
  id: string;
  companyId: string;
  companyName: string;

  code: string;
  title: string;
  shortName?: string | null;

  roles: number;
  status: number;

  taxOffice?: string | null;
  taxNumber?: string | null;

  authorizedPerson?: string | null;
  phone?: string | null;
  email?: string | null;

  paymentTerm?: string | null;
  creditLimit?: number | null;

  isActive: boolean;
}



export interface CurrentAccountSummary {
  totalReceivable: number;
  totalPayable: number;
  netBalance: number;
  accountCount: number;
}



export const currentAccountService = {


  getAll(companyId?: string) {

    const query = companyId
      ? `?companyId=${encodeURIComponent(companyId)}`
      : "";


    return apiClient<CurrentAccountListItem[]>(
      `current-accounts${query}`
    );

  },


  getSummary(filter: FinanceScopeFilter = {}) {

    return apiClient<CurrentAccountSummary>(
      `finance/cari-summary${financeScopeQuery(filter)}`
    );

  }

};
