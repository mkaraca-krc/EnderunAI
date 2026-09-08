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
