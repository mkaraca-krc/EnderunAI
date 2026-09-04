import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * TEST SAYISI CIRCIRI — KURAL 55'İN MEKANİK KARŞILIĞI.
 *
 * NEDEN VAR: ÖP/1b sırasında `cat >` ile var olan bir test dosyasının
 * üstüne yazıldı ve ÖP/1a'nın altı test niteliği silindi — paketin
 * asıl güvenlik bekçileri. Tam takım 2865/2865 YEŞİL verdi: sayı
 * doğruydu, kapsam eksikti. Silinen bekçiler koşmadıkları için
 * kırmızı da veremezler; hata yeşilin arkasına saklanır.
 *
 * O gün yakalatan şey `git status` çıktısındaki `M` harfiydi. Bu ŞANS,
 * DÜZENEK DEĞİL. Kural akılda değil, çizgide durmalı.
 *
 * ─────────────────────────────────────────────────────────────
 * BU CIRCIR ÇÖZÜLMÜŞ TEST SAYISINI DEĞİL, BİLDİRİM SAYISINI SAYAR.
 * ─────────────────────────────────────────────────────────────
 *
 * Çözülmüş sayı yalnız takım koşarken bilinir ve bir test dosyasından
 * okunamaz. Bildirim sayımı statik: `[Fact]`, `[InlineData]`, `it(`.
 * Bu, aranan şeyi tam karşılıyor — bir durum silinirse sayı düşer.
 *
 * İKİ EKSEN, ÇÜNKÜ BİR TEORİ TEK SATIRDIR AMA YÜZ DURUM ÜRETİR.
 * `[MemberData]` taşıyan sekiz dosya çalışma anında 148 durum
 * üretiyor (2726 statik + 148 = 2874 gerçek). Tek sayıda toplansaydı
 * o teorinin silinmesi "1 düşüş" gibi görünür, gürültüde kaybolurdu.
 * Dinamik kümeler ayrı sayılıyor.
 *
 * ─────────────────────────────────────────────────────────────
 * BU CIRCIRIN YAKALAYAMADIĞI ŞEY — ÖLÇÜLDÜ, GİZLENMİYOR.
 * ─────────────────────────────────────────────────────────────
 *
 * ÇİZGİNİN SESSİZCE DÜŞÜRÜLMESİNİ YAKALAYAMAZ. Biri testleri silip
 * çizgiyi 2726'dan 2600'e çekerse pozitif kontrol (>2000) yine geçer
 * ve cırcır susar. Bu ölçüldü, sonda olarak koşuldu ve doğrulandı:
 * düşürülmüş bir çizgi ile meşru birleştirme sonrası çizgi mekanik
 * olarak AYIRT EDİLEMEZ.
 *
 * Koruma bu yüzden mekanik değil, USULE AİT:
 *
 *   ÇİZGİ YUKARI SERBESTÇE GÜNCELLENİR.
 *   AŞAĞI HAREKET AYRI BİR COMMIT'TE, GEREKÇESİ COMMIT MESAJINDA
 *   YAZILI OLARAK YAPILIR.
 *
 * Korumanın değeri sayının kendisi değil, DÜŞÜŞÜN BİR KONUŞMAYA
 * DÖNÜŞMESİDİR.
 *
 * ─────────────────────────────────────────────────────────────
 * BİRLEŞTİRME MEŞRUDUR — BU CIRCIR İYİLEŞTİRMEYİ ENGELLEMEZ.
 * ─────────────────────────────────────────────────────────────
 *
 * KURULUM/1'de 23 ayrı test yerine tek parametreli test yazılması
 * İSTENDİ ve doğru olan buydu. Bu cırcır altında o iyi değişiklik
 * sayıyı 23'ten 1'e düşürür ve kırmızı verir.
 *
 * Çizginin düşürülmesi UCUZ, NORMAL VE KAYITLI bir işlemdir —
 * savunulması gereken bir şey değildir. Bu cırcır testin
 * SİLİNMESİNİ fark ettirmek için vardır; testin İYİLEŞTİRİLMESİNİ
 * engellemek için değil.
 *
 * (Kural 42: insanı doğru işi yapmaktan caydıran kural ya susturulur
 * ya da kötü kodu teşvik eder.)
 *
 * ─────────────────────────────────────────────────────────────
 * NEDEN ÖN YÜZ TAKIMINDA
 * ─────────────────────────────────────────────────────────────
 *
 * İki sayaç yazılsaydı (biri backend'de, biri burada) zamanla
 * ayrışırlardı: biri `[Theory]` sayımını düzeltir, diğeri kalırdı.
 * Tek sayaç, iki taraf. `tests/endpoint-guard.test.ts` zaten backend
 * kaynağını okuyor; desen yeni değil.
 *
 * BUNUN BEDELİ: arka uçtan bir test silen kişi kırmızıyı BAŞKA BİR
 * TAKIMDAN alır. Hata mesajı bu yüzden nerede olduğunu, hangi tarafın
 * düştüğünü ve ne yapılacağını açıkça yazıyor — yoksa yarım saat
 * yanlış yerde aranır.
 */

const ONYUZ_KOK = join(__dirname, "..");
const DEPO_KOK = join(ONYUZ_KOK, "..", "..");
const BACKEND_TEST = join(DEPO_KOK, "backend", "EnderunAI.Api.Tests");

type Sayim = { statik: number; dinamik: number };

function dosyalar(dizin: string, uzantilar: string[]): string[] {
  const bulunan: string[] = [];

  let girdiler: string[];
  try {
    girdiler = readdirSync(dizin);
  } catch {
    return bulunan;
  }

  for (const girdi of girdiler) {
    if (girdi === "node_modules" || girdi === "bin" || girdi === "obj") continue;

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...dosyalar(yol, uzantilar));
      continue;
    }

    if (uzantilar.some((u) => yol.endsWith(u))) bulunan.push(yol);
  }

  return bulunan;
}

