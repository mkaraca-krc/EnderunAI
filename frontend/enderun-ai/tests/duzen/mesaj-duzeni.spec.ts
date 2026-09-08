import { expect, test, type Page } from "@playwright/test";

/**
 * MESAJ EKRANI DÜZENİ — COMPOSER GÖRÜNEN ALANIN İÇİNDE (MESAJ/3 A2).
 *
 * ═══ ÖLÇÜLEN KUSUR (Mehmet, üretimde, 2026-09-07) ═══
 *
 *   2560x1215 → composer alt kenarı 1124 ≤ 1215  (91 px içeride)
 *    424x531  → composer alt kenarı  587 > 531   (56 px DIŞARIDA)
 *
 * Composer kaybolmuyor; mesaj listesinin yükseklik sınırı olmadığı
 * için BELGE uzuyor (989 px) ve composer katlanmanın altına düşüyor.
 *
 * ═══ NEDEN GERÇEK TARAYICI ═══
 *
 * Bu iddia grep ile geçilemez (Kural 70) ve jsdom ile de ölçülemez:
 * jsdom'da düzen motoru yok, `getBoundingClientRect` sıfır döner.
 * Yığını `deploy/scripts/duzen-testi.sh` kuruyor.
 */

const GORUNUMLER = [
  { ad: "telefon", width: 390, height: 664 },
  { ad: "tablet", width: 768, height: 900 },
  { ad: "masaustu", width: 1920, height: 1080 },
];

async function girisYap(sayfa: Page) {
  const kullanici = process.env.DUZEN_KULLANICI;
  const parola = process.env.DUZEN_PAROLA;

  // FAIL-CLOSED: kimlik yoksa test ATLANMAZ, düşer. "Ölçemedim" ile
  // "ölçtüm, sorun yok" aynı görünmemeli.
  expect(kullanici, "DUZEN_KULLANICI tanımlı değil — rig'i duzen-testi.sh ile koşturun").toBeTruthy();
  expect(parola, "DUZEN_PAROLA tanımlı değil").toBeTruthy();

  /*
   * GİRİŞ FORM ÜZERİNDEN DEĞİL, UÇ ÜZERİNDEN.
   *
   * Bu dosyanın konusu DÜZEN, giriş formu değil. Form üzerinden
   * denendiğinde giriş sessizce başarısız oluyordu ve ekranda hata
   * metni bile yoktu — hata "buton tıklanamadı" gibi görünüyordu ve
   * asıl sebep hiç okunamıyordu.
   *
   * Uç çağrısı `page.request` ile yapılıyor: aynı tarayıcı bağlamının
   * çerez kavanozunu kullanıyor, yani dönen `enderun_token` çerezi
   * sonraki gezinmelerde geçerli. Ve durum kodu GÖRÜNÜR oluyor.
   */
  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: kullanici, password: parola },
  });

  expect(
    yanit.status(),
    "Giriş ucu 200 dönmedi. Gövde: " + (await yanit.text()).slice(0, 300)
  ).toBe(200);

}

