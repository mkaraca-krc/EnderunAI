import { expect, test, type Page } from "@playwright/test";

/**
 * MESAJ SESİ — GERÇEK TARAYICIDA, GERÇEK play() ÇAĞRISI (MESAJ/3 B9).
 *
 * ═══ NE GÖZLENİYOR ═══
 *
 * "Fonksiyon var" yeterli değil. Burada `HTMLMediaElement.play`
 * SARILIYOR ve gerçekten çağrılıp çağrılmadığı sayılıyor. Sarmalayıcı
 * `addInitScript` ile sayfa kodundan ÖNCE kuruluyor.
 *
 * play() SAHTE DEĞİL, SARMALANMIŞ: orijinali çağrılmaya devam ediyor.
 * Tamamen sahte bir play, tarayıcının otomatik oynatma politikasını
 * da devre dışı bırakırdı ve B5'i hiç sınamazdık.
 *
 * ═══ MESAJ NASIL GELİYOR ═══
 *
 * İkinci kullanıcı (`duzen-karsi-taraf`) AYRI bir istek bağlamından
 * gerçek uca POST ediyor; mesaj SignalR ile A'nın açık sayfasına
 * düşüyor. Yani sınanan şey uçtan uca yol: sunucu yayını → istemci
 * dinleyicisi → ses kararı → play().
 */

const KULLANICI = process.env.DUZEN_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;
const KARSI = process.env.DUZEN_KARSI_KULLANICI;

async function sesCasusu(sayfa: Page) {
  await sayfa.addInitScript(() => {
    (window as unknown as { __playSayisi: number }).__playSayisi = 0;
    const asil = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function (...args) {
      (window as unknown as { __playSayisi: number }).__playSayisi += 1;
      return asil.apply(this, args as []);
    };
  });
}

async function girisYap(sayfa: Page) {
  expect(KULLANICI, "DUZEN_KULLANICI yok — rig'i duzen-testi.sh ile koşturun").toBeTruthy();

  /*
   * GİRİŞ `request` İLE, VERİ UÇLARI SAYFA İÇİNDEN — İKİSİ DE ÖLÇÜLDÜ.
   *
   * `page.request.post` ile giriş 200 dönüyor ve çerez tarayıcı
   * bağlamına geçiyor (sonraki `page.goto` kimlikli açılıyor). Ama
   * `page.request.get` ile API çağrısı 401 alıyor — o bağlam çerezi
   * taşımıyor. Veri uçları bu yüzden sayfa içinden `fetch` ile
   * çağrılıyor: uygulamanın kendi yaptığının birebir aynısı.
   *
   * Girişi de sayfa içinden yapmayı denedim ve OLMADI: /login sayfası
   * çerez düşer düşmez yönleniyor, `evaluate` "execution context was
   * destroyed" ile düşüyor.
   */
  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: KULLANICI, password: PAROLA },
  });
  expect(yanit.status(), "Giriş: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

/**
 * Karşı tarafın adına, AYRI bir tarayıcı bağlamından mesaj gönderir.
 *
 * AYRI BAĞLAM ŞART: aynı bağlamda giriş yapsaydık A'nın oturumu
 * düşerdi ve ölçmek istediğimiz sayfa oturumsuz kalırdı.
 */
async function karsiTarafGonder(sayfa: Page, konusmaId: string, govde: string) {
  const yeni = await sayfa.context().browser()!.newContext({
    baseURL: process.env.DUZEN_URL,
  });

  try {
    const giris = await yeni.request.post("/api/auth/login", {
      data: { username: KARSI, password: PAROLA },
    });
    expect(
      giris.status(),
      "Karşı taraf girişi: " + (await giris.text()).slice(0, 200)
    ).toBe(200);

    const p = await yeni.newPage();
    await p.goto("/dashboard");

    const sonuc = await p.evaluate(
      async ([id, metin]) => {
        const y = await fetch(
          `/api/backend/mesajlar/konusmalar/${id}/mesajlar`,
          {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ govde: metin }),
          }
        );
        return { durum: y.status, govde: (await y.text()).slice(0, 300) };
      },
      [konusmaId, govde]
    );

    expect(sonuc.durum, "Mesaj gönderilemedi: " + sonuc.govde).toBeLessThan(300);
  } finally {
    await yeni.close();
  }
}

/** Konuşma listesindeki konuşmaların kimlikleri. */
async function konusmaKimlikleri(sayfa: Page): Promise<string[]> {
  const sonuc = await sayfa.evaluate(async () => {
    const y = await fetch("/api/backend/mesajlar/konusmalar?limit=30");
    return { durum: y.status, govde: await y.text() };
  });

  expect(sonuc.durum, "Konuşma listesi: " + sonuc.govde.slice(0, 200)).toBe(200);

  const govde = JSON.parse(sonuc.govde) as { kayitlar: { id: string }[] };
  return (govde.kayitlar ?? []).map((x) => x.id);
}

