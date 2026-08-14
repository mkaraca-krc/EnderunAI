/**
 * TÜRKÇE SAYI VE PARA BİÇİMİ — arayüzün TEK kaynağı.
 *
 * Backend'deki <c>TurkishFormat</c> ile birebir aynı kuralları uygular:
 * binlik ayıracı nokta, ondalık ayıracı virgül, tutar iki ondalık.
 * Aynı rakam sunucu metninde "60.000,00", ekranda başka türlü
 * görünmemeli.
 *
 * NEDEN TEK YER: bugün arayüzde 153 ayrı <c>Intl.NumberFormat</c> ve 91
 * ayrı <c>toLocaleString("tr-TR")</c> çağrısı var; ondalık sayısı ve
 * para birimi konumu ekrandan ekrana değişiyor. Kural burada bir kez
 * yazılır.
 *
 * YALNIZCA GÖSTERİM İÇİNDİR. Makineye giden hiçbir yerde (dosya adı,
 * CSV, API gövdesi, kod üretimi) kullanılmamalı — oralarda ham sayı
 * doğrudur.
 */

const TURKISH_LOCALE = "tr-TR";

/**
 * Biçimleyiciler ÖNBELLEKLENİR: Intl.NumberFormat kurulumu pahalıdır ve
 * uzun tablolarda satır başına yeniden kurulursa fark edilir yavaşlık
 * yapar.
 */
const cache = new Map<string, Intl.NumberFormat>();

function formatter(options: Intl.NumberFormatOptions): Intl.NumberFormat {
  const key = JSON.stringify(options);
  const existing = cache.get(key);

  if (existing) return existing;

  const created = new Intl.NumberFormat(TURKISH_LOCALE, options);
  cache.set(key, created);

  return created;
}

/**
 * Sayısal olmayan girdi "—" olarak gösterilir.
 *
 * Null'ı sıfır saymak yasak: "veri yok" ile "sıfır" farklı şeylerdir ve
 * finansal bir ekranda bunları karıştırmak, olmayan bir bakiyeyi sıfır
 * bakiye gibi göstermek olurdu.
 */
export const EMPTY_VALUE = "—";

function isMissing(value: number | null | undefined): value is null | undefined {
  return value === null || value === undefined || Number.isNaN(value);
}

/** Tutar: iki ondalık, binlik ayıraçlı — "60.000,00". */
export function amount(value: number | null | undefined): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return formatter({
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}

/**
 * Para: tutarın sonuna simge — "60.000,00 ₺".
 *
 * SİMGE SONDA, çünkü Türkçe yazımda tutar önce okunur; ayrıca sağa
 * hizalı sütunlarda rakamlar hizada kalır, simge kaymaz.
 */
export function money(
  value: number | null | undefined,
  currency = "₺",
): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return `${amount(value)} ${currency}`;
}

/**
 * Ondalıksız para — "1.234.568 ₺".
 *
 * YALNIZCA BAŞLIK RAKAMI İÇİN: özet kartı, gösterge paneli, grafik
 * ekseni. Oralarda kuruş okunmaz, sayının büyüklüğü okunur ve iki
 * hane gürültüdür.
 *
 * TABLODA VE DEFTERDE KULLANILMAZ: yuvarlanmış satırların toplamı
 * gösterilen toplamla tutmaz, kullanıcı da hangisinin doğru olduğunu
 * bilemez. Orada `money` kullanılır.
 *
 * Bu işlev, sayfaların tek tek kurduğu `maximumFractionDigits: 0`
 * biçimleyicilerinin yerine geçiyor; kural burada bir kez yazılır.
 */
export function moneyWhole(
  value: number | null | undefined,
  currency = "₺",
): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return `${whole(value)} ${currency}`;
}

/** Oran değeri; yüzde işaretini çağıran koyar — "5,50". */
export function rate(value: number | null | undefined): string {
  return amount(value);
}

/** Yüzde — "%5,5". İşaret BAŞTA: Türkçe yazım kuralı. */
export function percent(
  value: number | null | undefined,
  decimals = 1,
): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return `%${formatter({
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value)}`;
}

/**
 * Miktar: dört ondalığa kadar ama SONDAKİ SIFIRLAR YAZILMAZ —
 * "1.250,75" ve "3". Stok miktarı "3,0000" diye görünmemeli; backend
 * dört hane tutuyor, ekranda gereksiz sıfır gürültüdür.
 */
export function quantity(value: number | null | undefined): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return formatter({
    minimumFractionDigits: 0,
    maximumFractionDigits: 4,
  }).format(value);
}

/** Ondalıksız sayı — "320". */
export function whole(value: number | null | undefined): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return formatter({
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  }).format(value);
}

/** Ondalık basamağı çağıran belirler. */
export function number(
  value: number | null | undefined,
  decimals: number,
): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return formatter({
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value);
}

/** Tarih — "13.08.2026". */
export function date(value: string | Date | null | undefined): string {
  if (!value) return EMPTY_VALUE;

  const parsed = value instanceof Date ? value : new Date(value);

  if (Number.isNaN(parsed.getTime())) return EMPTY_VALUE;

  return parsed.toLocaleDateString(TURKISH_LOCALE);
}

/** Tarih ve saat — "13.08.2026 14:05". */
export function dateTime(value: string | Date | null | undefined): string {
  if (!value) return EMPTY_VALUE;

  const parsed = value instanceof Date ? value : new Date(value);

  if (Number.isNaN(parsed.getTime())) return EMPTY_VALUE;

  return `${parsed.toLocaleDateString(TURKISH_LOCALE)} ${parsed.toLocaleTimeString(
    TURKISH_LOCALE,
    { hour: "2-digit", minute: "2-digit" },
  )}`;
}

export const turkishFormat = {
  amount,
  money,
  moneyWhole,
  rate,
  percent,
  quantity,
  whole,
  number,
  date,
  dateTime,
};
