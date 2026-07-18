const API_BASE =
  process.env.NEXT_PUBLIC_API_URL ??
  "http://136.144.213.33:5155";

export async function apiFetch(
  path: string,
  options: RequestInit = {}
) {
  const token =
    typeof window !== "undefined"
      ? localStorage.getItem("token")
      : null;

  const headers = new Headers(options.headers);

  if (token) {
    headers.set(
      "Authorization",
      `Bearer ${token}`
    );
  }

  if (
    options.body &&
    !(options.body instanceof FormData)
  ) {
    headers.set(
      "Content-Type",
      "application/json"
    );
  }

  const temizYol = path.startsWith("/")
    ? path
    : `/${path}`;

  return fetch(
    `${API_BASE}${temizYol}`,
    {
      ...options,
      headers,
    }
  );
}