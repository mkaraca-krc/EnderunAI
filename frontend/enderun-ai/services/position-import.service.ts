import { ApiError } from "@/lib/api/api-client";

export type SpreadsheetInspection = {
  sheetNames: string[];
  sheetName: string;
  headerRowIndex: number;
  headers: string[];
  sampleRows: string[][];
  totalRowCount: number;
};

/** Sütun numaraları 1 tabanlı; 0 "eşlenmedi" demek. */
export type PositionImportMapping = {
  sheetName?: string | null;
  headerRowIndex: number;
  codeColumn: number;
  nameColumn: number;
  unitColumn: number;
  priceColumn: number;
  categoryColumn?: number | null;
  descriptionColumn?: number | null;
};

export type PositionImportOptions = {
  companyId: string;
  year: number;
  institution: number;
  discipline: number;
  sourceNote?: string | null;
};

export const PositionImportAction = {
  Skip: 0,
  CreatePosition: 1,
  AddPrice: 2,
  UpdatePositionAndPrice: 3,
} as const;

export type PositionImportPreviewRow = {
  rowNumber: number;
  code?: string | null;
  name?: string | null;
  unit?: string | null;
  unitPrice?: number | null;
  action: number;
  actionName: string;
  error?: string | null;
  /** Tanım değişecekse eski hâli — sessiz değişiklik olmasın. */
  existingName?: string | null;
};

export type PositionImportPreview = {
  totalRows: number;
  validRows: number;
  invalidRows: number;
  newPositions: number;
  priceUpdates: number;
  descriptionChanges: number;
  fileWarnings: string[];
  rows: PositionImportPreviewRow[];
};

export type PositionImportCommitResult = {
  createdPositions: number;
  updatedPositions: number;
  upsertedPrices: number;
  skippedRows: number;
  message: string;
};

/**
 * Dosya her adımda yeniden gönderilir — sunucuda geçici dosya
 * tutulmuyor. FormData gönderildiği için apiClient yerine doğrudan
 * fetch: tarayıcının kendi content-type sınırını koruması gerekiyor.
 */
async function upload<T>(
  path: string,
  file: File,
  extra?: Record<string, string>
): Promise<T> {
  const formData = new FormData();
  formData.append("file", file);

  for (const [key, value] of Object.entries(extra ?? {})) {
    formData.append(key, value);
  }

  const response = await fetch(`/api/backend/${path}`, {
    method: "POST",
    body: formData,
    cache: "no-store",
  });

  if (response.status === 401) {
    if (typeof window !== "undefined") {
      window.location.href = "/login";
    }
    throw new ApiError("Oturum süresi doldu.", 401);
  }

  const payload = await response.json().catch(() => null);

  if (!response.ok) {
    const message =
      payload && typeof payload === "object" && "message" in payload
        ? String((payload as { message?: unknown }).message)
        : `Dosya işlenemedi: ${response.status}`;

    throw new ApiError(message, response.status, payload);
  }

  return payload as T;
}

export const positionImportService = {
  inspect(file: File, sheetName?: string, headerRow?: number) {
    const query = new URLSearchParams();
    if (sheetName) query.set("sheetName", sheetName);
    if (headerRow) query.set("headerRow", String(headerRow));

    const suffix = query.toString() ? `?${query.toString()}` : "";

    return upload<SpreadsheetInspection>(
      `engineering-positions/import/inspect${suffix}`,
      file
    );
  },

  preview(
    file: File,
    mapping: PositionImportMapping,
    options: PositionImportOptions
  ) {
    return upload<PositionImportPreview>(
      "engineering-positions/import/preview",
      file,
      { mapping: JSON.stringify(mapping), options: JSON.stringify(options) }
    );
  },

  commit(
    file: File,
    mapping: PositionImportMapping,
    options: PositionImportOptions
  ) {
    return upload<PositionImportCommitResult>(
      "engineering-positions/import/commit",
      file,
      { mapping: JSON.stringify(mapping), options: JSON.stringify(options) }
    );
  },
};
