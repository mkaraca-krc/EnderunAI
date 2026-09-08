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

  await page.goto("/dashboard");
  await tercihleriKur(page, { panelAcik: false, sonKonusmaId: null });

  const t0 = Date.now();
  await page.reload();

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

/**
 * PANEL/1 — DÖRT AYAK: İKİSİ YARIŞ, İKİSİ PL1.
 *
 * ═══ TEK KOD, İKİ BELİRTİ, TEK AYIRICI ═══
 *
 * Tıklama tercih yanıtından ÖNCE gelirse → yarış: gelen tercih
 * `setAcik(false)` ile paneli kapatır (rig'de ölçüldü).
 * Tıklama SONRA gelirse → panel açık kalır, ama `lastConversationId`
 * doluysa konuşma SEÇİLİ gelir ve mesajlar hiç istenmez (canlıda
 * ölçüldü: "Bu konuşmada henüz mesaj yok").
 *
 * PL1 YALNIZ lastConversationId DOLUYSA ürüyor. Rig artık tercih
 * tohumluyor — taze kullanıcıda kusur hiç görünmez ve test yalan
 * söylerdi (Kural 81'in tercih hâli).
 *
 * ═══ (b) VE (d) POZİTİF KONTROL ═══
 *
 * Onlar olmadan (a) ve (c)'nin kırmızısı "belki hiçbir şey
 * çalışmıyordur" ile karışırdı.
 */

/** Mesaj listesi isteklerini sayan dinleyici. */
function mesajIstekSayaci(page: Page) {
  const istekler: string[] = [];
  page.on("request", (istek) => {
    const y = istek.url();
    if (y.includes("/api/backend/") && /\/mesajlar(\?|$)/.test(y)) {
      istekler.push(new URL(y).pathname + new URL(y).search);
    }
  });
  return istekler;
}

/**
 * TESTİN KENDİ ZEMİNİNİ KURAR — SIRAYA BAĞIMLILIĞI KESER.
 *
 * ═══ ÖLÇÜLEN KUSUR: TESTLER BİRBİRİNİN ZEMİNİNİ DEĞİŞTİRİYORDU ═══
 *
 * Kırmızı turunda beyan ettiğim ayak (a) yeşil kaldı, kırmızı BİRİNCİ
 * testte belirdi. Sebep üründe değildi:
 *
 *   1. test taze tohumla koşuyor (messagePanelOpen=false) → paneli
 *      açıyor → SUNUCUYA `true` yazıyor.
 *   (a) sırası geldiğinde depolanan değer artık `true` → gelen tercih
 *      `setAcik(true)` yapıyor → panel kapanmıyor → (a) yeşil kalıyor.
 *
 * Yani (a) sırası ona uygun düştüğü için yeşildi; kusuru ayırt
 * edemiyordu. `paneliAc`i idempotent yapmak BELİRTİYİ çözdü,
 * PAYLAŞILAN SUNUCU DURUMUNU değil.
 *
 * ═══ ÇÖZÜM: HER TEST KENDİ ZEMİNİNİ KURAR ═══
 *
 * Tercihler UYGULAMANIN KENDİ UCUNDAN yazılıyor (rig'in SQL tohumundan
 * değil): testin kurduğu zemin ile uygulamanın gördüğü zemin aynı
 * yoldan geçsin. Zemin SQL'den kurulsaydı, uç bir gün başka bir alan
 * yazmaya başladığında test bunu göremezdi.
 */
/*
 * ÇAĞRI SAYFA İÇİNDEN — `page.request` ÇEREZ TAŞIMIYOR.
 *
 * İlk yazımda `page.request.put` kullandım ve beş testin beşi de
 * 575 ms'de 401 ile düştü. Ders ZATEN YAZILIYDI, `mesaj-sesi.spec.ts`
 * içinde: giriş `page.request.post` ile yapılabiliyor (çerez tarayıcı
 * bağlamına geçiyor) ama sonraki API çağrıları o bağlamdan 401 alıyor.
 * Veri uçları sayfa içinden `fetch` ile çağrılıyor — uygulamanın kendi
 * yaptığının birebir aynısı.
 *
 * Yazılı bir dersi okumadan yeni bir yardımcı yazdım; bedeli bir rig
 * turu oldu.
 *
 * SAYFA YÜKLÜ OLMALI: bu yüzden çağrılar `page.goto`dan SONRA.
 */
