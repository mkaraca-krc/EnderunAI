import { apiClient } from "@/lib/api/api-client";

export type CurrentAccountListItem = {
  id: string;
  companyId: string;
  code: string;
  title: string;
  roles: number;
  status: number;
};

export const currentAccountService = {
  getAll(companyId?: string) {
    const query = companyId
      ? `?companyId=${encodeURIComponent(companyId)}`
      : "";
    return apiClient<CurrentAccountListItem[]>(`current-accounts${query}`);
  },
};