/**
 * ARKA UÇ SAYIMI.
 *
 * `[Fact]` bir durum. `[Theory]` kendi başına sıfır durumdur —
 * altındaki `[InlineData]` satırları sayılır. `[MemberData]` /
 * `[ClassData]` çalışma anında çözülür ve statik sayılamaz; DİNAMİK
 * eksende bildirim başına 1 sayılır.
 */
function arkaUcSayimi(): {
  sayim: Sayim;
  dosyaSayisi: number;
  metot: number;
} {
  let statik = 0;
  let dinamik = 0;
  let dosyaSayisi = 0;

  /*
   * METOT EKSENİ — KOŞUCUYLA KARŞILAŞTIRMA İÇİN.
   *
   * `statik` BİLDİRİM sayıyor (bir teori kaç satır taşırsa o kadar).
   * Koşucu ise DURUM keşfediyor. İkisi farklı eksen ve
   * karşılaştırılamaz.
   *
   * Karşılaştırılabilir tek eksen METOT: bir `[Theory]` kaç durum
   * üretirse üretsin TEK metottur. Koşucunun bulduğu farklı metot
   * adıyla birebir tutmalı.
   */
  let metot = 0;

  for (const yol of dosyalar(BACKEND_TEST, [".cs"])) {
    const satirlar = readFileSync(yol, "utf8").split("\n");
    let bu = 0;

    for (const satir of satirlar) {
      const s = satir.trim();

      /*
       * `[SkippableFact]` DA SAYILIYOR — 2026-09-03'te BULUNDU.
       *
       * Sayaç yalnız `[Fact` ile başlayan satırları sayıyordu;
       * `[SkippableFact]` o desene UYMUYOR. Depoda 4 tane vardı
       * (3'ü `BookImportProfileTests`, biri sır bekçisi) ve DÖRDÜ DE
       * GÖRÜNMÜYORDU: silinseler çıra ötmezdi.
       *
       * Tam da bu cırcırın var oluş sebebi olan hata, cırcırın kendi
       * kör noktasındaydı. Bulunuşu tesadüfe yakın: sır bekçisi
       * paketinde 11 test eklendi ama gevşeklik 10 çıktı; aradaki 1
       * kovalanınca ortaya çıktı.
       *
       * DERS: bir sayaç, saymadığı şeyi de bildirmelidir. Burada
       * bildiremezdi — o yüzden desen genişletildi.
       */
      // METOT SAYIMI: her test özniteliği bir metoda karşılık gelir.
      if (
        s.startsWith("[Fact") ||
        s.startsWith("[SkippableFact") ||
        s.startsWith("[Theory") ||
        s.startsWith("[SkippableTheory")
      ) {
        metot += 1;
      }

      if (s.startsWith("[Fact")) bu += 1;
      else if (s.startsWith("[SkippableFact")) bu += 1;
      else if (s.startsWith("[SkippableTheory")) {
        // `[SkippableTheory]` kendi başına sıfır durumdur; altındaki
        // `[InlineData]` satırları zaten aşağıda sayılıyor. Burada
        // sayılmaması BİLİNÇLİ — `[Theory]` ile aynı davranış.
      }
      else if (s.startsWith("[InlineData")) bu += 1;
      else if (s.startsWith("[MemberData") || s.startsWith("[ClassData")) {
        dinamik += 1;
      }
    }

    if (bu > 0) {
      statik += bu;
      dosyaSayisi += 1;
    }
  }

  return { sayim: { statik, dinamik }, dosyaSayisi, metot };
}