async function playSayisi(sayfa: Page): Promise<number> {
  return sayfa.evaluate(
    () => (window as unknown as { __playSayisi: number }).__playSayisi ?? 0
  );
}

/**
 * Otomatik oynatma kilidini açan gerçek kullanıcı etkileşimi.
 *
 * ═══ NEDEN KOORDİNATA DEĞİL, ELEMANA TIKLANIYOR ═══
 *
 * Önce `mouse.click(5, 5)` yazıyordu ve test KARARSIZDI: aynı test
 * bir koşuda geçip diğerinde düşüyordu. Sebep ölçüldü — (5,5)
 * noktasında kenar çubuğunun MARKA BAĞLANTISI duruyor ve tıklama
 * bazen `/dashboard`a gidiyordu; ölçülecek sayfa altımızdan
 * kayıyordu.
 *
 * Composer girdisi hem güvenli (gezinme yok) hem de gerçek: kullanıcı
 * zaten oraya tıklayarak yazmaya başlıyor.
 */
async function etkilesimYap(sayfa: Page) {
  await sayfa.locator("form.mesaj-yaz input").click();
  await sayfa.waitForTimeout(300);
}

test.describe("mesaj sesi", () => {
  /**
   * SES VARLIĞI OTURUMSUZ DA ULAŞILABİLİR OLMALI.
   *
   * ═══ İKİ AYRI İDDİA, İKİSİ DE ÖLÇÜLDÜ ═══
   *
   * Bu testi önce OTURUM AÇMIŞ sayfadan yazdım ve sonda ISIRMADI:
   * matcher düzeltmesi geri alındığında bile yeşil kaldı. Sebep
   * ölçüldü — çerez varken middleware isteği geçiriyor. Yani test,
   * kusurun YAŞADIĞI durumu hiç kurmuyordu (Kural 81).
   *
   * Üretimde ölçülen kusur OTURUMSUZ istekte: `/sesler/mesaj.wav`
   * `307 → /login` dönüyordu, çünkü middleware'in uzantı listesinde
   * `wav` yoktu (png/jpg/svg vardı).
   *
   * DÜRÜST SINIR: ses oturum açmış kullanıcıda ZATEN ÇALIŞIYORDU.
   * Bu bir kırık özellik değil, yanlış katman — bir ses dosyasının
   * kimlik kapısından geçmesi gereksiz gecikme ve sessiz bir arıza
   * yolu. Test o katmanı tutuyor.
   *
   * Diğer testler `play()` ÇAĞRISINI sayıyor; `play()` kaynak hiç
   * yüklenmese de çağrılır. Varlığın gerçekten `audio/*` geldiğini
   * sınayan tek yer burası.
   */
  test("ses dosyası oturumsuz da audio olarak geliyor", async ({ browser }) => {
    // TEMİZ BAĞLAM: çerez YOK. Kusurun yaşadığı durum bu.
    const temiz = await browser.newContext({ baseURL: process.env.DUZEN_URL });

    try {
      const yanit = await temiz.request.get("/sesler/mesaj.wav");
      const govde = await yanit.body();
      const tur = yanit.headers()["content-type"] ?? "";

      expect(yanit.status(), "Ses dosyası oturumsuz alınamadı").toBe(200);
      expect(
        tur,
        `Ses dosyası audio değil, "${tur}" geldi — kimlik kapısına ` +
          "takılıp giriş sayfası dönmüş olabilir."
      ).toMatch(/^audio\//);
      expect(govde.byteLength, "Ses dosyası boş").toBeGreaterThan(1000);
    } finally {
      await temiz.close();
    }
  });

  test.beforeEach(async ({ page }) => {
    await sesCasusu(page);
    await girisYap(page);
  });

  /**
   * B1/B2 — BAŞKA KONUŞMAYA GELEN MESAJ SES ÇALAR.
   *
   * Kullanıcı birinci konuşmayı açmış; mesaj İKİNCİ konuşmaya
   * geliyor. Bakmadığı bir yere mesaj düştü, ses çalmalı.
   */
  test("başka konuşmaya gelen mesajda ses çalar", async ({ page }) => {
    await page.goto("/mesajlar");
    const kimlikler = await konusmaKimlikleri(page);
    expect(kimlikler.length, "İki konuşma tohumlanmalıydı").toBeGreaterThan(1);

    await page.locator(".mesaj-satir").first().click();
    await page.locator("form.mesaj-yaz input").waitFor({ timeout: 20000 });
    await etkilesimYap(page);

    const acikKonusma = await page.evaluate(() =>
      document.querySelector(".mesaj-satir-secili") ? true : false
    );
    expect(acikKonusma, "Bir konuşma açık olmalıydı").toBe(true);

    const once = await playSayisi(page);

    // Açık OLMAYAN konuşmayı seç.
    const acikId = kimlikler[0];
    const digerId = kimlikler.find((x) => x !== acikId)!;
    await karsiTarafGonder(page, digerId, "ses testi - baska konusma");

    await expect
      .poll(() => playSayisi(page), { timeout: 15000 })
      .toBeGreaterThan(once);
  });

  /**
   * B2 — EKRANDA AÇIK KONUŞMAYA GELEN MESAJ SES ÇALMAZ.
   *
   * POZİTİF KONTROL AYNI TESTİN İÇİNDE: mesajın GELDİĞİ ayrıca
   * doğrulanıyor. Olmasaydı, mesaj hiç ulaşmadığında da test yeşil
   * kalırdı ve hiçbir şey kanıtlamazdı (Kural 48).
   */
  test("ekranda açık konuşmaya gelen mesajda ses çalmaz", async ({ page }) => {
    await page.goto("/mesajlar");
    const kimlikler = await konusmaKimlikleri(page);

    await page.locator(".mesaj-satir").first().click();
    await page.locator("form.mesaj-yaz input").waitFor({ timeout: 20000 });
    await etkilesimYap(page);

    const once = await playSayisi(page);
    const metin = "ses testi - acik konusma " + Date.now();

    await karsiTarafGonder(page, kimlikler[0], metin);

    // POZİTİF KONTROL: mesaj GERÇEKTEN geldi.
    await expect(page.locator(".mesaj-akis").getByText(metin)).toBeVisible({
      timeout: 15000,
    });

    expect(
      await playSayisi(page),
      "Kullanıcı bu konuşmaya bakıyorken ses çalmamalıydı."
    ).toBe(once);
  });

  /**
   * B1 — KENDİ GÖNDERDİĞİNDE ASLA ÇALMAZ.
   *
   * Sunucu yayını GÖNDERENE DE gidiyor (başka sekmesi açık olabilir).
   * Bu yüzden "kendi mesajım" ayrı bir kapı gerektiriyor; yayının
   * gelmemesine güvenilemez.
   *
   * POZİTİF KONTROL: mesajın akışta göründüğü doğrulanıyor.
   */
  test("kendi gönderdiği mesajda ses çalmaz", async ({ page }) => {
    await page.goto("/mesajlar");
    await page.locator(".mesaj-satir").first().click();
    await page.locator("form.mesaj-yaz input").waitFor({ timeout: 20000 });
    await etkilesimYap(page);

    const once = await playSayisi(page);
    const metin = "kendi mesajim " + Date.now();

    await page.locator("form.mesaj-yaz input").fill(metin);
    await page.locator("form.mesaj-yaz button[type=submit]").click();

    const gonderilen = page.locator(".mesaj-akis").getByText(metin);
    await expect(gonderilen.first()).toBeVisible({ timeout: 15000 });

    /*
     * TAM BİR KEZ — İKİ KEZ DE KUSUR.
     *
     * Bu satır bir üretim kusurunu yakaladı: kendi mesajımız hem
     * POST yanıtından hem sunucu yayınından geliyor ve tekilleştirme
     * yalnız yayın yolundaydı. Yayın önce varınca mesaj akışta iki
     * kez görünüyordu. Yarışın hangi tarafının kazandığına
     * güvenilemez, o yüzden SAYI sınanıyor.
     */
    await expect
      .poll(() => gonderilen.count(), { timeout: 5000 })
      .toBe(1);

    expect(
      await playSayisi(page),
      "Kendi gönderdiği mesajda ses çalmamalıydı."
    ).toBe(once);
  });

  /**
   * B8 — SEKME BAŞLIĞINDAKİ OKUNMAMIŞ SAYACI.
   *
   * Ses duyulmadığında görsel yedek bu. Mehmet ölçmemi istedi;
   * ölçüm testin kendisi.
   */
  test("okunmamış mesaj sekme başlığına yazılır", async ({ page }) => {
    await page.goto("/mesajlar");
    const kimlikler = await konusmaKimlikleri(page);
    const digerId = kimlikler[1];

    await page.locator(".mesaj-satir").first().click();
    await page.locator("form.mesaj-yaz input").waitFor({ timeout: 20000 });

    await karsiTarafGonder(page, digerId, "baslik testi " + Date.now());

    await expect
      .poll(() => page.title(), { timeout: 15000 })
      .toMatch(/^\(\d+\)\s/);
  });
});
