/**
 * STOK SEVİYESİ ETİKETİ — "NORMAL" ÖLÇÜLMEDEN YAZILMAZ (S1, 2026-09-16).
 *
 * ═══ ÖLÇÜLEN KUSUR ═══
 *
 * Sütun iki hâl tanıyordu: `Kritik` ve `Normal`. Kritik olmayan HER
 * satıra `Normal` yazıyordu — asgari seviyesi hiç tanımlanmamış olan
 * satırlara da.
 *
 * ÖLÇÜM (2026-09-16, canlı veritabanı):
 *   warehouse_stock_levels ... 0 satır
 *   warehouse_stocks ......... 0 satır
 *   stock_movements .......... 0 satır
 *
 * Yani dokuz kartın dokuzunda da `Normal` yazıyordu; ne stok vardı, ne
 * asgari seviye, ne de tek bir hareket. Sütun bilgi taşımıyordu ve
 * taşıdığı tek şey YANLIŞTI: "normal seviyede" demek, ölçülmemiş bir
 * hükümdür (Kural 88 — etiketsiz hüküm yazılmaz).
 *
 * ÜÇÜNCÜ HÂL EKLENDİ: seviye tanımlı değilse `—`. Bilmemek, iyi
 * haber değildir.
 *
 * ═══ KART DURUMU BU SÜTUNDA DEĞİLDİR ═══
 *
 * Kartın Aktif/Pasif oluşu AYRI bir kavramdır ve Malzeme sütununda
 * rozetle işaretlenir. Başlığın "Stok Durumu"ndan "Stok Seviyesi"ne
 * çevrilmesinin sebebi de budur: "durum" sözcüğü ikisini karıştırıyordu.
 */

export type StokSeviyesi = "kritik" | "normal" | "tanimsiz";

export function stokSeviyesi(
  seviyeTanimli: boolean,
  kritik: boolean
): StokSeviyesi {
  if (!seviyeTanimli) return "tanimsiz";
  return kritik ? "kritik" : "normal";
}

export function stokSeviyesiEtiketi(seviye: StokSeviyesi): string {
  switch (seviye) {
    case "kritik":
      return "Kritik";
    case "normal":
      return "Normal";
    case "tanimsiz":
      return "—";
  }
}

/** Ekranda ve dışa aktarımda okunan açıklama; `—` tek başına sessizdir. */
export function stokSeviyesiAciklamasi(seviye: StokSeviyesi): string {
  switch (seviye) {
    case "kritik":
      return "Asgari seviyenin altında";
    case "normal":
      return "Asgari seviyenin üstünde";
    case "tanimsiz":
      return "Asgari seviye tanımlı değil — ölçülmedi";
  }
}
