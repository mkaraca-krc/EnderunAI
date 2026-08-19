import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * REDWOOD SÖZLEŞMESİ — bir kez temizlenen ekran temiz kalır.
 *
 * Yayma (A4) bitti: 160 ekranın tamamı `design="redwood"` yazıyor.
 * Bu kurallar testle bağlanmasaydı, sıradaki geliştirme sırasında bir
 * ekrana yeniden yerel bir Intl biçimleyici ya da bir window.confirm
 * girer ve kimse fark etmezdi — ekran "çalışmaya" devam ederdi.
 *
 * KAPSAM İKİ AYRI KÜME, ÇÜNKÜ İKİ AYRI SORU SORULUYOR:
 *
 *   allScreens    — ErpShell açan dosyalar. "Bu ekran Redwood mu?"
 *   numberSurface — app/ + components/ + services/ altındaki her şey.
 *                   "Bu sayı doğru tipten mi geçiyor?"
 *
 * İkisini tek kümede toplamak, yaymanın iki kez kaçırdığı hatanın ta
 * kendisiydi: kural dosyaya değil, kullanıcının GÖRDÜĞÜ şeye ait.
 */

const ROOT = join(__dirname, "..");

/**
 * EKRANIN YERİ page.tsx DEĞİL, ErpShell'İ AÇAN DOSYADIR.
 *
 * Bu tarama önce yalnızca `app/**\/page.tsx` dosyalarına bakıyordu ve
 * altı ekranı kaçırdı: `sekreterya/toplantilar|telefon-notlari|
 * randevular` ve `depo-stok/cikis|transfer` sayfaları iki-beş
 * satırlık birer yeniden-dışa-aktarım; kabuğu `components/` altındaki
 * ortak bir bileşen açıyor. Sayfa dosyasında `<ErpShell` geçmediği
 * için "geçirilmemiş" bile sayılmıyorlardı — kullanıcı o altı ekranda
 * eski tasarımı görmeye devam ediyordu.
 *
 * O yüzden ölçüt tek: NEREDE `<ErpShell` açılıyorsa orası bir ekrandır.
 */
function screens(directory: string): string[] {
  const found: string[] = [];

  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);

    if (statSync(path).isDirectory()) {
      found.push(...screens(path));
      continue;
    }

    if (/\.tsx$/.test(entry) && readFileSync(path, "utf8").includes("<ErpShell")) {
      found.push(path);
    }
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

/**
 * `new Date( … )` bölgelerini tek bir `D` ile değiştirir.
 *
 * Parantez sayarak yürüyor, çünkü çağrı çok satıra yayılabiliyor:
 * muhasebe fişinde `new Date(\n item.postedAtUtc\n ).toLocaleString(…)`
 * böyle yazılı. Düz bir düzenli ifade bunu tarih olarak tanıyamaz ve
 * meşru bir tarih biçimini sayı ihlali sanardı.
 */
function maskDateExpressions(source: string): string {
  const parts: string[] = [];
  let index = 0;

  for (;;) {
    const start = source.indexOf("new Date(", index);

    if (start === -1) {
      parts.push(source.slice(index));
      break;
    }

    parts.push(source.slice(index, start));

    let depth = 1;
    let cursor = start + "new Date(".length;

    while (cursor < source.length && depth > 0) {
      if (source[cursor] === "(") depth += 1;
      else if (source[cursor] === ")") depth -= 1;
      cursor += 1;
    }

    parts.push("D");
    index = cursor;
  }

  return parts.join("");
}

/**
 * Ham hex'in MEŞRU olduğu yerler — dördü de gerekçeli, liste kapalıdır.
 *
 *   yazdir / tutanak — Yazdırma sayfaları. Çıktı her zaman beyaz kâğıda
 *     gidiyor; tema tokenı bağlanırsa koyu temadaki kullanıcı sözleşme
 *     ekini siyah zeminle bastırır. Renkler burada bilinçli sabit.
 *   layout.tsx — `themeColor` tarayıcı üst çubuğuna gidiyor ve meta
 *     etiketi CSS değişkeni kabul etmiyor; literal olmak zorunda.
 *   portal/[token] — Müşteriye açık, kabuk dışı tek sayfa. Tema
 *     anahtarı yok, kendi kapalı görsel dünyası var.
 */
