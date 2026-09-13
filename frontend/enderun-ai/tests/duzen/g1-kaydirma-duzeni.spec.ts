import { expect, test, type Page } from "@playwright/test";

/**
 * G1 — KAYDIRMA SIRASINDA TEK KARE BOZUK DÜZEN: TEKRARLANABİLİR Mİ?
 *
 * ═══ BİLDİRİM ═══
 *
 * Mehmet Bey `/depo-stok`ta aşağı kaydırırken BİR KAREDE yerleşimin
 * bozulduğunu, kenar çubuğunun aşağı kaydığını, kaydırma durunca
 * düzeldiğini gördü. Kendisi de "kusur demiyorum" dedi: ekran görüntüsü
 * kaydırma animasyonunun ortasında alınmış olabilir.
 *
 * ═══ BU SONDA NE YAPAR ═══
 *
 * "Gördüm / görmedim" ile yetinmez, SAYI üretir. Kenar çubuğu
 * `position: sticky; top: 0` — yani DOĞRU davranış, sayfa kaydırılırken
 * görünür alanın tepesinde KALMASIDIR. Bozulma, `getBoundingClientRect().top`
 * değerinin 0'dan SAPMASIDIR.
 *
 * Kaydırma sırasında yüzlerce kare örneklenir ve sapmanın EN BÜYÜĞÜ
 * raporlanır. Tekrarlanabilirlik için tur sayısı > 1.
 *
 * ═══ NEDEN `requestAnimationFrame` ═══
 *
 * Kusur "bir kare" sürüyor. Kareler arasında ölçmezsek tam o kareyi
 * kaçırırız ve "sorun yok" deriz — ölçüm aletinin körlüğünü bulgu diye
 * yazmış oluruz. Örnekleme tarayıcının kendi çizim döngüsünde yapılıyor.
 */

const KULLANICI = process.env.DUZEN_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;

/** Kaç piksel sapma "bozuk" sayılır. Alt piksel yuvarlaması normaldir. */
const TOLERANS = 2;

