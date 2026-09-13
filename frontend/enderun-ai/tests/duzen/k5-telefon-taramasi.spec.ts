import { expect, test, type Page } from "@playwright/test";

/**
 * K5 — EKRANA SIĞIYOR MU (CANLI/1).
 *
 * 14 ekran, DÖRT GENİŞLİK. Soru üç parçalı ve ÜÇÜ AYRI RAPORLANIR:
 *   1. Ekran AÇILIYOR MU        — hata sınırına düşmüyor mu
 *   2. YATAY TAŞMA VAR MI       — scrollWidth <= innerWidth
 *   3. İSTEMCİ HATASI VAR MI    — console error / sayfa hatası
 *
 * ═══ NEDEN DÖRT GENİŞLİK — ASIL DERS BURADA (2026-09-13) ═══
 *
 * Bu kapı 14 Eylül'e kadar YALNIZ 390'ı ölçüyordu ve yeşildi. Mehmet Bey
 * canlıdan elle ölçünce şu çıktı:
 *
 *     390 / 950 / 1000 / 1150  → yatay kayma 0
 *     1201 → 70px · 1280 → 70px · 1366 → 48px · 1440 → 24px · 1536 → 0
 *
 * Yani kusur, kapının BAKMADIĞI genişliklerde duruyordu ve tam olarak
 * bu yüzden kaçtı. TEK GENİŞLİK ÖLÇEN KAPI, ÖTEKİ GENİŞLİKLERİ KORUMAZ —
 * ve "yeşil" yazarak korunduğu izlenimi verir. Bir ölçüm yalnız ölçtüğü
 * yolu kanıtlar (Kural 82); bir genişlik de yalnız kendini.
 *
 * Seçilen dört genişlik körlüğü kapatmıyor, DARALTIYOR: 390 telefon,
 * 768 tablet, 1280 dizüstü (medya sorgusunun ÜSTÜ — kaçan kusurun
 * yaşadığı bölge), 1536 masaüstü. DÜRÜST SINIR: aradaki genişlikler
 * (ör. 1366, 1440) hâlâ ölçülmüyor; 1280 ve 1536 yeşilken ikisinin
 * arasında taşma OLABİLİR.
 *
 * ═══ TARAMA TEK NOKTADA İPTAL OLMAZ ═══
 *
 * Her ekran kendi try bloğunda. Ölçülemeyen ekran ATLANMAZ ve
 * GİZLENMEZ: "ÖLÇEMEDİ" diye yazılır, tarama ötekilerle devam eder.
 * (PN4'te tek noktanın bütün taramayı düşürmesi 8 tur boyunca elimde
 * sayı olmamasına yol açtı; ders orada öğrenildi.)
 *
 * ═══ "BOŞ" İLE "KIRIK" AYRI ═══
 *
 * Rig'in verisi ince. Bir ekranın boş görünmesi kusur DEĞİL, veri
 * yokluğu olabilir. O yüzden bu sonda "veri gösteriyor mu" diye
 * SORMUYOR — sorsaydı rig inceliğini kusur diye raporlardı (Kural 65).
 * Ölçtüğü şey EKRANA SIĞIYOR MU ve AÇILIYOR MU.
 *
 * ═══ GİRİŞ YARDIMCISI KOPYALANDI, HATIRLANMADI ═══
 *
 * `mesaj-paneli-acilir.spec.ts`'ten aynen alındı: rig girişi formdan
 * değil `/api/auth/login` isteğinden geçiyor ve değişkenler DUZEN_*.
 */

const KULLANICI = process.env.DUZEN_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;

const EKRANLAR = [
  "/login",
  "/parola",
  "/dashboard",
  "/mesajlar",
  "/yapilacaklar",
  "/gorevler",
  "/muhasebe/faturalar",
  "/muhasebe/satis-faturalari",
  "/muhasebe/fisler",
  "/finans/cekler",
  "/finans/odeme-planlari",
  "/finans/kasa-banka",
  "/cariler",
  "/raporlar",
];

type Olcek = { ad: string; genislik: number; yukseklik: number };

const OLCEKLER: Olcek[] = [
  { ad: "telefon", genislik: 390, yukseklik: 664 },
  { ad: "tablet", genislik: 768, yukseklik: 1024 },
  { ad: "dizüstü", genislik: 1280, yukseklik: 800 },
  { ad: "masaüstü", genislik: 1536, yukseklik: 864 },
];

