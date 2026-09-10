import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { chromium, expect, test, type BrowserContext, type Page } from "@playwright/test";

const URL_KOKU = process.env.DUZEN_URL ?? "http://127.0.0.1:3001";
const A_KULLANICI = process.env.DUZEN_KULLANICI;
const B_KULLANICI = process.env.DUZEN_KARSI_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;

/**
 * SW/1 — TARAYICI ÖNBELLEĞİNDE KİMLİKLİ VERİ KALMAZ (kişisel veri).
 *
 * ═══ ÖLÇÜLEN KUSUR (2026-09-10) ═══
 *
 * `public/sw.js` her GET cevabını Cache Storage'a koyuyordu: bir
 * oturumdan sonra 26 kimlikli API cevabı önbellekteydi ve ÇIKIŞTAN
 * SONRA da duruyordu. Şantiye tabletleri ortak kullanılıyor.
 *
 * ═══ ÜÇ SONDA, ÜÇÜ DE KENDİ POZİTİF KONTROLÜYLE ═══
 *
 *   (d) gez → kimlikli kayıt yok; çık → önbellek boş; tarayıcıyı
 *       kapat-aç → yine boş. Kontrol: önbelleği okuyan düzenek elle
 *       konan bir kaydı GÖRÜYOR, ve kapat-aç kalıcılığı bir nöbetçi
 *       kayıtla kanıtlanıyor — "boş" iddiası düzenek kör olduğu için
 *       geçemez.
 *   (e) SABOTAJ: eski sürümün önbelleği bırakılır, yeni SW etkinleşir
 *       → eski kayıtlar silinmiş olmalı. Sabotaj olmadan "etkinleşince
 *       temizler" iddiası sınanmamış olur.
 *   (f) Ağ hatasında önbellekten BAŞKA KULLANICIYA cevap dönüyor mu —
 *       çağırarak.
 *
 * Kalıcı profil (`launchPersistentContext`) kullanılıyor: tarayıcıyı
 * kapatıp açmak ancak diskteki profille ölçülebilir.
 */

type Liste = { adlar: string[]; kayitlar: string[] };

async function onbellegiListele(sayfa: Page): Promise<Liste> {
  return sayfa.evaluate(async () => {
    const adlar = await caches.keys();
    const kayitlar: string[] = [];
    for (const ad of adlar) {
      const c = await caches.open(ad);
      for (const r of await c.keys()) kayitlar.push(`${ad} ${new URL(r.url).pathname}`);
    }
    return { adlar, kayitlar };
  });
}

const kimlikli = (l: Liste) => l.kayitlar.filter((k) => k.includes("/api/"));

async function kayitKoy(sayfa: Page, onbellek: string, yol: string, govde: unknown) {
  await sayfa.evaluate(
    async ({ onbellek, yol, govde }) => {
      const c = await caches.open(onbellek);
      await c.put(
        new Request(yol),
        new Response(JSON.stringify(govde), { headers: { "content-type": "application/json" } }),
      );
    },
    { onbellek, yol, govde },
  );
}

