import { apiClient } from "@/lib/api/api-client";

export type FactoringCalculation = {
  chequeAmount: number;
  commissionRate: number;
  commissionAmount: number;
  bsmvRate: number;
  bsmvAmount: number;
  expenseAmount: number;
  totalDeductionAmount: number;
  netAmount: number;
};

export type FactoringTransaction = FactoringCalculation & {
  id: string;
  companyId: string;
  internalNumber: string;
  chequeId: string;
  chequeNumber: string;
  chequeBankName: string;
  chequeDueDate: string;
  factoringCurrentAccountId?: string | null;
  factoringCurrentAccountTitle?: string | null;
  cashAccountId: string;
  cashAccountName: string;
  projectId?: string | null;
  projectCode?: string | null;
  transactionDate: string;
  currencyCode: string;
  description?: string | null;
  accountingVoucherId?: string | null;
  accountingVoucherNumber?: string | null;
};

export type FactoringPreviewPayload = {
  chequeAmount: number;
  commissionRate?: number | null;
  commissionAmount?: number | null;
  bsmvRate?: number | null;
  expenseAmount: number;
};

export type CreateFactoringPayload = {
  chequeId: string;
  cashAccountId: string;
  factoringCurrentAccountId?: string | null;
  projectId?: string | null;
  transactionDate: string;
  commissionRate?: number | null;
  commissionAmount?: number | null;
  bsmvRate?: number | null;
  expenseAmount: number;
  description?: string | null;
};

export const factoringService = {
  getAll(
    params: {
      companyId?: string;
      projectId?: string;
      startDate?: string;
      endDate?: string;
    } = {}
  ) {
    const query = new URLSearchParams();
    if (params.companyId) query.set("companyId", params.companyId);
    if (params.projectId) query.set("projectId", params.projectId);
    if (params.startDate) query.set("startDate", params.startDate);
    if (params.endDate) query.set("endDate", params.endDate);

    const suffix = query.toString();
    return apiClient<FactoringTransaction[]>(`factoring${suffix ? `?${suffix}` : ""}`);
  },

  preview(payload: FactoringPreviewPayload) {
    return apiClient<FactoringCalculation>("factoring/preview", {
      method: "POST",
      body: payload,
    });
  },

  create(payload: CreateFactoringPayload) {
    return apiClient<FactoringTransaction>("factoring", {
      method: "POST",
      body: payload,
    });
  },
};
