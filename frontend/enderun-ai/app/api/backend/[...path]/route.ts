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

    return new NextResponse(responseBody, {
      status: response.status,
      headers: responseHeaders,
    });
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
