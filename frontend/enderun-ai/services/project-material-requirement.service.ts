import { apiClient } from "@/lib/api/api-client";

/**
 * Proje malzeme ihtiyacı: icmal → reçete → ihtiyaç, sonra depo mevcudu
 * ve açık talepler düşülerek EKSİK.
 *
 * Talep OTOMATİK açılmaz — liste bir öneridir, kullanıcı seçer.
 */

export type ProjectMaterialRequirementLine = {
  inventoryItemId?: string | null;
  materialCode: string;
  materialName: string;
  unit: string;
  /** Reçetelerden çıkan brüt ihtiyaç (fire dahil). */
  requiredQuantity: number;
  stockQuantity: number;
  openRequestedQuantity: number;
  /** ihtiyaç − mevcut − açık talep (negatife düşmez). */
  shortageQuantity: number;
  sourceLineCount: number;
  /** Stok kartına bağlı olmayan malzeme talep edilemez. */
  canRequest: boolean;
};

export type MaterialRequirementIssue = {
  lineNumber: number;
  positionCode?: string | null;
  positionName?: string | null;
  reason: string;
};

export type ProjectMaterialRequirement = {
  projectId: string;
  projectCode: string;
  projectName: string;
  boqId?: string | null;
  boqNumber?: string | null;
  boqStatusName?: string | null;
  positionLineCount: number;
  positionsWithoutRecipe: number;
  includesCentralWarehouse: boolean;
  lines: ProjectMaterialRequirementLine[];
  missingRecipes: MaterialRequirementIssue[];
  unitConflicts: MaterialRequirementIssue[];
  warnings: string[];
};

export type CreateMaterialRequestResult = {
  purchaseRequestId: string;
  requestNumber: string;
  itemCount: number;
  totalQuantity: number;
  /** Talebe girmeyen ya da kırpılan satırlar. */
  adjustments: string[];
};

export const projectMaterialRequirementService = {
  get(projectId: string, includeCentralWarehouse = false) {
    const query = includeCentralWarehouse
      ? "?includeCentralWarehouse=true"
      : "";

    return apiClient<ProjectMaterialRequirement>(
      `projects/${projectId}/material-requirement${query}`
    );
  },

  createRequest(
    projectId: string,
    payload: {
      requestedByName: string;
      neededByDate?: string | null;
      priority: number;
      lines: { inventoryItemId: string; quantity: number }[];
    }
  ) {
    return apiClient<CreateMaterialRequestResult>(
      `projects/${projectId}/material-requirement/create-request`,
      { method: "POST", body: payload }
    );
  },
};