async function girisYap(sayfa: Page) {
  expect(KULLANICI, "DUZEN_KULLANICI yok — rig'i duzen-testi.sh ile koşturun").toBeTruthy();
  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: KULLANICI, password: PAROLA },
  });
  expect(yanit.status(), "Giriş: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

test("G1: kaydırma sırasında kenar çubuğu yerinden oynuyor mu", async ({ page }) => {
  test.setTimeout(600_000);

  await girisYap(page);
  await page.setViewportSize({ width: 1536, height: 864 });
  await page.goto("/depo-stok", { waitUntil: "domcontentloaded", timeout: 60_000 });
  await page.waitForTimeout(2_500);

  const TUR = 5;
  const olcumler: {
    tur: number;
    kare: number;
    enBuyukSapma: number;
    enBuyukTasma: number;
    sapmaKaresi: number;
  }[] = [];

  for (let tur = 1; tur <= TUR; tur++) {
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(400);

    const sonuc = await page.evaluate(async () => {
      const kenar = document.querySelector(".erp-sidebar");
      if (!kenar) return { yok: true as const };

      let enBuyukSapma = 0;
      let sapmaKaresi = -1;
      let enBuyukTasma = 0;
      let kare = 0;

      const bitis = document.documentElement.scrollHeight - window.innerHeight;
      const adim = Math.max(8, Math.round(bitis / 120));

      await new Promise<void>((bitti) => {
        const tik = () => {
          const r = kenar.getBoundingClientRect();
          // sticky top:0 → tepe HER KAREDE 0 olmalı
          const sapma = Math.abs(r.top);
          if (sapma > enBuyukSapma) {
            enBuyukSapma = sapma;
            sapmaKaresi = kare;
          }
          const tasma = Math.max(
            0,
            document.documentElement.scrollWidth - window.innerWidth,
          );
          if (tasma > enBuyukTasma) enBuyukTasma = tasma;

          kare += 1;
          if (window.scrollY >= bitis || kare > 400) {
            bitti();
            return;
          }
          window.scrollBy(0, adim);
          requestAnimationFrame(tik);
        };
        requestAnimationFrame(tik);
      });

      return { yok: false as const, enBuyukSapma, enBuyukTasma, kare, sapmaKaresi };
    });

    expect(sonuc.yok, ".erp-sidebar bulunamadı — ölçüm yapılamadı").toBe(false);
    if (sonuc.yok) return;

    olcumler.push({
      tur,
      kare: sonuc.kare,
      enBuyukSapma: Math.round(sonuc.enBuyukSapma * 100) / 100,
      enBuyukTasma: sonuc.enBuyukTasma,
      sapmaKaresi: sonuc.sapmaKaresi,
    });
  }

  console.log("\n=== G1 — KAYDIRMA SIRASINDA KENAR ÇUBUĞU ===");
  console.log("tur  kare  en büyük sapma(px)  sapma karesi  yatay taşma(px)");
  for (const o of olcumler) {
    console.log(
      String(o.tur).padEnd(5) +
        String(o.kare).padEnd(6) +
        String(o.enBuyukSapma).padEnd(20) +
        String(o.sapmaKaresi).padEnd(14) +
        String(o.enBuyukTasma),
    );
  }

  const toplamKare = olcumler.reduce((t, o) => t + o.kare, 0);
  const bozukTur = olcumler.filter((o) => o.enBuyukSapma > TOLERANS);
  console.log(
    `\nTOPLAM ÖRNEKLENEN KARE: ${toplamKare}  ·  TOLERANS: ${TOLERANS}px`,
  );
  console.log(
    `SAPMA GÖRÜLEN TUR: ${bozukTur.length}/${olcumler.length}` +
      (bozukTur.length
        ? `  (en büyük ${Math.max(...bozukTur.map((o) => o.enBuyukSapma))}px)`
        : ""),
  );

  /*
   * ═══ POZİTİF KONTROL (Kural 48) ═══
   *
   * "Sapma 0" tek başına iki şeyin kanıtı olabilir: düzen sağlamdır,
   * YA DA ölçüm aleti sapmayı göremiyor. Ayırmak için sapma BİLEREK
   * üretilir: kenar çubuğunun `position: sticky` kuralı kaldırılır;
   * o zaman çubuk sayfayla birlikte yukarı kaymalı ve `top` değeri
   * 0'dan UZAKLAŞMALIDIR. Kontrol sapma göstermezse yukarıdaki "sapma
   * yok" sonucu hiçbir şey söylemez.
   */
  const kontrol = await page.evaluate(async () => {
    const kenar = document.querySelector(".erp-sidebar") as HTMLElement | null;
    if (!kenar) return -1;
    const eski = kenar.style.position;
    kenar.style.position = "static"; // sapmayı BİLEREK üret
    window.scrollTo(0, 0);
    await new Promise((r) => requestAnimationFrame(() => r(null)));
    window.scrollBy(0, 300);
    await new Promise((r) => requestAnimationFrame(() => r(null)));
    const sapma = Math.abs(kenar.getBoundingClientRect().top);
    kenar.style.position = eski; // geri al
    return Math.round(sapma);
  });
  console.log(`POZİTİF KONTROL (sticky kaldırıldı): sapma ${kontrol}px — ` +
    `alet ${kontrol > TOLERANS ? "sapmayı GÖRÜYOR ✓" : "sapmayı GÖREMİYOR ✗"}`);

  expect(
    kontrol,
    "POZİTİF KONTROL DÜŞTÜ: sapma bilerek üretildiği hâlde ölçülemedi. " +
      "Bu hâlde yukarıdaki 'sapma yok' sonucu düzenin sağlamlığını DEĞİL, " +
      "ölçüm aletinin körlüğünü gösteriyor olabilir.",
  ).toBeGreaterThan(TOLERANS);

  /*
   * ÖLÇÜM SAĞLIĞI (Kural 48). "Sapma yok" sonucu, HİÇ KARE
   * ÖRNEKLENMEDİĞİNDE de doğrudur. Önce aletin çalıştığı kanıtlanır.
   */
  expect(
    toplamKare,
    `Yalnız ${toplamKare} kare örneklendi — "sapma yok" bu hâlde hiçbir şey söylemez.`,
  ).toBeGreaterThan(200);

  expect(
    olcumler.length,
    "Turların hepsi ölçülemedi — tekrarlanabilirlik hükmü verilemez.",
  ).toBe(TUR);
});