/**
 * KOŞUCUNUN BİLDİRDİĞİ FARKLI TEST METODU SAYISI.
 *
 * `deploy/scripts/kosucu-test-sayimi.sh` üretiyor ve depo DIŞINA
 * yazıyor (yayın çalışma ağacını kirletmesin diye).
 *
 * Dosya yoksa `null` dönüyor — ve karşılaştırma SESSİZCE ATLANMIYOR,
 * çıranın kendi çıktısında görünür biçimde yazılıyor.
 */
function kosucuSayisi(): { metot: number; commit: string } | null {
  const yol = "/var/lib/enderun-ai/kosucu-test-sayisi.txt";

  try {
    const metin = readFileSync(yol, "utf8");
    const oku = (anahtar: string) =>
      metin
        .split("\n")
        .map((x) => x.trim())
        .find((x) => x.startsWith(`${anahtar}=`))
        ?.slice(anahtar.length + 1) ?? "";

    const metot = Number(oku("METOT"));
    if (!Number.isFinite(metot) || metot <= 0) return null;

    return { metot, commit: oku("COMMIT") };
  } catch {
    return null;
  }
}

/**
 * ÖN YÜZ SAYIMI.
 *
 * `it(` / `test(` bir durum. `it.each(` bir DİNAMİK küme: tek satır,
 * dizisi kadar durum üretir.
 */
