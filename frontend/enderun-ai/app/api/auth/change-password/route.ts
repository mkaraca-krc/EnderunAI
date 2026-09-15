import { NextRequest, NextResponse } from "next/server";
import { istemciAdresiniIlet } from "@/lib/vekil/istemci-adresi";

const rawBackendUrl =
  process.env.BACKEND_API_URL ??
  process.env.BACKEND_URL ??
  "http://127.0.0.1:5155";

const cleanBackendUrl = rawBackendUrl.replace(/\/+$/, "");

const backendApiUrl = cleanBackendUrl.endsWith("/api")
  ? cleanBackendUrl
  : `${cleanBackendUrl}/api`;

/**
 * PAROLA DEĞİŞTİRME — ÇEREZİ DE YENİLER.
 *
 * ── NEDEN AYRI ROTA, NEDEN GENEL PROXY DEĞİL ──
 *
 * Parola değişince o kullanıcının ÖNCEDEN üretilmiş tüm jetonları
 * geçersiz oluyor — kendi çerezindeki jeton dahil. Genel proxy'den
 * geçseydi cevap başarıyla dönerdi ama çerez eski jetonu taşımaya
 * devam ederdi ve kullanıcı BİR SONRAKİ istekte dışarı düşerdi:
 * "parolamı değiştirdim, sistem beni attı".
 *
 * Bu rota, arka ucun döndürdüğü YENİ jetonu çereze yazıyor. Login
 * rotasındaki desenin aynısı — bilerek, çünkü iki kopya zamanla
 * ayrışır ve o zaman biri `secure` bayrağını alır diğeri almaz.
 *
 * PAROLALAR YALNIZ GÖVDEDE: ne sorgu parametresinde ne günlükte.
 * Sorgu parametresi erişim kaydına, tarayıcı geçmişine ve proxy
 * kayıtlarına düşerdi — portal jetonunda yaşanan sızıntının aynısı.
 */
export async function POST(request: NextRequest) {
  try {
    const body = await request.json();
    const token = request.cookies.get("enderun_token")?.value;

    if (!token) {
      return NextResponse.json(
        { message: "Oturum bulunamadı. Yeniden giriş yapın." },
        { status: 401 }
      );
    }

    // VEKİL/1: bu rota istemci adresini taşımıyordu ve arka uçtaki
    // deneme sayacı HERKESİ 127.0.0.1 anahtarında topluyordu — bir
    // kullanıcının 5 hatası tüm kullanıcıları 15 dakika kilitliyordu.
    const arkaUcBasliklari = new Headers({
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    });
    istemciAdresiniIlet(request.headers, arkaUcBasliklari);

    const backend = await fetch(`${backendApiUrl}/auth/change-password`, {
      method: "POST",
      headers: arkaUcBasliklari,
      body: JSON.stringify(body),
      cache: "no-store",
    });

    const result = await backend.json().catch(() => null);

    if (!backend.ok) {
      return NextResponse.json(
        result ?? { message: "Parola değiştirilemedi." },
        { status: backend.status }
      );
    }

    if (!result?.token) {
      /*
       * YENİ JETON GELMEDİYSE DURULUR.
       *
       * Parola arka uçta DEĞİŞMİŞ olabilir ama elimizdeki çerez artık
       * geçersiz. "Başarılı" demek, kullanıcıyı bir sonraki istekte
       * sebepsiz bir çıkışa göndermek olurdu.
       */
      return NextResponse.json(
        {
          message:
            "Parola değişti ancak oturum yenilenemedi. " +
            "Lütfen yeniden giriş yapın.",
        },
        { status: 502 }
      );
    }

    const expiresInSeconds =
      Number(result.expiresInSeconds) > 0
        ? Number(result.expiresInSeconds)
        : 43_200;

    const response = NextResponse.json({
      message: result.message ?? "Parola değiştirildi.",
      expiresInSeconds,
    });

    response.cookies.set({
      name: "enderun_token",
      value: result.token,
      httpOnly: true,
      secure: process.env.NODE_ENV === "production",
      sameSite: "lax",
      path: "/",
      maxAge: expiresInSeconds,
      expires: new Date(Date.now() + expiresInSeconds * 1_000),
    });

    response.headers.set("Cache-Control", "no-store, max-age=0");

    return response;
  } catch {
    // HATA AYRINTISI GÜNLÜĞE YAZILMIYOR: gövdede parolalar var.
    return NextResponse.json(
      { message: "Parola servisine ulaşılamadı." },
      { status: 502 }
    );
  }
}
