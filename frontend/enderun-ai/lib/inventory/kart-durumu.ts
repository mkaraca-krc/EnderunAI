/**
 * KART DURUMU ≠ STOK DURUMU (S1, 2026-09-13).
 *
 * Mehmet Bey canlıda şunu gördü: END0001 kartının detayında sağ üstte
 * "Pasif" yazıyor, listede DURUM sütunu "Normal" diyor. Ölçüldü: ikisi
 * ÇELİŞMİYOR, iki AYRI kavram tek sözcüğü paylaşıyordu.
 *
 *   · kart durumu  → `isActive`  (Aktif / Pasif)  — kart arşivlenmiş mi
 *   · stok durumu  → asgarinin altında mı (Normal / Kritik)
 *
 * Ayrımın görünmemesi teorik bir kusur değildi: ölçüm anında canlıdaki
 * 9 malzeme kartının 9'u da PASİF'ti, yani listedeki her "Normal"
 * satırı aslında pasif bir kartı gösteriyordu.
 *
 * Etiketler tek kaynakta, çünkü aynı hastalığın (beş kopya etiket
 * haritası) bedeli daha önce ölçüldü.
 */
export const KART_DURUM_ETIKETLERI = {
  aktif: "Aktif",
  pasif: "Pasif",
} as const;

export function kartDurumEtiketi(isActive: boolean): string {
  return isActive ? KART_DURUM_ETIKETLERI.aktif : KART_DURUM_ETIKETLERI.pasif;
}

/** Pasif kart listede işaretlenir; aktif kart işaretsizdir (gürültü olmasın). */
export function kartDurumIsareti(isActive: boolean): string | null {
  return isActive ? null : KART_DURUM_ETIKETLERI.pasif;
}
