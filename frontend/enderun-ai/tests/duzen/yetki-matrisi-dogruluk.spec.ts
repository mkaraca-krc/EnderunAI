import { expect, test } from "@playwright/test";

/**
 * MATRİS DOĞRULUĞU — EKRANDAKİ ✓ SAYISI = API'nin GRANTS SAYISI.
 *
 * ═══ NEDEN BU TEST VAR (MATRİS/ACİL, 2026-09-07) ═══
 *
 * Y2-4'te matrise arama, bölüm çipleri ve yapışkan başlık ekledim.
 * Dört test yazdım: arama süzüyor mu, çip götürüyor mu, başlık
 * yapışık mı, belge kayıyor mu.
 *
 * DÖRDÜ DE YEŞİLDİ VE EKRAN YANLIŞ VERİ GÖSTERİYORDU.
 *
 * Çünkü hepsi KULLANILABİLİRLİĞİ sınıyordu, DOĞRULUĞU değil. Bir
 * tablonun kaydırılabilir olması, içindeki sayıların doğru olmasını
 * garanti etmiyor. Ders bu: yeni bir yetenek eklerken var olan
 * doğruluğu tutan bir kapı yoksa, o doğruluk sessizce gidebilir.
 *
 * ═══ NE ÖLÇÜYOR ═══
 *
 *   ekrandaki işaretli hücre sayısı == API'nin grants sayısı
 *
 * "Admin sütunu" özel: rol adı Admin olan sütun her zaman işaretli
 * gösteriliyor (kod bilerek öyle) ve grants'ta da tam liste var.
 * Sayım bu yüzden API tarafında da aynı kuralla yapılıyor.
 */

test.describe.configure({ timeout: 90_000 });

test("ekrandaki ✓ sayısı API'nin grants sayısına eşit", async ({ page }) => {
  const giris = await page.request.post("/api/auth/login", {
    data: {
      username: process.env.DUZEN_KULLANICI,
      password: process.env.DUZEN_PAROLA,
    },
  });
  expect(giris.status(), "Giriş").toBe(200);

  await page.setViewportSize({ width: 1366, height: 768 });
  await page.goto("/sistem-yonetimi/yetki-matrisi");
  await page.locator("table tbody tr").first().waitFor({ timeout: 60000 });

  const veri = await page.evaluate(async () => {
    const y = await fetch("/api/backend/user-management/permission-matrix");
    const g = (await y.json()) as {
      permissions: { key: string }[];
      roles: { id: string; name: string }[];
      grants: { roleId: string; permissionKey: string }[];
    };
    return g;
  });

  // POZİTİF KONTROL: veri gerçekten geldi ve büyük.
  expect(veri.permissions.length, "İzin listesi boş").toBeGreaterThan(100);
  expect(veri.roles.length, "Rol listesi boş").toBeGreaterThan(5);

  /*
   * BEKLENEN İŞARETLİ SAYISI.
   * Ekran Admin sütununu her zaman işaretli gösteriyor; API'de de
   * Admin'in tam listesi olmayabilir. Beklenen sayı EKRANIN kuralıyla
   * hesaplanıyor ki iki taraf aynı soruyu cevaplasın.
   */
  const grantKumesi = new Set(
    veri.grants.map((x) => `${x.roleId}::${x.permissionKey}`)
  );
  const adminRolleri = new Set(
    veri.roles.filter((r) => r.name === "Admin").map((r) => r.id)
  );

  let beklenen = 0;
  for (const rol of veri.roles) {
    for (const izin of veri.permissions) {
      if (adminRolleri.has(rol.id) || grantKumesi.has(`${rol.id}::${izin.key}`))
        beklenen += 1;
    }
  }

  const toplamHucre = veri.roles.length * veri.permissions.length;

  // Beklenen, TÜM hücrelerden az olmalı — yoksa test hiçbir şey ayırmaz.
  expect(
    beklenen,
    "Beklenen işaretli sayısı tüm hücrelere eşit; test kırık ekranı ayırt edemezdi"
  ).toBeLessThan(toplamHucre);

  const ekranda = await page.evaluate(() =>
    document.querySelectorAll("table tbody button.bg-emerald-500").length
  );

  expect(
    ekranda,
    `Ekranda ${ekranda} işaretli hücre var, olması gereken ${beklenen} ` +
      `(toplam hücre ${toplamHucre}). Ekran veriyle uyuşmuyor.`
  ).toBe(beklenen);

  /*
   * ═══ GÖRÜNEN ✓ SAYISI — SINIF SAYMAK YETMEZ ═══
   *
   * Üstteki iddia `bg-emerald-500` SINIFINI sayıyor. Ama işaretsiz
   * hücrede de ✓ karakteri DOM'da duruyor; yalnız `text-transparent`
   * ile görünmez kılınıyor. O sınıf bir gün uygulanmazsa ekranda
   * HER hücre işaretli GÖRÜNÜR ve sınıf sayan test bunu göremez.
   *
   * Mehmet'in bildirdiği belirti tam olarak buydu ("2145/2145").
   * Bu yüzden burada HESAPLANMIŞ RENK okunuyor: kullanıcının gerçekte
   * gördüğü şey.
   */
  const gorunenTik = await page.evaluate(() => {
    let n = 0;
    for (const b of document.querySelectorAll("table tbody button")) {
      const renk = getComputedStyle(b).color;
      const saydam =
        renk === "transparent" ||
        /rgba\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*0\s*\)/.test(renk);
      if (!saydam) n += 1;
    }
    return n;
  });

  expect(
    gorunenTik,
    `Kullanıcının GÖRDÜĞÜ ✓ sayısı ${gorunenTik}, olması gereken ${beklenen}. ` +
      "İşaretsiz hücrelerdeki ✓ gizlenmiyor olabilir (text-transparent)."
  ).toBe(beklenen);
});