const HEX_ALLOWED = [
  join("yazdir", "page.tsx"),
  join("tutanak", "page.tsx"),
  join("app", "layout.tsx"),
  join("portal", "[token]", "page.tsx"),
];

function read(path: string) {
  const text = readFileSync(path, "utf8");
  return { path: path.slice(ROOT.length + 1), text, code: code(text) };
}

const allScreens = [
  ...screens(join(ROOT, "app")),
  ...screens(join(ROOT, "components")),
].map(read);

const redwoodPages = allScreens.filter((page) => page.text.includes('design="redwood"'));

/**
 * SAYI KURALI EKRANIN DEĞİL, KATMANIN TAMAMININ KURALIDIR.
 *
 * Bu kurallar önce yalnızca ErpShell açan dosyalara uygulanıyordu ve
 * uygulamanın yarısını ıskaladı: tutarı ekrana yazan şey çoğu zaman
 * sayfanın kendisi değil, içine gömülü bir bileşen. `hakedis-editor`
 * tek bir biçimleyiciyi TUTAR, METRAJ, ORAN ve BİRİM FİYAT için birden
 * kullanıyordu; `offer-chain-panel` sözleşme bedelini ve hakediş
 * tutarını kuruşsuz basıyordu; `boq-import-match-table` birim fiyatı
 * iki haneye kırpıyordu. Hiçbiri ErpShell açmadığı için hiçbiri
 * taranmamıştı.
 *
 * Bu yüzden aşağıdaki sayı kuralları app/ ve components/ altındaki
 * BÜTÜN .tsx dosyalarına uygulanır.
 */
function everyFile(directory: string): string[] {
  const found: string[] = [];

  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);

    if (statSync(path).isDirectory()) {
      found.push(...everyFile(path));
      continue;
    }

    if (/\.tsx?$/.test(entry)) found.push(path);
  }

  return found;
}

const numberSurface = [
  ...everyFile(join(ROOT, "app")),
  ...everyFile(join(ROOT, "components")),
  ...everyFile(join(ROOT, "services")),
]
  .filter((path) => !path.endsWith(join("lib", "format", "turkish.ts")))
  .map(read);

