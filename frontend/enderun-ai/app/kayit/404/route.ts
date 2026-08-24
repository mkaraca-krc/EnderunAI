import { NextRequest, NextResponse } from "next/server";

/**
 * 404 KAYDI — JOURNALD'A, TABLOYA DEĞİL.
 *
 * YOL NEDEN `/api/` ALTINDA DEĞİL: nginx `location /api/` bloğunu
 * BACKEND'e (5155) veriyor; yalnız `/api/auth/` ve `/api/backend/`
 * Next.js'e gidiyor. Bu uç `/api/not-found` olsaydı backend'e düşer
 * ve 404 alırdı — yani 404 kaydı sessizce hiç yazılmazdı. Üçüncü bir
 * nginx istisnası açmak yerine yol `/kayit/404` yapıldı; `location /`
 * zaten Next.js'e gidiyor.
 *
 * Kayıt `console.warn` ile stdout'a yazılıyor; systemd bunu
 * `enderunai-frontend` biriminin journald akışına düşürüyor. Tablo
 * AÇILMADI (karar): 404 kaydı bir iş verisi değil, teşhis izi;
 * tabloda dursaydı yedeklenir, taşınır ve temizlenmesi gereken bir
 * borç olurdu.
 *
 * KİŞİSEL VERİ KURALI: kullanıcı KİMLİĞİ yazılır, adı ve e-postası
 * YAZILMAZ. journald'ı `journalctl` okuyabilen herkes okur; orası
 * kişi listesi tutulacak yer değil.
 */

/** JWT gövdesinden yalnız `sub` (kullanıcı kimliği) alınır. */
function kullaniciKimligi(token: string | undefined): string | null {
  if (!token) return null;

  try {
    const govde = token.split(".")[1];
    if (!govde) return null;

    const cozulmus = JSON.parse(
      Buffer.from(govde.replace(/-/g, "+").replace(/_/g, "/"), "base64").toString(
        "utf8"
      )
    ) as Record<string, unknown>;

    const sub =
      cozulmus.sub ??
      cozulmus["nameid"] ??
      cozulmus["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"];

    return typeof sub === "string" ? sub : null;
  } catch {
    return null;
  }
}

/*
 * BOT SÜZGECİ — AÇIKÇA YAZILI.
 *
 * İki katmanlı:
 *   1. OTURUM ŞARTI: `enderun_token` çerezi yoksa kayıt YAZILMAZ.
 *      Tarayıcı botları ve zafiyet tarayıcıları oturum açmaz, yani
 *      gürültünün büyük kısmı burada durur. Bu aynı zamanda
 *      kararın kendisi: "yalnız oturum açmış kullanıcıların
 *      uygulama içi 404'leri".
 *   2. USER-AGENT: oturumlu bir istekte bile bilinen tarama
 *      imzaları elenir. Kayıt gövdesine user-agent YAZILMAZ,
 *      yalnız süzgeçte kullanılır.
 *
 * Süzgeç kaydı bir kişiye bağlamıyor; yalnız gürültüyü kesiyor.
 */
const BOT_IMZALARI = [
  "bot", "crawl", "spider", "slurp", "curl", "wget", "python-requests",
  "httpclient", "scanner", "nikto", "sqlmap", "nmap", "masscan",
  "headlesschrome", "phantomjs", "postman", "insomnia",
];

export async function POST(request: NextRequest) {
  const token = request.cookies.get("enderun_token")?.value;

  // 1. KATMAN: oturum yoksa kayıt yok.
  if (!token) return NextResponse.json({ ok: true });

  const userAgent = (request.headers.get("user-agent") ?? "").toLowerCase();

  // 2. KATMAN: bilinen tarama imzaları.
  if (BOT_IMZALARI.some((imza) => userAgent.includes(imza))) {
    return NextResponse.json({ ok: true });
  }

  let govde: { path?: unknown; referrer?: unknown };

  try {
    govde = await request.json();
  } catch {
    return NextResponse.json({ ok: true });
  }

  const yol = typeof govde.path === "string" ? govde.path : null;
  if (!yol) return NextResponse.json({ ok: true });

  const referrer =
    typeof govde.referrer === "string" && govde.referrer.length > 0
      ? govde.referrer
      : "-";

  const kimlik = kullaniciKimligi(token) ?? "-";

  /*
   * TEK SATIR, AYRIŞTIRILABİLİR BİÇİM.
   * Alanlar: zaman (journald kendisi ekler), kullanıcı kimliği,
   * istenen yol, geldiği yol. Ad ve e-posta YOK.
   */
  console.warn(
    `404-KAYDI kullanici=${kimlik} yol=${yol} geldigi=${referrer}`
  );

  return NextResponse.json({ ok: true });
}
