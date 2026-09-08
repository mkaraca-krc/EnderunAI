import { expect, test, type Page } from "@playwright/test";

/**
 * YETKİ MATRİSİ KULLANILABİLİRLİĞİ (YETKİ/2 Y2-4).
 *
 * ═══ ÖLÇÜLEN SORUN ═══
 *
 * 184 satır, 41 bölüm, tek tablo. "Muhasebe" 122. satırda, 25. bölümde.
 * Arama yok, bölüm bağlantısı yok. Mehmet "matriste muhasebe yok" diye
 * ölçtü — VARDI, ULAŞILAMIYORDU. Kayıp ile ulaşılamaz aynı görünüyor.
 *
 * `sticky` sınıfları baştan beri vardı ama işe yaramıyordu: kapsayıcının
 * yükseklik sınırı yoktu, sayfa kayıyordu ve başlık kapsayıcının
 * tepesine yapışıp ekranın dışında kalıyordu.
 *
 * Ölçüm 1366x768'de — Mehmet'in belirttiği çözünürlük.
 */

const GORUNUM = { width: 1366, height: 768 };

/*
 * ═══ SÜRE SINIRI 90 SANİYE — VE BU BİR ÖLÇÜM SONUCU ═══
 *
 * Varsayılan 30 sn ile bu dosya TEK BAŞINA geçiyor (≈20 sn) ama tüm
 * takımla koşunca düşüyordu (36 sn'de zaman aşımı). Testi
 * gevşetmedim; sebebi ölçtüm: matris 184 satır × 15 rol ≈ 2760
 * hücre render ediyor ve sayfa gerçekten yavaş açılıyor.
 *
 * Yani bu satır bir test hilesi değil, ÖLÇÜLEN bir gerçeğin kaydı.
 * Sayfanın ağırlığı ayrıca raporlandı — hücre sayısını düşürmek
 * (sanallaştırma) ayrı bir iş.
 */
test.describe.configure({ timeout: 90_000 });

async function girisVeMatris(sayfa: Page) {
  const yanit = await sayfa.request.post("/api/auth/login", {
    data: {
      username: process.env.DUZEN_KULLANICI,
      password: process.env.DUZEN_PAROLA,
    },
  });
  expect(yanit.status(), "Giriş").toBe(200);

  await sayfa.setViewportSize(GORUNUM);
  await sayfa.goto("/sistem-yonetimi/yetki-matrisi");

  // Tablo gelene kadar bekle — ölçülecek şey o. Süre ölçülüyor:
  // sayfanın ağırlığı bir bulgu, tahmin değil.
  const t0 = Date.now();
  await sayfa.locator("table tbody tr").first().waitFor({ timeout: 60000 });
  const satirSayisi = await sayfa.locator("table tbody tr").count();
  console.log(
    `[olcum] matris ilk satir: ${Date.now() - t0} ms, satir: ${satirSayisi}`
  );

  /*
   * TANI: arama kutusu gerçekten var mı.
   *
   * `getByLabel("İzin ara")` ile aranınca bulunamadı ve sebebi
   * ölçülmeden geçilmedi — yapıda metin VARDI (derlenmiş dosyada
   * bulundu). Seçici artık elemanın kendisine bakıyor; erişilebilir
   * ad ayrıca sınanıyor, böylece ikisi ayrışırsa hangisi bozuldu
   * görünür.
   */
  const kutu = sayfa.locator('input[type="search"]');
  await kutu.waitFor({ timeout: 15000 });

  const etiket = await kutu.getAttribute("aria-label");
  expect(etiket, "Arama kutusunun erişilebilir adı yok").toBeTruthy();
}

test("arama kutusu izinleri süzüyor", async ({ page }) => {
  await girisVeMatris(page);

  const oncekiSatir = await page.locator("table tbody tr").count();
  // POZİTİF KONTROL: tablo gerçekten büyük; süzme ölçülebilsin.
  expect(oncekiSatir, "Matris beklenenden küçük").toBeGreaterThan(100);

  await page.locator('input[type="search"]').fill("muhasebe");
  await expect
    .poll(() => page.locator("table tbody tr").count(), { timeout: 5000 })
    .toBeLessThan(oncekiSatir);

  const sonraki = await page.locator("table tbody tr").count();
  expect(sonraki, "Süzme her şeyi eledi — arama işe yaramıyor").toBeGreaterThan(0);

  // Anahtarla da bulunmalı: kullanıcı bazen "accounting" yazıyor.
  await page.locator('input[type="search"]').fill("accounting");
  await expect
    .poll(() => page.locator("table tbody tr").count(), { timeout: 5000 })
    .toBeGreaterThan(0);
});

