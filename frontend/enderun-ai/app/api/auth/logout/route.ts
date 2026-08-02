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

export async function POST(
  request: NextRequest
) {
  const token =
    request.cookies.get("enderun_token")?.value;

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
