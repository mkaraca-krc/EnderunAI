import { NextRequest, NextResponse } from "next/server";
import {
  AUTH_COOKIE_NAME,
  getSessionCookieOptions,
  SESSION_TTL_SECONDS,
} from "../../../../lib/auth";

const rawBackendUrl =
  process.env.BACKEND_API_URL ??
  process.env.BACKEND_URL ??
  "http://127.0.0.1:5155";
const cleanBackendUrl = rawBackendUrl.replace(/\/+$/, "");
const apiUrl = cleanBackendUrl.endsWith("/api")
  ? cleanBackendUrl
  : `${cleanBackendUrl}/api`;

type LoginBody = {
  username?: unknown;
  password?: unknown;
};

function noStore(response: NextResponse) {
  response.headers.set(
    "Cache-Control",
    "no-store, no-cache, must-revalidate"
  );
  response.headers.set("Pragma", "no-cache");
  return response;
}

export async function POST(request: NextRequest) {
  try {
    const body = (await request.json()) as LoginBody;
    const username =
      typeof body.username === "string"
        ? body.username.trim()
        : "";
    const password =
      typeof body.password === "string"
        ? body.password
        : "";

    if (!username || !password) {
      return noStore(
        NextResponse.json(
          { message: "Kullanıcı adı ve şifre zorunludur." },
          { status: 400 }
        )
      );
    }

    const backend = await fetch(`${apiUrl}/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username, password }),
      cache: "no-store",
    });
    const result = await backend.json().catch(() => null);

    if (!backend.ok) {
      return noStore(
        NextResponse.json(
          result ?? {
            message: "Kullanıcı adı veya şifre hatalı.",
          },
          { status: backend.status }
        )
      );
    }

    if (!result?.token) {
      return noStore(
        NextResponse.json(
          {
            message:
              "Backend geçerli bir oturum anahtarı döndürmedi.",
          },
          { status: 502 }
        )
      );
    }

    const requestedMaxAge = Number(result.expiresInSeconds);
    const maxAge = Number.isFinite(requestedMaxAge)
      ? requestedMaxAge
      : SESSION_TTL_SECONDS;
    const response = NextResponse.json({
      message: "Giriş başarılı.",
      user: result.user,
      expiresInSeconds: Math.min(
        SESSION_TTL_SECONDS,
        Math.max(1, Math.trunc(maxAge))
      ),
    });

    response.cookies.set(
      AUTH_COOKIE_NAME,
      result.token,
      getSessionCookieOptions(maxAge)
    );

    return noStore(response);
  } catch (error) {
    console.error("Login route error:", error);
    return noStore(
      NextResponse.json(
        { message: "Giriş servisine ulaşılamadı." },
        { status: 502 }
      )
    );
  }
}
