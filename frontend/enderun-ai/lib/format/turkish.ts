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
 *
 * ---------------------------------------------------------------
 * SAYI TİPİ → İŞLEV
 *
 *   Tutar (para)          → money / currencyMoney
 *                           sabit 2 hane, simge sonda
 *   Kuruşsuz özet/başlık  → moneyWhole
 *                           0 hane; YALNIZ özet kartı, huni, grafik
 *   Birim fiyat           → unitPrice
 *                           min 2 / max 4 — tutar DEĞİLDİR
 *   Katsayı / endeks      → coefficient
 *                           max 8, sondaki sıfır yazılmaz
 *   Oran (yüzde)          → percent
 *   Miktar                → quantity (max 4, trim)
 *                           decimalRange (belge sütunu, alt sınırlı)
 *   Serbest ondalık       → decimal(değer, hane)
 *   Tam sayı              → whole
 *
 * ÇAĞRI YERİNDE `maximumFractionDigits`, `style: "currency"` ya da
 * elle `toLocaleString` YAZILMAZ — hepsi buradaki adlandırılmış
 * işlevlerden geçer. `tests/redwood-contract.test.ts` bunu koruma
 * altına alıyor.
 *
 * NEDEN BU KADAR KESKİN: aynı biçimleyiciyi yanlış sayı tipine
 * uygulamak sessizce yanlış rakam gösteriyor ve iki kez oldu —
 * teklif listesinde `grandTotal` kuruşsuz basılıyordu (sözleşmeye
 * giren rakam yuvarlanmış görünüyordu), üretici birim fiyatı ise
 * iki haneye kırpılacaktı (veritabanında `numeric(18,6)`).
 *
 * ŞÜPHEDEYSEN VERİTABANI TİPİNE BAK: kuruş altı hane taşıyan bir
 * kolon tutar değildir — birim fiyat ya da katsayıdır. Sözleşmeye
 * ve belgeye giren rakamlar (toplam tutar, birim fiyat, hakediş
 * tutarı, katsayı) asla yuvarlanarak gösterilmez.
 * ---------------------------------------------------------------
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
 * ISO para kodları için ekranda yazılan gösterge.
 *
 * TL'de simge (₺) kullanılır — herkesin tanıdığı tek simge odur.
 * DİĞERLERİNDE ISO KODU yazılır: "$" tek başına ABD, Kanada ve
 * Avustralya dolarını birden gösteriyor; bir tedarikçi teklifinde
 * hangisi olduğu belirsiz kalamaz.
 */
const CURRENCY_LABELS: Record<string, string> = {
  TRY: "₺",
};

/**
 * Tutar + para birimi — "1.250,00 ₺", "1.250,00 USD".
 *
 * NEDEN AYRI İŞLEV: ekranların çoğu para birimini kayıttan alıyor ve
 * her biri kendi `Intl.NumberFormat({ style: "currency" })`'ini
 * kuruyordu. O biçim simgeyi çoğu kodda BAŞA koyuyor, sağa hizalı
 * sütunda basamakları kaydırıyordu; üstelik tanımadığı bir kodda
 * istisna fırlattığı için her sayfa ayrıca try/catch yedeği yazmak
 * zorunda kalmıştı. Burada ne istisna var ne de yedek gerekiyor.
 */
export function currencyMoney(
  value: number | null | undefined,
  code = "TRY",
): string {
  return money(value, CURRENCY_LABELS[code] ?? code);
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

/**
 * En çok N ondalık; SONDAKİ SIFIRLAR YAZILMAZ — "1,5" ve "0,00012345".
 *
 * `quantity` bunun 4 haneli hâli. Ayrı bir işlev gerekti çünkü bazı
 * alanlar dört haneden fazlasını taşıyor: fiyat farkı endeks
 * katsayıları sekiz haneye kadar iniyor ve sabit haneli biçimle
 * yazılsaydı "1,5" ekranda "1,50000000" görünürdü.
 *
 * Sabit hane isteniyorsa `number` kullanılır (kur gibi).
 */
export function decimal(
  value: number | null | undefined,
  maxDecimals: number,
): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return formatter({
    minimumFractionDigits: 0,
    maximumFractionDigits: maxDecimals,
  }).format(value);
}

/**
 * Alt ve üst ondalık sınırı ayrı verilen sayı — "2,00" ve "0,3125".
 *
 * `decimal` alt sınırı sıfır kabul eder, `number` alt ve üst sınırı
 * eşitler; ikisinin de karşılamadığı bir aralık var: EN AZ iki hane
 * yazılsın ama gerekirse dörde kadar çıksın. Antetli teklif çıktısında
 * miktar sütunu böyle: "2,00" hizada durur, "0,3125" ise kırpılmaz.
 * `quantity` ile yazılsaydı "2,00" ekranda "2" olurdu ve basılı
 * belgede sütun bozulurdu.
 */
export function decimalRange(
  value: number | null | undefined,
  minDecimals: number,
  maxDecimals: number,
): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return formatter({
    minimumFractionDigits: minDecimals,
    maximumFractionDigits: maxDecimals,
  }).format(value);
}

/**
 * Katsayı / endeks — "1,5" ve "0,00012345".
 *
 * SEKİZ HANE YALNIZCA GÖSTERİM İÇİN: fiyat farkı hesabı sunucuda tam
 * hassasiyette yapılıyor, burada kırpılan tek şey ekrandaki basamak
 * sayısı. Hesaba giren değere dokunulmuyor.
 *
 * Sabit haneli biçim kullanılamaz: katsayıların çoğu 1,5 gibi kısa
 * sayılar ve "1,50000000" diye yazılsalardı tablo okunmaz olurdu.
 *
 * TUTAR DEĞİLDİR — para sütununda kullanılmaz.
 */
export function coefficient(value: number | null | undefined): string {
  return decimal(value, 8);
}

/**
 * Birim fiyat — "12,4567 ₺", "8,50 ₺".
 *
 * TUTAR DEĞİLDİR, bu yüzden iki hane kuralının dışındadır. Üretici
 * fiyat listesinde birim fiyat veritabanında `numeric(18,6)` olarak
 * duruyor: metrelik bir kablo 12,4567 ₺ olabiliyor. İki haneye
 * yuvarlansaydı ekranda 12,46 ₺ görünür, kullanıcı o rakamla miktarı
 * çarptığında toplam tutmazdı.
 *
 * Dört hane sınırı bilinçli: altıncı haneye kadar yazmak sütunu
 * okunmaz yapıyor, dördüncü haneden sonrası fiyat listelerinde
 * pratikte kullanılmıyor.
 *
 * TOPLAM VE ARA TOPLAM İÇİN KULLANILMAZ — onlar tutardır, `money` /
 * `currencyMoney` ile iki hane yazılır.
 */
export function unitPrice(
  value: number | null | undefined,
  code = "TRY",
): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return `${decimalRange(value, 2, 4)} ${CURRENCY_LABELS[code] ?? code}`;
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
  coefficient,
  decimal,
  decimalRange,
  money,
  currencyMoney,
  moneyWhole,
  unitPrice,
  rate,
  percent,
  quantity,
  whole,
  number,
  date,
  dateTime,
};
