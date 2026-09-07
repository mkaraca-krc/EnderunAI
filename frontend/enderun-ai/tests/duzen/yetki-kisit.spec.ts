import { expect, test, type Page } from "@playwright/test";

/**
 * YETKİ/1 — KULLANICI BAZLI KISIT (Deny) EKRANDA UYGULANIYOR MU.
 *
 * ═══ CANLIDA ÖLÇÜLEN DURUM (2026-09-07) ═══
 *
 * uakkaya: 19 kısıt kaydı (dashboard.view dahil). Kısıtlar sunucunun
 * kanonik çözücüsünde DOĞRU okunuyor — `dashboard.view` için
 * `izinli = false` döndüğü SQL ile ölçüldü. Buna rağmen ekran
 * açılıyor.
 *
 * ═══ ÖLÇÜLEN SEBEP ═══
 *
 * `routePermission("/dashboard")` **null** dönüyor: haritada
 * `/dashboard` için kural YOK. `routeErisimi` null gördüğünde `true`
 * dönüyor — yani kuralı olmayan ekran HERKESE AÇIK. Kısıt kaydı
 * doğru, çözücü doğru, ama o ekranda kısıta BAKAN kimse yok.
 *
 * ═══ NEDEN GERÇEK TARAYICI ═══
 *
 * "Kural var mı" sorusu kaynaktan okunabilir; "kullanıcı GİREBİLİYOR
 * MU" sorusu okunamaz (Kural 70). Kısıtlı kullanıcı gerçekten giriş
 * yapıp gerçekten geziniyor.
 */

const KISITLI = process.env.DUZEN_KISITLI_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;

/** Deny konan izinler ve onları gerektirmesi beklenen ekranlar. */
const KISITLI_EKRANLAR = [
  { yol: "/dashboard", izin: "dashboard.view" },
  { yol: "/muhasebe", izin: "accounting.view" },
];

/*
 * `/yapilacaklar` BU LİSTEDE DEĞİL — VE SEBEBİ KAYDA GEÇTİ.
 *
 * İlk yazımda buradaydı ve test "kısıtlıyken açıldı" diyordu. YANLIŞTI:
 * kısıt anahtarı olarak `todo.view` kullanmıştım ve o anahtar
 * KATALOGDA YOK (ölçüldü: 143 izin arasında yok; karşılığı
 * `tasks.view`). Yani Deny hiçbir şeye denk gelmiyordu ve test
 * kısıtlanmamış bir ekranın açıldığını "kısıt uygulanmadı" diye
 * raporluyordu — TOTOLOJİ.
 *
 * Ekranın kuralsız olduğu doğru bir bulgu ama o KAPSAM AÇIĞI
 * (YETKİ/2 Y2-2), kısıt zorlaması değil. Kapsam açığı ayrı raporlanıyor
 * ve hangi izinlerin ekleneceğine Mehmet karar verecek (Y2-3).
 */

async function kisitliGiris(sayfa: Page) {
  expect(KISITLI, "DUZEN_KISITLI_KULLANICI yok — rig'i duzen-testi.sh ile koşturun").toBeTruthy();

  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: KISITLI, password: PAROLA },
  });
  expect(yanit.status(), "Kısıtlı kullanıcı girişi: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

/** Kısıtsız kullanıcının izinleri — AYRI bağlamdan okunuyor. */
async function sayfa2Izinleri(sayfa: Page): Promise<string[]> {
  const yeni = await sayfa.context().browser()!.newContext({
    baseURL: process.env.DUZEN_URL,
  });

  try {
    const giris = await yeni.request.post("/api/auth/login", {
      data: { username: process.env.DUZEN_KULLANICI, password: PAROLA },
    });
    expect(giris.status(), "Kısıtsız kullanıcı girişi").toBe(200);

    const p = await yeni.newPage();
    await p.goto("/parola");

    return await p.evaluate(async () => {
      const y = await fetch("/api/backend/auth/me");
      const g = (await y.json()) as { permissions: string[] };
      return g.permissions ?? [];
    });
  } finally {
    await yeni.close();
  }
}

test.describe("kullanıcı bazlı kısıt", () => {
  /**
   * POZİTİF KONTROL — KISIT KAYDI GERÇEKTEN VAR VE SUNUCU BİLİYOR.
   *
   * Bu olmadan alttaki testler, kısıt hiç yazılmamış olsaydı da aynı
   * sonucu verirdi ve hiçbir şey kanıtlamazdı (Kural 48).
   */
  test("sunucu kısıtlanan izinleri VERMİYOR", async ({ page }) => {
    await kisitliGiris(page);
    await page.goto("/parola");

    const izinler = await page.evaluate(async () => {
      const y = await fetch("/api/backend/auth/me");
      const g = (await y.json()) as { permissions: string[]; hasAllPermissions?: boolean };
      return { izinler: g.permissions ?? [], hepsi: g.hasAllPermissions ?? false };
    });

    expect(izinler.hepsi, "Kısıtlı kullanıcı süper kullanıcı olmamalı").toBe(false);

    /*
     * TOTOLOJİ KAPISI — AYNI İZİN, KISITSIZ KULLANICIDA VAR MI.
     *
     * "listede yok" iddiası, izin HİÇ VAR OLMASAYDI da doğru çıkardı.
     * Bu tam olarak bir kez başıma geldi: kısıt anahtarı olarak
     * `todo.view` kullanmıştım, o anahtar katalogda YOK (143 iznin
     * arasında yok; karşılığı `tasks.view`) ve test kısıtlanmamış bir
     * ekranı "kısıt uygulanmadı" diye raporluyordu.
     *
     * Katalog ucu yok; onun yerine DAHA GÜÇLÜ bir kontrol: aynı izin,
     * kısıtsız kullanıcıda VAR. Yani anahtar hem gerçek, hem rol
     * tarafından veriliyor — kısıtlıda yokluğu ancak o zaman
     * Deny'ın eseri.
     */
    const kisitsiz = await sayfa2Izinleri(page);

    for (const { izin } of KISITLI_EKRANLAR) {
      expect(
        kisitsiz,
        `${izin} kısıtsız kullanıcıda da yok — anahtar gerçek değil ya da ` +
          "rol vermiyor; kısıt testi anlamsız olurdu"
      ).toContain(izin);

      expect(izinler.izinler, `${izin} kısıtlanmıştı, sunucu yine de verdi`).not.toContain(izin);
    }
  });

  for (const { yol, izin } of KISITLI_EKRANLAR) {
    test(`${yol} kısıtlıyken açılmamalı (${izin})`, async ({ page }) => {
      await kisitliGiris(page);
      await page.goto(yol);
      await page.waitForLoadState("domcontentloaded");
      await page.waitForTimeout(1500);

      const varilan = new URL(page.url()).pathname;

      expect(
        varilan,
        `KISIT UYGULANMADI: ${izin} kısıtlı olmasına rağmen ${yol} açıldı. ` +
          "Kısıt kaydı var, sunucu izni vermiyor, ama ekran kapısı bakmıyor."
      ).not.toBe(yol);
    });
  }
});
