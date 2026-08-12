import { apiClient } from "@/lib/api/api-client";

const root = "hakedis/files";

/**
 * Hakediş dosyaları: yükleme, listeleme, analiz, indirme, silme.
 *
 * Uçların tamamı hazırdı ve hiçbiri çağrılmıyordu. Yalnız "analiz"i
 * bağlamak işe yaramazdı: kullanıcı analiz edeceği dosyayı hiçbir
 * yerde göremiyordu. Aile bir bütün olarak ekrana bağlandı.
 *
 * ANALİZ KAYIT YAZMAZ — yüklenen PDF/Excel'den proje, dönem, tutar
 * ve stopaj önerisi çıkarır. Çıkanlar ÖNERİDİR; güven skoru ve
 * uyarılar da dönüyor ve ekran ikisini de gösteriyor. Sessizce
 * "kesin bilgi" gibi sunmak, düşük güvenli bir okumanın hakedişe
 * girmesine yol açardı.
 *
 * Yetki: yükleme `hakedis.create`, liste/analiz/indirme
 * `hakedis.view`, silme `hakedis.delete`.
 */

export interface HakedisFile {
  originalName: string;
  storedName: string;
  extension: string;
  contentType: string;
  size: number;
  uploadedAtUtc: string;
}

export interface HakedisAnalysis {
  status: string;
  fileName: string | null;
  project: string | null;
  employer: string | null;
  progressPaymentNo: string | null;
  period: string | null;
  amountExcludingVat: number | null;
  vatRate: number | null;
  vatAmount: number | null;
  suggestedWithholding: string | null;
  /** 0-1 arası; düşükse okuma güvenilmez. */
  confidence: number;
  requiresOcr: boolean;
  extractedText: string;
  warnings: string[];
}

/** Dosya indirme adresi — tarayıcı kendi indirme akışını kullanır. */
export function hakedisFileUrl(storedName: string) {
  return `/api/backend/${root}/${encodeURIComponent(storedName)}`;
}

export const hakedisFileService = {
  list() {
    return apiClient<HakedisFile[]>(root);
  },

  analyze(storedName: string) {
    return apiClient<HakedisAnalysis>(
      `${root}/${encodeURIComponent(storedName)}/analyze`,
      { method: "POST" }
    );
  },

  remove(storedName: string) {
    return apiClient<{ message: string }>(
      `${root}/${encodeURIComponent(storedName)}`,
      { method: "DELETE" }
    );
  },

  /**
   * Dosya yükler ve yüklenen dosyanın analizini birlikte döner.
   *
   * apiClient KULLANILMIYOR: gövdeyi JSON'a çeviriyor ve
   * Content-Type'ı kendisi koyuyor. FormData'da sınırı (boundary)
   * tarayıcı yazmalı, yoksa sunucu dosyayı ayrıştıramaz.
   */
  async upload(file: File) {
    const form = new FormData();
    form.append("file", file);

    const response = await fetch("/api/backend/hakedis/upload", {
      method: "POST",
      body: form,
      cache: "no-store",
    });

    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      throw new Error(
        (payload as { message?: string } | null)?.message ??
          `Dosya yüklenemedi: ${response.status}`
      );
    }

    return payload as { file: HakedisFile; analysis: HakedisAnalysis };
  },
};
