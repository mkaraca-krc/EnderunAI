import { expect, test, type Browser, type Page, type Request } from "@playwright/test";

const YONETICI = process.env.DUZEN_KULLANICI;
const MESAI_KULLANICISI = process.env.DUZEN_MESAI_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;

/**
 * MESAİ/1 — GERÇEK TARAYICIDA: KALICI ÇIKIŞI YALNIZ GERÇEK "MESAİ DIŞI"
 * KARARI ÜRETİR.
 *
 * Birim testi izleyicinin KODUNU ölçüyor. Bu sonda UYGULAMAYI ölçüyor:
 * gerçek çerez, gerçek vekil, gerçek arka uç kararı.
 *
 * İKİ AİLE AYAK:
 *   1. POZİTİF (gerçek arka uç): muaf olmayan kullanıcının rol
 *      penceresi API ile kapatılıyor; izleyicinin sorusunu GERÇEK arka
 *      uç cevaplıyor ("mesai-disi") ve çıkış GERÇEKLEŞİYOR — çerez
 *      siliniyor, adres `/login?reason=work-hours`.
 *   2. OKUMA HATASI (yakalanmış cevap): aynı ölçüm düzeneği, izleyicinin
 *      sorusuna bozuk/geçici cevaplar veriyor; çıkış OLMUYOR.
 *
 * ÖLÇÜM DÜZENEĞİ HER AYAKTA AYNI ve bir yakalanmış-pozitif ayak onun
 * çıkışı GÖREBİLDİĞİNİ gösteriyor. "Çıkış yok" iddiası, düzenek
 * çıkışı göremediği için geçemez (Kural 48).
 */

/*
 * SERVICE WORKER ENGELLİ — ÖLÇÜLDÜ, TAHMİN DEĞİL.
 *
 * İlk yeşil koşuda pozitif ayak düştü: ikinci `goto`da `public/sw.js`
 * sayfayı kontrol ediyordu ve istekler `page.route`u ATLADI. İzde bu
 * isteklerin `pageref`/`_frameref` taşımadığı görüldü — sayfanın değil
 * worker'ın attığı istekler. Kesilmesi gereken 401'ler sunucuya gitti.
 *
 * Burada ölçülen şey izleyicinin KARARI. SW'nin kendi etkisi (her GET'i
 * önbelleğe alıp ağ hatasında geri vermesi) AYRI bir soru ve ayrı
 * ölçülüyor; bu sondanın içine karışırsa hangisinin kırmızı ürettiği
 * ayırt edilemez.
 */
test.use({ serviceWorkers: "block" });

const DURUM_UCU = "**/api/backend/auth/work-hours-status";

