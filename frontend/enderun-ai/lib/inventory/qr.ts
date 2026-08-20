import QRCode from "qrcode";

/**
 * STOK QR KODLARI.
 *
 * QR'a URL yazılıyor, ham kimlik değil: depo görevlisi telefonun
 * kamerasıyla okutunca doğrudan sayfa açılsın. Ham GUID yazsaydık
 * okuma sonrası kullanıcı elinde anlamsız bir metinle kalırdı ve
 * ayrı bir uygulama gerekirdi.
 *
 * ZİMMET TUTANAĞINDAKİ DESENİN AYNISI (`QRCode.toDataURL`) — kütüphane
 * zaten kurulu ve orada kullanılıyor.
 */

/** Stok kartının QR'ı → kart sayfası (ad, konum, stok, min/max). */
export function itemQrTarget(origin: string, itemId: string) {
  return `${origin}/depo-stok/malzeme/${itemId}`;
}

/** Rafın QR'ı → "bu rafta ne var" listesi. */
export function shelfQrTarget(origin: string, warehouseId: string, shelfId: string) {
  return `${origin}/depo-stok/raf/${warehouseId}/${shelfId}`;
}

export async function toDataUrl(value: string) {
  return QRCode.toDataURL(value, {
    width: 320,
    margin: 2,
    errorCorrectionLevel: "M",
  });
}

/**
 * Konumu tek satırda yazar: "Oda 2 · Raf 3 · Kat 2".
 *
 * AÇIK bölgede yalnız bölge adı döner — raf ve kat yoktur, olmayan
 * ayrıntıyı boş göstermek kafa karıştırır.
 */
export function formatLocation(
  zoneName?: string | null,
  shelfCode?: string | null,
  levelCode?: string | null
) {
  return [zoneName, shelfCode, levelCode].filter(Boolean).join(" · ") || "—";
}

/**
 * OKUTULAN DEĞERİ ÇÖZER.
 *
 * QR okuyucu bir klavyedir: okuduğu metni yazar ve Enter'a basar.
 * Kasada üç farklı şey okutulabiliyor ve üçü de aynı kutuya düşüyor:
 *
 * 1. Bizim ürettiğimiz stok etiketi — içinde kart sayfasının URL'i var
 *    (`.../depo-stok/malzeme/{id}`). Kimliği oradan söküyoruz.
 * 2. Üreticinin barkodu — kartın `barcode` alanıyla eşleşir.
 * 3. Elle yazılan stok kodu.
 *
 * Kimlik ile arama terimi AYRILIYOR: kimlikle eşleşme kesindir, terim
 * ise birden çok karta uyabilir. İkisi karıştırılsaydı, bir GUID'i
 * metin olarak aratmak hiçbir sonuç döndürmezdi ve etiket okutmak
 * sessizce çalışmazdı.
 */
export function parseScannedItem(
  raw: string
): { kind: "id"; id: string } | { kind: "term"; term: string } | null {
  const value = raw.trim();
  if (!value) return null;

  const fromUrl = value.match(
    /\/depo-stok\/malzeme\/([0-9a-fA-F-]{36})/
  );
  if (fromUrl) return { kind: "id", id: fromUrl[1].toLowerCase() };

  // Çıplak GUID de okutulabilir (eski etiketler, elle kopyalama).
  if (/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(value)) {
    return { kind: "id", id: value.toLowerCase() };
  }

  return { kind: "term", term: value };
}
