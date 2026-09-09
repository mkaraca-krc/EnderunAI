import { expect, test, type Page } from "@playwright/test";

/**
 * K5 — TELEFONDAN KULLANILABİLİYOR MU (CANLI/1).
 *
 * 14 ekran, 390x664. Soru üç parçalı ve ÜÇÜ AYRI RAPORLANIR:
 *   1. Ekran AÇILIYOR MU        — hata sınırına düşmüyor mu
 *   2. YATAY TAŞMA VAR MI       — scrollWidth <= innerWidth
 *   3. İSTEMCİ HATASI VAR MI    — console error / sayfa hatası
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
 * Ölçtüğü şey TELEFONA SIĞIYOR MU ve AÇILIYOR MU.
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

async function girisYap(sayfa: Page) {
  expect(KULLANICI, "DUZEN_KULLANICI yok — rig'i duzen-testi.sh ile koşturun").toBeTruthy();

  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: KULLANICI, password: PAROLA },
  });
  expect(yanit.status(), "Giriş: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

test("K5: 14 ekran 390x664'te açılıyor ve yatay taşmıyor", async ({ page }) => {
  test.setTimeout(900_000);

  await girisYap(page);
  await page.setViewportSize({ width: 390, height: 664 });

  type Satir = {
    yol: string;
    olcemedi?: string;
    genislik?: number;
    tasma?: number;
    hataSayisi?: number;
    hataOrnegi?: string;
  };
  const satirlar: Satir[] = [];

  for (const yol of EKRANLAR) {
    const hatalar: string[] = [];
    const dinle = (m: { type: () => string; text: () => string }) => {
      if (m.type() === "error") hatalar.push(m.text().slice(0, 120));
    };
    page.on("console", dinle);
    const sayfaHatasi = (e: Error) => hatalar.push("pageerror: " + e.message.slice(0, 120));
    page.on("pageerror", sayfaHatasi);

    try {
      await page.goto(yol, { waitUntil: "domcontentloaded", timeout: 60_000 });
      // Yerleşim otursun; ağ boşta kalmayabilir (yoklama var).
      await page.waitForTimeout(2_500);

      const olcum = await page.evaluate(() => ({
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
      page.off("console", dinle);
      page.off("pageerror", sayfaHatasi);
    }
  }

  console.log("\n=== K5 TARAMASI (390x664) ===");
  console.log(
    "ekran".padEnd(30) + "scrollW".padStart(9) + "taşma".padStart(7) + "  hata"
  );
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
  const tasan = olculen.filter((s) => (s.tasma ?? 0) > 0).map((s) => s.yol);
  const hatali = olculen.filter((s) => (s.hataSayisi ?? 0) > 0).map((s) => s.yol);

  console.log(
    `\nÖLÇÜLEN ${olculen.length}/${satirlar.length}` +
      (olcemeyen.length ? `  ·  ÖLÇEMEDİ: ${olcemeyen.join(", ")}` : "")
  );
  console.log(`YATAY TAŞAN : ${tasan.length}  ${tasan.join(", ")}`);
  console.log(`İSTEMCİ HATASI: ${hatali.length}  ${hatali.join(", ")}`);

  /*
   * TEK İDDİA: ÖLÇÜM YAPILDI MI.
   *
   * Taşma ve istemci hatası bu turda RAPORLANIYOR, kapıya
   * BAĞLANMIYOR — karar Mehmet Bey'in ("önce listeyi getir").
   * Ama sondanın kendisi ölçebilmiş olmalı: ölçülemeyen ekran kalırsa
   * liste eksiktir ve eksik liste, "temiz" diye okunabilir.
   */
  /*
   * ═══ ÖNCE ÖLÇÜMÜN YAŞADIĞINI KANITLA (Kural 48) ═══
   *
   * "Taşma 0" tek başına, HİÇ EKRAN ÖLÇÜLMEDİĞİNDE de doğrudur.
   * Liste boşalırsa ya da tarama erken biterse `tasan` yine boş çıkar
   * ve sonda yeşil yanar — ölçüm ölmüş, dünya değişmemiş olur.
   *
   * Bu yüzden önce ÖLÇÜLEN EKRAN SAYISI sınanıyor.
   *
   * BU İDDİA "BAK-VE-KARAR" SINIFINDANDIR: bozulması kusur göstermez.
   * Bir ekran bilerek kaldırılmış OLABİLİR (o zaman liste güncellenir)
   * ya da tarama erken bitmiştir (o zaman sebep aranır). İHLAL'den
   * ayrı okunmalı.
   */
  expect(
    olculen.length,
    `ÖLÇÜM ÖLDÜ: yalnız ${olculen.length} ekran ölçüldü (14 olmalı). ` +
      "Taşma bulgusu bu hâlde hiçbir şey söylemez."
  ).toBeGreaterThanOrEqual(EKRANLAR.length);

  expect(
    olcemeyen,
    `ÖLÇEMEDİ kalan ekran(lar): ${olcemeyen.join(", ")} — liste EKSİK, ` +
      "bu ekranlar hakkında hiçbir sonuç yok"
  ).toEqual([]);
});
