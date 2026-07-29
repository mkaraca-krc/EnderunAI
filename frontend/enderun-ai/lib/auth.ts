export const AUTH_COOKIE_NAME = "enderun_token";
export const LEGACY_AUTH_COOKIE_NAME = "enderun_session";
export const SESSION_TTL_SECONDS = 60 * 60 * 12;

export function getCookieName(): string {
  return AUTH_COOKIE_NAME;
}

export function getSessionCookieOptions(
  requestedMaxAge = SESSION_TTL_SECONDS
) {
  const maxAge = Math.max(
    1,
    Math.min(
      SESSION_TTL_SECONDS,
      Math.trunc(requestedMaxAge)
    )
  );

  return {
    httpOnly: true,
    secure: true,
    sameSite: "lax" as const,
    path: "/",
    maxAge,
    expires: new Date(Date.now() + maxAge * 1000),
  };
}

export const clearSessionCookieOptions = {
  httpOnly: true,
  secure: true,
  sameSite: "lax" as const,
  path: "/",
  maxAge: 0,
  expires: new Date(0),
};
