/**
 * MALZEME TİPİ — TEK ETİKET KAYNAĞI.
 *
 * ═══ ÖLÇÜLEN KUSUR (2026-09-13) ═══
 *
 * Arka uç (`Models/InventoryItem.cs`):
 *   `Material = 0, Equipment = 1, Consumable = 2, SparePart = 3`
 *
 * Ön yüzde BEŞ ayrı kopya vardı ve dördü yanlıştı:
 *
 * | değer | arka uç    | kart açma | düzenleme       | liste     | detay            |
 * |-------|------------|-----------|-----------------|-----------|------------------|
 * | 0     | Material   | Malzeme ✓ | Stok malzemesi ✓| Stok ✓    | Stok malzemesi ✓ |
 * | 1     | Equipment  | Ekipman ✓ | **Sarf** ✗      | **Sarf** ✗| **Sarf** ✗       |
 * | 2     | Consumable | Sarf ✓    | **Demirbaş** ✗  | **Demirbaş** ✗ | **Demirbaş** ✗ |
 * | 3     | SparePart  | Yedek P. ✓| **YOK** ✗       | **YOK** ✗ | **YOK** ✗        |
 *
 * Beşinci kopya `services/inventory.service.ts`teki tipin kendisiydi:
 * `type InventoryItemType = 0 | 1 | 2` — yani TypeScript `SparePart`i
 * hiç kabul etmiyordu; kullanıcı "Yedek parça"yı düzenleme ekranından
 * seçemiyordu.
 *
 * ═══ BU SADECE ETİKET DEĞİL, VERİ YAZDIRDI ═══
 *
 * 5 Ağustos 2026'da KART AÇMA formu da ters eşlemeyi kullanıyordu
 * (`0 Stok malzemesi / 1 Sarf malzemesi / 2 Demirbaş`). Canlıdaki
 * `END0003 İZOLE BANT` o gün açıldı ve `Type = 1` yazıldı;
 * `UpdatedByUserId` boş, yani kart hiç düzenlenmemiş.
 *
 * Okuması net: kullanıcı **"Sarf malzemesi" seçti, veriye Equipment
 * yazıldı.** Kaydın tipi kullanıcının hatası değil, EKRANIN hatası.
 * Kart açma formu sonradan düzeltildi; düzenleme formu ve etiketler
 * düzeltilmedi, kusur orada kaldı.
 */

/** Arka uçtaki `InventoryItemType` ile BİREBİR. Sonda doğruluyor. */
export const MALZEME_TIPI = {
  Material: 0,
  Equipment: 1,
  Consumable: 2,
  SparePart: 3,
} as const;

export type MalzemeTipi = (typeof MALZEME_TIPI)[keyof typeof MALZEME_TIPI];

/**
 * Kullanıcıya gösterilen etiketler — TEK KOPYA.
 *
 * Sözcükler, ÖLÇÜMDE DOĞRU ÇIKAN kart açma formundan alındı; o form
 * arka uçla hizalıydı ve kullanıcının bugün tanıdığı sözcükler bunlar.
 */
export const MALZEME_TIPI_ETIKETLERI: Record<number, string> = {
  [MALZEME_TIPI.Material]: "Malzeme",
  [MALZEME_TIPI.Equipment]: "Ekipman",
  [MALZEME_TIPI.Consumable]: "Sarf",
  [MALZEME_TIPI.SparePart]: "Yedek Parça",
};

/**
 * Açılır listelerin TEK kaynağı. Sıra enum değerine göre sabit; bir
 * form kendi sırasını kurarsa yeni bir kopya doğar.
 */
export const MALZEME_TIPI_SECENEKLERI: ReadonlyArray<{
  deger: number;
  etiket: string;
}> = Object.entries(MALZEME_TIPI_ETIKETLERI)
  .map(([deger, etiket]) => ({ deger: Number(deger), etiket }))
  .sort((a, b) => a.deger - b.deger);

/**
 * Bilinmeyen tip GİZLENMEZ. Arka uca yeni bir üye eklenip buraya
 * eklenmezse ekran "Tip 4" yazar — sessizce boş bırakmak kusuru
 * görünmez yapardı. `SparePart` tam olarak böyle kaybolmuştu.
 */
export function malzemeTipiEtiketi(tip: number): string {
  return MALZEME_TIPI_ETIKETLERI[tip] ?? `Tip ${tip}`;
}
