import { apiClient, ApiError } from "@/lib/api/api-client";

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
/**
 * Dosya yükleyen aktarım uçları için ortak çağrı. Reçete aktarımı da
 * bunu kullanır — oturum sonu ve hata mesajı yorumlama kuralı tek
 * yerde kalsın diye ikinci bir kopya yazılmadı.
 */
export async function uploadImportFile<T>(
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

/** ÇŞB / TEDAŞ gibi düzeni bilinen kitaplar için hazır eşleme. */
export type BookImportProfile = {
  key: string;
  name: string;
  description: string;
  institution: number;
  defaultDiscipline: number;
  /** "xlsx" ya da "pdf". */
  fileKind: string;
};

export type BookImportSummary = {
  profileKey: string;
  profileName: string;
  parsedRows: number;
  groupHeaders: number;
  /** Okunuşundan emin olunamayan satırlar — uydurma yapılmadı. */
  suspiciousRows: number;
  createdPositions: number;
  updatedPositions: number;
  upsertedPrices: number;
  inheritedUnits: number;
  suspiciousLines: string[];
  warnings: string[];
  message: string;
};

export const bookImportService = {
  getProfiles() {
    return apiClient<BookImportProfile[]>(
      "engineering-positions/import/profiles"
    );
  },

  preview(file: File, fields: BookImportFields) {
    return uploadImportFile<BookImportSummary>(
      "engineering-positions/import/profile/preview",
      file,
      toFormFields(fields)
    );
  },

  commit(file: File, fields: BookImportFields) {
    return uploadImportFile<BookImportSummary>(
      "engineering-positions/import/profile/commit",
      file,
      toFormFields(fields)
    );
  },
};

export type BookImportFields = {
  profileKey: string;
  companyId: string;
  year: number;
  sourceNote?: string | null;
  /** Yalnızca bu ön ekle başlayan pozlar aktarılır (örn. 35.). */
  codePrefix?: string | null;
};

function toFormFields(fields: BookImportFields): Record<string, string> {
  const form: Record<string, string> = {
    profileKey: fields.profileKey,
    companyId: fields.companyId,
    year: String(fields.year),
  };

  if (fields.sourceNote?.trim()) form.sourceNote = fields.sourceNote.trim();
  if (fields.codePrefix?.trim()) form.codePrefix = fields.codePrefix.trim();

  return form;
}

export const positionImportService = {
  inspect(file: File, sheetName?: string, headerRow?: number) {
    const query = new URLSearchParams();
    if (sheetName) query.set("sheetName", sheetName);
    if (headerRow) query.set("headerRow", String(headerRow));

    const suffix = query.toString() ? `?${query.toString()}` : "";

    return uploadImportFile<SpreadsheetInspection>(
      `engineering-positions/import/inspect${suffix}`,
      file
    );
  },

  preview(
    file: File,
    mapping: PositionImportMapping,
    options: PositionImportOptions
  ) {
    return uploadImportFile<PositionImportPreview>(
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
    return uploadImportFile<PositionImportCommitResult>(
      "engineering-positions/import/commit",
      file,
      { mapping: JSON.stringify(mapping), options: JSON.stringify(options) }
    );
  },
};
