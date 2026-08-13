import { uploadImportFile } from "./position-import.service";

/**
 * Reçete toplu aktarımı — poz kitabı aktarımıyla aynı akış:
 * incele → eşle → önizle → aktar.
 *
 * DOSYA İNCELEME AYRI SERVİSTE DEĞİL: sayfa/başlık/örnek satır
 * incelemesi poz aktarımındaki `positionImportService.inspect` ile
 * yapılır — dosya biçimi aynı, ikinci bir uç aynı işi iki yerde
 * tutardı.
 */

export type RecipeImportMapping = {
  sheetName?: string | null;
  headerRowIndex: number;
  positionCodeColumn: number;
  materialNameColumn: number;
  quantityColumn: number;
  unitColumn: number;
  materialCodeColumn?: number | null;
  wastePercentColumn?: number | null;
  notesColumn?: number | null;
};

export type RecipeImportOptions = {
  companyId: string;
  createMissingInventoryItems: boolean;
};

export const RecipeImportAction = {
  Skip: 0,
  UseExistingItem: 1,
  CreateItem: 2,
} as const;

export type RecipeImportPreviewRow = {
  rowNumber: number;
  positionCode?: string | null;
  positionName?: string | null;
  materialCode?: string | null;
  materialName?: string | null;
  quantity?: number | null;
  unit?: string | null;
  wastePercent: number;
  action: number;
  actionName: string;
  error?: string | null;
  /** Poz kodu bu satırda yazmıyordu, üstten devralındı. */
  positionCodeInherited: boolean;
  existingItemUnit?: string | null;
};

export type RecipeImportPositionSummary = {
  positionCode: string;
  positionName?: string | null;
  positionFound: boolean;
  materialCount: number;
  currentVersion: number;
};

export type RecipeImportPreview = {
  totalRows: number;
  validRows: number;
  invalidRows: number;
  positionCount: number;
  missingPositionCount: number;
  newInventoryItemCount: number;
  inheritedPositionCodeCount: number;
  fileWarnings: string[];
  positions: RecipeImportPositionSummary[];
  rows: RecipeImportPreviewRow[];
};

export type RecipeImportCommitResult = {
  createdRecipes: number;
  createdInventoryItems: number;
  importedMaterials: number;
  skippedRows: number;
  message: string;
};

function fields(
  mapping: RecipeImportMapping,
  options: RecipeImportOptions
): Record<string, string> {
  return {
    mapping: JSON.stringify(mapping),
    options: JSON.stringify(options),
  };
}

export const recipeImportService = {
  preview(
    file: File,
    mapping: RecipeImportMapping,
    options: RecipeImportOptions
  ) {
    return uploadImportFile<RecipeImportPreview>(
      "recipe-import/preview",
      file,
      fields(mapping, options)
    );
  },

  commit(
    file: File,
    mapping: RecipeImportMapping,
    options: RecipeImportOptions
  ) {
    return uploadImportFile<RecipeImportCommitResult>(
      "recipe-import/commit",
      file,
      fields(mapping, options)
    );
  },
};