async function girisYap(baglam: BrowserContext, kullanici: string | undefined) {
  expect(kullanici, "Rig kullanıcısı yok — duzen-testi.sh ile koşturun").toBeTruthy();
  const yanit = await baglam.request.post(`${URL_KOKU}/api/auth/login`, {
    data: { username: kullanici, password: PAROLA },
  });
  expect(yanit.status(), "Giriş: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

/** SW etkin ve sayfayı kontrol ediyor — yoksa ölçüm SW'yi ölçmez. */
async function swEtkin(sayfa: Page) {
  await sayfa.evaluate(() => navigator.serviceWorker.ready.then(() => undefined));
  await expect
    .poll(() => sayfa.evaluate(() => !!navigator.serviceWorker.controller), {
      message: "SW sayfayı kontrol etmiyor — ölçüm yapılamadı",
      timeout: 10_000,
    })
    .toBe(true);
}

async function gez(sayfa: Page) {
  for (const yol of ["/dashboard", "/projeler", "/satin-alma", "/hakedis"]) {
    await sayfa.goto(`${URL_KOKU}${yol}`);
    await sayfa.waitForTimeout(2_000);
  }
}

test("SW/1 (d): gez → kimlikli kayıt yok; çık → boş; kapat-aç → yine boş", async () => {
  const dizin = mkdtempSync(join(tmpdir(), "sw1-d-"));
  try {
    let baglam = await chromium.launchPersistentContext(dizin, { baseURL: URL_KOKU });
    let sayfa = baglam.pages()[0] ?? (await baglam.newPage());

    await girisYap(baglam, A_KULLANICI);
    await sayfa.goto(`${URL_KOKU}/dashboard`);
    await swEtkin(sayfa);
    await gez(sayfa);

    // DÜZENEK KONTROLÜ: okuyucu elle konan kaydı görüyor.
    await kayitKoy(sayfa, "olcum-duzenek", "/api/olcum/duzenek", { n: 1 });
    const gezdikten = await onbellegiListele(sayfa);
    expect(gezdikten.kayitlar, "Okuyucu elle konan kaydı görmüyor").toContain(
      "olcum-duzenek /api/olcum/duzenek",
    );

    // (a) Gezinti tek bir kimlikli cevap bırakmamalı (düzenek kaydı hariç).
    const gezintiKaydi = kimlikli(gezdikten).filter((k) => !k.startsWith("olcum-duzenek "));
    expect(gezintiKaydi, "Gezinti kimlikli cevapları önbelleğe koydu").toEqual([]);

    // (c) Ürünün çıkış düğmesi — önbellek TAMAMEN boşalmalı (düzenek
    // kaydı dahil: temizlik adı ne olursa olsun hepsini silmeli).
    await sayfa.getByRole("button", { name: /çıkış/i }).first().click();
    await sayfa.waitForURL(/\/login/);
    await expect
      .poll(async () => (await onbellegiListele(sayfa)).kayitlar, {
        message: "Çıkıştan sonra önbellekte kayıt kaldı",
        timeout: 5_000,
      })
      .toEqual([]);

    // KALICILIK KONTROLÜ: kapat-aç'tan sağ çıkması gereken nöbetçi.
    await kayitKoy(sayfa, "olcum-kalicilik", "/olcum/nobetci", { n: 1 });
    await baglam.close();

    baglam = await chromium.launchPersistentContext(dizin, { baseURL: URL_KOKU });
    sayfa = baglam.pages()[0] ?? (await baglam.newPage());
    await sayfa.goto(`${URL_KOKU}/login`);

    const acildiktan = await onbellegiListele(sayfa);
    expect(acildiktan.kayitlar, "Kapat-aç kalıcılığı kanıtlanamadı — ölçüm yapılamadı").toContain(
      "olcum-kalicilik /olcum/nobetci",
    );
    expect(kimlikli(acildiktan), "Kapat-aç sonrası kimlikli kayıt var").toEqual([]);

    await baglam.close();
  } finally {
    rmSync(dizin, { recursive: true, force: true });
  }
});

test("SW/1 (e) SABOTAJ: eski sürümün önbelleği, yeni SW etkinleşince silinir", async () => {
  const dizin = mkdtempSync(join(tmpdir(), "sw1-e-"));
  try {
    // Oturum 1 — SW ENGELLİ: önbellek, eski SW'nin bıraktığı biçimde
    // dolduruluyor (aynı ad, aynı kimlikli yol). Cache Storage kökene
    // ait; kaydı kimin yazdığı saklanmaz, durum birebir aynı.
    let baglam = await chromium.launchPersistentContext(dizin, {
      baseURL: URL_KOKU,
      serviceWorkers: "block",
    });
    let sayfa = baglam.pages()[0] ?? (await baglam.newPage());
    await sayfa.goto(`${URL_KOKU}/login`);
    await kayitKoy(sayfa, "enderun-erp-v1", "/api/backend/auth/me", {
      user: { username: "eski-surum-kullanicisi" },
    });
    await kayitKoy(sayfa, "yabanci-onbellek", "/api/backend/hr/personnel", [{ ad: "x" }]);
    const sabotaj = await onbellegiListele(sayfa);
    expect(sabotaj.adlar.sort(), "Sabotaj kurulamadı — ölçüm yapılamadı").toEqual([
      "enderun-erp-v1",
      "yabanci-onbellek",
    ]);
    await baglam.close();

    // Oturum 2 — SW serbest: yeni SW kaydolup etkinleşiyor.
    baglam = await chromium.launchPersistentContext(dizin, { baseURL: URL_KOKU });
    sayfa = baglam.pages()[0] ?? (await baglam.newPage());
    await sayfa.goto(`${URL_KOKU}/login`);
    await swEtkin(sayfa);

    await expect
      .poll(async () => (await onbellegiListele(sayfa)).adlar, {
        message: "Yeni SW etkinleşti ama eski önbellekler duruyor",
        timeout: 10_000,
      })
      .toEqual([]);

    await baglam.close();
  } finally {
    rmSync(dizin, { recursive: true, force: true });
  }
});

test("SW/1 (f): ağ hatasında önbellekten başka kullanıcıya cevap dönmüyor", async ({ context, page }) => {
  // A oturum açıp gezer.
  await girisYap(context, A_KULLANICI);
  await page.goto("/dashboard");
  await swEtkin(page);
  await gez(page);

  const kim = () =>
    page.evaluate(async () => {
      try {
        const y = await fetch("/api/backend/auth/me", { cache: "no-store" });
        const g = (await y.json().catch(() => null)) as { username?: string; user?: { username?: string } } | null;
        return { durum: y.status, kullanici: g?.user?.username ?? g?.username ?? null };
      } catch (e) {
        return { durum: -1, kullanici: null, hata: String(e) };
      }
    });

  const a = await kim();
  expect(a.kullanici, "A'nın kimliği okunamadı — ölçüm yapılamadı").toBe(A_KULLANICI);

  // Oturum DÜĞMESİZ kapanıyor (jeton süresi dolması gibi): çerez gider,
  // önbellek temizliği ÇALIŞMAZ. En kötü durum bu.
  await page.evaluate(() =>
    fetch("/api/auth/logout", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reason: "kullanici-dugmesi" }),
    }),
  );

  // B aynı tarayıcıda oturum açıyor; ağ kesiliyor.
  await girisYap(context, B_KULLANICI);
  await context.setOffline(true);
  const cevrimdisi = await kim();
  await context.setOffline(false);

  // KONTROL: B'nin oturumu geçerli ve uç çalışıyor — çevrimdışı
  // "veri yok" sonucu bozuk bir oturumdan gelmiyor.
  const b = await kim();
  expect(b.kullanici, "B'nin kimliği çevrimiçi okunamadı — ölçüm yapılamadı").toBe(B_KULLANICI);

  console.log(`[ölçüm] (f) çevrimdışı B'ye dönen: durum=${cevrimdisi.durum} kullanici=${cevrimdisi.kullanici}`);
  expect(cevrimdisi.kullanici, "Ağ hatasında B'ye A'nın kimliği döndü").not.toBe(A_KULLANICI);
  expect(cevrimdisi.kullanici, "Ağ hatasında B'ye önbellekten bir kimlik döndü").toBeNull();
});
