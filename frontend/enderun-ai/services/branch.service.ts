import { apiClient } from "@/lib/api/api-client";

export type BranchListItem = {
  id: string;
  companyId: string;
  companyName?: string;
  code: string;
  name: string;
  isActive?: boolean;
};

export const branchService = {
  getAll(companyId?: string) {
    const query = companyId
      ? `?companyId=${encodeURIComponent(companyId)}`
      : "";
    return apiClient<BranchListItem[]>(`branches${query}`);
  },
};