describe("Redwood ekranları", () => {
  it("en az bir ekran geçirilmiş durumda", () => {
    // Bu olmadan aşağıdaki testler boş kümede koşup sessizce geçerdi.
    expect(redwoodPages.length).toBeGreaterThan(0);
  });

  /**
   * YAYMA BİTTİ: artık ARTIK HER ErpShell bayraklı olmak zorunda.
   *
   * Yayma sürerken bu kural yalnızca bayrağı yazan sayfalara
   * uygulanıyordu, yoksa geçiş tek hamlede yapılmak zorunda kalırdı.
   * 158 ekranın tamamı geçtiğine göre kural tersine çevrildi: bayraksız
   * bir ErpShell kalması artık geriye düşüş demek.
   *
   * Sayı karşılaştırması da önemli: sayfanın yükleme ve hata dalları
   * ayrı birer ErpShell açıyor. Biri bayraksız kalırsa kullanıcı sayfa
   * yüklenirken bir tasarımı, yüklendikten sonra ötekini görür.
   */
  it("bütün ErpShell çağrıları bayraklı", () => {
    const mismatched = allScreens
      .map((page) => ({
        path: page.path,
        shells: (page.text.match(/<ErpShell/g) ?? []).length,
        flags: (page.text.match(/design="redwood"/g) ?? []).length,
      }))
      .filter((page) => page.shells !== page.flags)
      .map((page) => `${page.path} (${page.flags}/${page.shells})`);

    expect(
      mismatched,
      "Bayraksız ErpShell kaldı. Ekran nerede açılıyorsa orada " +
        'design="redwood" yazmalı — sayfa dosyasında değilse kabuğu ' +
        "açan bileşende.",
    ).toEqual([]);
  });

  /**
   * Para biçimi tek yerden gelir. Sayfa kendi Intl.NumberFormat'ını
   * kurarsa ondalık sayısı ve simge konumu ekrandan ekrana değişir;
   * bugün düzeltilen "₺1.250,00" geri gelir.
   */
  it("kendi sayı biçimleyicisini kurmuyor", () => {
    const offenders = numberSurface
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
    const offenders = numberSurface
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
    const offenders = numberSurface
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

    const offenders = numberSurface
      .filter((page) => pattern.test(page.code))
      .map((page) => page.path);

    expect(offenders).toEqual([]);
  });

  /**
   * SEÇENEKSİZ `toLocaleString("tr-TR")` DE YASAK — SAYIDA.
   *
   * Yukarıdaki kural seçenek nesnesi arıyordu ve bu yüzden en sessiz
   * hâli kaçırdı: seçeneksiz çağrının varsayılanı EN ÇOK 3 hane ve
   * ALT SINIRI YOK. Yani 1250.5 ekranda "1.250,5", 1250 ise "1.250"
   * oluyordu — teklif maliyet kırılımı böyle basılıyordu, kuruş
   * hanesi olmadan. Hane sayısı yazılmadığı için de "çağrı yerinde
   * hane belirlenmiş" gibi görünmüyordu.
   *
   * TARİH SERBEST. `new Date(x).toLocaleString("tr-TR")` meşru bir
   * tarih-saat biçimi ve uygulamada on bir yerde kullanılıyor. Ayrımı
   * yapabilmek için önce `new Date( … )` bölgeleri parantez sayarak
   * maskeleniyor; geriye kalan alıcı bir sayıdır.
   */
  it("seçeneksiz toLocaleString ile sayı basmıyor", () => {
    // Alıcı parantezli de olabiliyor: `(Number(a) * Number(b)).toLocaleString(…)`
    // reçete editöründe tam olarak böyle yazılıydı. Sadece tanımlayıcı
    // arayan bir desen bu biçimi görmez.
    const call = /([\w.[\]?!()]*)\.toLocaleString\(\s*["']tr-TR["']\s*\)/g;

    const offenders = numberSurface
      .filter((page) => {
        const masked = maskDateExpressions(page.code);

        return [...masked.matchAll(call)].some(
          // Maskeleme sonrası alıcı yalnızca `D` ise o bir tarihtir.
          (match) => match[1].replace(/[()]/g, "") !== "D",
        );
      })
      .map((page) => page.path);

    expect(
      offenders,
      "Seçeneksiz toLocaleString bir sayıya uygulanmış. Varsayılanı " +
        "en çok 3 hane ve alt sınırı yok; para '1.250,5' diye çıkar. " +
        "Sayı tipine göre money / quantity / percent / decimal kullanın.",
    ).toEqual([]);
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
   * TAZELEME DÜĞMESİ ORTAK BİLEŞENDEN GELİR.
   *
   * "Yenile" dört ayrı biçimde yazılmıştı — ham `<button className=…>`
   * (20), `erp-secondary-button` sınıflı `<button>` (26), düz
   * `<button>` (5), `<button style=…>` (2) — ve yalnız 5'i ortak
   * `<Button>` bileşenini kullanıyordu. Aynı işi yapan düğme ekrandan
   * ekrana farklı görünüyordu.
   *
   * Bu, "tek Button varyantı" maddesinin gerçekte geçmediği yerdi;
   * ben de tam turda `variant="primary"` sayarak ölçtüğüm için
   * kaçırmıştım. Sayım o soruyu hiç sormuyordu.
   */
  /**
   * BİRİM FİYAT ALANI `money`DEN GEÇMEZ.
   *
   * `money` SABİT iki hane; birim fiyat kolonları veritabanında dört
   * ya da altı ondalık taşıyor. Aradaki fark ekranda görünmüyor —
   * "1.250,00 ₺" gayet doğru bir tutar gibi duruyor — ama kullanıcı
   * o rakamla miktarı çarptığında toplam tutmuyor.
   *
   * Yakalanma biçimi öğreticiydi: `unitPrice` max 4'ten 6'ya
   * çıkarılırken tarandığında, birim fiyat alanlarının hâlâ `money`ye
   * bağlı olduğu yerler bulundu — yani biçimleyiciyi düzeltmek tek
   * başına yetmiyordu, çağıranın doğru işlevi seçmesi gerekiyordu.
   *
   * KAPSAM DIŞI TUTULANLAR: ölçeği zaten iki olan kolonlar
   * (subcontractor_progress_payment_items.UnitPrice) — orada `money`
   * doğru işlev.
   */
  it("birim fiyat alanını money ile basmıyor", () => {
    const call = /\b(?:money|currencyMoney|moneyWhole|amount)\(\s*[\w.]*\b(\w*(?:[Uu]nitPrice|[Ll]istPrice))\b/g;

    /*
     * Ölçeği ZATEN İKİ olan kolon: taşeron hakedişi
     * (`subcontractor_progress_payment_items.UnitPrice` = numeric(_,2)).
     * Orada `money` DOĞRU işlev — ilke "kolonun ölçeğini karşıla"
     * diyor, "her yerde altı hane yaz" demiyor.
     */
    const scaleTwoScreens = [join("taseronlar", "[id]", "page.tsx")];

    const offenders: string[] = [];

    for (const page of numberSurface) {
      // Yerel `money` sarmalayıcısı `unitPrice`e gidiyorsa sorun yok;
      // birkaç ekran biçimleyiciyi böyle adlandırmış.
      if (/(?:function|const)\s+money[^\n]*\n?[^\n]*unitPrice\(/.test(page.code)) continue;

      if (scaleTwoScreens.some((screen) => page.path.includes(screen))) continue;

      for (const match of page.code.matchAll(call)) {
        offenders.push(`${page.path} (${match[1]})`);
      }
    }

    expect(
      offenders,
      "Birim fiyat `money` ile basılmış; money sabit 2 hane, kolon 4-6 " +
        "ondalık taşıyor. `unitPrice` kullanın.",
    ).toEqual([]);
  });

  /**
   * YÜZDE İŞARETİ İKİ KEZ YAZILMAZ.
   *
   * `percent()` işareti KENDİSİ koyuyor ("%5,5") — Türkçe yazımda
   * işaret başta. Çağrı yerinde bir de literal `%` varsa ekrana
   * "%%5,5" çıkıyor.
   *
   * Bu hatayı yayma sırasında ben yaptım: eski kod işareti JSX'te
   * yazıyordu ve biçimleyiciyi `percent`e bağlarken o literali
   * silmeyi üç yerde unuttum. Gerçek veri provasında görüldü —
   * derleyici için kusursuz bir metin, testler de sessizdi.
   *
   * YEREL `percent` İŞLEVLERİ AYRI: bazı ekranlarda aynı adda yerel
   * bir yardımcı var ve o ÇIPLAK sayı döndürüyor; oralarda literal
   * işaret doğru. Bu yüzden kural yalnız ortak biçimleyiciyi içe
   * aktaran dosyalara uygulanıyor.
   */
  it("yüzde işaretini iki kez yazmıyor", () => {
    const offenders = numberSurface
      .filter((page) =>
        /import\s*\{[^}]*\bpercent\b[^}]*\}\s*from\s*"@\/lib\/format\/turkish"/.test(page.code),
      )
      .filter((page) => /%\s*\$?\{\s*percent\(/.test(page.code))
      .map((page) => page.path);

    expect(
      offenders,
      "Yüzde işareti hem çağrı yerinde hem percent() içinde yazılmış; " +
        'ekranda "%%5,5" çıkar. Literal işareti kaldırın.',
    ).toEqual([]);
  });

  /**
   * HER LİSTE EKRANINDA TAZELEME VAR.
   *
   * Liste ekranı, kullanıcının veriye bakıp bir işlem yaptıktan sonra
   * geri döndüğü yer; tazeleme yoksa tek çare sayfayı yeniden
   * yüklemek oluyor ve o da filtreleri sıfırlıyor.
   *
   * FORM VE DETAY EKRANLARI KAPSAM DIŞI: `/yeni`, `/duzenle`,
   * `/[id]/…` ve içe aktarım sayfalarında tablo bulunsa bile o tablo
   * bir liste değil, formun parçası.
   *
   * ETİKET HER ZAMAN "Yenile" DEĞİL: piyasa ekranı sağlayıcıdan yeni
   * kur çekiyor ve düğmesi "Şimdi güncelle" diyor — yaptığı iş sayfayı
   * yeniden okumak değil, veriyi kaynağından tazelemek. Etiketi
   * "Yenile"ye çevirmek onu yanlış anlatırdı.
   */
  it("her liste ekranında tazeleme var", () => {
    const formLike = /[/\\](yeni|duzenle|aktar|ice-aktar|e-fatura-ice-aktar)[/\\]page\.tsx$|[/\\]\[id\][/\\]/;
    const refreshLabels = /Yenile|Şimdi güncelle/;

    const offenders = redwoodPages
      .filter((page) => /<DataTable|erp-table|<table/.test(page.code))
      .filter((page) => !formLike.test(page.path))
      .filter((page) => !refreshLabels.test(page.code))
      .map((page) => page.path);

    expect(
      offenders,
      "Liste ekranında tazeleme yok. " +
        '<Button variant="secondary" onClick={() => void load()}>Yenile</Button> ' +
        "ekleyin.",
    ).toEqual([]);
  });

  it("tazeleme düğmesi ortak bileşeni kullanıyor", () => {
    const offenders = redwoodPages
      .filter((page) => {
        for (const match of page.code.matchAll(/>\s*Yenile\s*</g)) {
          const before = page.code.slice(0, match.index);

          if (before.lastIndexOf("<button") > before.lastIndexOf("<Button")) {
            return true;
          }
        }

        return false;
      })
      .map((page) => page.path);

    expect(
      offenders,
      'Ham <button> ile tazeleme yazılmış. <Button variant="secondary"> ' +
        "kullanın: biçim tek yerden geliyor ve koyu temada zemini " +
        "tokena bağlı.",
    ).toEqual([]);
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
   *
   * KAPSAM ARTIK EKRANLA SINIRLI DEĞİL. Ekran dosyalarını taramak
   * koyu temada bir deliği açık bıraktı: renk çoğu zaman sayfada değil,
   * içine gömülü bileşende yazılı. İK panellerinde, gantt çizelgesinde
   * ve poz eşleme tablosunda 168 ham hex duruyordu — hepsi açık slate
   * tonları, yani koyu zeminde okunmaz. Koyu temayı "bitti" saymak bu
   * yüzden erkendi.
   */
  it("ham hex renk taşımıyor", () => {
    const offenders = numberSurface
      .filter((page) => !HEX_ALLOWED.some((allowed) => page.path.includes(allowed)))
      .filter((page) => /#[0-9a-fA-F]{3,8}\b/.test(page.code))
      .map((page) => page.path);

    expect(
      offenders,
      "Ham hex renk kaldı. Anlamsal renk için rw-value-* sınıflarını, " +
        "grafik serisi için --color-chart-* değişkenlerini kullanın.",
    ).toEqual([]);
  });
});
