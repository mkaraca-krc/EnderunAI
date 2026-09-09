import { expect, test, type Page } from "@playwright/test";
import { PANEL_DAR_EKRAN_ESIGI } from "@/lib/mesajlasma/panel-esigi";

/**
 * PN4 — DAR VE ORTA GENİŞLİKTE YAZMA ALANI TAŞIYOR MU.
 *
 * Mehmet Bey'in ASIL şikayeti buydu ve uzun süre ÖLÇÜLEMEDİ: panel
 * açılmıyordu (PN1), sonra açıldı ama mesajlar gelmiyordu (PL1) ve
 * liste kısa kaldığı için taşma OLUŞMUYORDU. PL1 kapandı; ölçüm artık
 * mümkün.
 *
 * ÖLÇÜT İKİ TANE, AYRI RAPORLANIR:
 *   birincil : composer.bottom <= panel.bottom   (panelin İÇİNDE mi)
 *   ikincil  : composer.bottom <= innerHeight    (ekranın İÇİNDE mi)
 * İkisi ayrı şeydir: panel ekranın dışına taşmışsa composer panelin
 * içinde ama ekranın dışında olabilir.
 *
 * ÇOK MESAJLI KONUŞMA ŞART: liste dolmadan taşma üremez. Sonda
 * mesajları kendisi üretiyor — "rig'de az mesaj vardı" ile "taşma yok"
 * karışmasın (Kural 65).
 */

/*
 * ORTAK YARDIMCILAR MEVCUT SONDADAN AYNEN ALINDI.
 *
 * İlk yazımda giriş adımını HATIRLADIĞIM gibi yazdım (form doldurma,
 * `#username`) ve sonda 300 saniye bekleyip düştü — ölçüm hiç
 * yapılmadı. Rig girişi formdan değil `/api/auth/login` isteğinden
 * geçiyor ve ortam değişkenleri DUZEN_* adında. Çalışan bir yardımcı
 * varken yenisini uydurmak, bugün birkaç kez bedel ödetti.
 */
const KULLANICI = process.env.DUZEN_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;

/*
 * İKİ AYRI YÜZEY, ÇÜNKÜ ÜRÜN İKİYE AYIRIYOR — ÖLÇÜLDÜ.
 *
 * `PANEL_DAR_EKRAN_ESIGI = 900` (lib/mesajlasma/panel-esigi.ts:29).
 * 900'ün ALTINDA baloncuk paneli AÇMIYOR, doğrudan `/mesajlar` tam
 * sayfasına götürüyor (mesaj-baloncugu.tsx:370). Yani telefon
 * genişliğinde ortada panel YOK.
 *
 * İlk yazımda 390'da `form.mesaj-yaz` aranıp bulunamadı ve sonda
 * düştü. Bu bir taşma bulgusu DEĞİLDİ: aranan yüzey o genişlikte hiç
 * var olmuyordu. Seçicinin bulamaması ile elemanın olmaması ayrı
 * şeylerdir (Kural 48'in DOM tarafı).
 *
 * Dar ekranlar ATILMADI — Mehmet Bey'in şikayeti telefondan geliyordu.
 * Orada TAM SAYFA ölçülüyor ve yalnız İKİNCİL ölçüt (ekranın içinde
 * mi) anlamlı; "panelin içinde mi" sorusunun orada karşılığı yok.
 */
/*
 * MESAJ SAYISI DIŞARIDAN VERİLEBİLİR (PN4-2).
 * Varsayılan DOLU konuşma; `PN4_MESAJ=2` ile az mesajlı ayak koşar.
 */
const MESAJ_SAYISI = Number(process.env.PN4_MESAJ ?? "40");

/*
 * YÜKSEKLİK TARAMASI (PN4-5) — AYIRT EDİCİ ÖLÇÜM.
 *
 * NE ÖLÇTÜ — İKİ HİPOTEZ ÇÜRÜTÜLDÜ, MEKANİZMA BULUNDU.
 *
 * Sınanan hipotez (aritmetikten türetilmişti): panel `min()` ile
 * kısılırken iç kolon kısılmamış dalı kullanıyorsa
 * `taşma = max(0, 100vh - 45rem)` olmalıydı: 0 / 80 / 180 / 280.
 * Rakip hipotez `.mesaj-govde{max-height:70vh}`: ~30 / 93 / 150 / 220.
 *
 * ÖLÇÜLEN (düzeltmesiz kod, 41 mesaj): 80 / 80 / 80 / 80.
 * İkisi de çürüdü. Taşma YÜKSEKLİKTEN BAĞIMSIZ ve SABİT.
 *
 * Aynı koşuda ölçülen composer kutusu mekanizmayı verdi:
 *   composer h = 68, composer üst = panel alt + 12, her yükseklikte.
 * Yani taşma viewport aritmetiğinden değil, COMPOSER'IN KENDİ
 * KUTUSUNDAN geliyor: gövde kaydırılabilir olduğu için liste dolunca
 * composer bir BÜTÜN olarak kırpılan bölgenin dışına itiliyor.
 * (12 px'in kaynağı bulunamadı; ölçülmüş ama açıklanmamış bir sabit.)
 *
 * `globals.css`'te tam sayfa için yazılan kayıt aynı sayıyı taşıyor:
 * "taşma TAM 68 px, composer'ın kendi yüksekliği kadar".
 *
 * Genişlik SABİT tutuluyor: değişkeni tek tutmadan mekanizma
 * ayırt edilemez.
 *
 * `PN4_YUKSEKLIK=1` ile bu tarama koşar.
 */
