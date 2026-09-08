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

/**
 * KB1 — İKİNCİ KATMANIN KENDİ TESTİ.
 *
 * ═══ BU KORUMA FAZLALIK DEĞİLDİR ═══
 *
 * Yarın koda bakan biri şöyle diyecek: "`api-client` zaten 401'de
 * giriş ekranındayken yönlendirmiyor; `mesaj-baloncugu`ndaki
 * `if (oturumYukleniyor || !user) return;` fazlalık." HAKLI GÖRÜNECEK
 * VE YANILACAK.
 *
 * İki katman FARKLI hataları kapatıyor:
 *   api-client düzeltmesi → 401 DÖNGÜSÜNÜ keser.
 *   baloncuk koruması     → çağrının HİÇ YAPILMAMASINI sağlar; yani
 *                           401 dışındaki her hatayı da kapsar —
 *                           503, ağ kopması, zaman aşımı.
 *
 * 2026-09-08'de canlıda ölçülen 503 yeşili TAMAMEN ikinci katmana
 * dayanıyordu: arka uç 503 verirken sayfa 10 saniyede 0 istek attı.
 * Birinci katman tek başına 503'te ne yapardı — BİLMİYORUZ, ölçmedik.
 *
 * Bu test o katmanı kaldıran kişiyi durduracak tek şey. Koruma var ama
 * neyi koruduğu yazılı değilse ilk temizlikte gider.
 *
 * ═══ KB3 — ÖLÇÜT KÖK DÜZENDEKİ HER BİLEŞEN İÇİN ═══
 *
 * İddia tek bir bileşen hakkında değil: OTURUM YOKKEN KÖK DÜZENDEKİ
 * HİÇBİR BİLEŞEN ARKA UCA İSTEK ATMAMALI. Ölçüldü (2026-09-08):
 * kök düzende dört bileşen var ve yalnız `MesajBaloncugu` ağ isteği
 * yapıyor (3 çağrı); `TaslakDeposuSaglayici`,
 * `ServiceWorkerRegistration` ve `HataSiniri` sıfır.
 *
 * Yarın köke ağ isteği atan bir bileşen eklenirse bu test düşer —
 * hangi bileşen olduğu fark etmeksizin.
 */
test("oturumsuz giriş ekranında kök düzen hiçbir arka uç isteği atmıyor", async ({
  page,
}) => {
  const arkaUcCagrilari: string[] = [];
  const tumIstekler: string[] = [];

  page.on("request", (istek) => {
    tumIstekler.push(istek.url());
    if (istek.url().includes("/api/backend/")) {
      arkaUcCagrilari.push(new URL(istek.url()).pathname);
    }
  });

  await page.context().clearCookies();
  await page.goto("/login");

  const kullaniciAlani = page.locator('input[autocomplete="username"]');
  await kullaniciAlani.first().waitFor({ timeout: 15_000 });

  // Baloncuğun tercih okuması gecikmeli olabilir; bekleyip öyle sayıyoruz.
  await page.waitForTimeout(5_000);

  // POZİTİF KONTROL: dinleyici gerçekten istek görüyor mu. Sıfır istek
  // sayılsaydı "arka uç çağrısı yok" sonucu dinleyicinin ölü olmasından
  // da gelebilirdi (Kural 48).
  expect(
    tumIstekler.length,
    "Hiç istek sayılmadı; dinleyici çalışmıyor demektir"
  ).toBeGreaterThan(0);

  expect(
    arkaUcCagrilari.length,
    `Oturum yokken kök düzen ${arkaUcCagrilari.length} arka uç isteği ` +
      `attı: ${arkaUcCagrilari.join(", ")}. Sıfır olmalı — bu istekler ` +
      `401 (ya da arka uç düştüyse 503) alır ve GİRİŞ-DÖNGÜ/1'in ` +
      `zeminini geri getirir.`
  ).toBe(0);

  // Adıyla da söylensin: bu ikisi döngüyü besleyen çağrılardı.
  expect(arkaUcCagrilari.filter((y) => y.includes("auth/me"))).toHaveLength(0);
  expect(
    arkaUcCagrilari.filter((y) => y.includes("user-preferences"))
  ).toHaveLength(0);
});
