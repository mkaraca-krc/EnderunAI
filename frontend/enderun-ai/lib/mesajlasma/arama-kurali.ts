/**
 * MESAJ ARAMA KURALI — ARAYÜZ TARAFI.
 *
 * ═══ NEDEN BU DOSYA VAR ═══
 *
 * 2026-09-06: ekranda "Kişi ara (en az 2 harf)" yazıyordu, sunucu ise
 * üç harf istiyordu. Kullanıcı iki harf yazıyor, ekran ona bir şey
 * demiyor, sunucu anlamlı bir hata dönüyor ve arayüz onu yutuyordu.
 * Sonuç: hiçbir açıklaması olmayan boş bir liste.
 *
 * ═══ TEK SAYI, İKİ YERDE OKUNUR ═══
 *
 * Asgari harf sayısının GEREKÇESİ sunucuda ölçüldü
 * (`MesajAramaKurali.EnAzHarf`): iki harflik sorguda trigram indeksi
 * devre dışı kalıyor ve sorgu sıra taramasına düşüyor. Karar oraya
 * ait; burası onu TEKRARLAMIYOR, ona UYUYOR.
 *
 * İkisinin aynı kaldığını `mesaj-arama-asgari-harf.test.ts` tutuyor:
 * test iki kaynağı da okuyup sayıları karşılaştırıyor. Biri
 * değiştirilip diğeri unutulursa test kırmızı yanar.
 */
export const MESAJ_ARAMA_EN_AZ_HARF = 3;

/** Kutunun altında ve yer tutucuda gösterilecek metin. */
export const MESAJ_ARAMA_IPUCU =
  `Kişi ara (en az ${MESAJ_ARAMA_EN_AZ_HARF} harf)`;