test("iki rol için sütun başına tik sayısı grants ile birebir", async ({ page }) => {
  const giris = await page.request.post("/api/auth/login", {
    data: {
      username: process.env.DUZEN_KULLANICI,
      password: process.env.DUZEN_PAROLA,
    },
  });
  expect(giris.status()).toBe(200);

  await page.setViewportSize({ width: 1366, height: 768 });
  await page.goto("/sistem-yonetimi/yetki-matrisi");
  await page.locator("table tbody tr").first().waitFor({ timeout: 60000 });

  const veri = await page.evaluate(async () => {
    const y = await fetch("/api/backend/user-management/permission-matrix");
    return (await y.json()) as {
      permissions: { key: string }[];
      roles: { id: string; name: string }[];
      grants: { roleId: string; permissionKey: string }[];
    };
  });

  // Admin OLMAYAN, birbirinden FARKLI sayıda izne sahip iki rol seç.
  const sayim = new Map<string, number>();
  for (const g of veri.grants)
    sayim.set(g.roleId, (sayim.get(g.roleId) ?? 0) + 1);

  const adaylar = veri.roles
    .filter((r) => r.name !== "Admin" && (sayim.get(r.id) ?? 0) > 0)
    .sort((a, b) => (sayim.get(b.id) ?? 0) - (sayim.get(a.id) ?? 0));

  expect(adaylar.length, "Karşılaştırılacak rol yok").toBeGreaterThan(1);

  const secilen = [adaylar[0], adaylar[adaylar.length - 1]];

  // İKİ ROL FARKLI SAYIDA OLMALI: aynı olsalardı "hepsi eşit" hatası
  // yine yakalanmazdı.
  expect(
    sayim.get(secilen[0].id),
    "Seçilen iki rolün izin sayısı aynı; test ayırt edemezdi"
  ).not.toBe(sayim.get(secilen[1].id));

  for (const rol of secilen) {
    const sutun = veri.roles.findIndex((r) => r.id === rol.id);

    const ekranda = await page.evaluate((i) => {
      let n = 0;
      for (const tr of document.querySelectorAll("table tbody tr")) {
        const hucreler = tr.querySelectorAll("td");
        // İlk sütun izin adı; rol sütunları 1'den başlıyor.
        const td = hucreler[i + 1];
        if (!td) continue;
        if (td.querySelector("button.bg-emerald-500")) n += 1;
      }
      return n;
    }, sutun);

    expect(
      ekranda,
      `${rol.name}: ekranda ${ekranda} tik, grants ${sayim.get(rol.id)}`
    ).toBe(sayim.get(rol.id));
  }
});
