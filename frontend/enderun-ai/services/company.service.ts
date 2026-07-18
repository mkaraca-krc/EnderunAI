import { apiClient } from "@/lib/api/api-client";

export type CompanyListItem = {
  id: string;
  code: string;
  name: string;
  isActive?: boolean;
};

export const companyService = {
  getAll() {
    return apiClient<CompanyListItem[]>("companies");
  },
};
