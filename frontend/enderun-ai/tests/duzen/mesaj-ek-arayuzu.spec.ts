import { expect, test, type Page } from "@playwright/test";

/**
 * MESAJ EKİ ARAYÜZÜ — GERÇEK TARAYICIDA (MESAJ/3 C10).
 *
 * ═══ NEDEN TARAYICI ═══
 *
 * "input[type=file] sayısı > 0 mu ve ataç düğmesi çalışıyor mu"
 * Mehmet'in kendi ölçüm listesinde. Dosya seçici gizli bir input;
 * kaynakta var olması onun ERİŞİLEBİLİR olduğunu göstermez
 * (`display:none` verilseydi klavyeyle ulaşılamazdı).
 */

test.describe.configure({ timeout: 90_000 });

async function girisVeKonusma(sayfa: Page) {
  const giris = await sayfa.request.post("/api/auth/login", {
    data: {
      username: process.env.DUZEN_KULLANICI,
      password: process.env.DUZEN_PAROLA,
    },
  });
  expect(giris.status(), "Giriş").toBe(200);

  await sayfa.goto("/mesajlar");
  await sayfa.locator(".mesaj-satir").first().waitFor({ timeout: 30000 });
  await sayfa.locator(".mesaj-satir").first().click();
  await sayfa.locator("form.mesaj-yaz input[type=text]").waitFor({ timeout: 20000 });
}

test("ataç düğmesi ve dosya girdisi var", async ({ page }) => {
  await girisVeKonusma(page);

  const girdi = page.locator('input[type="file"]');
  await expect(girdi).toHaveCount(1);

  // GİZLİ AMA ERİŞİLEBİLİR: `display:none` olsaydı klavyeyle
  // ulaşılamazdı. Ekran dışına alma yöntemi bunu korur.
  const gorunurluk = await girdi.evaluate((el) => {
    const s = getComputedStyle(el);
    return { display: s.display, visibility: s.visibility };
  });
  expect(gorunurluk.display).not.toBe("none");
  expect(gorunurluk.visibility).not.toBe("hidden");

  const dugme = page.getByRole("button", { name: "Dosya ekle" });
  await expect(dugme).toBeVisible();
  await expect(dugme).toBeEnabled();
});

test("seçilen dosya listeleniyor ve kaldırılabiliyor", async ({ page }) => {
  await girisVeKonusma(page);

  await page.locator('input[type="file"]').setInputFiles({
    name: "ornek-rapor.pdf",
    mimeType: "application/pdf",
    buffer: Buffer.from("%PDF-1.7\nsahte ama gecerli basligi olan icerik"),
  });

  const secili = page.locator(".mesaj-ek-secili");
  await expect(secili).toHaveCount(1);
  await expect(secili).toContainText("ornek-rapor.pdf");

  await page.getByRole("button", { name: /ornek-rapor\.pdf dosyasını kaldır/ }).click();
  await expect(secili).toHaveCount(0);
});

/**
 * İZİNSİZ UZANTI HİÇ LİSTEYE GİRMİYOR VE SEBEBİ SÖYLENİYOR.
 *
 * İstemci kapısı korumanın kendisi DEĞİL (o sunucuda); kullanıcıya
 * baştan söylemek için var. Sessizce yok saymak, dosyanın gittiğini
 * sandırırdı.
 */
test("izinsiz uzantı reddediliyor ve sebebi görünüyor", async ({ page }) => {
  await girisVeKonusma(page);

  await page.locator('input[type="file"]').setInputFiles({
    name: "kotu.exe",
    mimeType: "application/octet-stream",
    buffer: Buffer.from([0x4d, 0x5a, 0x90, 0x00]),
  });

  await expect(page.locator(".mesaj-ek-secili")).toHaveCount(0);
  await expect(page.locator(".mesaj-ek-hata")).toContainText("kotu.exe");
});

/**
 * DOSYA SAYISI SINIRI — ALTINCI DOSYA ALINMIYOR.
 *
 * POZİTİF KONTROL: beşi ALINIYOR. Hepsi reddedilseydi test yine
 * "6 yok" derdi ve hiçbir şey kanıtlamazdı (Kural 48).
 */
test("beş dosya alınıyor, altıncısı alınmıyor", async ({ page }) => {
  await girisVeKonusma(page);

  const dosyalar = Array.from({ length: 6 }, (_, i) => ({
    name: `dosya-${i + 1}.pdf`,
    mimeType: "application/pdf",
    buffer: Buffer.from("%PDF-1.7\nicerik"),
  }));

  await page.locator('input[type="file"]').setInputFiles(dosyalar);

  await expect(page.locator(".mesaj-ek-secili")).toHaveCount(5);
  await expect(page.locator(".mesaj-ek-hata")).toContainText("en fazla 5");
});
