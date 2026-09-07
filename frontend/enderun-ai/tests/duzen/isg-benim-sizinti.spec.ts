import { expect, test, type Page } from "@playwright/test";

/**
 * /isg/benim — BAŞKASININ KAYDINI GÖSTEREBİLİYOR MU (KARAR 2).
 *
 * ═══ NEDEN VARSAYMIYORUM ═══
 *
 * "Kendi kaydı" bir VARSAYIM; kaynakta öyle yazması yetmez (Kural 70).
 * Uç gerçekten çağrılıyor, iki AYRI kullanıcıyla, ve dönen kayıtların
 * BİRBİRİNDEN FARKLI ve HER BİRİNİN ÇAĞIRANA AİT olduğu ölçülüyor.
 *
 * POZİTİF KONTROL ŞART: iki kullanıcının da personel kaydı OLMASI
 * gerekiyor. Kaydı olmayan iki kullanıcı da 404 alır ve test "sızıntı
 * yok" der — hiçbir şey kanıtlamadan (Kural 48). Rig bu yüzden iki
 * personel kaydı tohumluyor.
 *
 * AYRICA PARAMETRE DENENİYOR: uç kaynakta parametre almıyor ama
 * sorgu dizesiyle kimlik geçirilebiliyorsa bu bir sızıntıdır.
 */

const A = process.env.DUZEN_KULLANICI;
const B = process.env.DUZEN_KARSI_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;

async function girisVeKart(sayfa: Page, kullanici: string, ek = "") {
  const giris = await sayfa.request.post("/api/auth/login", {
    data: { username: kullanici, password: PAROLA },
  });
  expect(giris.status(), `${kullanici} girişi`).toBe(200);

  await sayfa.goto("/parola");

  return sayfa.evaluate(async (sorgu) => {
    const y = await fetch("/api/backend/isg/benim" + sorgu);
    return { durum: y.status, govde: (await y.text()).slice(0, 600) };
  }, ek);
}

test("her kullanıcı YALNIZ kendi İSG kartını görüyor", async ({ browser }) => {
  expect(A, "DUZEN_KULLANICI yok").toBeTruthy();
  expect(B, "DUZEN_KARSI_KULLANICI yok").toBeTruthy();

  const baglamA = await browser.newContext({ baseURL: process.env.DUZEN_URL });
  const baglamB = await browser.newContext({ baseURL: process.env.DUZEN_URL });

  try {
    const sayfaA = await baglamA.newPage();
    const sayfaB = await baglamB.newPage();

    const kartA = await girisVeKart(sayfaA, A!);
    const kartB = await girisVeKart(sayfaB, B!);

    // POZİTİF KONTROL: ikisi de gerçekten kart aldı. 404 alsalardı
    // "sızıntı yok" sonucu boş bir iddia olurdu.
    expect(kartA.durum, "A kart alamadı: " + kartA.govde).toBe(200);
    expect(kartB.durum, "B kart alamadı: " + kartB.govde).toBe(200);

    // Kartlar BİRBİRİNDEN FARKLI olmalı.
    expect(
      kartA.govde,
      "İki kullanıcı AYNI kartı aldı — kimlik oturumdan çözülmüyor olabilir"
    ).not.toBe(kartB.govde);

    // Her kart ÇAĞIRANA ait olmalı. Rig 'BirinciKisi' / 'IkinciKisi'
    // adlarıyla tohumluyor; ad karşı tarafınkini içeriyorsa sızıntı var.
    expect(kartA.govde, "A, B'nin kaydını gördü — SIZINTI").not.toContain("IkinciKisi");
    expect(kartB.govde, "B, A'nın kaydını gördü — SIZINTI").not.toContain("BirinciKisi");
    expect(kartA.govde).toContain("BirinciKisi");
    expect(kartB.govde).toContain("IkinciKisi");
  } finally {
    await baglamA.close();
    await baglamB.close();
  }
});

test("sorgu dizesiyle başkasının kimliği geçirilemiyor", async ({ browser }) => {
  const baglam = await browser.newContext({ baseURL: process.env.DUZEN_URL });

  try {
    const sayfa = await baglam.newPage();

    // Önce B'nin personel kimliğini öğren (B olarak giriş yaparak).
    const kartB = await girisVeKart(sayfa, B!);
    expect(kartB.durum).toBe(200);

    const eslesme = /"personnelId"\s*:\s*"([0-9a-f-]+)"/i.exec(kartB.govde);
    const bKimlik = eslesme?.[1];

    // POZİTİF KONTROL: kimliği gerçekten bulduk, yoksa test boşa döner.
    expect(bKimlik, "B'nin personel kimliği yanıtta yok; deneme anlamsız olurdu").toBeTruthy();

    // Şimdi A olarak, B'nin kimliğini sorgu dizesiyle geçirmeyi dene.
    const sayfa2 = await baglam.newPage();
    for (const ad of ["personnelId", "personelId", "id", "userId"]) {
      const sonuc = await girisVeKart(sayfa2, A!, `?${ad}=${bKimlik}`);
      expect(
        sonuc.govde,
        `${ad} parametresiyle B'nin kaydı döndü — SIZINTI`
      ).not.toContain("IkinciKisi");
    }
  } finally {
    await baglam.close();
  }
});
