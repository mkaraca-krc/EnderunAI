import { apiClient } from "@/lib/api/api-client";

export type BranchListItem = {
  id: string;
  companyId: string;
  companyName?: string;
  code: string;
  name: string;
  /** Merkez ofis. Şirket başına tek şube bu bayrağı taşır. */
  isHeadOffice?: boolean;
  /**
   * Muhasebe masraf merkezi kodu. Boş bırakılmışsa şube kodu döner —
   * backend bu düşmeyi kendisi yapar.
   */
  costCenterCode?: string | null;
  address?: string | null;
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
