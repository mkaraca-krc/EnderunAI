import { expect, test } from "@playwright/test";

/**
 * GİRİŞ-DÖNGÜ/1 · GD4 — GİRİŞ EKRANI KENDİNİ YENİDEN YÜKLEMİYOR.
 *
 * ═══ ÖLÇÜLEN KUSUR (2026-09-08, CANLI) ═══
 *
 * Giriş ekranı sonsuz yeniden yükleme döngüsüne giriyordu. Canlı
 * nginx kaydından ölçüldü, TEK tarayıcı:
 *
 *   60 saniyede toplam istek      : 1256   (~21 istek/sn)
 *   60 saniyede /login yüklemesi  :  358   (~6 sayfa/sn)
 *   konsol hatası                 :    0   (her yükleme konsolu siliyordu)
 *
 * ZİNCİR: kök layout `MesajBaloncugu`u HER rotada monte ediyor →
 * baloncuk `auth/me` ve `user-preferences` çağırıyor → oturum yokken
 * 401 → `api-client` `/login`e yönlendiriyor → sayfa baştan yükleniyor
 * → aynı iki istek → başa dön.
 *
 * Baloncuğun `if (!user) return null` koruması ARAYÜZÜ gizliyordu ama
 * `useEffect` render'ın dönüşünden bağımsız koşar; istekler yine
 * gidiyordu.
 *
 * ═══ BU TEST NE ÖLÇÜYOR ═══
 *
 * Oturumsuz `/login` açılıyor ve 10 saniye bekleniyor. Belge (document)
 * isteği sayısı 1 olmalı. Kusurlu sürümde bu sayı 50'yi aşıyordu.
 *
 * NEDEN İSTEK SAYIYORUZ, KONSOL DEĞİL: konsol sessizdi. Her tam
 * yükleme konsolu temizlediği için hata görünmüyordu. Kusuru yalnız
 * ağ kaydı gösterdi — test de aynı yerden bakıyor.
 */
test.describe.configure({ timeout: 60_000 });

test("oturumsuz giriş ekranı 10 saniyede kendini yeniden yüklemiyor", async ({
  page,
}) => {
  const belgeIstekleri: string[] = [];

  page.on("request", (istek) => {
    if (istek.resourceType() === "document" && istek.url().includes("/login")) {
      belgeIstekleri.push(istek.url());
    }
  });

  // Oturum YOK: çerez temizleniyor ki 401 yolu gerçekten koşsun.
  await page.context().clearCookies();

  await page.goto("/login");

  /*
   * SEÇİCİ `autocomplete` ÜZERİNDEN.
   *
   * İlk yazımda `input[name='username'], input#username` kullandım ve
   * test 15 saniye bekleyip düştü. Kusur düzeltmede değil SEÇİCİDEYDİ:
   * giriş formundaki alanların `name`i de `id`si de YOK, yalnız
   * `autoComplete` var. Ölçtüğümü sandığım şey ile gerçekten
   * ölçtüğüm şey ayrışmıştı (Kural 65).
   *
   * Etikete göre aramadım: Türkçe "İ" ile erişilebilir ad eşleşmesi
   * bu projede daha önce sorun çıkardı.
   */
  const kullaniciAlani = page.locator('input[autocomplete="username"]');
  await kullaniciAlani.first().waitFor({ timeout: 15_000 });

  // POZİTİF KONTROL: sayfa gerçekten yüklendi ve en az bir belge
  // isteği sayıldı. Sıfır olsaydı test hiçbir şey ölçmemiş olurdu.
  expect(
    belgeIstekleri.length,
    "Hiç belge isteği sayılmadı; dinleyici yanlış yere bakıyor"
  ).toBeGreaterThan(0);

  await page.waitForTimeout(10_000);

  expect(
    belgeIstekleri.length,
    `10 saniyede ${belgeIstekleri.length} kez /login belgesi istendi. ` +
      `1 olmalı — ekran kendini yeniden yüklüyor.`
  ).toBe(1);

  // VE ekran hâlâ kullanılabilir durumda: giriş alanı yerinde.
  await expect(kullaniciAlani.first()).toBeVisible();
});

