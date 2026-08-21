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
 *                           min 2 / max 6 — tutar DEĞİLDİR
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
 * ---------------------------------------------------------------
 * ÖLÇEK İLKESİ — BİRİM FİYAT
 *
 * Birim fiyatın GÖSTERİM ölçeği, ilgili kolonun VERİTABANI ondalık
 * ölçeğini karşılamak zorundadır. Gösterim ölçeği sütunun
 * okunurluğuna göre değil, verinin taşıdığı hassasiyete göre
 * seçilir; çünkü o rakam sözleşmeye ve belgeye giriyor ve kırpılmış
 * gösterilen bir fiyat, kullanıcı miktarla çarptığında tutmayan bir
 * toplam üretiyor.
 *
 * Bugünkü en geniş ölçek 6: `project_boq_items.UnitPrice`,
 * `ManufacturerPriceListItem.ListPrice`, `offer_items.*UnitPrice`,
 * `sales_invoice_items.UnitPrice`. Bu yüzden `unitPrice` max 6.
 * (Önce max 4'tü ve gerekçesi "dörtten fazlası pratikte
 * kullanılmıyor" idi — ölçüt yanlıştı: kolon altı hane taşıyabildiği
 * sürece dördüncüde kesmek gizli bir kırpmadır.)
 *
 * İLERİDE ALTIDAN BÜYÜK ÖLÇEKLİ BİR BİRİM-FİYAT KOLONU ÇIKARSA
 * `unitPrice` aynı kuralla yükseltilir. Alt sınır 2 kalır: tipik
 * fiyat kompakt görünsün.
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
 * Birim fiyat — "8,50 ₺", "1,5234 ₺", "1,523456 ₺".
 *
 * TUTAR DEĞİLDİR, bu yüzden iki hane kuralının dışındadır. Birim fiyat
 * veritabanında `numeric(_,6)` olarak duruyor
 * (`project_boq_items.UnitPrice`, `ManufacturerPriceListItem.ListPrice`):
 * metrelik bir kablo 12,4567 ₺ olabiliyor. İki haneye yuvarlansaydı
 * ekranda 12,46 ₺ görünür, kullanıcı o rakamla miktarı çarptığında
 * toplam tutmazdı.
 *
 * ÖLÇEK ALTI HANE, ÇÜNKÜ KOLONUN ÖLÇEĞİ ALTI. Burada önce dört hane
 * yazıyordu ve "altıncı haneye kadar yazmak sütunu okunmaz yapar,
 * dördünden fazlası pratikte kullanılmıyor" diye gerekçelendirilmişti.
 * Gerekçe yanlıştı: ölçeği belirleyen şey sütunun okunurluğu değil,
 * VERİTABANININ TAŞIDIĞI HASSASİYET. Dört hanede kalsaydı altı haneli
 * bir fiyat girildiği gün ekran onu sessizce kırpardı — ve o rakam
 * sözleşmeye/belgeye giriyor.
 *
 * İLKE: birim fiyat gösterim ölçeği, ilgili kolonun DB ondalık
 * ölçeğini KARŞILAMALI. İleride altıdan büyük ölçekli bir birim-fiyat
 * kolonu çıkarsa bu işlev aynı kuralla yükseltilir.
 *
 * Alt sınır iki, üst sınır altı: tipik fiyat kompakt kalıyor
 * (1,5000 -> "1,50"), hassas fiyat kırpılmıyor (1,523456 ->
 * "1,523456"). Sondaki sıfırlar ikinci haneden sonra yazılmaz.
 *
 * TOPLAM VE ARA TOPLAM İÇİN KULLANILMAZ — onlar tutardır, `money` /
 * `currencyMoney` ile SABİT iki hane yazılır. Buradaki trim tutara
 * sızmaz; ikisi ayrı işlev olmasının sebebi tam olarak budur.
 */
