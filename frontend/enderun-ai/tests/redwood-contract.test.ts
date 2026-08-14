import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * REDWOOD SÖZLEŞMESİ — bir kez temizlenen ekran temiz kalır.
 *
 * Yayma (A4) ekran ekran ilerliyor. Bir sayfa `design="redwood"`
 * yazdığı anda dört kuralı kabul etmiş olur. Bu kurallar testle
 * bağlanmasaydı, sıradaki geliştirme sırasında sayfaya yeniden
 * yerel bir Intl biçimleyici ya da bir window.confirm girer ve
 * kimse fark etmezdi — ekran "çalışmaya" devam ederdi.
 *
 * Kurallar yalnızca BAYRAĞI YAZAN sayfalara uygulanır: henüz
 * geçirilmemiş 145 sayfa bu testten etkilenmez, aksi halde yayma
 * tek hamlede yapılmak zorunda kalırdı.
 */

const ROOT = join(__dirname, "..");
const APP = join(ROOT, "app");

function pages(directory: string): string[] {
  const found: string[] = [];

  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);

    if (statSync(path).isDirectory()) {
      found.push(...pages(path));
      continue;
    }

    if (entry === "page.tsx") found.push(path);
  }

  return found;
}

/**
 * Yorumları atar; geriye çalışan kod kalır.
 *
 * Bu kod tabanı kararlarını yorumda anlatıyor ve o yorumlar doğal
 * olarak yasaklı kalıpların adını geçiriyor ("tek bir
 * `maximumFractionDigits: 0` biçimleyici hem başlığı hem satırı
 * basıyordu"). Ham metin üzerinde arama yapan bir koruma, hatayı
 * ANLATAN yorumu hatanın kendisi sanardı — ve doğru çözümü yorumu
 * silmek gibi gösterirdi.
 */
