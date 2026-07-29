import { NextResponse } from "next/server";
import {
  AUTH_COOKIE_NAME,
  clearSessionCookieOptions,
  LEGACY_AUTH_COOKIE_NAME,
} from "../../../../lib/auth";

export async function POST() {
  const response = NextResponse.json({ success: true });

  for (const cookieName of [
    AUTH_COOKIE_NAME,
    LEGACY_AUTH_COOKIE_NAME,
  ]) {
    response.cookies.set(
      cookieName,
      "",
      clearSessionCookieOptions
    );
  }

  response.headers.set(
    "Cache-Control",
    "no-store, no-cache, must-revalidate"
  );
  response.headers.set("Pragma", "no-cache");
  return response;
}