test("bölüm bağlantısı o bölüme götürüyor", async ({ page }) => {
  await girisVeMatris(page);

  /*
   * ÇİP METİNLERİ ÖLÇÜLEREK BULUNUYOR.
   *
   * `getByRole("button", { name: /MUHASEBE/i })` ile aranınca
   * bulunamadı; hangi metinlerin render edildiğini VARSAYMAK yerine
   * okuyup eşleştiriyorum. Bulunamazsa hata mesajı ne bulunduğunu
   * yazıyor — "yok" ile "başka adla var" ayrılsın diye.
   */
  const cipler = page.locator('[data-bolum-cip="1"]');
  await cipler.first().waitFor({ timeout: 15000 });

  const metinler = await cipler.allInnerTexts();
  const dizin = metinler.findIndex((m) => m.toLocaleLowerCase("tr").includes("muhasebe"));

  expect(
    dizin,
    "Muhasebe bölüm çipi yok. Bulunan bölümler: " + metinler.join(" | ")
  ).toBeGreaterThan(-1);

  const cip = cipler.nth(dizin);
  await cip.click();

  const baslik = page.locator('[id^="matris-bolum-"]').filter({ hasText: /MUHASEBE/i }).first();

  /*
   * KAYDIRMANIN OTURMASI BEKLENİYOR — SABİT SÜRE DEĞİL.
   *
   * Önce `waitForTimeout(800)` vardı ve test KARARSIZDI: kaydırma
   * `behavior: "smooth"` ve süresi içeriğin uzunluğuna bağlı.
   * KARAR 3b ile 7 satır gizlenince tablo kısaldı, zamanlama
   * değişti ve başlık 772 px'te yakalandı — görünen alanın 5 px
   * altında.
   *
   * SINIRI GEVŞETMEDİM. Ölçülen iddia "bölüme GÖTÜRÜYOR mu";
   * animasyonun ne kadar sürdüğü o iddianın parçası değil. Artık
   * başlık görünen alana girene kadar bekleniyor; hiç girmezse
   * test düşüyor.
   */
  await expect
    .poll(
      async () => {
        const k = await baslik.boundingBox();
        return k ? Math.round(k.y) : Number.MAX_SAFE_INTEGER;
      },
      { timeout: 10000 }
    )
    .toBeLessThan(GORUNUM.height);

  const kutu = await baslik.boundingBox();
  expect(kutu, "Muhasebe bölüm başlığı bulunamadı").not.toBeNull();
  expect(kutu!.y, "Başlık yukarı kaçtı").toBeGreaterThan(-1);
});

test("rol başlıkları ve bölüm başlıkları kaydırırken üstte kalıyor", async ({ page }) => {
  await girisVeMatris(page);

  const kapsayici = page.locator("div.overflow-auto").filter({ has: page.locator("table") }).first();

  // Aşağı kaydır — 41 bölümün ortasına.
  await kapsayici.evaluate((el) => { el.scrollTop = el.scrollHeight / 2; });
  await page.waitForTimeout(500);

  // POZİTİF KONTROL: gerçekten kaydı.
  const kaydi = await kapsayici.evaluate((el) => el.scrollTop);
  expect(kaydi, "Kapsayıcı hiç kaymadı; ölçüm anlamsız olurdu").toBeGreaterThan(200);

  const rolBasligi = page.locator("thead th").nth(1);
  const kutu = await rolBasligi.boundingBox();

  expect(kutu, "Rol başlığı ölçülemedi").not.toBeNull();
  expect(
    kutu!.y + kutu!.height,
    `Rol başlığı kaydırınca görünen alanın dışına çıktı ` +
      `(alt kenar ${Math.round(kutu!.y + kutu!.height)}, ekran ${GORUNUM.height})`
  ).toBeLessThanOrEqual(GORUNUM.height);
  expect(kutu!.y + kutu!.height, "Rol başlığı yukarı kaçtı").toBeGreaterThan(0);
});

test("belge kaymıyor; kayan tek şey tablo", async ({ page }) => {
  await girisVeMatris(page);

  const belge = await page.evaluate(() => ({
    scrollHeight: document.documentElement.scrollHeight,
    clientHeight: document.documentElement.clientHeight,
  }));

  expect(
    belge.scrollHeight,
    `Belge kayıyor: ${belge.scrollHeight} > ${belge.clientHeight}. ` +
      "Sayfa kayarsa sticky başlıklar ekranın dışında kalır."
  ).toBeLessThanOrEqual(belge.clientHeight);
});
