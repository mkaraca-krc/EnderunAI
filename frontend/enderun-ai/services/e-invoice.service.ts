import { ApiError, apiClient } from "@/lib/api/api-client";

/** Faturanın bizim açımızdan yönü — VKN'den belirlenir. */
export const InvoiceDirection = {
  Unknown: 0,
  Purchase: 1,
  Sales: 2,
} as const;

export const DIRECTION_COLORS: Record<number, string> = {
  0: "gray",
  1: "yellow",
  2: "green",
};

export type ImportPreviewLine = {
  description: string;
  quantity: number;
  unit: string;
  unitPrice: number;
  vatRate: number;
  lineSubtotal: number;
  vatAmount: number;
};

export type ImportPreviewItem = {
  fileName: string;
  canImport: boolean;
  direction: number;
  directionName: string;
  invoiceNumber?: string | null;
  issueDate?: string | null;
  counterpartyTaxNumber?: string | null;
  counterpartyName?: string | null;
  matchedCurrentAccountId?: string | null;
  matchedCurrentAccountTitle?: string | null;
  subtotal: number;
  vatTotal: number;
  withholdingAmount: number;
  grandTotal: number;
  lines: ImportPreviewLine[];
  parseSource: number;
  parseSourceName: string;
  requiresManualReview: boolean;
  duplicateOfId?: string | null;
  problems: string[];
  token: string;
};

export type ImportSkippedFile = {
  fileName: string;
  reason: string;
};

export type ImportPreviewResult = {
  totalFiles: number;
  readableCount: number;
  skippedCount: number;
  items: ImportPreviewItem[];
  skipped: ImportSkippedFile[];
};

export type ImportCommitItem = {
  token: string;
  currentAccountId?: string | null;
  createCurrentAccount: boolean;
  projectId?: string | null;
};

export type ImportCommitCreated = {
  fileName: string;
  direction: number;
  directionName: string;
  invoiceId: string;
  internalNumber: string;
  invoiceNumber?: string | null;
  currentAccountTitle: string;
  currentAccountCreated: boolean;
  grandTotal: number;
  requiresManualReview: boolean;
};

export type ImportCommitResult = {
  createdCount: number;
  skippedCount: number;
  created: ImportCommitCreated[];
  skipped: ImportSkippedFile[];
};

const root = "e-invoice/import";

export const eInvoiceService = {
  /**
   * Dosyaları okur ve önizleme döner — hiçbir kayıt yazmaz.
   * JSON gövde göndermediğimiz için apiClient yerine doğrudan fetch:
   * FormData'nın kendi content-type sınırını (boundary) koruması gerekir.
   */
  async preview(companyId: string, files: File[]): Promise<ImportPreviewResult> {
    const formData = new FormData();
    files.forEach((file) => formData.append("files", file));

    const response = await fetch(
      `/api/backend/${root}/preview?companyId=${encodeURIComponent(companyId)}`,
      { method: "POST", body: formData, cache: "no-store" }
    );

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
          : `Dosyalar okunamadı: ${response.status}`;

      throw new ApiError(message, response.status, payload);
    }

    return payload as ImportPreviewResult;
  },

  commit(companyId: string, items: ImportCommitItem[]) {
    return apiClient<ImportCommitResult>(
      `${root}/commit?companyId=${encodeURIComponent(companyId)}`,
      { method: "POST", body: { items } }
    );
  },
};