/**
 * GD4 · ÜÇÜNCÜ AYAK — ARKA UÇ 503 DÖNERKEN DE DÖNGÜ YOK.
 *
 * ═══ ÖNEMLİ AYRIM (Mehmet, 2026-09-08, canlı ölçüm) ═══
 *
 * Sayfa 503'ü ZARİFÇE KARŞILAMIYOR — 503'e yol açan çağrıyı HİÇ
 * YAPMIYOR. İkinci katman devrede: baloncuk oturum yokken
 * `user-preferences` isteğini atmıyor, `useCurrentUser` de öyle.
 *
 * YAPILMAYAN ÇAĞRININ HATASINDA DÖNGÜ OLAMAZ. Bu, "hatayı iyi
 * karşıla"dan daha sağlam bir sonuç: iyi karşılama kodu bir gün
 * bozulabilir, hiç yapılmayan çağrı bozulamaz.
 *
 * Canlı ölçüm (yayın koşarken, arka uç 503 verirken, 10 saniye):
 *   sayfanın kendi attığı istek : 0
 *   belge yüklemesi             : 1
 *   pozitif kontrol             : elle 3 istek, izleyici 3'ünü de gördü
 * Karşılaştırma: bir gün önce aynı koşulda 10 saniyede 862 istek.
 *
 * ═══ POZİTİF KONTROL NEDEN BÖYLE ═══
 *
 * Taklit sunucu HİÇ ÇAĞRILMAZSA test totoloji olurdu: "çağrı yok"
 * hem düzeltmenin çalıştığı hem taklidin kurulmadığı durumda doğru.
 * Bu yüzden testin sonunda BİLEREK bir çağrı yapılıyor ve 503
 * döndüğü doğrulanıyor — taklidin canlı olduğunun kanıtı.
 */
test("arka uç 503 dönerken giriş ekranı kendini yeniden yüklemiyor", async ({
  page,
}) => {
  const belgeIstekleri: string[] = [];
  const arkaUcCagrilari: string[] = [];

  page.on("request", (istek) => {
    if (istek.resourceType() === "document" && istek.url().includes("/login")) {
      belgeIstekleri.push(istek.url());
    }
    if (istek.url().includes("/api/backend/")) {
      arkaUcCagrilari.push(istek.url());
    }
  });

  // ARKA UÇ 503 TAKLİDİ.
  await page.route("**/api/backend/**", (route) =>
    route.fulfill({
      status: 503,
      contentType: "application/json",
      body: JSON.stringify({ message: "Sunucu şu an hizmet veremiyor." }),
    })
  );

  await page.context().clearCookies();
  await page.goto("/login");

  const kullaniciAlani = page.locator('input[autocomplete="username"]');
  await kullaniciAlani.first().waitFor({ timeout: 15_000 });

  await page.waitForTimeout(10_000);

  expect(
    belgeIstekleri.length,
    `Arka uç 503 dönerken 10 saniyede ${belgeIstekleri.length} kez /login ` +
      `belgesi istendi. 1 olmalı.`
  ).toBe(1);

  // ASIL BULGU: sayfa 503'e yol açan çağrıyı HİÇ YAPMIYOR.
  expect(
    arkaUcCagrilari.length,
    `Giriş ekranı oturumsuzken ${arkaUcCagrilari.length} arka uç çağrısı ` +
      `yaptı: ${arkaUcCagrilari.join(", ")}. Sıfır olmalı.`
  ).toBe(0);

  // POZİTİF KONTROL: taklit gerçekten canlı mı.
  const sondaDurumu = await page.evaluate(async () => {
    const y = await fetch("/api/backend/auth/me", { cache: "no-store" });
    return y.status;
  });

  expect(
    sondaDurumu,
    "503 taklidi kurulmamış — yukarıdaki 'çağrı yok' sonucu totoloji olurdu"
  ).toBe(503);

  // Ekran hâlâ kullanılabilir.
  await expect(kullaniciAlani.first()).toBeVisible();
});