async function tercihleriKur(
  sayfa: Page,
  tercih: { panelAcik: boolean; sonKonusmaId: string | null }
) {
  const durum = await sayfa.evaluate(async (t) => {
    const y = await fetch("/api/backend/user-preferences", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      cache: "no-store",
      body: JSON.stringify({
        sidebarCollapsed: false,
        favoritePaths: [],
        messagePanelOpen: t.panelAcik,
        lastConversationId: t.sonKonusmaId,
        messageSoundMuted: false,
      }),
    });
    return y.status;
  }, tercih);

  expect(durum, "Tercih kurulamadı").toBeLessThan(300);
}

/** Rig'in tohumladığı ilk konuşmanın kimliği — sayfa içinden. */
async function ilkKonusmaId(sayfa: Page): Promise<string> {
  const ilk = await sayfa.evaluate(async () => {
    const y = await fetch("/api/backend/mesajlar/konusmalar?limit=30", {
      cache: "no-store",
    });
    if (!y.ok) return null;
    const g = (await y.json()) as { kayitlar?: { id: string }[] };
    return g.kayitlar?.[0]?.id ?? null;
  });

  expect(ilk, "Rig hiç konuşma tohumlamamış — zemin kurulamaz").toBeTruthy();
  return ilk!;
}

/**
 * Paneli AÇIK HÂLE GETİRİR — körü körüne tıklamaz.
 *
 * ═══ NEDEN "TIKLA" DEĞİL "AÇIK OLMASINI SAĞLA" ═══
 *
 * İlk yazımda bu yardımcı sadece tıklıyordu ve (b) ayağı düştü.
 * Sebep ÜRÜN DEĞİL, TEST KİRLİLİĞİYDİ: (a) ayağı paneli açıyor ve
 * `tercihYaz({messagePanelOpen:true})` bunu SUNUCUYA yazıyor. Rig'de
 * kullanıcı testler arasında aynı. (b) koştuğunda tercih artık `true`,
 * panel kendiliğinden açılıyor, ve körü körüne tıklama AÇIK PANELİ
 * KAPATIYOR.
 *
 * Yani yardımcının varsayımı ("tıkladığımda panel kapalıdır")
 * doğrulanmamıştı. Testler sunucuda kalıcı durum bırakıyorsa,
 * sıraları birbirine gizli bağımlılık üretir.
 *
 * Bunu (b) POZİTİF KONTROLÜ yakaladı. O ayak olmasaydı (a) ve (c)
 * yeşil raporlanır, bağımlılık görünmezdi.
 */
async function paneliAc(page: Page) {
  const panel = page.locator(".mesaj-panel");
  const baloncuk = page.locator("button.mesaj-baloncuk");

  await baloncuk.waitFor({ state: "visible", timeout: 30_000 });

  if (await panel.isVisible()) return panel;

  await baloncuk.click();
  return panel;
}

/** (a) YARIŞ — tercih yanıtı gelmeden tıkla, panel AÇIK KALMALI. */
test("(a) tercihler gelmeden tıklanınca panel açık kalıyor", async ({ page }) => {
  // TERCİH YANITINI GECİKTİR: yarış penceresini bilerek açıyoruz.
  await page.route("**/api/backend/user-preferences", async (route) => {
    await new Promise((r) => setTimeout(r, 4_000));
    await route.continue();
  });

  await girisYap(page);
  await page.setViewportSize({ width: 1536, height: 800 });
  await page.goto("/dashboard");

  // ZEMİN: panel KAPALI. Bu ayak, gelen tercihin kullanıcının açma
  // kararını ezip ezmediğini ölçüyor; zemin `true` olsaydı ezme
  // görünmezdi ve ayak sırasına bağımlı hale gelirdi.
  await tercihleriKur(page, { panelAcik: false, sonKonusmaId: null });
  await page.reload();

  const panel = await paneliAc(page);
  await panel.waitFor({ state: "visible", timeout: 15_000 });

  // Gecikmeli tercih yanıtı gelsin ve ezmeye çalışsın.
  await page.waitForTimeout(8_000);

  await expect(
    panel,
    "Panel kendiliğinden kapandı: gecikmeli tercih yanıtı kullanıcının " +
      "kararını ezdi (YARIŞ)."
  ).toBeVisible();
});

