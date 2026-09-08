/**
 * MESAJ EKİ SINIRLARI — SUNUCUDAKİYLE AYNI OLMAK ZORUNDA.
 *
 * ═══ NEDEN İSTEMCİDE DE VAR ═══
 *
 * Koruma SUNUCUDAKİ sınırdır; burası kullanıcıya yardım eder.
 * 20 MB'lık bir dosyayı yükleyip sonra reddedilmek, baştan
 * söylenmekten çok daha kötü bir deneyim.
 *
 * ═══ NEDEN AYRIŞMA TEHLİKELİ ═══
 *
 * İki liste ayrışırsa iki yönde de zarar var:
 *   istemci daha GENİŞSE  -> kullanıcı yükler, sunucu reddeder
 *   istemci daha DARSA    -> meşru dosya hiç denenmez, sebebi de
 *                            görünmez
 *
 * `ek-sinirlari-tek-kaynak.test.ts` bu dosyayı sunucudaki
 * `MesajEkiKurallari.cs` ile karşılaştırıyor; ayrışırsa test düşer.
 */

export const DOSYA_BASINA_EN_FAZLA_BAYT = 20 * 1024 * 1024;
export const MESAJ_BASINA_EN_FAZLA_DOSYA = 5;

export const IZINLI_UZANTILAR = [
  ".pdf", ".docx", ".xlsx", ".pptx", ".odt", ".ods",
  ".jpg", ".jpeg", ".png", ".webp", ".gif",
  ".txt", ".csv",
  ".zip",
] as const;

/** Dosya seçicinin `accept` özniteliği — kullanıcıya baştan süzülmüş liste. */
export const ACCEPT = IZINLI_UZANTILAR.join(",");

export type EkKarari =
  | { kabul: true }
  | { kabul: false; sebep: string };

/**
 * Seçilen dosya kabul edilebilir mi.
 *
 * İÇERİK DENETİMİ BURADA YOK ve olamaz: tarayıcıda dosyanın
 * baytlarını okuyup imza doğrulamak mümkün ama ANLAMSIZ — istemci
 * tarafı zaten saldırganın kontrolünde. İçerik kapısı sunucuda
 * (`DosyaIcerikDenetimi`).
 */
export function dosyayiDenetle(dosya: File): EkKarari {
  const nokta = dosya.name.lastIndexOf(".");
  const uzanti = nokta < 0 ? "" : dosya.name.slice(nokta).toLowerCase();

  if (!uzanti) {
    return { kabul: false, sebep: "Dosyanın uzantısı yok." };
  }

  if (!IZINLI_UZANTILAR.includes(uzanti as (typeof IZINLI_UZANTILAR)[number])) {
    return {
      kabul: false,
      sebep: "Yalnız belge, görsel, metin ve zip dosyaları yüklenebilir.",
    };
  }

  if (dosya.size <= 0) {
    return { kabul: false, sebep: "Dosya boş." };
  }

  if (dosya.size > DOSYA_BASINA_EN_FAZLA_BAYT) {
    return {
      kabul: false,
      sebep: `Dosya boyutu en fazla ${
        DOSYA_BASINA_EN_FAZLA_BAYT / 1024 / 1024
      } MB olabilir.`,
    };
  }

  return { kabul: true };
}

/** İnsan okur boyut — "1,4 MB". Türkçe ondalık ayırıcı virgül. */
export function boyutMetni(bayt: number): string {
  if (bayt < 1024) return `${bayt} B`;
  if (bayt < 1024 * 1024) return `${(bayt / 1024).toFixed(0)} KB`;
  return `${(bayt / 1024 / 1024).toFixed(1).replace(".", ",")} MB`;
}
