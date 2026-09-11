// Enderun ERP service worker — yalnızca PWA kurulabilirliği için.
// Bilinçli olarak agresif önbellekleme YAPMAZ: her istek önce ağdan denenir
// (network-first), sadece ağ tamamen başarısız olursa (çevrimdışı) en son
// başarılı yanıt önbellekten döner. Portal sayfası (/portal/[token]) dahil
// hiçbir sayfa bayat/eski veriyle takılı kalmaz.

const CACHE_NAME = "enderun-erp-v1";

self.addEventListener("install", (event) => {
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(
        keys
          .filter((key) => key !== CACHE_NAME)
          .map((key) => caches.delete(key))
      )
    )
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  if (event.request.method !== "GET") {
    return;
  }

  event.respondWith(
    fetch(event.request)
      .then((response) => {
        const copy = response.clone();
        caches.open(CACHE_NAME).then((cache) => {
          cache.put(event.request, copy).catch(() => {});
        });
        return response;
      })
      .catch(() =>
        caches.match(event.request).then((cached) => {
          if (cached) return cached;
          throw new Error("Ağ ve önbellek başarısız oldu.");
        })
      )
  );
});