async function girisYap(sayfa: Page, kullanici: string | undefined) {
  expect(kullanici, "Rig kullanıcısı yok — duzen-testi.sh ile koşturun").toBeTruthy();
  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: kullanici, password: PAROLA },
  });
  expect(yanit.status(), "Giriş: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

/**
 * OTURUMLU ÇAĞRI SAYFA İÇİ `fetch` İLE — `page.request` Secure çerezi
 * TAŞIMIYOR (`gunluk-uctan-uca.spec.ts`, `mesaj-sesi.spec.ts:47`).
 */
async function arkaUc(sayfa: Page, yontem: string, yol: string, govde?: unknown) {
  return sayfa.evaluate(
    async ({ yontem, yol, govde }) => {
      const y = await fetch(`/api/backend/${yol}`, {
        method: yontem,
        cache: "no-store",
        headers: govde === undefined ? {} : { "Content-Type": "application/json" },
        body: govde === undefined ? undefined : JSON.stringify(govde),
      });
      return { durum: y.status, govde: await y.text() };
    },
    { yontem, yol, govde },
  );
}

/** Çıkışı ölçen düzenek: çıkış isteği + çerez + adres. */
function cikisGozcusu(sayfa: Page) {
  const cikislar: string[] = [];
  sayfa.on("request", (r: Request) => {
    if (r.method() === "POST" && new URL(r.url()).pathname === "/api/auth/logout") {
      cikislar.push(r.postData() ?? "");
    }
  });
  return {
    cikislar,
    async cerezVarMi() {
      return (await sayfa.context().cookies()).some((c) => c.name === "enderun_token");
    },
  };
}

async function yoneticiSayfasi(browser: Browser) {
  const baglam = await browser.newContext();
  const sayfa = await baglam.newPage();
  await girisYap(sayfa, YONETICI);
  await sayfa.goto("/dashboard");
  return sayfa;
}

async function mesaiRolu(yonetici: Page): Promise<string> {
  const liste = await arkaUc(yonetici, "GET", "company-settings/work-hour-windows");
  expect(liste.durum, `Pencere listesi: ${liste.govde.slice(0, 200)}`).toBe(200);
  const rol = (JSON.parse(liste.govde) as Array<{ id: string; name: string }>).find(
    (r) => r.name === "DuzenMesai",
  );
  expect(rol, "DuzenMesai rolü rig'de tohumlanmamış — ZEMİN YOK, ölçüm yapılamaz").toBeTruthy();
  return rol!.id;
}

async function pencereyiAyarla(yonetici: Page, rolId: string, acik: boolean) {
  const windows = acik
    ? [0, 1, 2, 3, 4, 5, 6].map((gun) => ({
        dayOfWeek: gun,
        startTime: "00:00:00",
        endTime: "23:59:59",
      }))
    : [];
  const sonuc = await arkaUc(yonetici, "PUT", `company-settings/work-hour-windows/${rolId}`, {
    windows,
  });
  expect(sonuc.durum, `Pencere ${acik ? "açma" : "kapama"}: ${sonuc.govde.slice(0, 200)}`).toBe(200);
}

test("MESAİ/1 pozitif: gerçek arka uç 'mesai-disi' der → çıkış VAR", async ({ browser, page }) => {
  const yonetici = await yoneticiSayfasi(browser);
  const rolId = await mesaiRolu(yonetici);

  // Pencere açıkken giriş: ZEMİN — kapalıyken giriş 403 döner.
  await pencereyiAyarla(yonetici, rolId, true);
  await girisYap(page, MESAI_KULLANICISI);
  const gozcu = cikisGozcusu(page);

  const ilkDurum = page.waitForResponse((r) => r.url().includes("auth/work-hours-status"));
  await page.goto("/dashboard");
  const ilk = await ilkDurum;
  expect(ilk.status()).toBe(200);
  expect((await ilk.json()).karar, "Pencere açıkken karar").toBe("izinli");
  expect(gozcu.cikislar, "Pencere açıkken çıkış olmamalı").toHaveLength(0);

  // İLK SAYFA BOŞALTILIYOR — kuyruktaki istekler kapanıştan sonra varıyordu.
  //
  // ÖLÇÜLDÜ (arka uç günlüğü, sıralı): pano ~25 istek atıyor; tarayıcı
  // HTTP/1.1'de köken başına 6 bağlantı açtığı için gerisi TARAYICIDA
  // kuyrukta bekliyor. Bu istekler kesme kurulmadan "gönderilmiş"
  // sayıldığından kesmeye takılmadı ve pencere kapandıktan SONRA
  // sunucuya vardı (PUT 6065–6669. satır, istekler 6683+). Mesai 401'i
  // aldılar — karar DOĞRUYDU — `apiClient` `/login`e yönlendirdi ve
  // sondanın `goto`su iptal oldu. Ürün kusuru değil, düzenek sırası.
  await page.goto("about:blank");

  // YALNIZ izleyicinin sorusu arka uca gidiyor. Mesai kapanınca 401 alan
  // diğer bileşenler de `/login`e yönlendiriyor ve izleyiciyle yarışıyor
  // (ürünün gerçek davranışı, CANLI-1'e yazıldı); burada ölçülen şey
  // İZLEYİCİNİN kararı olduğu için yarış dışarıda bırakılıyor.
  await page.route("**/api/backend/**", (route) =>
    route.request().url().includes("auth/work-hours-status") ? route.continue() : route.abort(),
  );

  // Pencere kapanıyor — kararı GERÇEK arka uç verecek.
  await pencereyiAyarla(yonetici, rolId, false);

  const ikinciDurum = page.waitForResponse((r) => r.url().includes("auth/work-hours-status"));
  const cikisIstegi = page.waitForRequest(
    (r) => r.method() === "POST" && new URL(r.url()).pathname === "/api/auth/logout",
  );
  // `load` BEKLENMEZ: izleyicinin yönlendirmesi sayfanın `load`
  // olayından önce gelebilir ve bekleyen `goto`yu iptal eder.
  await page.goto("/dashboard", { waitUntil: "commit" });

  const ikinci = await ikinciDurum;
  expect(ikinci.status()).toBe(200);
  expect((await ikinci.json()).karar, "Pencere kapalıyken karar").toBe("mesai-disi");

  expect(JSON.parse((await cikisIstegi).postData() ?? "{}")).toEqual({ reason: "mesai-izleyicisi" });
  await page.waitForURL(/\/login\?reason=work-hours/);
  expect(await gozcu.cerezVarMi(), "Gerçek mesai dışında çerez silinmeli").toBe(false);

  await yonetici.context().close();
});

type Ayak = {
  ad: string;
  cevap: Parameters<import("@playwright/test").Route["fulfill"]>[0] | "ag-hatasi";
};

const MESAI_DISI_GOVDESI = {
  karar: "mesai-disi",
  isAllowed: false,
  isExempt: false,
  windowEndsAtUtc: null,
  minutesRemaining: null,
};

const OKUMA_HATALARI: Ayak[] = [
  {
    ad: "503 belirlenemedi",
    cevap: { status: 503, json: { karar: "belirlenemedi", message: "okunamadı" } },
  },
  { ad: "500 sunucu hatası", cevap: { status: 500, json: { message: "boom" } } },
  {
    ad: "200 ama HTML",
    cevap: {
      status: 200,
      contentType: "text/html",
      body: "<!doctype html><title>Giriş</title>",
    },
  },
  { ad: "200 ama karar alanı yok", cevap: { status: 200, json: { isAllowed: false } } },
  { ad: "ağ hatası", cevap: "ag-hatasi" },
];

async function yakalanmisAyak(page: Page, cevap: Ayak["cevap"]) {
  await girisYap(page, YONETICI);
  const gozcu = cikisGozcusu(page);

  let cevaplanan = 0;
  await page.route(DURUM_UCU, async (route) => {
    cevaplanan += 1;
    if (cevap === "ag-hatasi") await route.abort("failed");
    else await route.fulfill(cevap);
  });

  await page.goto("/dashboard");
  await expect.poll(() => cevaplanan, { message: "İzleyici hiç sormadı — ayak ölçüm yapmadı" }).toBeGreaterThan(0);
  // Karar verilip çıkış isteği atılacaksa atılmış olsun.
  await page.waitForTimeout(2_000);

  return {
    cikislar: gozcu.cikislar,
    cerez: await gozcu.cerezVarMi(),
    adres: new URL(page.url()).pathname,
  };
}

test("MESAİ/1 düzenek kontrolü: yakalanmış 'mesai-disi' → çıkış GÖRÜLÜYOR", async ({ page }) => {
  const sonuc = await yakalanmisAyak(page, { status: 200, json: MESAI_DISI_GOVDESI });

  expect(sonuc.cikislar, "Düzenek çıkışı göremiyor — okuma hatası ayakları hiçbir şey kanıtlamaz").toHaveLength(1);
  expect(sonuc.cerez).toBe(false);
});

for (const ayak of OKUMA_HATALARI) {
  test(`MESAİ/1 okuma hatası: ${ayak.ad} → çıkış YOK`, async ({ page }) => {
    const sonuc = await yakalanmisAyak(page, ayak.cevap);

    expect(sonuc.cikislar, "Okuma hatası kalıcı çıkış üretti").toHaveLength(0);
    expect(sonuc.cerez, "Okuma hatasında çerez silindi").toBe(true);
    expect(sonuc.adres).toBe("/dashboard");
  });
}