function code(text: string): string {
  return text
    .replace(/\/\*[\s\S]*?\*\//g, " ")
    .replace(/(^|[^:])\/\/[^\n]*/g, "$1");
}

const redwoodPages = pages(APP)
  .map((path) => ({
    path: path.slice(ROOT.length + 1),
    text: readFileSync(path, "utf8"),
  }))
  .filter((page) => page.text.includes('design="redwood"'))
  .map((page) => ({ ...page, code: code(page.text) }));

describe("Redwood ekranları", () => {
  it("en az bir ekran geçirilmiş durumda", () => {
    // Bu olmadan aşağıdaki testler boş kümede koşup sessizce geçerdi.
    expect(redwoodPages.length).toBeGreaterThan(0);
  });

  /**
   * Sayfanın yükleme ve hata dalları da ayrı birer ErpShell açıyor.
   * Biri bayraksız kalırsa kullanıcı sayfa yüklenirken bir tasarımı,
   * yüklendikten sonra ötekini görür — göz kırpması gibi.
   */
  it("sayfadaki bütün ErpShell çağrıları bayraklı", () => {
    const mismatched = redwoodPages
      .map((page) => ({
        path: page.path,
        shells: (page.text.match(/<ErpShell/g) ?? []).length,
        flags: (page.text.match(/design="redwood"/g) ?? []).length,
      }))
      .filter((page) => page.shells !== page.flags)
      .map((page) => `${page.path} (${page.flags}/${page.shells})`);

    expect(mismatched).toEqual([]);
  });

  /**
   * Para biçimi tek yerden gelir. Sayfa kendi Intl.NumberFormat'ını
   * kurarsa ondalık sayısı ve simge konumu ekrandan ekrana değişir;
   * bugün düzeltilen "₺1.250,00" geri gelir.
   */
  it("kendi sayı biçimleyicisini kurmuyor", () => {
    const offenders = redwoodPages
      .filter((page) => page.code.includes("new Intl.NumberFormat"))
      .map((page) => page.path);

    expect(offenders).toEqual([]);
  });

  /**
   * ÇAĞRI YERİNDE HANE SAYISI VERİLMEZ.
   *
   * Asıl tehlike ekrandan ekrana değişen simge değil; aynı
   * biçimleyicinin YANLIŞ SAYI TİPİNE uygulanması. İki kez oldu:
   * teklif listesinde `grandTotal` kuruşsuz basılıyordu (sözleşmeye
   * giren rakam yuvarlanmış görünüyordu) ve üretici birim fiyatı iki
   * haneye kırpılacaktı (veritabanında `numeric(18,6)`). İkisi de
   * "hane sayısını çağrı yerinde yazmak" yüzünden oldu: orada sayının
   * TİPİ değil, o an gözle uygun görünen bir rakam seçiliyor.
   *
   * Hane sayısı `lib/format/turkish.ts` içinde, sayı tipiyle birlikte
   * bir kez kararlaştırılır: money, unitPrice, coefficient, quantity…
   */
  it("hane sayısını çağrı yerinde belirlemiyor", () => {
    const offenders = redwoodPages
      .filter((page) => /(?:minimum|maximum)FractionDigits/.test(page.code))
      .map((page) => page.path);

    expect(
      offenders,
      "Hane sayısı çağrı yerinde verilmiş. lib/format/turkish.ts " +
        "içindeki adlandırılmış işlevlerden birini kullanın " +
        "(money, moneyWhole, unitPrice, coefficient, quantity, decimal).",
    ).toEqual([]);
  });

  /**
   * `style: "currency"` simgeyi çoğu kodda BAŞA koyuyor ve sağa
   * hizalı sütunda basamakları kaydırıyor; tanımadığı para kodunda da
   * istisna fırlatıyor. Yerine `money` / `currencyMoney` var.
   */
  it("elle para biçimi kurmuyor", () => {
    const offenders = redwoodPages
      .filter((page) => /style:\s*["']currency["']/.test(page.code))
      .map((page) => page.path);

    expect(offenders).toEqual([]);
  });

  /**
   * `toLocaleString` SAYI SEÇENEĞİYLE yasak — tarih biçimi serbest.
   *
   * Ayrım bilinçli: dosya listesindeki "13.08.2026 14:05" de
   * `toLocaleString("tr-TR", { … })` ile üretiliyor ve o meşru.
   * Yasaklanan, sayının hane kuralını çağrı yerinde yazmak.
   */
  it("sayıyı elle yerelleştirmiyor", () => {
    const pattern = /\.toLocaleString\(\s*["']tr-TR["']\s*,\s*\{[^}]*(?:FractionDigits|style:\s*["']currency["'])/;

    const offenders = redwoodPages
      .filter((page) => pattern.test(page.code))
      .map((page) => page.path);

    expect(offenders).toEqual([]);
  });

  /**
   * Tarayıcı diyaloğu biçimlendirilemez, gerekçe zorunlu tutamaz ve
   * hata mesajını aynı yerde gösteremez. Geçirilen ekranlarda
   * Modal/ConfirmDialog/Drawer kullanılır.
   */
  it("tarayıcı diyaloğu kullanmıyor", () => {
    const pattern = /(?<![.\w])(?:window\.)?(?:confirm|prompt|alert)\s*\(/;

    const offenders = redwoodPages
      .filter((page) => pattern.test(page.code))
      .map((page) => page.path);

    expect(offenders).toEqual([]);
  });

  /**
   * HİÇBİR YERDE ham hex renk yok — yalnızca `style={{ }}` içinde
   * değil.
   *
   * Kural eskiden sadece satır içi stil özniteliğini tarıyordu ve
   * bunu kaçırdı: renkler `varianceColor()` / `profitColor()` gibi
   * yardımcı işlevlerden dönüyordu, grafik serileri de bir dizi
   * sabitinde duruyordu. Dört sayfada on üç hex bu boşluktan geçmişti.
   * Renk nerede yazılırsa yazılsın tokendan kaçmış olur: marka rengi
   * değiştiğinde o hücre geride kalır, koyu temada ise ya okunmaz ya
   * da yanlış anlam taşır.
   *
   * Anlam taşıyan renk için `rw-value-danger` / `rw-value-success` /
   * `rw-value-warning` sınıfları, grafik serileri için
   * `--color-chart-*` değişkenleri var.
   */
  it("ham hex renk taşımıyor", () => {
    const offenders = redwoodPages
      .filter((page) => /#[0-9a-fA-F]{3,8}\b/.test(page.code))
      .map((page) => page.path);

    expect(
      offenders,
      "Ham hex renk kaldı. Anlamsal renk için rw-value-* sınıflarını, " +
        "grafik serisi için --color-chart-* değişkenlerini kullanın.",
    ).toEqual([]);
  });
});
