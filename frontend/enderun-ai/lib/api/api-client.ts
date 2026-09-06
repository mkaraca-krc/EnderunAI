export class ApiError extends Error {
  status: number;
  payload?: unknown;

  constructor(message: string, status: number, payload?: unknown) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.payload = payload;
  }
}

type ApiOptions = Omit<RequestInit, "body"> & {
  body?: unknown;
};

export async function apiClient<T>(
  path: string,
  options: ApiOptions = {}
): Promise<T> {
  const response = await fetch(`/api/backend/${path.replace(/^\/+/, "")}`, {
    ...options,
    cache: "no-store",
    headers: {
      ...(options.body ? { "Content-Type": "application/json" } : {}),
      ...(options.headers ?? {}),
    },
    body:
      options.body === undefined
        ? undefined
        : JSON.stringify(options.body),
  });

  if (response.status === 401) {
    if (typeof window !== "undefined") {
      window.location.href = "/login";
    }
    throw new ApiError("Oturum süresi doldu.", 401);
  }

  const contentType = response.headers.get("content-type") ?? "";
  const payload = contentType.includes("application/json")
    ? await response.json().catch(() => null)
    : await response.text().catch(() => "");

  if (!response.ok) {
    throw new ApiError(hataMesaji(payload, response.status), response.status, payload);
  }

  return payload as T;
}

/**
 * SUNUCUNUN SÖYLEDİĞİNİ KULLANICIYA GÖSTER.
 *
 * ═══ NEDEN YAZILDI (2026-09-06) ═══
 *
 * Ekranda "İşlem başarısız: 400" yazıyordu ve bu cümle kullanıcıya
 * hiçbir şey söylemiyor. Sunucu susmuyordu — ASP.NET model bağlama
 * hatasında `ProblemDetails` döndürüyor: `title`, `errors`, `traceId`.
 * Ama `message` alanı YOK, ve eski kod yalnız `message`a bakıyordu.
 * Anlamlı cevap elimizdeydi, okumuyorduk.
 *
 * ÜÇ BİÇİM, ÜÇÜ DE OKUNUYOR — geniş yakalamak yerine SIRAYLA:
 *   1. `message` — bu uygulamanın kendi iş kuralı hataları
 *   2. `errors`  — ProblemDetails alan doğrulaması (en yararlısı:
 *                  hangi alanın neden reddedildiğini söyler)
 *   3. `title`   — ProblemDetails genel başlığı
 *
 * SON ÇARE HÂLÂ DURUM KODU: hiçbiri yoksa kullanıcıya bir şey demek
 * gerek. Ama artık "hiçbiri yok" gerçekten nadir bir durum, varsayılan
 * değil.
 */
function hataMesaji(payload: unknown, status: number): string {
  if (typeof payload !== "object" || payload === null) {
    return `İşlem başarısız: ${status}`;
  }

  const p = payload as Record<string, unknown>;

  if (typeof p.message === "string" && p.message.trim().length > 0) {
    return p.message;
  }

  // ProblemDetails.errors: { "alan": ["sebep", ...] }
  if (typeof p.errors === "object" && p.errors !== null) {
    const satirlar = Object.entries(p.errors as Record<string, unknown>)
      .map(([alan, sebepler]) => {
        const metin = Array.isArray(sebepler)
          ? sebepler.map(String).join(" ")
          : String(sebepler);

        // Alan adı ".body" gibi teknik olabilir; boşsa yalnız sebebi yaz.
        return alan && alan !== "$" ? `${alan}: ${metin}` : metin;
      })
      .filter((x) => x.trim().length > 0);

    if (satirlar.length > 0) {
      return `İstek reddedildi — ${satirlar.join(" · ")}`;
    }
  }

  if (typeof p.title === "string" && p.title.trim().length > 0) {
    return `${p.title} (${status})`;
  }

  return `İşlem başarısız: ${status}`;
}