function onYuzSayimi(): { sayim: Sayim; dosyaSayisi: number } {
  let statik = 0;
  let dinamik = 0;
  let dosyaSayisi = 0;

  for (const yol of dosyalar(join(ONYUZ_KOK, "tests"), [".ts", ".tsx"])) {
    const satirlar = readFileSync(yol, "utf8").split("\n");
    let bu = 0;

    for (const satir of satirlar) {
      const s = satir.trim();

      if (/^(it|test)\.each\s*\(/.test(s)) dinamik += 1;
      else if (/^(it|test)\s*\(/.test(s)) bu += 1;
    }

    if (bu > 0) {
      statik += bu;
      dosyaSayisi += 1;
    }
  }

  return { sayim: { statik, dinamik }, dosyaSayisi };
}

function cizgi(dosya: string): Sayim {
  const metin = readFileSync(join(ONYUZ_KOK, "tests", "bekci", dosya), "utf8");
  const oku = (anahtar: string) => {
    const satir = metin
      .split("\n")
      .map((s) => s.trim())
      .find((s) => s.startsWith(`${anahtar}:`));

    if (!satir) throw new Error(`${dosya} içinde "${anahtar}" satırı yok.`);

    return Number(satir.slice(anahtar.length + 1).trim());
  };

  return { statik: oku("statik"), dinamik: oku("dinamik") };
}

/**
 * HATA MESAJI — NEREDE OLDUĞUNU AÇIKLAR.
 *
 * Cırcır ön yüz takımında yaşıyor; arka uçtan test silen biri
 * kırmızıyı BAŞKA BİR TAKIMDAN alacak. Mesaj bunu söylemezse yarım
 * saat yanlış yerde aranır.
 */
function mesaj(taraf: string, cizgiDosyasi: string, olculen: Sayim, temel: Sayim): string {
  const dusenler: string[] = [];

  if (olculen.statik < temel.statik)
    dusenler.push(`STATİK eksen: ${temel.statik} → ${olculen.statik} (${temel.statik - olculen.statik} düştü)`);

  if (olculen.dinamik < temel.dinamik)
    dusenler.push(`DİNAMİK eksen: ${temel.dinamik} → ${olculen.dinamik} (${temel.dinamik - olculen.dinamik} düştü)`);

  return [
    ``,
    `TEST SAYISI DÜŞTÜ — ${taraf.toUpperCase()} TARAFINDA.`,
    ``,
    ...dusenler.map((d) => `  ${d}`),
    ``,
    `NEREDE ARANIR: düşen testler ${taraf} tarafında, ama bu cırcır ÖN`,
    `YÜZ takımında yaşıyor. Sebep: tek sayaç, iki taraf. İki ayrı`,
    `sayaç yazılsaydı zamanla ayrışırlardı — biri sayım kuralını`,
    `düzeltir, diğeri eski hâlinde kalırdı.`,
    ``,
    `MUHTEMEL SEBEP: bir test dosyası üstüne yazıldı ya da silindi.`,
    `Önce \`git status\` ve \`git diff --stat\` bakın; yeni sandığınız`,
    `bir dosya "M" görünüyorsa o dosya yeni değildir (Kural 55).`,
    ``,
    `DÜŞÜŞ MEŞRUYSA — ve birleştirme MEŞRUDUR:`,
    `23 ayrı testi tek parametreli teste çevirmek İYİ bir`,
    `değişikliktir ve bu sayıyı düşürür. Bu cırcır testin`,
    `SİLİNMESİNİ fark ettirmek için var; İYİLEŞTİRİLMESİNİ`,
    `engellemek için değil.`,
    ``,
    `NE YAPILIR: \`tests/bekci/${cizgiDosyasi}\` içindeki sayıyı`,
    `güncelleyin. Çizgi YUKARI serbestçe güncellenir; AŞAĞI hareket`,
    `AYRI BİR COMMIT'te, gerekçesi commit mesajında yazılı olarak`,
    `yapılır. Düşürmek ucuz, normal ve kayıtlı bir işlemdir —`,
    `savunulması gereken bir şey değil. Değerli olan, düşüşün bir`,
    `KONUŞMAYA dönüşmesidir.`,
    ``,
  ].join("\n");
}

/**
 * GEVŞEKLİK HER KOŞUDA BASILIR — KURAL 55/D.
 *
 * ÇİZGİ BİR TABANDIR: gerçek sayı çizginin ÜSTÜNDEYSE cırcır susar.
 * Aradaki fark GEVŞEKLİKTİR ve cırcır, gevşeklik tükenene kadar
 * SESSİZDİR — o kadar test silinebilir, hiçbir şey ötmez.
 *
 * ÖLÇÜLDÜ (2026-09-02, İŞEMRİ/2 Faz 1): çizgi 2798'de dururken gerçek
 * sayı 2824'tü. 26 testlik gevşeklik BEŞ COMMIT boyunca birikmişti,
 * çünkü çizgi YUKARI serbest ve aradaki paketler onu güncellemek
 * zorunda değildi. Yani cırcır o gün bir cırcır değil, bir süstü.
 *
 * NEDEN HATIRLAMAYA BIRAKILMIYOR: gevşekliği o gün ancak çıranın
 * hareketini KALEMLEMEYE çalışırken fark ettim. Kuralı hatırlamaya
 * bırakırsan unutulur; sayıyı ekrana basarsan unutulamaz. Gevşeklik
 * sıfır değilse her koşuda görünür ve biri sorar.
 *
 * NEDEN KIRMIZI DEĞİL, BASKI: gevşekliği kırmızıya çevirmek çizgiyi
 * bir TAVANA dönüştürürdü ve "yukarı serbest" kuralını iptal ederdi.
 * Aranan şey engel değil GÖRÜNÜRLÜK — düşüşün bir konuşmaya dönüşmesi
 * gibi, gevşekliğin de bir konuşmaya dönüşmesi.
 */
function gevseklikSatiri(taraf: string, olculen: Sayim, temel: Sayim): string {
  const statikBosluk = olculen.statik - temel.statik;
  const dinamikBosluk = olculen.dinamik - temel.dinamik;

  return (
    `çıra · ${taraf}: ` +
    `çizgi ${temel.statik} · gerçek ${olculen.statik} · gevşeklik ${statikBosluk}` +
    `   ‖ dinamik: ` +
    `çizgi ${temel.dinamik} · gerçek ${olculen.dinamik} · gevşeklik ${dinamikBosluk}`
  );
}

describe("test sayısı cırcırı", () => {
  const arkaUc = arkaUcSayimi();
  const onYuz = onYuzSayimi();

  /*
   * BASKI `describe` GÖVDESİNDE, BİR TESTİN İÇİNDE DEĞİL.
   *
   * Test içinde olsaydı, o test atlandığında ya da ondan önce başka
   * bir test patladığında gevşeklik BASILMAZDI — tam da bir şeylerin
   * ters gittiği koşuda. Gövde her toplamada çalışır.
   */
  console.log(gevseklikSatiri("arka uç", arkaUc.sayim, cizgi("test-sayisi-backend.txt")));
  console.log(gevseklikSatiri("ön yüz ", onYuz.sayim, cizgi("test-sayisi-onyuz.txt")));

  /*
   * İKİ BAĞIMSIZ SAYIM HER KOŞUDA BASILIYOR.
   *
   * Karşılaştırmanın YAPILMADIĞI durum da görünür olmalı: dosya
   * yoksa bunu yazıyoruz. Sessizce atlanmış bir karşılaştırma,
   * yapılmış bir karşılaştırma gibi görünürdü.
   */
  const kosucu = kosucuSayisi();

  console.log(
    kosucu
      ? `çıra · metot: kaynak ${arkaUc.metot} · koşucu ${kosucu.metot} · ` +
        `fark ${arkaUc.metot - kosucu.metot}`
      : "çıra · metot: KOŞUCU SAYIMI YOK — karşılaştırma YAPILMADI " +
        "(deploy/scripts/kosucu-test-sayimi.sh ile üretilir)",
  );

  /**
   * TARAMA BOŞA DÜŞMÜYOR.
   *
   * Sayaç bozulup 0 dönerse "sayı düşmedi" testi zaten kırmızı verir
   * — bu cırcır kendi bozulmasına karşı doğru tarafa düşüyor. Ama
   * çizgi bir gün elle sıfırlanırsa sessizleşir; pozitif kontrol o
   * durumu tutuyor.
   *
   * DİKKAT — BU KONTROLÜN SINIRI ÖLÇÜLDÜ: çizginin 2726'dan 2600'e
   * çekilmesini YAKALAMAZ, çünkü 2600 de bu eşiğin üstünde. Eşiği
   * gerçek sayıya yaklaştırmak da çözüm değil: o zaman her meşru
   * birleştirme eşiği de güncellemeyi gerektirirdi ve eşik ikinci bir
   * çizgiye dönüşürdü. Sessiz düşüşün karşılığı usuldedir (yukarıya
   * bakınız), mekanizmada değil.
   */
  it("tarama boşa düşmüyor", () => {
    expect(arkaUc.dosyaSayisi).toBeGreaterThan(200);
    expect(arkaUc.sayim.statik).toBeGreaterThan(2000);
    expect(onYuz.dosyaSayisi).toBeGreaterThan(30);
    expect(onYuz.sayim.statik).toBeGreaterThan(250);
  });

  it("çıranın saydığı metot, koşucunun bulduğuyla TUTUYOR", () => {
    /*
     * ═══ BİR SAYAÇ YALNIZ TANIDIĞINI SAYAR ═══
     *
     * Çıra `[Fact]` ve `[InlineData]` satırlarını sayıyordu.
     * `[SkippableFact]` o desene UYMUYORDU: depoda 4 tane vardı ve
     * DÖRDÜ DE GÖRÜNMÜYORDU — silinseler çıra ötmezdi. Cırcırın var
     * oluş sebebi olan hata, cırcırın KENDİ KÖR NOKTASINDAYDI.
     *
     * `[SkippableFact]`i desene eklemek yetmez: yarın başka bir
     * öznitelik gelir ve aynı sessizlik tekrarlanır.
     *
     * ÇÖZÜM İKİ BAĞIMSIZ SAYIM: biri kaynaktan öznitelik sayıyor,
     * diğeri koşucudan keşif alıyor. Uyuşmazlık, çıranın bir şeyi
     * TANIMADIĞINI söyler — ne olduğunu bilmesine gerek yok.
     *
     * NEDEN METOT EKSENİ: koşucu 3059 DURUM buluyor (teori satırları
     * ayrı), çıra 2914 BİLDİRİM sayıyor. Farklı eksenler.
     * Karşılaştırılabilir tek eksen METOT: bir teori kaç durum
     * üretirse üretsin tek metottur. Ölçüldü: kaynakta 2533, koşucuda
     * 2533.
     *
     * DOSYA YOKSA ATLANIYOR — ama SESSİZCE DEĞİL: yukarıdaki baskı
     * "KARŞILAŞTIRMA YAPILMADI" yazıyor. CI'da bu dosya yok; güvence
     * sunucudaki koşuda geçerli.
     */
    const kosucu = kosucuSayisi();

    if (!kosucu) {
      expect(arkaUc.metot).toBeGreaterThan(100);
      return;
    }

    expect(
      arkaUc.metot,
      [
        "",
        "ÇIRANIN SAYDIĞI METOT, KOŞUCUNUN BULDUĞUYLA TUTMUYOR.",
        "",
        `  kaynak (çıra)   : ${arkaUc.metot}`,
        `  koşucu (keşif)  : ${kosucu.metot}`,
        "",
        "NE DEMEK: çıra, koşucunun gördüğü bir testi TANIMIYOR",
        "olabilir — ya da tersi. En olası sebep, tanınmayan yeni bir",
        "test özniteliği (`[SkippableFact]` bir kez tam olarak bunu",
        "yaptı ve 4 test görünmez kaldı).",
        "",
        "NE YAPILIR: `arkaUcSayimi` içindeki öznitelik listesini",
        "güncelleyin. Sayının kendisini değil, TANIMAYI düzeltin —",
        "çizgiyi kaydırmak sorunu gizler.",
        "",
        "KOŞUCU SAYIMI ESKİ OLABİLİR: dosyadaki commit",
        `  ${kosucu.commit.slice(0, 8)}`,
        "Bu koşudan farklıysa önce `deploy/scripts/kosucu-test-sayimi.sh`",
        "ile tazeleyin.",
        "",
      ].join("\n"),
    ).toBe(kosucu.metot);
  });

  it("arka uç test sayısı düşmüyor", () => {
    const temel = cizgi("test-sayisi-backend.txt");

    expect(
      arkaUc.sayim.statik >= temel.statik && arkaUc.sayim.dinamik >= temel.dinamik,
      mesaj("arka uç", "test-sayisi-backend.txt", arkaUc.sayim, temel),
    ).toBe(true);
  });

  it("ön yüz test sayısı düşmüyor", () => {
    const temel = cizgi("test-sayisi-onyuz.txt");

    expect(
      onYuz.sayim.statik >= temel.statik && onYuz.sayim.dinamik >= temel.dinamik,
      mesaj("ön yüz", "test-sayisi-onyuz.txt", onYuz.sayim, temel),
    ).toBe(true);
  });
});
