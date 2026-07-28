import crypto from "node:crypto";

const COOKIE_NAME = "enderun_session";
const SESSION_TTL_SECONDS = 60 * 60 * 12;

function getSecret(): string {
  const secret = process.env.AUTH_SECRET;

  if (!secret || secret.length < 32) {
    throw new Error(
      "AUTH_SECRET en az 32 karakter olmalıdır."
    );
  }

  return secret;
}

export function getCookieName(): string {
  return COOKIE_NAME;
}

export function createSessionToken(
  username: string
): string {
  const expiresAt =
    Math.floor(Date.now() / 1000) +
    SESSION_TTL_SECONDS;

  const payload = Buffer.from(
    JSON.stringify({
      username,
      expiresAt,
    }),
    "utf8"
  ).toString("base64url");

  const signature = crypto
    .createHmac("sha256", getSecret())
    .update(payload)
    .digest("base64url");

  return `${payload}.${signature}`;
}

export function verifySessionToken(
  token: string | undefined
): boolean {
  if (!token) {
    return false;
  }

  const [payload, signature] =
    token.split(".");

  if (!payload || !signature) {
    return false;
  }

  const expectedSignature = crypto
    .createHmac("sha256", getSecret())
    .update(payload)
    .digest("base64url");

  const actual = Buffer.from(signature);
  const expected =
    Buffer.from(expectedSignature);

  if (
    actual.length !== expected.length ||
    !crypto.timingSafeEqual(
      actual,
      expected
    )
  ) {
    return false;
  }

  try {
    const data = JSON.parse(
      Buffer.from(
        payload,
        "base64url"
      ).toString("utf8")
    ) as {
      username?: string;
      expiresAt?: number;
    };

    return Boolean(
      data.username &&
        data.expiresAt &&
        data.expiresAt >
          Math.floor(Date.now() / 1000)
    );
  } catch {
    return false;
  }
}

export function validateCredentials(
  username: string,
  password: string
): boolean {
  const expectedUsername =
    process.env.ADMIN_USERNAME;

  const expectedPassword =
    process.env.ADMIN_PASSWORD;

  if (
    !expectedUsername ||
    !expectedPassword
  ) {
    throw new Error(
      "ADMIN_USERNAME ve ADMIN_PASSWORD tanımlanmalıdır."
    );
  }

  return (
    username === expectedUsername &&
    password === expectedPassword
  );
}

export const sessionCookieOptions = {
  httpOnly: true,

  // HTTPS kurunca tekrar true yapacağız.
  secure: false,

  sameSite: "lax" as const,

  path: "/",

  maxAge: SESSION_TTL_SECONDS,
};
