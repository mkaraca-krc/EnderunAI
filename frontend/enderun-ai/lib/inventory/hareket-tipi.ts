/**
 * STOK HAREKET TİPİ — TEK ETİKET KAYNAĞI.
 *
 * ═══ NEDEN VAR: ÖLÇÜLEN KUSUR (2026-09-13) ═══
 *
 * Arka uçtaki `StockMovementType` şöyle: `TransferIn = 2`,
 * `TransferOut = 3`. Depo transferinde KAYNAK depoya `TransferOut`
 * yazılıyor (`InventoryController.cs`, transfer ucu) — yani veride
 * **2 = giriş, 3 = çıkış**.
 *
 * Ön yüzde ise ÜÇ AYRI yerde aynı eşleme elle yazılmıştı ve ÜÇÜ DE
 * tersti:
 *
 *     app/depo-stok/hareketler/page.tsx : 2 "Transfer çıkış", 3 "Transfer giriş"
 *     app/depo-stok/page.tsx            : 2 "Transfer Çıkış", 3 "Transfer Giriş"
 *     app/dashboard/page.tsx            : 2 "Transfer Çıkışı", 3 "Transfer Girişi"
 *
 * Sonuç: transfer GİRİŞLERİ ekranda ÇIKIŞ görünüyordu. Stok modülü
 * henüz hiç kullanılmadığı için kimse fark etmemişti; ilk transferin
 * girileceği gün yanlış gösterecekti.
 *
 * ═══ ASIL SEBEP: ÜÇ KOPYA ═══
 *
 * Kusur "birinin yanlış yazması" değil, AYNI BİLGİNİN ÜÇ YERDE
 * durmasıdır. Üçünü düzeltmek kusuru bugün kapatır, yarın dördüncü
 * kopya açılır. Bu yüzden eşleme buraya alındı ve
 * `tests/stok-hareket-etiketi.test.ts` onu ARKA UCUN KENDİ ENUM'UYLA
 * karşılaştırıyor — ön yüzdeki bir kopyayla değil, çünkü kopya da
 * yanlış olabilir; nitekim üçü de yanlıştı.
 *
 * (Aynı ders `gorev-durum-etiketi.test.ts`'te bir kez öğrenilmişti:
 * "Sunucudaki sıra ile aynı" diye yazılmış bir yorum yanlıştı ve kimse
 * ölçmemişti. Burada yorum değil, sonda var.)
 */

/** Arka uçtaki `StockMovementType` ile BİREBİR. Sonda doğruluyor. */
export const STOK_HAREKET_TIPI = {
  Receipt: 0,
  Issue: 1,
  TransferIn: 2,
  TransferOut: 3,
  Return: 4,
  Adjustment: 5,
  Count: 6,
} as const;

export type StokHareketTipi =
  (typeof STOK_HAREKET_TIPI)[keyof typeof STOK_HAREKET_TIPI];

/**
 * Kullanıcıya gösterilen etiketler. TEK KOPYA — yeni bir ekran
 * etiketi kendi yazmaz, buradan alır.
 *
 * `Count = 6` arka uçta TANIMLI ama hiçbir yerde YAZILMIYOR (ölü
 * değer, 2026-09-13 ölçümü). Yine de eşlemede duruyor: bir gün
 * yazılırsa ekran "Hareket 6" göstermesin.
 */
export const STOK_HAREKET_ETIKETLERI: Record<number, string> = {
  [STOK_HAREKET_TIPI.Receipt]: "Giriş",
  [STOK_HAREKET_TIPI.Issue]: "Çıkış",
  [STOK_HAREKET_TIPI.TransferIn]: "Transfer giriş",
  [STOK_HAREKET_TIPI.TransferOut]: "Transfer çıkış",
  [STOK_HAREKET_TIPI.Return]: "İade",
  [STOK_HAREKET_TIPI.Adjustment]: "Sayım düzeltme",
  [STOK_HAREKET_TIPI.Count]: "Sayım",
};

/**
 * Bilinmeyen tip GİZLENMEZ. Arka uca yeni bir değer eklenip buraya
 * eklenmezse ekran "Hareket 7" yazar — sessizce boş bırakmak, kusuru
 * görünmez yapardı.
 */
export function stokHareketEtiketi(tip: number): string {
  return STOK_HAREKET_ETIKETLERI[tip] ?? `Hareket ${tip}`;
}

/**
 * Depodan AZALTAN hareket mi?
 *
 * Renk ve "bugünkü çıkış" gibi SAYILAR bunu üç ayrı ekranda elle
 * yazıyordu ve üçü de `TransferIn = 2`yi çıkış sayıyordu — yani
 * transfer GİRİŞLERİ hem sarı boyanıyor hem de günlük çıkış
 * toplamına ekleniyordu (2026-09-13 ölçümü). Etiket yanlışsa
 * kullanıcı görür; SAYI yanlışsa görmez.
 *
 * `Return = 4` ve `Adjustment = 5` bilerek DIŞARIDA: iade yönü ve
 * düzeltmenin işareti duruma göre değişir, ölçülmeden hüküm
 * verilmiyor.
 */
export function stokAzaltanHareket(tip: number): boolean {
  return (
    tip === STOK_HAREKET_TIPI.Issue || tip === STOK_HAREKET_TIPI.TransferOut
  );
}

/** Depoya EKLEYEN kesin hareketler (mal kabul + transfer girişi). */
export function stokArtiranHareket(tip: number): boolean {
  return (
    tip === STOK_HAREKET_TIPI.Receipt || tip === STOK_HAREKET_TIPI.TransferIn
  );
}