export function unitPrice(
  value: number | null | undefined,
  code = "TRY",
): string {
  if (isMissing(value)) return EMPTY_VALUE;

  return `${decimalRange(value, 2, 6)} ${CURRENCY_LABELS[code] ?? code}`;
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

/* =================================================================
 * GİRİŞ TARAFI — metin → sayı, canlı biçimleme, imleç hesabı.
 *
 * NEDEN AYNI DOSYADA: yukarısı gösterimin tek kaynağı. Giriş tarafı
 * ayrı bir dosyaya yazılsaydı iki biçimleme mantığı doğardı ve
 * zamanla ayrışırlardı — listedeki tutar ile formdaki tutar farklı
 * davranmaya başlardı. Aynı kural, aynı yer.
 *
 * BURASI GÖSTERİMİN TERSİ: yukarıdaki işlevler sayıyı metne çevirir,
 * buradakiler kullanıcının yazdığı metni sayıya. İkisi birbirinin
 * aynası olmak zorunda.
 * ================================================================= */

/** Çözümleme sonucu: ekranda görünecek metin ve makineye gidecek sayı. */
export type AmountInputState = {
  /** Kullanıcıya gösterilecek biçimli metin — "2.814.000,00". */
  text: string;
  /**
   * Sunucuya gidecek HAM sayı. Alan boşsa null.
   *
   * BOŞ İLE SIFIR FARKLIDIR: boş "girilmedi", sıfır "sıfır lira"
   * demek. Boşu 0 saymak, girilmemiş bir tutarı sıfır tutar gibi
   * kaydetmek olurdu.
   */
  value: number | null;
};

/** Tutar alanında en fazla iki ondalık — kuruş. */
const AMOUNT_INPUT_MAX_DECIMALS = 2;

/**
 * Ondalık ayıracını BULUR — hem virgül hem NOKTA kabul edilir.
 *
 * NOKTA ŞART: sayısal tuş takımında virgül yok, muhasebeci noktaya
 * basar. Kabul edilmezse "1234.5" yazan kullanıcı 12.345 kaydeder ve
 * bunu fark etmez — on kat hata, sessizce.
 *
 * Ama nokta Türkçe gösterimde BİNLİK ayıracı da. Ayrım kuralı:
 *   - hem virgül hem nokta varsa: SONRA gelen ondalıktır
 *     ("2.814.000,00" ve "2,814,000.00" ikisi de doğru okunur),
 *   - tek virgül varsa: virgül ondalıktır (Türkçe yazım),
 *   - birden çok virgül varsa: hepsi binliktir (ABD yazımı),
 *   - tek nokta varsa: ARDINDA 1-2 rakam varsa ondalık, 3 rakam
 *     varsa binlik ("1234.5" → 1234,5 ama "1.234" → 1234).
 *
 * Son kural yapıştırma için: Türkçe biçimli "1.234" metni binlik
 * taşır, elle yazılan "1234.5" ise kuruş.
 */
function findDecimalSeparator(raw: string): number {
  const lastComma = raw.lastIndexOf(",");
  const lastDot = raw.lastIndexOf(".");

  const digitsAfter = (at: number) =>
    raw.slice(at + 1).replace(/\D/g, "").length;

  // İkisi birden varsa SONRA gelen ondalıktır: "1.234,5" (Türkçe) ve
  // "2,814,000.00" (ABD) aynı kuralla doğru okunur.
  if (lastComma >= 0 && lastDot >= 0) return Math.max(lastComma, lastDot);

  if (lastComma >= 0) {
    /*
     * TEK VİRGÜL KOŞULSUZ ONDALIKTIR — "ardındaki rakam sayısı"
     * kuralına TABİ DEĞİL. Tuzağı kapatan şey bu.
     *
     * Alan ondalık ayıracını her zaman virgülle yazdığı için, kullanıcı
     * noktaya bassa bile sonraki tuşta metin "1,5" oluyor. Virgül de
     * rakam sayısı kuralına tabi olsaydı, 1,50 yazıp fazladan bir sıfır
     * basan kullanıcının metni "1,500" olur, kural onu BİNLİĞE çevirir
     * ve tutar sessizce BİN KATINA çıkardı. Üçüncü hane yorumu
     * değiştirmez; iki hane sınırında düşer.
     *
     * Bu iki testle bağlı ("...üçüncü hane tutarı bin katına
     * çıkarmaz"); sonda ile doğrulandı — kural gevşetilince ikisi de
     * düşüyor.
     */
    const commaCount = raw.split(",").length - 1;
    if (commaCount === 1) return lastComma;

    return digitsAfter(lastComma) <= AMOUNT_INPUT_MAX_DECIMALS ? lastComma : -1;
  }

  if (lastDot >= 0) {
    /*
     * NOKTA: ardında ÜÇ rakam varsa binliktir, 0-2 rakam varsa
     * ondalıktır.
     *
     * Nokta SAYISINA bakılamaz — alan yazdıkça biçimlendiği için
     * ekranda zaten binlik noktaları duruyor: "1.234" yazıp nokta
     * basan kullanıcının metni "1.234." oluyor ve "birden çok nokta
     * varsa hepsi binliktir" kuralı o noktayı yutuyordu. Sonuç:
     * "1234.5" yazan kullanıcı 12.345 kaydediyordu — düzeltmeye
     * çalıştığımız hatanın ta kendisi.
     *
     * Binlik öbeği HER ZAMAN üç rakamdır; bu yüzden "ardındaki rakam
     * sayısı" ayrımı güvenli.
     */
    return digitsAfter(lastDot) <= AMOUNT_INPUT_MAX_DECIMALS ? lastDot : -1;
  }

  return -1;
}

/**
 * Yazılan metni biçimli metne ve ham sayıya çevirir.
 *
 * YAZDIKÇA BİÇİMLENİR: binlik noktaları anında girer. Yalnız blur'da
 * biçimlenseydi kullanıcı yazarken rakamları sayamaz, 2.814.000 ile
 * 28.140.000'i gözle ayıramazdı.
 *
 * ÜÇÜNCÜ ONDALIK YAZILAMAZ, YUVARLANMAZ. Sessizce yuvarlamak
 * kullanıcının yazdığından farklı bir tutar kaydeder; alan onu hiç
 * kabul etmeyerek durumu görünür kılıyor.
 */
export function formatAmountInput(raw: string): AmountInputState {
  if (raw === null || raw === undefined) return { text: "", value: null };

  const separatorAt = findDecimalSeparator(raw);

  const integerPart = separatorAt >= 0 ? raw.slice(0, separatorAt) : raw;
  const fractionPart = separatorAt >= 0 ? raw.slice(separatorAt + 1) : "";

  const integerDigits = integerPart.replace(/\D/g, "").replace(/^0+(?=\d)/, "");
  const fractionDigits = fractionPart
    .replace(/\D/g, "")
    .slice(0, AMOUNT_INPUT_MAX_DECIMALS);

  const hasSeparator = separatorAt >= 0;

  if (integerDigits === "" && fractionDigits === "" && !hasSeparator) {
    return { text: "", value: null };
  }

  const grouped = integerDigits === ""
    ? "0"
    : formatter({ minimumFractionDigits: 0, maximumFractionDigits: 0 })
        .format(Number(integerDigits));

  // Ayıraç YAZILDIĞI ANDA korunuyor: "1234," yazan kullanıcı bir
  // sonraki tuşta kuruşu yazacak. Silinseydi virgül ekranda hiç
  // durmaz, kuruş yazmak imkânsızlaşırdı.
  const text = hasSeparator ? `${grouped},${fractionDigits}` : grouped;

  const value = Number(`${integerDigits === "" ? "0" : integerDigits}.${fractionDigits || "0"}`);

  return { text, value: Number.isFinite(value) ? value : null };
}

/**
 * Alan odaktan çıkınca tutarı TAM biçime tamamlar — "1.234,5" →
 * "1.234,50". Boş alan boş kalır (bkz. AmountInputState.value).
 */
export function normalizeAmountInput(value: number | null): string {
  return value === null ? "" : amount(value);
}

/**
 * İmleçten önceki RAKAM sayısı.
 *
 * Karakter indeksi kullanılamaz: biçimleme ayıraç ekleyip çıkardıkça
 * indeks kayar ve imleç her tuşta bir hane sağa/sola atlar. Rakam
 * sayısı ise biçimlemeden ETKİLENMEZ — sabit olan tek şey odur.
 */
export function digitsBeforeCaret(text: string, caret: number): number {
  return text.slice(0, caret).replace(/\D/g, "").length;
}

/**
 * Verilen rakam sayısına denk gelen imleç konumu.
 *
 * `afterSeparator`: kullanıcı AZ ÖNCE ondalık ayıracı yazdıysa imleç
 * ayıracın SAĞINA konur. Yoksa rakam sayısı ayıracı saymadığı için
 * imleç virgülün soluna düşüyor ve bir sonraki tuş kuruş yerine
 * tam kısma giriyordu: "1234," yazıp "5" basan kullanıcı 1.234,50
 * yerine 12.345 kaydediyordu.
 */
export function caretAfterDigits(
  text: string,
  digitCount: number,
  afterSeparator = false,
): number {
  if (afterSeparator) {
    const separator = text.indexOf(",");
    return separator < 0 ? text.length : separator + 1;
  }

  if (digitCount <= 0) {
    // Baştaki ayıraçların önüne değil, ilk rakamın önüne konumlan.
    const firstDigit = text.search(/\d/);
    return firstDigit < 0 ? text.length : firstDigit;
  }

  let seen = 0;

  for (let index = 0; index < text.length; index += 1) {
    if (/\d/.test(text[index])) {
      seen += 1;
      if (seen === digitCount) return index + 1;
    }
  }

  return text.length;
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

  // Giriş tarafı — aynı kuralın tersi.
  formatAmountInput,
  normalizeAmountInput,
  digitsBeforeCaret,
  caretAfterDigits,
};