const YUKSEKLIK_TARAMASI = [
  { ad: "1280x700", w: 1280, h: 700 },
  { ad: "1280x800", w: 1280, h: 800 },
  { ad: "1280x900", w: 1280, h: 900 },
  { ad: "1280x1000", w: 1280, h: 1000 },
];

const GENISLIK_TARAMASI = [
  { ad: "390x664 (tam sayfa)", w: 390, h: 664 },
  { ad: "420x760 (tam sayfa)", w: 420, h: 760 },
  { ad: "900x800", w: 900, h: 800 },
  { ad: "1024x800", w: 1024, h: 800 },
  { ad: "1152x800", w: 1152, h: 800 },
  { ad: "1280x800", w: 1280, h: 800 },
  { ad: "1440x800", w: 1440, h: 800 },
  { ad: "1536x800", w: 1536, h: 800 },
];

const OLCULER =
  process.env.PN4_YUKSEKLIK === "1" ? YUKSEKLIK_TARAMASI : GENISLIK_TARAMASI;

async function girisYap(sayfa: Page) {
  expect(KULLANICI, "DUZEN_KULLANICI yok — rig'i duzen-testi.sh ile koşturun").toBeTruthy();

  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: KULLANICI, password: PAROLA },
  });
  expect(yanit.status(), "Giriş: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

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

/** Konuşmayı DOLDURUR: taşma ancak dolu listede ürer. */
async function mesajDoldur(sayfa: Page, konusmaId: string, adet: number) {
  const yazilan = await sayfa.evaluate(
    async ([id, n]) => {
      let ok = 0;
      for (let i = 0; i < Number(n); i++) {
        const y = await fetch(`/api/backend/mesajlar/konusmalar/${id}/mesajlar`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ govde: `PN4 taşma ölçümü satırı ${i + 1}` }),
        });
        if (y.ok) ok++;
      }
      return ok;
    },
    [konusmaId, String(adet)] as const
  );

  // KURULUM ÖLÇÜLÜYOR: mesaj yazılamadıysa sonuç "taşma yok" değil,
  // ÖLÇEMEDİ'dir (Kural 67).
  expect(
    yazilan,
    `ÖLÇEMEDİ: ${adet} mesajın yalnız ${yazilan} tanesi yazılabildi; ` +
      "liste dolmadan taşma ölçülemez"
  ).toBe(adet);
}

/** Mevcut sondadan AYNEN alındı — ikinci bir kopya yazılmadı. */
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

/**
 * YAZMA ALANINI GÖRÜNÜR HÂLE GETİRİR — İKİ YÜZEY, İKİ YOL.
 *
 * ÖLÇÜLDÜ, ikisi de "eleman var ama gizli" diye çıktı:
 *   panel (>=900)      : iplik TERCİHTEN doğrudan açılıyor; konuşma
 *                        listesi gizli. Satır tıklamak imkânsız.
 *   tam sayfa (<900)   : önce LİSTE görünüyor; yazma alanı DOM'da var
 *                        ama gizli, iplik ancak seçimle açılıyor.
 *
 * Tek yolu dayatan bir sonda, ürünün yapmadığı bir şeyi ölçmeye
 * çalışır ve düşer. Bu yardımcı hangi görünümde olduğunu VARSAYMIYOR,
 * ölçüyor.
 */