/*
 * ═══ ÇİZGİ — YALNIZ AŞAĞI İNER ═══
 *
 * Her genişlik için "taşan ekran sayısı" üst sınırı. Değerler
 * 2026-09-13'te ÖLÇÜLEREK kondu, tahminle değil. Bir sayı düşerse
 * (düzeltme yapıldıysa) çizgi O GÜN aşağı çekilir; YUKARI ÇEKİLMEZ.
 * Yukarı çekmek, kapıyı kusurun peşinden sürüklemek olur.
 *
 * Sayılar burada AYRI AYRI duruyor, toplam olarak değil: toplam,
 * bir genişlikteki düzelmenin bir başkasındaki gerilemeyi örtmesine
 * izin verirdi.
 */
const CIZGI: Record<number, number> = {
  390: 0,
  768: 0,
  // BİLİNEN BORÇ — K5-ÜST/1: `/dashboard` 1280'de +72px taşıyor
  // (`.erp-quick-grid` sağ sütunda 4 sütun çiviliyor). Kusur
  // SINIFLANDIRILDI, düzeltmesi Mehmet Bey'in kararını bekliyor.
  // K5-ÜST/1 kapanınca BU SAYI 0'A İNECEK.
  1280: 1,
  1536: 0,
};

async function girisYap(sayfa: Page) {
  expect(KULLANICI, "DUZEN_KULLANICI yok — rig'i duzen-testi.sh ile koşturun").toBeTruthy();

  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: KULLANICI, password: PAROLA },
  });
  expect(yanit.status(), "Giriş: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

type Satir = {
  yol: string;
  olcemedi?: string;
  genislik?: number;
  tasma?: number;
  hataSayisi?: number;
  hataOrnegi?: string;
};

async function olcekiTara(sayfa: Page, olcek: Olcek): Promise<Satir[]> {
  await sayfa.setViewportSize({ width: olcek.genislik, height: olcek.yukseklik });

  const satirlar: Satir[] = [];

  for (const yol of EKRANLAR) {
    const hatalar: string[] = [];
    const dinle = (m: { type: () => string; text: () => string }) => {
      if (m.type() === "error") hatalar.push(m.text().slice(0, 120));
    };
    sayfa.on("console", dinle);
    const sayfaHatasi = (e: Error) => hatalar.push("pageerror: " + e.message.slice(0, 120));
    sayfa.on("pageerror", sayfaHatasi);

    try {
      await sayfa.goto(yol, { waitUntil: "domcontentloaded", timeout: 60_000 });
      // Yerleşim otursun; ağ boşta kalmayabilir (yoklama var).
      await sayfa.waitForTimeout(2_500);

      const olcum = await sayfa.evaluate(() => ({
        scrollWidth: document.documentElement.scrollWidth,
        innerWidth: window.innerWidth,
      }));

      satirlar.push({
        yol,
        genislik: olcum.scrollWidth,
        tasma: Math.max(0, olcum.scrollWidth - olcum.innerWidth),
        hataSayisi: hatalar.length,
        hataOrnegi: hatalar[0],
      });
    } catch (hata) {
      satirlar.push({
        yol,
        olcemedi: hata instanceof Error ? hata.message.slice(0, 110) : String(hata),
      });
    } finally {
      sayfa.off("console", dinle);
      sayfa.off("pageerror", sayfaHatasi);
    }
  }

  return satirlar;
}

test("K5: 14 ekran dört genişlikte açılıyor ve yatay taşmıyor", async ({ page }) => {
  test.setTimeout(1_800_000);

  await girisYap(page);

  const sonuc = new Map<number, Satir[]>();

  for (const olcek of OLCEKLER) {
    try {
      sonuc.set(olcek.genislik, await olcekiTara(page, olcek));
    } catch (hata) {
      // Bir ÖLÇEK tamamen düşerse ötekiler yine ölçülür; eksiklik
      // aşağıdaki tarama sağlığı sayacında KIRMIZI olarak görünür.
      console.log(
        `\n!! ${olcek.genislik}px ÖLÇEĞİ DÜŞTÜ: ` +
          (hata instanceof Error ? hata.message.slice(0, 160) : String(hata))
      );
    }
  }

  const ozet: { genislik: number; tasan: number; cizgi: number; kirmizi: boolean }[] = [];

  for (const olcek of OLCEKLER) {
    const satirlar = sonuc.get(olcek.genislik);
    console.log(`\n=== K5 TARAMASI — ${olcek.genislik}x${olcek.yukseklik} (${olcek.ad}) ===`);

    if (!satirlar) {
      console.log("ÖLÇEK HİÇ ÖLÇÜLEMEDİ");
      continue;
    }

    console.log("ekran".padEnd(30) + "scrollW".padStart(9) + "taşma".padStart(7) + "  hata");
    for (const s of satirlar) {
      if (s.olcemedi) {
        console.log(s.yol.padEnd(30) + "   ÖLÇEMEDİ — " + s.olcemedi);
        continue;
      }
      console.log(
        s.yol.padEnd(30) +
          String(s.genislik).padStart(9) +
          String(s.tasma).padStart(7) +
          "  " +
          (s.hataSayisi ? `${s.hataSayisi} · ${s.hataOrnegi}` : "-")
      );
    }

    const olculen = satirlar.filter((s) => !s.olcemedi);
    const olcemeyen = satirlar.filter((s) => s.olcemedi).map((s) => s.yol);
    const tasan = olculen.filter((s) => (s.tasma ?? 0) > 0);
    const hatali = olculen.filter((s) => (s.hataSayisi ?? 0) > 0).map((s) => s.yol);

    console.log(
      `ÖLÇÜLEN ${olculen.length}/${satirlar.length}` +
        (olcemeyen.length ? `  ·  ÖLÇEMEDİ: ${olcemeyen.join(", ")}` : "")
    );
    console.log(
      `YATAY TAŞAN : ${tasan.length}  ` +
        tasan.map((s) => `${s.yol}(+${s.tasma}px)`).join(", ")
    );
    console.log(`İSTEMCİ HATASI: ${hatali.length}  ${hatali.join(", ")}`);

    const cizgi = CIZGI[olcek.genislik] ?? 0;
    ozet.push({
      genislik: olcek.genislik,
      tasan: tasan.length,
      cizgi,
      kirmizi: olculen.length < EKRANLAR.length || tasan.length > cizgi,
    });
  }

  console.log("\n=== K5 ÖZET (genişlik başına AYRI) ===");
  console.log("genişlik".padEnd(12) + "taşan".padStart(7) + "çizgi".padStart(7) + "  durum");
  for (const s of ozet) {
    console.log(
      `${s.genislik}px`.padEnd(12) +
        String(s.tasan).padStart(7) +
        String(s.cizgi).padStart(7) +
        (s.kirmizi ? "  KIRMIZI" : "  yeşil")
    );
  }
  console.log(`ÖLÇÜLEN GENİŞLİK: ${ozet.length}/${OLCEKLER.length}`);

  /*
   * ═══ ÖNCE ÖLÇÜMÜN YAŞADIĞINI KANITLA (Kural 48) ═══
   *
   * "Taşma 0" tek başına, HİÇ EKRAN ÖLÇÜLMEDİĞİNDE de doğrudur.
   * Liste boşalırsa, bir ölçek düşerse ya da tarama erken biterse
   * taşan sayısı yine 0 çıkar ve sonda yeşil yanar — ölçüm ölmüş,
   * dünya değişmemiş olur.
   *
   * BU İDDİA "BAK-VE-KARAR" SINIFINDANDIR: bozulması kusur göstermez.
   * Bir genişlik bilerek çıkarılmış OLABİLİR (o zaman liste güncellenir)
   * ya da tarama erken bitmiştir (o zaman sebep aranır). İHLAL'den
   * ayrı okunmalı.
   */
  expect(
    ozet.length,
    `ÖLÇÜM ÖLDÜ: yalnız ${ozet.length} genişlik ölçüldü (${OLCEKLER.length} olmalı). ` +
      "Taşma bulgusu bu hâlde hiçbir şey söylemez."
  ).toBe(OLCEKLER.length);

  for (const s of ozet) {
    expect(
      s.tasan,
      `${s.genislik}px: ${s.tasan} ekran yatay taşıyor, çizgi ${s.cizgi}. ` +
        "Çizgi YALNIZ AŞAĞI iner — yukarı çekmek kapıyı kusurun peşinden sürüklemek olur."
    ).toBeLessThanOrEqual(s.cizgi);
  }

  const eksikOlcek = ozet.filter((s) => s.kirmizi && s.tasan <= s.cizgi);
  expect(
    eksikOlcek.map((s) => s.genislik),
    "Bu genişlik(ler)de 14 ekranın hepsi ölçülemedi — liste EKSİK"
  ).toEqual([]);
});
