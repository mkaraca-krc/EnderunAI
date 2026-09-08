import { expect, test, type Page } from "@playwright/test";

/**
 * PANEL/1 — BALONCUK PANELİ AÇILIYOR MU (oturum AÇIKKEN).
 *
 * ═══ NEDEN BU TEST YOKTU VE BOŞLUK NEREDEYDİ ═══
 *
 * GİRİŞ-DÖNGÜ/1'de baloncuğa "oturum yokken istek atma" koruması
 * eklendi ve OTURUMSUZ taraf ölçüldü (GD4: /login'de 0 istek, 1 belge).
 * OTURUM AÇIK haldeki baloncuk HİÇ test edilmemişti — `/mesajlar` tam
 * sayfası test ediliyordu ama paneli açan yol değil.
 *
 * Sonuç: koruma doğruydu, ölçüm tek yönlüydü. "Koruma ekledim ama
 * neyi kırdığını ölçmedim" sınıfı.
 *
 * ═══ KB5 — TEK YÖNLÜ TEST BUGÜNKÜ KUSURU ÜRETİR ═══
 *
 * Bu dosya iki yönü de tutuyor: oturum YOKKEN istek atılmamalı
 * (giris-dongusu.spec.ts), oturum VARKEN panel açılmalı (burası).
 * Biri olmadan öteki yanlış güvence verir.
 */

const KULLANICI = process.env.DUZEN_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;

test.describe.configure({ timeout: 120_000 });

async function girisYap(sayfa: Page) {
  expect(KULLANICI, "DUZEN_KULLANICI yok — rig'i duzen-testi.sh ile koşturun").toBeTruthy();

  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: KULLANICI, password: PAROLA },
  });
  expect(yanit.status(), "Giriş: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

test("oturum açıkken baloncuk görünür ve panel açılır", async ({ page }) => {
  const cagrilar: string[] = [];
  page.on("request", (istek) => {
    if (istek.url().includes("/api/backend/")) {
      cagrilar.push(new URL(istek.url()).pathname);
    }
  });

  await girisYap(page);

  // GENİŞ EKRAN: dar ekranda `ac()` bilerek /mesajlar'a gidiyor.
  await page.setViewportSize({ width: 1536, height: 800 });

  const t0 = Date.now();
  await page.goto("/dashboard");

  const baloncuk = page.locator("button.mesaj-baloncuk");
  await baloncuk.waitFor({ state: "visible", timeout: 30_000 });
  const baloncukMs = Date.now() - t0;

  // PN2 — GEÇ GELME AYRI BİR BELİRTİ, SAYIYLA ÖLÇÜLÜYOR.
  // Oturum çözülene kadar beklemek makul; 12 saniye değil.
  expect(
    baloncukMs,
    `Baloncuk ${baloncukMs} ms sonra göründü. Oturumun çözülmesini ` +
      `beklemek makul ama bu kadarı değil.`
  ).toBeLessThan(5_000);

  // POZİTİF KONTROL: baloncuk gerçekten tıklanabilir durumda.
  await expect(baloncuk).toBeEnabled();

  const oncekiCagriSayisi = cagrilar.length;

  await baloncuk.click();

  const panel = page.locator(".mesaj-panel");
  await panel.waitFor({ state: "visible", timeout: 15_000 });

  await expect(
    panel,
    "Panel DOM'a geldi ama görünür değil"
  ).toBeVisible();

  // PANEL AÇILINCA KONUŞMALAR ÇAĞRILMALI — açılışın veri ayağı.
  await expect
    .poll(
      () => cagrilar.slice(oncekiCagriSayisi).filter((y) => y.includes("konusma")).length,
      {
        message:
          "Panel açıldı ama konuşma listesi HİÇ çağrılmadı; panel boş kalır.",
        timeout: 15_000,
      }
    )
    .toBeGreaterThan(0);

  /*
   * PANEL AÇIK KALMALI — YARIŞ AYAĞI.
   *
   * Tıklamadan sonra gelen bir tercih yanıtı `setAcik(false)` ile
   * paneli geri kapatabilir. Bu ayak olmadan test, bir an açılıp
   * kapanan paneli "açıldı" sayardı.
   */
  await page.waitForTimeout(3_000);
  await expect(
    panel,
    "Panel açıldı ama 3 saniye içinde kendiliğinden kapandı — " +
      "gecikmeli bir tercih yanıtı durumu ezmiş olabilir."
  ).toBeVisible();
});
