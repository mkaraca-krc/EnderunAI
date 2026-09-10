// Enderun ERP service worker — SW/1 (2026-09-10).
//
// ═══ HİÇBİR ŞEY ÖNBELLEĞE ALINMAZ ═══
//
// ÖLÇÜLDÜ: önceki sürüm her GET cevabını Cache Storage'a koyuyordu.
// Bir oturumdan sonra 26 kimlikli API cevabı (`auth/me`, `hr/personnel`,
// kârlılık özeti, mesaj listesi…) önbellekteydi, ÇIKIŞTAN SONRA da
// duruyordu ve ağ hatasında geri veriliyordu. Şantiye tabletleri ortak
// kullanılıyor: bu, bir kullanıcının kişisel verisinin cihazda
// kalması demekti (CANLI-1, SW/1 — "kişisel veri").
//
// Kural (MESAJ/4 için konmuştu, burada da geçerli): SW HİÇBİR ŞEYİ
// önbelleğe almaz. Bu yüzden `fetch` dinleyicisi YOK — tarayıcı istekleri
// SW'ye hiç uğratmadan doğrudan ağa gönderir. Çevrimdışı "son cevap"
// özelliği bilinçli olarak bırakıldı: bayat ya da BAŞKASININ cevabını
// göstermektense hata göstermek doğrudur.
//
// ═══ ETKİNLEŞİRKEN ÖNBELLEKLERİN HEPSİ SİLİNİR ═══
//
// Kod düzeltmesi tek başına dünün kayıtlarını silmez: eski SW'nin
// doldurduğu önbellek, yeni SW gelse de cihazda kalır. Bu köken Cache
// Storage'ı SW dışında hiçbir yerde kullanmıyor (ölçüldü) — yani
// "bizim olmayan ya da eski sürüm olan" her şey = HEPSİ.
//
// Temizlik `waitUntil` İÇİNDE: SW, silme bitmeden "etkin" sayılmaz.

self.addEventListener("install", () => {
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((adlar) => Promise.all(adlar.map((ad) => caches.delete(ad))))
      .then(() => self.clients.claim()),
  );
});
