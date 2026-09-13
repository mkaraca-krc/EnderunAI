/**
 * İKİ PARALEL KATEGORİ KAVRAMI — TEK ETİKET (2026-09-13).
 *
 * Kartlarda kategori İKİ yerde duruyor:
 *   · `category`             — ESKİ serbest metin ("AYDINLATMA")
 *   · `inventoryCategoryId`  — yapılandırılmış kategori (birim ve
 *                              özellik kuralları, muhasebe türü buna bağlı)
 *
 * Uç ikisini `categoryLabel` içinde zaten birleştiriyor
 * (`InventoryCategory.Name ?? Category`), ama liste ekranı ESKİ alanı
 * basıyordu. Ölçüldü: ekrandan açılan yeni kart `category=null`,
 * `categoryLabel="Özel İmalat"` → listede **"—"** görünüyordu.
 *
 * Kartlar sıfırdan kurulacağı için bu, açılan HER kartı etkilerdi.
 */
export type KategoriAlanlari = {
  categoryLabel?: string | null;
  category?: string | null;
};

export function kategoriEtiketi(kart: KategoriAlanlari): string {
  const etiket = kart.categoryLabel?.trim() || kart.category?.trim();
  return etiket && etiket.length > 0 ? etiket : "—";
}