async function yazmaAlaniniAc(page: Page) {
  const yazma = page.locator("form.mesaj-yaz").first();

  /*
   * BEKLEME 30 -> 90 SANİYE (PN4-3).
   *
   * ÖLÇÜLDÜ: 900 ve 1536'da panel AÇILIYORDU (panel=1) ama içi 30
   * saniyede dolmuyordu; iki genişlik ÖLÇEMEDİ kaldı. Rig'de 40
   * mesajlık konuşma + soğuk derleme, 30 saniyeyi zorluyor.
   *
   * Süreyi uzatmak kusuru gizlemez: taşma ölçümü YERLEŞİMİ ölçüyor,
   * HIZI değil. Yavaş yüklemeyi ayrı bir kusur olarak raporlamak
   * gerekirse ayrı sonda yazılır — bu sondanın sorusu o değil.
   */
  try {
    await yazma.waitFor({ state: "visible", timeout: 90_000 });
    return yazma;
  } catch {
    // Liste görünümünde olabiliriz.
  }

  /*
   * GERİ ÇEKİLME YALNIZ GÖRÜNÜR SATIR VARSA.
   *
   * Önceki yazımda 8 saniyede yazma alanı gelmeyince KOŞULSUZ satır
   * tıklamaya geçiyordu. Panel kipinde satırlar GİZLİ olduğu için o
   * dal hiç başarılı olamazdı ve sonda, yavaş yüklemeyi "liste
   * görünümü" sanıp 30 saniye gizli bir satırı bekliyordu.
   */
  const satir = page.locator("button.mesaj-satir").first();

  if (await satir.isVisible().catch(() => false)) {
    await satir.click();
    await yazma.waitFor({ state: "visible", timeout: 90_000 });
    return yazma;
  }

  throw new Error(
    "ÖLÇEMEDİ: yazma alanı görünmedi ve tıklanacak GÖRÜNÜR konuşma " +
      "satırı da yok. Taşma hakkında bu ölçüden sonuç çıkmaz."
  );
}

/*
 * PANELİ AÇ — TIKLAMADAN ÖNCE KENDİLİĞİNDEN AÇILMASINI BEKLE.
 *
 * ÖLÇÜLDÜ: bazı genişliklerde panel açık ama İÇİ BOŞ görünüyordu
 * (panel=1, satır=0, yazma=0). Sebep büyük olasılıkla YARIŞ: tercih
 * `messagePanelOpen: true` olduğu için panel kendiliğinden açılacak;
 * `isVisible()` o an henüz false dönerse sonda baloncuğa tıklıyor ve
 * AÇILMAKTA OLAN PANELİ KAPATIYOR. Bu, PANEL/1'in (a) ayağının
 * ölçtüğü yarışın sondadaki hâli — ve mevcut sondanın `paneliAc`
 * yorumunda yazılı olan tuzağın aynısı.
 *
 * Çözüm: önce kendiliğinden açılmasına SÜRE TANI, sonra tıkla.
 */
async function paneliAc(page: Page) {
  const panel = page.locator(".mesaj-panel");
  const baloncuk = page.locator("button.mesaj-baloncuk");
  await baloncuk.waitFor({ state: "visible", timeout: 60_000 });

  try {
    await panel.waitFor({ state: "visible", timeout: 15_000 });
    return panel;
  } catch {
    // Tercih paneli açmadı; kullanıcı gibi tıkla.
  }

  await baloncuk.click();
  await panel.waitFor({ state: "visible", timeout: 60_000 });
  return panel;
}