/** (b) POZİTİF KONTROL — tercihler geldikten sonra tıkla. */
test("(b) tercihler geldikten sonra tıklanınca panel açık kalıyor", async ({
  page,
}) => {
  await girisYap(page);
  await page.setViewportSize({ width: 1536, height: 800 });
  await page.goto("/dashboard");

  await tercihleriKur(page, { panelAcik: false, sonKonusmaId: null });
  await page.reload();

  // Tercihler rahatça gelsin.
  await page.waitForTimeout(6_000);

  const panel = await paneliAc(page);
  await panel.waitFor({ state: "visible", timeout: 15_000 });
  await page.waitForTimeout(6_000);

  await expect(
    panel,
    "Panel bu koşulda da kapandıysa kusur yarıştan bağımsız — " +
      "(a)'nın kırmızısı yanlış yorumlanırdı."
  ).toBeVisible();
});

/** (c) PL1 — lastConversationId DOLU, panel açılınca mesaj İSTENMELİ. */
test("(c) seçili gelen konuşmanın mesajları isteniyor", async ({ page }) => {
  const istekler = mesajIstekSayaci(page);

  await girisYap(page);
  await page.setViewportSize({ width: 1536, height: 800 });
  await page.goto("/dashboard");

  // ZEMİN: son konuşma DOLU. PL1 yalnız bu koşulda ürüyor; boş
  // olsaydı panel seçimsiz açılır ve ayak yeşil verip yalan söylerdi.
  await tercihleriKur(page, {
    panelAcik: false,
    sonKonusmaId: await ilkKonusmaId(page),
  });
  await page.reload();

  // Tercihler gelsin ki `baslangicKonusmaId` dolu olsun.
  await page.waitForTimeout(6_000);

  const panel = await paneliAc(page);
  await panel.waitFor({ state: "visible", timeout: 15_000 });

  await expect
    .poll(() => istekler.length, {
      message:
        "PANEL AÇILDI, konuşma SEÇİLİ geldi, ama MESAJLAR İSTENMEDİ. " +
        "Ekran 'henüz mesaj yok' der ve kullanıcı bunu veri kaybı sanır.",
      timeout: 15_000,
    })
    .toBeGreaterThan(0);
});

/** (d) POZİTİF KONTROL — listeden elle seçim mesajları getiriyor. */
test("(d) listeden seçilen konuşmanın mesajları isteniyor", async ({ page }) => {
  const istekler = mesajIstekSayaci(page);

  await girisYap(page);
  await page.setViewportSize({ width: 1536, height: 800 });
  await page.goto("/mesajlar");

  // ZEMİN: son konuşma BOŞ — bu ayak ELLE seçimi ölçüyor, geri
  // yüklemeyi değil.
  await tercihleriKur(page, { panelAcik: false, sonKonusmaId: null });
  await page.reload();

  const satirlar = page.locator("button.mesaj-satir");

  // SEÇİCİYE POZİTİF KONTROL — yokluğu bildirmeden önce varlığı göster.
  await expect
    .poll(() => satirlar.count(), {
      message: "Hiç konuşma satırı yok; seçici yanlış olabilir.",
      timeout: 20_000,
    })
    .toBeGreaterThan(0);

  await satirlar.first().click();

  await expect
    .poll(() => istekler.length, {
      message:
        "Elle seçimde de mesaj istenmedi — kusur seçim yolundan bağımsız, " +
        "(c)'nin kırmızısı yanlış yorumlanırdı.",
      timeout: 15_000,
    })
    .toBeGreaterThan(0);
});
