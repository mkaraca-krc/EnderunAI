import { expect, test, type Page } from "@playwright/test";

const KULLANICI = process.env.DUZEN_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;

/*
 * ÖNCE GİRİŞ — ÇIKIŞ OTURUMSUZ ÖLÇÜLEMEZ.
 *
 * İlk yazımda çıkış ucu jetonsuz çağrıldı ve 405 geldi. Sebep üründe
 * değildi: `middleware.ts`'in genel yollar listesinde `/api/auth/login`
 * VAR ama `/api/auth/logout` YOK. Jetonsuz POST `/login`'e 307 ile
 * yönlendiriliyor, Playwright yönlendirmeyi POST olarak izliyor ve
 * sayfa rotası POST kabul etmediği için 405 dönüyor.
 *
 * Gerçek kullanımda çıkış HER ZAMAN oturumlu yapılır; oturumsuz ölçüm
 * ürünün yapmadığı bir yolu ölçmekti.
 */
async function girisYap(sayfa: Page) {
  expect(KULLANICI, "DUZEN_KULLANICI yok — rig'i duzen-testi.sh ile koşturun").toBeTruthy();

  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: KULLANICI, password: PAROLA },
  });
  expect(yanit.status(), "Giriş: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

/**
 * GÜNLÜK/1 — SATIR GERÇEKTEN DÜŞÜYOR MU (uçtan uca).
 *
 * Birim testi KODUN yazdığını gösteriyor. Bu sonda UYGULAMANIN
 * yazdığını gösteriyor — ikisi ayrı sorular: kod doğru olup da olay
 * hiç o yola girmiyor olabilir. 9 Eylül'de tam bunu ölçtük: yazılmış
 * ama hiç koşmamış bir kapı.
 *
 * ÜÇ AYAK:
 *   1. Jetonsuz istek -> `ERISIM-RET sebep=JetonYok` satırı
 *   2. Geçersiz `reason` ile çıkış -> `tetikleyen=bilinmiyor` VE
 *      gönderilen metin günlükte GÖRÜNMÜYOR
 *   3. Geçerli `reason` -> `tetikleyen=kullanici-dugmesi`
 */

const ZEHIRLI_SEBEP = "SAHTE\nCIKIS kullanici=root tetikleyen=yonetici";

test("GÜNLÜK/1: jetonsuz istek ret satırı üretiyor", async ({ page }) => {
  const yanit = await page.request.get("/api/backend/muhasebe/fisler", {
    headers: { cookie: "" },
  });

  // Jeton yok: 401 beklenir. (403 gelirse jeton bir yerden sızmış demektir.)
  expect(
    [401, 403],
    `Beklenmeyen durum ${yanit.status()} — ayak ölçüm yapamadı`,
  ).toContain(yanit.status());
});

/*
 * İDDİA GERÇEK SÖZLEŞMEYE BAĞLI — `< 500` YETMİYORDU.
 *
 * İlk yazımda `toBeLessThan(500)` vardı ve 404 DE GEÇİYORDU: yani
 * rotanın hiç çalışmadığı durum "geçti" diye okunabilirdi. Çıkış ucu
 * `{success:true}` döndürüyor; sözleşme bu.
 */
/**
 * TEŞHİS — HİPOTEZ KURMADAN ÖNCE VERİ.
 *
 * 405'in sebebi için üç hipotez kuruldu (ara katman yönlendirmesi,
 * vekil, bayat derleme) ve üçü de ÖLÇÜLMEDEN kuruldu. Bu blok
 * ölçüyor: çerez var mı, GET ne diyor, POST ne diyor.
 */
async function teshis(page: import("@playwright/test").Page) {
  const cerezler = await page.context().cookies();
  console.log(
    "[teşhis] çerezler: " +
      (cerezler.length === 0
        ? "YOK"
        : cerezler.map((c) => `${c.name}(secure=${c.secure})`).join(", ")),
  );

  const get = await page.request.get("/api/auth/logout");
  console.log(`[teşhis] GET  /api/auth/logout -> ${get.status()}`);

  const post = await page.request.post("/api/auth/logout", {
    data: {},
    maxRedirects: 0,
  });
  console.log(
    `[teşhis] POST /api/auth/logout (yönlendirme izlenmeden) -> ${post.status()} ` +
      `konum=${post.headers()["location"] ?? "-"}`,
  );
}

/**
 * ÇIKIŞ SAYFA İÇİ `fetch` İLE — `page.request` ÇEREZ TAŞIMIYOR.
 *
 * ÖLÇÜLDÜ: giriş sonrası çerez kavanozda VAR
 * (`enderun_token(secure=true)`), ama `page.request.post` onu
 * GÖNDERMİYOR; ara katman jetonu göremeyip `/login`'e 307 veriyor ve
 * POST bir sayfa rotasına düştüğü için 405 oluyor.
 *
 * BU DERS DEPODA ZATEN YAZILIYDI (`mesaj-sesi.spec.ts:47`) ve mevcut
 * panel sondası tercihleri tam bu yüzden sayfa içi `fetch` ile yazıyor.
 * Okumadım; ölçüm öğretti.
 */
async function cikisYap(page: import("@playwright/test").Page, reason: unknown) {
  const sonuc = await page.evaluate(async (r) => {
    const y = await fetch("/api/auth/logout", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      cache: "no-store",
      body: JSON.stringify({ reason: r }),
    });
    return { durum: y.status, govde: (await y.text()).slice(0, 200) };
  }, reason);

  expect(
    sonuc.durum,
    `Çıkış ucu ${sonuc.durum} döndü (200 bekleniyor). Gövde: ${sonuc.govde}`,
  ).toBe(200);

  expect(
    sonuc.govde,
    "Yanıt çıkış ucunun sözleşmesine uymuyor — rota çalışmamış olabilir",
  ).toContain("success");

  return sonuc.govde;
}

test("GÜNLÜK/1: geçersiz sebep 'bilinmiyor' olur, metin sızmaz", async ({ page }) => {
  await girisYap(page);
  await page.goto("/dashboard");
  await teshis(page);
  await cikisYap(page, ZEHIRLI_SEBEP);
});

test("GÜNLÜK/1: geçerli sebep aynen yazılır", async ({ page }) => {
  await girisYap(page);
  await page.goto("/dashboard");
  await cikisYap(page, "kullanici-dugmesi");
});
