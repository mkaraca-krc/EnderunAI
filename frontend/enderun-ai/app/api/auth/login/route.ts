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

export async function POST(
  request: NextRequest
) {
  try {
    const body = await request.json();

    const backend = await fetch(
      `${backendApiUrl}/auth/login`,
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify(body),
        cache: "no-store",
      }
    );

    const result =
      await backend.json().catch(() => null);

    if (!backend.ok) {
      return NextResponse.json(
        result ?? {
          message:
            "Kullanıcı adı veya şifre hatalı.",
        },
        {
          status: backend.status,
        }
      );
    }

    if (!result?.token) {
      return NextResponse.json(
        {
          message:
            "Backend geçerli bir token döndürmedi.",
        },
        {
          status: 502,
        }
      );
    }

    const expiresInSeconds =
      Number(result.expiresInSeconds) > 0
        ? Number(result.expiresInSeconds)
        : 43_200;

    const response = NextResponse.json({
      message: "Giriş başarılı.",
      user: result.user,
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
      expires: new Date(
        Date.now() +
          expiresInSeconds * 1_000
      ),
    });

    response.cookies.set({
      name: "enderun_session",
      value: "",
      httpOnly: true,
      secure: process.env.NODE_ENV === "production",
      sameSite: "lax",
      path: "/",
      expires: new Date(0),
      maxAge: 0,
    });

    response.headers.set(
      "Cache-Control",
      "no-store, max-age=0"
    );

    return response;
  } catch (error) {
    console.error(
      "Login route error:",
      error
    );

    return NextResponse.json(
      {
        message:
          "Giriş servisine ulaşılamadı.",
      },
      {
        status: 502,
      }
    );
  }
}
