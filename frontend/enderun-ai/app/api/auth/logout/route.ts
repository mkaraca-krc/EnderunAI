import { NextRequest, NextResponse } from "next/server";

const rawBackendUrl =
  process.env.BACKEND_API_URL ??
  process.env.BACKEND_URL ??
  "http://127.0.0.1:5155";

const cleanBackendUrl =
  rawBackendUrl.replace(/\/+$/, "");

const backendApiUrl =
  cleanBackendUrl.endsWith("/api")
    ? cleanBackendUrl
    : `${cleanBackendUrl}/api`;

const cookieNames = [
  "enderun_token",
  "enderun_session",
];

/**
 * ÇIKIŞI KİM TETİKLEDİ — SAYILI KÜME (GÜNLÜK/1).
 *
 * 9 Eylül'de bir kullanıcı oturumunu kaybetti ve "kim çıkardı"
 * sorusunun cevabı YOKTU. Çıkış BURADA oluyor — arka uca ulaşılamasa
 * bile çerezler siliniyor — o yüzden satır da burada yazılıyor.
 * Arka uca yollasaydık, EN ÇOK ÖNEMSEDİĞİMİZ durumda (yeniden
 * başlatma penceresi) satır hiç düşmezdi.
 */
const GECERLI_SEBEPLER = new Set([
  "kullanici-dugmesi",
  "mesai-izleyicisi",
]);

/**
 * İSTEMCİDEN GELEN DEĞER GÜVENİLMEZ VERİDİR.
 *
 * Kümede yoksa "bilinmiyor" yazılır ve GÖNDERİLEN METİN GÜNLÜĞE
 * GİRMEZ — aksi hâlde bir çağıran günlüğe sahte satır yazdırabilir
 * (satır sonu enjeksiyonu). Uzunluk da sınırlı.
 *
 * "bilinmiyor" SESSİZCE GEÇMEZ, kayda geçer: yarın üçüncü bir çağıran
 * eklenirse günlükte görünür ve fark edilir.
 */
function sebebiDogrula(ham: unknown): string {
  if (typeof ham !== "string") return "bilinmiyor";
  const kirpik = ham.slice(0, 40).replace(/[\r\n\t\u0000-\u001f]/g, "");
  return GECERLI_SEBEPLER.has(kirpik) ? kirpik : "bilinmiyor";
}

/** Jetonun gövdesinden kullanıcı kimliği — YALNIZ GÜNLÜK İÇİN. */
function kullaniciKimligi(token: string | undefined): string {
  if (!token) return "-";
  try {
    const govde = token.split(".")[1];
    if (!govde) return "-";
    const cozulen = JSON.parse(
      Buffer.from(govde.replace(/-/g, "+").replace(/_/g, "/"), "base64").toString("utf8"),
    ) as Record<string, unknown>;
    const kimlik = cozulen.sub ?? cozulen.nameid;
    return typeof kimlik === "string" ? kimlik : "-";
  } catch {
    return "-";
  }
}

export async function POST(
  request: NextRequest
) {
  const token =
    request.cookies.get("enderun_token")?.value;

  let hamSebep: unknown = undefined;
  try {
    hamSebep = (await request.clone().json())?.reason;
  } catch {
    // Gövdesiz çağrı: sebep "bilinmiyor" olur, çıkış yine yapılır.
  }

  console.warn(
    `CIKIS kullanici=${kullaniciKimligi(token)} tetikleyen=${sebebiDogrula(hamSebep)}`,
  );

  if (token) {
    try {
      await fetch(`${backendApiUrl}/auth/logout`, {
        method: "POST",
        headers: {
          authorization: `Bearer ${token}`,
        },
        cache: "no-store",
        signal: AbortSignal.timeout(5_000),
      });
    } catch {
      // The browser session must still be closed if the backend
      // does not expose token revocation yet or is temporarily unavailable.
    }
  }

  const response = NextResponse.json({ success: true });

  for (const name of cookieNames) {
    response.cookies.set(name, "", {
      httpOnly: true,
      secure: process.env.NODE_ENV === "production",
      sameSite: "lax",
      path: "/",
      expires: new Date(0),
      maxAge: 0,
    });
  }

  response.headers.set(
    "Cache-Control",
    "no-store, max-age=0"
  );

  return response;
}
