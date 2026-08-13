import { NextRequest, NextResponse } from "next/server";

// YOL → İZİN HARİTASI TEK KAYNAKTAN. Burada ikinci bir kopya
// tutuluyordu ve menüdeki haritayla ayrışmıştı: menüde gizlenen dokuz
// ekran (elden ödemeler, gider merkezi dahil) adres çubuğuna yazan
// kullanıcıya açılıyordu.
import { canAccessRoute } from "@/lib/auth/route-permissions";

const PUBLIC_PATHS = [
  "/login",
  "/api/auth/login",
  "/api/auth/access-requests",
  "/yetkisiz",
  "/portal",
];

function values(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value.map(String);
  }
  return typeof value === "string" ? [value] : [];
}

function tokenAccess(token: string) {
  try {
    const part = token.split(".")[1];
    const base64 = part.replace(/-/g, "+").replace(/_/g, "/");
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, "=");
    const payload = JSON.parse(atob(padded)) as Record<string, unknown>;
    const roles = [
      ...values(payload.roles),
      ...values(payload.role),
      ...values(
        payload[
          "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
        ]
      ),
    ];
    const permissions = [
      ...values(payload.permissions),
      ...values(payload.permission),
    ];
    // Kataloğun tamamına sahip kullanıcıda backend izin listesini
    // TEK TEK yazmıyor, tek bir bayrak koyuyor: 129 anahtar token'ı
    // 4096 baytlık çerez sınırının üstüne çıkarıyor ve tarayıcı
    // çerezi sessizce atıyordu (giriş 200 dönüyor ama oturum
    // açılmıyordu). Bayrak varsa her izin verilmiş sayılır.
    const all =
      payload.all_permissions === true ||
      payload.all_permissions === "true";

    return {
      roles: new Set(roles),
      permissions: new Set(permissions),
      all,
    };
  } catch {
    return {
      roles: new Set<string>(),
      permissions: new Set<string>(),
      all: false,
    };
  }
}


export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (PUBLIC_PATHS.some((path) => pathname.startsWith(path))) {
    return NextResponse.next();
  }

  const token = request.cookies.get("enderun_token")?.value;
  if (!token) {
    return NextResponse.redirect(new URL("/login", request.url));
  }

  // SÜPER KULLANICI ROL ADINDAN DEĞİL, all_permissions BAYRAĞINDAN
  // anlaşılıyor: rol yeniden adlandırılırsa ya da başka bir role tüm
  // izinler verilirse ad kontrolü yanlış cevap verirdi.
  const access = tokenAccess(token);

  if (!canAccessRoute(pathname, access.permissions, access.all)) {
    return NextResponse.redirect(new URL("/yetkisiz", request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: [
    "/((?!api/backend|_next/static|_next/image|favicon\\.ico|manifest\\.json|sw\\.js|.*\\.(?:png|jpg|jpeg|svg|ico|webp|webmanifest)$).*)",
  ],
};
