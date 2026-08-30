import { NextRequest, NextResponse } from "next/server";

const BACKEND_URL =
  process.env.BACKEND_API_URL?.replace(/\/+$/, "") ||
  "http://127.0.0.1:5155";

type RouteContext = {
  params: Promise<{
    path: string[];
  }>;
};

async function proxy(
  request: NextRequest,
  context: RouteContext
) {
  const { path } = await context.params;

  const backendPath = path
    .map((part) => encodeURIComponent(part))
    .join("/");

  const targetUrl = new URL(
    `${BACKEND_URL}/api/${backendPath}`
  );

  request.nextUrl.searchParams.forEach((value, key) => {
    targetUrl.searchParams.append(key, value);
  });

  const headers = new Headers();

  const contentType = request.headers.get("content-type");
  if (contentType) {
    headers.set("content-type", contentType);
  }

  const accept = request.headers.get("accept");
  if (accept) {
    headers.set("accept", accept);
  }

  const token =
    request.cookies.get("enderun_token")?.value;

  if (token) {
    headers.set("authorization", `Bearer ${token}`);
  }

  let body: BodyInit | undefined;

  if (
    request.method !== "GET" &&
    request.method !== "HEAD"
  ) {
    body = await request.arrayBuffer();
  }

  try {
    const response = await fetch(targetUrl, {
      method: request.method,
      headers,
      body,
      cache: "no-store",
      redirect: "manual",
    });

    const responseBody = await response.arrayBuffer();

    const responseHeaders = new Headers();

    const responseContentType =
      response.headers.get("content-type");

    if (responseContentType) {
      responseHeaders.set(
        "content-type",
        responseContentType
      );
    }

    /*
     * GÖVDESİZ DURUM KODLARI (204, 205, 304) GÖVDE KABUL ETMEZ.
     *
     * `new Response(gövde, { status: 204 })` Web standardına göre
     * FIRLATIR. Bu proxy her yanıtı `arrayBuffer()` ile okuyup
     * gövde olarak geçiriyordu; boş bir tampon bile 204'te geçersiz.
     *
     * SONUÇ: fırlatan yapıcı `catch`e düşüyor ve proxy 502
     * döndürüyordu. Yani arka uç DOĞRU çalışıp 204 dönerken,
     * tarayıcı "Backend servisine bağlantı kurulamadı" görüyordu.
     *
     * CANLIDA OLAN (2026-08-30): `istemci-hatalari` ucu 204 dönüyor
     * ve 502'ye çevriliyordu — yani HATA BİLDİRİM KANALININ KENDİSİ
     * çökmüştü. Ekranlar çöküyor, bildirmeye çalışıyor, bildirim de
     * düşüyordu. Kimsenin haberi olmamasının sebebi buydu.
     *
     * KAPSAM: arka uçta 11 kontrolcüde 21 uç 204 dönüyor — ödeme
     * planı satır işlemleri dahil. Hepsi aynı şekilde 502 veriyordu.
     * Düzeltme TEK YERDE çünkü hata proxy'de, uçlarda değil.
     */
    const govdesizDurumlar = new Set([204, 205, 304]);

    return new NextResponse(
      govdesizDurumlar.has(response.status) ? null : responseBody,
      {
        status: response.status,
        headers: responseHeaders,
      },
    );
  } catch (error) {
    console.error("Backend proxy error:", error);

    return NextResponse.json(
      {
        message:
          "Backend servisine bağlantı kurulamadı.",
      },
      {
        status: 502,
      }
    );
  }
}

export async function GET(
  request: NextRequest,
  context: RouteContext
) {
  return proxy(request, context);
}

export async function POST(
  request: NextRequest,
  context: RouteContext
) {
  return proxy(request, context);
}

export async function PUT(
  request: NextRequest,
  context: RouteContext
) {
  return proxy(request, context);
}

export async function PATCH(
  request: NextRequest,
  context: RouteContext
) {
  return proxy(request, context);
}

export async function DELETE(
  request: NextRequest,
  context: RouteContext
) {
  return proxy(request, context);
}
