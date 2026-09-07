import { NextRequest, NextResponse } from "next/server";

// YOL → İZİN HARİTASI TEK KAYNAKTAN. Burada ikinci bir kopya
// tutuluyordu ve menüdeki haritayla ayrışmıştı: menüde gizlenen dokuz
// ekran (elden ödemeler, gider merkezi dahil) adres çubuğuna yazan
// kullanıcıya açılıyordu.
import { routeErisimi } from "@/lib/auth/route-permissions";

// JETON KODLAMASI TEK YERDE ÇÖZÜLÜR. Üç kodlama (hepsi / tümleyen /
// liste) burada yeniden yorumlansaydı, arka uçtaki karşılığıyla
// zamanla ayrışırdı — ve ayrışma sessiz olurdu: kullanıcıya olmayan
// bir yetki verilir ya da olan bir yetki gizlenir, ikisi de hata
// vermeden.
import { izinVarMi, jetonErisimi } from "@/lib/auth/jeton-izinleri";

const PUBLIC_PATHS = [
  "/login",
  "/api/auth/login",
  "/api/auth/access-requests",
  "/yetkisiz",
  "/portal",
];

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (PUBLIC_PATHS.some((path) => pathname.startsWith(path))) {
    return NextResponse.next();
  }

  const token = request.cookies.get("enderun_token")?.value;
  if (!token) {
    return NextResponse.redirect(new URL("/login", request.url));
  }

  // SÜPER KULLANICI ROL ADINDAN ANLAŞILMAZ: rol yeniden adlandırılırsa
  // ya da başka bir role tüm izinler verilirse ad kontrolü yanlış
  // cevap verirdi. Karar jetonun izin kodlamasından geliyor ve o
  // kodlamayı çözen tek yer `lib/auth/jeton-izinleri`.
  const erisim = jetonErisimi(token);

  if (!routeErisimi(pathname, (izin) => izinVarMi(erisim, izin))) {
    return NextResponse.redirect(new URL("/yetkisiz", request.url));
  }

  return NextResponse.next();
}

/*
 * STATİK VARLIKLAR KİMLİK KAPISINDAN GEÇMEZ.
 *
 * ═══ NEDEN `wav|mp3|ogg` EKLENDİ (2026-09-07, ÜRETİMDE ÖLÇÜLDÜ) ═══
 *
 * `/sesler/mesaj.wav` istendiğinde middleware onu KORUMALI ROTA
 * sayıp `307 → /login` döndürüyordu. Uzantı listesi görselleri
 * (png/jpg/svg…) dışlıyordu ama sesi dışlamıyordu.
 *
 * Oturum açmış kullanıcıda çerez gittiği için ses muhtemelen
 * çalışıyordu; ama bir SES DOSYASININ kimlik kapısından geçmesi
 * yanlış: gereksiz gecikme ve tamamen sessiz bir arıza yolu.
 *
 * BU KUSURU TESTİM YAKALAMADI ve sebebi ölçüldü: test `play()`
 * ÇAĞRISINI sayıyordu, dosyanın YÜKLENDİĞİNİ değil. `play()`
 * kaynak hiç yüklenmese de çağrılıyor. Test artık varlığın
 * gerçekten `audio/*` olarak geldiğini de sınıyor.
 */
export const config = {
  matcher: [
    "/((?!api/backend|_next/static|_next/image|favicon\\.ico|manifest\\.json|sw\\.js|.*\\.(?:png|jpg|jpeg|svg|ico|webp|webmanifest|wav|mp3|ogg)$).*)",
  ],
};