async function konusmayiAc(sayfa: Page) {
  await sayfa.goto("/mesajlar");

  /*
   * DOLU KONUŞMA SIRAYA GÖRE DEĞİL, ÖLÇÜMLE SEÇİLİYOR.
   *
   * Önce `.first()` tıklanıyordu ve tek başına koşarken çalışıyordu.
   * Ses testleriyle birlikte koşunca DÜŞTÜ: onlar mesaj gönderip
   * konuşma sıralamasını değiştiriyor ve "ilk satır" artık tek
   * mesajlık konuşma oluyordu. Testin doğruluğu, yanında hangi
   * testin koştuğuna bağlı olamaz.
   *
   * Liste `LastMessageAtUtc` azalan sıralı; uçtan alınan sıra
   * ekrandaki sırayla aynı. Mesajı en çok olan konuşmanın DİZİNİ
   * bulunup o satır tıklanıyor.
   */
  const dizin = await sayfa.evaluate(async () => {
    const liste = await fetch("/api/backend/mesajlar/konusmalar?limit=30");
    const govde = (await liste.json()) as { kayitlar: { id: string }[] };
    const kayitlar = govde.kayitlar ?? [];

    let enIyi = 0;
    let enCok = -1;

    for (let i = 0; i < kayitlar.length; i += 1) {
      const y = await fetch(
        `/api/backend/mesajlar/konusmalar/${kayitlar[i].id}/mesajlar?limit=50`
      );
      const m = (await y.json()) as { kayitlar: unknown[] };
      const sayi = (m.kayitlar ?? []).length;
      if (sayi > enCok) {
        enCok = sayi;
        enIyi = i;
      }
    }

    return enIyi;
  });

  const satir = sayfa.locator(".mesaj-satir").nth(dizin);
  await satir.waitFor({ timeout: 20000 });
  await satir.click();

  // Composer görünene kadar bekle — ölçülecek şey o.
  await sayfa.locator("form.mesaj-yaz input[type=text]").waitFor({ timeout: 20000 });

  /*
   * MESAJLARIN GELMESİ BEKLENİYOR — YARIŞ KOŞULU ÖLÇÜLDÜ.
   *
   * Composer, konuşma seçilir seçilmez render ediliyor; mesajlar ise
   * ASENKRON geliyor. Sayımı hemen yapmak üç görünümden ikisinde 0
   * verdi, birinde vermedi — yani test rastgele yeşil/kırmızı
   * oluyordu. Önce ilk mesaj bekleniyor.
   */
  await sayfa.locator(".mesaj-akis .mesaj").first().waitFor({ timeout: 20000 });

  // POZİTİF KONTROL: akış gerçekten dolu. Boş bir akışta taşma hiç
  // oluşmaz ve test kırık düzende de yeşil verirdi.
  const mesajSayisi = await sayfa.locator(".mesaj-akis .mesaj").count();
  expect(
    mesajSayisi,
    "Tohumlanan mesajlar görünmüyor — test kırık düzeni yakalayamaz"
  ).toBeGreaterThan(10);
}

for (const gorunum of GORUNUMLER) {
  test(`${gorunum.ad} ${gorunum.width}x${gorunum.height}: composer görünen alanda, belge kaymıyor`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: gorunum.width, height: gorunum.height });
    await girisYap(page);
    await konusmayiAc(page);

    const kutu = await page.locator("form.mesaj-yaz").boundingBox();
    expect(kutu, "Composer kutusu ölçülemedi").not.toBeNull();

    const alt = kutu!.y + kutu!.height;

    expect(
      Math.round(alt),
      `Composer alt kenarı ${Math.round(alt)} px, görünen alan ${gorunum.height} px — `
        + `${Math.round(alt - gorunum.height)} px DIŞARIDA.`
    ).toBeLessThanOrEqual(gorunum.height);

    const belge = await page.evaluate(() => ({
      scrollHeight: document.documentElement.scrollHeight,
      clientHeight: document.documentElement.clientHeight,
    }));

    expect(
      belge.scrollHeight,
      `Belge kayıyor: scrollHeight ${belge.scrollHeight} > clientHeight ${belge.clientHeight}. `
        + "Kayması gereken tek şey mesaj listesi."
    ).toBeLessThanOrEqual(belge.clientHeight);
  });
}

/**
 * KAYAN ŞEY MESAJ LİSTESİ OLMALI.
 *
 * Üstteki testler "belge kaymıyor" diyor. Bu tek başına, mesaj akışı
 * da kaymıyorsa (yani içerik kırpılmışsa) sağlanırdı — kullanıcı eski
 * mesajlara hiç ulaşamazdı ve test yine yeşil kalırdı.
 */
test("mesaj akışı kendi içinde kayar", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 664 });
  await girisYap(page);
  await konusmayiAc(page);

  const akis = await page.locator(".mesaj-akis").evaluate((el) => ({
    scrollHeight: el.scrollHeight,
    clientHeight: el.clientHeight,
  }));

  expect(
    akis.scrollHeight,
    "Mesaj akışı kendi içinde kaymıyor — içerik kırpılmış olabilir."
  ).toBeGreaterThan(akis.clientHeight);
});
