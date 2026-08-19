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
