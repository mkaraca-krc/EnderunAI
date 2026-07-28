import { NextRequest, NextResponse } from "next/server";

const PUBLIC_PATHS = ["/login", "/api/auth/login", "/yetkisiz"];

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
    return { roles: new Set(roles), permissions: new Set(permissions) };
  } catch {
    return { roles: new Set<string>(), permissions: new Set<string>() };
  }
}

function requiredPermission(pathname: string): string | null {
  if (pathname.startsWith("/sistem-yonetimi")) return "system.users.manage";
  if (
    /^\/insan-kaynaklari\/(bordro|ucret-kartlari|ek-ucretler|avanslar)/.test(
      pathname
    )
  )
    return "payroll.view";
  if (
    /^\/insan-kaynaklari\/(puantaj|gunluk-puantaj|izinler|fazla-mesai)/.test(
      pathname
    )
  )
    return "attendance.view";
  if (pathname.startsWith("/insan-kaynaklari")) return "personnel.view";
  if (pathname.startsWith("/muhasebe")) return "accounting.view";
  if (pathname.startsWith("/finans")) return "finance.view";
  if (
    pathname.startsWith("/hakedis") ||
    pathname.startsWith("/fiyat-farki") ||
    pathname.startsWith("/metrajlar")
  )
    return "hakedis.view";
  if (pathname.startsWith("/satin-alma")) return "purchasing.view";
  if (pathname.startsWith("/depo")) return "inventory.view";
  if (
    pathname.startsWith("/muhendislik") ||
    pathname.startsWith("/kesifler")
  )
    return "engineering.view";
  if (
    pathname.startsWith("/projeler") ||
    pathname.startsWith("/teklifler")
  )
    return "projects.view";
  if (
    pathname.startsWith("/sekreterya") ||
    pathname.startsWith("/dokumanlar")
  )
    return "secretariat.view";
  if (pathname.startsWith("/gorevler")) return "tasks.view";
  if (pathname.startsWith("/raporlar")) return "reports.view";
  if (pathname.startsWith("/ai-asistan")) return "ai.use";
  if (
    pathname.startsWith("/sirketler") ||
    pathname.startsWith("/subeler") ||
    pathname.startsWith("/cariler")
  )
    return "companies.view";
  return null;
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

  const permission = requiredPermission(pathname);
  if (permission) {
    const access = tokenAccess(token);
    if (
      !access.roles.has("Admin") &&
      !access.roles.has("Genel Müdür") &&
      !access.permissions.has(permission)
    ) {
      return NextResponse.redirect(new URL("/yetkisiz", request.url));
    }
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/((?!api/backend|_next/static|_next/image|favicon.ico).*)"],
};
