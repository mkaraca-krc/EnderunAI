import { NextRequest, NextResponse } from "next/server";
import {
  AUTH_COOKIE_NAME,
  clearSessionCookieOptions,
} from "./lib/auth";

const PUBLIC_PATHS = [
  "/login",
  "/api/auth/login",
  "/api/auth/logout",
  "/yetkisiz",
];

const rawBackendUrl =
  process.env.BACKEND_API_URL ??
  process.env.BACKEND_URL ??
  "http://127.0.0.1:5155";
const cleanBackendUrl = rawBackendUrl.replace(/\/+$/, "");
const apiUrl = cleanBackendUrl.endsWith("/api")
  ? cleanBackendUrl
  : `${cleanBackendUrl}/api`;

type CurrentSession = {
  roles: string[];
  permissions: string[];
};

function isPublicPath(pathname: string) {
  return PUBLIC_PATHS.some(
    (path) => pathname === path || pathname.startsWith(`${path}/`)
  );
}

function requiredPermission(pathname: string): string | null {
  if (pathname.startsWith("/sistem-yonetimi"))
    return "system.users.manage";
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
  if (pathname.startsWith("/insan-kaynaklari"))
    return "personnel.view";
  if (pathname.startsWith("/muhasebe"))
    return "accounting.view";
  if (pathname.startsWith("/finans")) return "finance.view";
  if (
    pathname.startsWith("/hakedis") ||
    pathname.startsWith("/fiyat-farki") ||
    pathname.startsWith("/metrajlar")
  )
    return "hakedis.view";
  if (pathname.startsWith("/satin-alma"))
    return "purchasing.view";
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

async function validateSession(
  token: string
): Promise<CurrentSession | null> {
  try {
    const response = await fetch(`${apiUrl}/auth/me`, {
      headers: {
        Accept: "application/json",
        Authorization: `Bearer ${token}`,
      },
      cache: "no-store",
    });

    if (!response.ok) return null;

    const session = (await response.json()) as Partial<CurrentSession>;
    if (
      !Array.isArray(session.roles) ||
      !Array.isArray(session.permissions)
    ) {
      return null;
    }

    return {
      roles: session.roles.map(String),
      permissions: session.permissions.map(String),
    };
  } catch {
    return null;
  }
}

function rejectSession(request: NextRequest) {
  if (request.nextUrl.pathname.startsWith("/api/")) {
    const response = NextResponse.json(
      { message: "Oturum geçersiz veya süresi dolmuş." },
      { status: 401 }
    );
    response.cookies.set(
      AUTH_COOKIE_NAME,
      "",
      clearSessionCookieOptions
    );
    return response;
  }

  const loginUrl = new URL("/login", request.url);
  loginUrl.searchParams.set(
    "returnUrl",
    request.nextUrl.pathname
  );
  const response = NextResponse.redirect(loginUrl);
  response.cookies.set(
    AUTH_COOKIE_NAME,
    "",
    clearSessionCookieOptions
  );
  return response;
}

export async function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (isPublicPath(pathname)) {
    return NextResponse.next();
  }

  const token =
    request.cookies.get(AUTH_COOKIE_NAME)?.value;
  if (!token) {
    return rejectSession(request);
  }

  const session = await validateSession(token);
  if (!session) {
    return rejectSession(request);
  }

  const permission = requiredPermission(pathname);
  if (
    permission &&
    !session.roles.includes("Admin") &&
    !session.roles.includes("Genel Müdür") &&
    !session.permissions.includes(permission)
  ) {
    return NextResponse.redirect(
      new URL("/yetkisiz", request.url)
    );
  }

  return NextResponse.next();
}

export const config = {
  matcher: [
    "/((?!api/backend|_next/static|_next/image|favicon.ico).*)",
  ],
};
