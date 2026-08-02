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

    const clientIp =
      request.headers.get("x-forwarded-for") ??
      request.headers.get("x-real-ip") ??
      "";

    const backend = await fetch(
      `${backendApiUrl}/auth/access-requests`,
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...(clientIp ? { "X-Forwarded-For": clientIp } : {}),
        },
        body: JSON.stringify(body),
        cache: "no-store",
      }
    );

    const result =
      await backend.json().catch(() => null);

    return NextResponse.json(
      result ?? {
        message: "Erişim talebi gönderilemedi.",
      },
      { status: backend.status }
    );
  } catch (error) {
    console.error(
      "Access request route error:",
      error
    );

    return NextResponse.json(
      {
        message:
          "Erişim talebi servisine ulaşılamadı.",
      },
      { status: 502 }
    );
  }
}