test("PN4: yazma alanı panelin ve ekranın içinde mi", async ({ page }) => {
  /*
   * TARAMA UZUN: 8 ölçü × (gezinme + panel + 40 mesajlık iplik).
   * 300 saniyede tarayıcı ortasında kapandı ve SON ÜÇ NOKTA
   * "browser has been closed" ile ölçülemedi — o ÖLÇEMEDİ'ler ürün
   * hakkında değil, sondanın süresi hakkındaydı.
   */
  test.setTimeout(1_200_000);

  await girisYap(page);

  /*
   * ÖNCE SAYFAYA GİT, SONRA SAYFA İÇİ `fetch`.
   *
   * `page.evaluate` içindeki göreli adres, sayfanın bir kökü yoksa
   * çözümlenemiyor ("Failed to parse URL"). Giriş isteği sayfayı
   * GEZDİRMİYOR; bu yüzden ilk `goto` burada şart.
   */
  await page.goto("/dashboard");
  const konusmaId = await ilkKonusmaId(page);
  /*
   * İKİ NOKTA, ÇÜNKÜ ÜRETEN DEĞİŞKEN MESAJ SAYISI (PN4-2).
   *
   * Tek noktalı bir sonda "kırmızı" der ama NEDEN kırmızı olduğunu
   * söylemez. Az mesajlı konuşmada taşma ÜREMEMELİ; çok mesajlıda
   * (düzeltme öncesi) ürüyordu. İkisi birlikte, taşmayı genişliğin
   * değil liste yüksekliğinin ürettiğini gösteriyor.
   */
  await mesajDoldur(page, konusmaId, MESAJ_SAYISI);

  /*
   * SEÇİLİ KONUŞMA TERCİHTEN GELİYOR — SATIR TIKLANMIYOR.
   *
   * ÖLÇÜLDÜ: 900'de `button.mesaj-satir` DOM'da VAR ama GİZLİ (62 kez
   * "hidden"). Panel, seçili konuşmanın ipliğini gösterip listeyi
   * gizliyor — PL1 düzeltmesinin davranışı bu. Satır tıklamaya
   * çalışmak, ürünün yapmadığı bir yolu zorlamaktı.
   */
  await tercihleriKur(page, { panelAcik: true, sonKonusmaId: konusmaId });

  /*
   * KAÇ MESAJ OLDUĞU VARSAYILMIYOR, ÖLÇÜLÜYOR.
   *
   * `mesajDoldur` EKLER; rig'in kendi tohumu da mesaj içeriyor ve
   * turlar arası birikme ihtimali vardı. "2 mesajla ölçtüm" demeden
   * önce konuşmada gerçekten kaç mesaj olduğunu saymak gerek —
   * yoksa iki nokta arasındaki fark, sandığım değişkenden değil
   * başka bir şeyden gelebilir (Kural 65).
   */
  const gercekMesajSayisi = await page.evaluate(async (id) => {
    const y = await fetch(
      `/api/backend/mesajlar/konusmalar/${id}/mesajlar?limit=500`,
      { cache: "no-store" }
    );
    if (!y.ok) return -1;
    const g = (await y.json()) as { kayitlar?: unknown[] };
    return g.kayitlar?.length ?? -1;
  }, konusmaId);

  const panel = page.locator(".mesaj-panel");

  type Satir = {
    ad: string;
    olcemedi?: string;
    panelAlt: number | null;
    yazmaAlt: number;
    ekranYuksek: number;
    panelIcinde: boolean | null;
    ekranIcinde: boolean;
  };
  const satirlar: Satir[] = [];

  for (const olcu of OLCULER) {
    const panelliMi = olcu.w >= PANEL_DAR_EKRAN_ESIGI;

    /*
     * NOKTA BAŞINA SONUÇ — TARAMA TEK NOKTADA İPTAL OLMAZ.
     *
     * Önceki yazımda ölçülemeyen ilk genişlik bütün taramayı
     * düşürüyordu ve ÖLÇÜLEBİLEN genişlikler de rapora giremiyordu.
     * Bir tarama, ölçemediği noktayı ATLAMAZ ve GİZLEMEZ — o noktayı
     * "ÖLÇEMEDİ" diye yazar, ötekileri ölçmeye devam eder (Kural 67).
     */
    try {

    await page.setViewportSize({ width: olcu.w, height: olcu.h });

    if (panelliMi) {
      await page.goto("/dashboard");
      await paneliAc(page);
    } else {
      // Panel yok: ürün bu genişlikte tam sayfaya gidiyor.
      await page.goto("/mesajlar");
    }

    /*
     * TEŞHİS ÇIKTISI — SEÇİCİYE POZİTİF KONTROL.
     *
     * "Eleman yok" ile "aradığım adla eleman yok" ayrı şeyler. Hangi
     * ölçüde neyin var olduğunu ölçmeden sonuç yazmıyoruz.
     */
    console.log(
      `[teşhis] ${olcu.ad.padEnd(20)} url=${page.url()}` +
        ` panel=${await page.locator(".mesaj-panel").count()}` +
        ` satır=${await page.locator("button.mesaj-satir").count()}` +
        ` yazma=${await page.locator("form.mesaj-yaz").count()}`
    );

    const yazma = await yazmaAlaniniAc(page);
    await page.waitForTimeout(600); // yerleşim otursun

    const panelKutu = panelliMi ? await panel.boundingBox() : null;
    const yazmaKutu = await yazma.boundingBox();

    /*
     * DOM POZİTİF KONTROLÜ (Kural 48'in DOM tarafı).
     *
     * Boş kutu "taşma yok" DEĞİLDİR — seçici tutmamış ya da eleman
     * gizli demektir. Bu ayrım yapılmazsa sonda, ölçmediği bir şeyi
     * yeşil raporlar.
     */
    if (panelliMi) {
      expect(
        panelKutu && panelKutu.height > 0,
        `ÖLÇEMEDİ (${olcu.ad}): .mesaj-panel kutusu yok/sıfır`
      ).toBeTruthy();
    }
    expect(
      yazmaKutu && yazmaKutu.height > 0,
      `ÖLÇEMEDİ (${olcu.ad}): form.mesaj-yaz kutusu yok/sıfır`
    ).toBeTruthy();

    const panelAlt = panelKutu ? panelKutu.y + panelKutu.height : null;
    const yazmaAlt = yazmaKutu!.y + yazmaKutu!.height;

    satirlar.push({
      ad: olcu.ad,
      panelAlt: panelAlt === null ? null : Math.round(panelAlt),
      yazmaAlt: Math.round(yazmaAlt),
      ekranYuksek: olcu.h,
      // Panel yoksa birincil ölçütün karşılığı yok — "geçti" demiyoruz,
      // "uygulanmıyor" diyoruz.
      panelIcinde: panelAlt === null ? null : yazmaAlt <= panelAlt + 1,
      ekranIcinde: yazmaAlt <= olcu.h + 1,
    });

    if (panelAlt !== null) {
      const tasma = Math.max(0, Math.round(yazmaAlt - panelAlt));
      console.log(
        `[taşma] ${olcu.ad.padEnd(12)} ${String(tasma).padStart(4)} px` +
          `  ·  composer h=${Math.round(yazmaKutu!.height)}` +
          `  ·  composer üst=${Math.round(yazmaKutu!.y)}` +
          `  ·  panel alt=${Math.round(panelAlt)}`
      );
    }
    } catch (hata) {
      satirlar.push({
        ad: olcu.ad,
        olcemedi: hata instanceof Error ? hata.message.slice(0, 120) : String(hata),
        panelAlt: null,
        yazmaAlt: -1,
        ekranYuksek: olcu.h,
        panelIcinde: null,
        ekranIcinde: false,
      });
    }
  }

  console.log(
    `\n=== PN4 ÖLÇÜM TABLOSU ` +
      `(eklenen ${MESAJ_SAYISI}, konuşmada ÖLÇÜLEN ${gercekMesajSayisi} mesaj) ===`
  );
  console.log(
    "ölçü".padEnd(20) +
      "panel.alt".padStart(11) +
      "yazma.alt".padStart(11) +
      "ekran.h".padStart(9) +
      "  panelİçinde  ekranİçinde"
  );
  for (const s of satirlar) {
    if (s.olcemedi) {
      console.log(s.ad.padEnd(20) + "   ÖLÇEMEDİ — " + s.olcemedi);
      continue;
    }
    console.log(
      s.ad.padEnd(20) +
        String(s.panelAlt ?? "—").padStart(11) +
        String(s.yazmaAlt).padStart(11) +
        String(s.ekranYuksek).padStart(9) +
        (s.panelIcinde === null
          ? "     panel yok"
          : s.panelIcinde
            ? "         EVET"
            : "        HAYIR") +
        (s.ekranIcinde ? "        EVET" : "       HAYIR")
    );
  }

  const olculen = satirlar.filter((s) => !s.olcemedi);
  const olcemeyen = satirlar.filter((s) => s.olcemedi).map((s) => s.ad);
  const panelTasan = olculen
    .filter((s) => s.panelIcinde === false)
    .map((s) => s.ad);
  const ekranTasan = olculen.filter((s) => !s.ekranIcinde).map((s) => s.ad);

  console.log(
    `\nÖLÇÜLEN ${olculen.length}/${satirlar.length}` +
      (olcemeyen.length ? `  ·  ÖLÇEMEDİ: ${olcemeyen.join(", ")}` : "")
  );
  console.log(`\nBİRİNCİL (panelin içinde): taşan ${panelTasan.length}/${satirlar.length}  ${panelTasan.join(", ")}`);
  console.log(`İKİNCİL (ekranın içinde) : taşan ${ekranTasan.length}/${satirlar.length}  ${ekranTasan.join(", ")}`);

  /*
   * ÖLÇEMEDİ KALAN NOKTA VARSA SONDA YEŞİL SAYILMAZ.
   *
   * "Ölçebildiklerim temiz" ile "temiz" aynı şey değil. Ölçülemeyen
   * genişlik, taşmanın orada olmadığının kanıtı değildir (Kural 48).
   */
  expect(
    olcemeyen,
    `ÖLÇEMEDİ kalan genişlik(ler): ${olcemeyen.join(", ")} — ` +
      "bu noktalarda taşma hakkında sonuç YOK"
  ).toEqual([]);

  expect(panelTasan, `BİRİNCİL ÖLÇÜT DÜŞTÜ: ${panelTasan.join(", ")}`).toEqual([]);
  expect(ekranTasan, `İKİNCİL ÖLÇÜT DÜŞTÜ: ${ekranTasan.join(", ")}`).toEqual([]);
});
