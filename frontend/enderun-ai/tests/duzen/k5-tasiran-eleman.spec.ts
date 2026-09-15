import { expect, test, type Page } from "@playwright/test";

/**
 * K5 — 390 px'TE SAYFAYI TAŞIRAN ELEMANI BULUR (ölçüm, düzeltme değil).
 *
 * ═══ AYRIM ÖLÇÜYE ÇEVRİLDİ ═══
 *
 *   SAYFANIN yatay kayması            = KUSUR
 *   Tablonun KENDİ KABINDA kayması    = doğru tasarım, kusur DEĞİL
 *
 * 12 sütunlu bir tabloyu 390 px'e sıkıştırmak onu okunmaz yapar;
 * doğru düzeltme `overflow-x:auto` bir kaptır. Bu yüzden sonda her
 * taşan eleman için AYRICA soruyor: kaydırılabilir bir ATASI var mı?
 * Varsa o eleman sayfayı taşırmıyor, kendi kabında kayıyor demektir.
 *
 * ═══ ADAYLAR İPUCU, BULGU DEĞİL ═══
 *
 * Mehmet Bey 1536 px'te `.erp-quick-grid`, `.erp-table` ve `aside`
 * adaylarını çıkardı. Sonda onları ARAMIYOR — 390 px'te NE varsa onu
 * ölçüyor. Aday listesiyle başlamak, aranan şeyi bulup ötekini
 * kaçırmanın yoludur.
 */

const KULLANICI = process.env.DUZEN_KULLANICI;
const PAROLA = process.env.DUZEN_PAROLA;

const EKRANLAR = ["/dashboard", "/finans/kasa-banka", "/finans/odeme-planlari"];

/*
 * ═══ NEDEN BİRDEN ÇOK GENİŞLİK (K5-ÜST/1, 2026-09-13) ═══
 *
 * Bu sonda 390'da yazıldı ve 390'ı düzeltti. Mehmet Bey canlıdan elle
 * ölçünce taşmanın 1201–1440 arasında YAŞADIĞI çıktı (1201→70px,
 * 1280→70px, 1366→48px, 1440→24px, 1536→0). Tek genişlikte koşan bir
 * ölçüm aleti, başka genişlikteki kusuru göremez ve görmediğini
 * "yok" diye raporlar.
 *
 * 1280 seçildi çünkü `.enderun-dashboard-layout`ın tek sütuna düşme
 * medya sorgusu `max-width:1200px` — yani 1201 ve üstü İKİ SÜTUNLU
 * dalı ölçen ilk genişlik. 390 korundu: eski ölçümün zemini.
 */
type Olcek = { genislik: number; yukseklik: number };
/*
 * DÖRT GENİŞLİK (2026-09-16). Öncesinde iki taneydi (390, 1280) ve
 * aradaki band ölçülmüyordu.
 *
 * NEDEN BU DÖRDÜ:
 *   390  telefon — eski ölçümün zemini, korunuyor.
 *   768  tablet eşiği — tek sütuna düşme ile iki sütun arasındaki
 *        geçişin ALT ucu. Hiç ölçülmemişti.
 *   1280 iki sütunlu dalın ilk genişliği (`max-width:1200px` medya
 *        sorgusunun ÜSTÜ). Mehmet Bey'in elle ölçtüğü taşma burada
 *        en büyüktü (70 px).
 *   1536 taşmanın SIFIRLANDIĞI genişlik — üst uç. Taşmanın nerede
 *        bittiğini ölçmeden "düzeldi" denemez.
 *
 * ELLE ÖLÇÜM (Mehmet Bey, canlı): 1201→70px · 1280→70 · 1366→48 ·
 * 1440→24 · 1536→0. Yani taşma 1201-1440 bandında YAŞIYOR ve tek
 * genişlikte koşan bir sonda onu HİÇ GÖRMEZ.
 */
const OLCEKLER: Olcek[] = [
  { genislik: 390, yukseklik: 664 },
  { genislik: 768, yukseklik: 1024 },
  { genislik: 1280, yukseklik: 800 },
  { genislik: 1536, yukseklik: 864 },
];

/*
 * KAPI: ÖLÇÜLEN GENİŞLİK SAYISI 4'ÜN ALTINA DÜŞERSE KIRMIZI.
 *
 * Sonda rapor basıyor; raporun kendisi hüküm vermiyordu. Bir gün biri
 * diziden bir ölçek silerse ya da bir viewport sessizce kurulamazsa,
 * sonda YİNE YEŞİL YANARDI ve daha az ölçtüğünü kimse görmezdi —
 * Kural 90: süzgeçli koşu, koşmadığın hakkında hiçbir şey söylemez.
 */
const BEKLENEN_OLCEK_SAYISI = 4;

async function girisYap(sayfa: Page) {
  expect(KULLANICI, "DUZEN_KULLANICI yok — rig'i duzen-testi.sh ile koşturun").toBeTruthy();
  const yanit = await sayfa.request.post("/api/auth/login", {
    data: { username: KULLANICI, password: PAROLA },
  });
  expect(yanit.status(), "Giriş: " + (await yanit.text()).slice(0, 200)).toBe(200);
}

test("K5: hangi eleman sayfayı taşırıyor", async ({ page }) => {
  test.setTimeout(600_000);

  await girisYap(page);

  const olculenGenislikler: number[] = [];

  for (const olcek of OLCEKLER) {
  await page.setViewportSize({ width: olcek.genislik, height: olcek.yukseklik });

  // Viewport GERÇEKTEN kuruldu mu — istenen genişlik değil, OLAN genişlik
  // sayılıyor. `setViewportSize` sessizce başka bir değere düşerse ölçüm
  // başka bir genişliği ölçmüş olur ve etiketi yalan söyler.
  const olanGenislik = await page.evaluate(() => window.innerWidth);
  if (olanGenislik === olcek.genislik) {
    olculenGenislikler.push(olanGenislik);
  } else {
    console.log(
      `  !! ÖLÇEMEDİ: viewport ${olcek.genislik} istendi, ${olanGenislik} oldu — bu ölçek sayılmıyor.`
    );
  }

  console.log(`\n########## ${olcek.genislik}x${olcek.yukseklik} ##########`);

  for (const yol of EKRANLAR) {
    await page.goto(yol, { waitUntil: "domcontentloaded", timeout: 60_000 });
    await page.waitForTimeout(2_500);

    const rapor = await page.evaluate(() => {
      const gorunur = window.innerWidth;
      const belge = document.documentElement.scrollWidth;

      type Suclu = {
        etiket: string;
        sag: number;
        genislik: number;
        kayanAta: string | null;
      };
      const suclular: Suclu[] = [];

      const kayanAtaBul = (el: Element): string | null => {
        let p = el.parentElement;
        while (p && p !== document.documentElement) {
          const st = getComputedStyle(p);
          const ox = st.overflowX;
          if (
            (ox === "auto" || ox === "scroll" || ox === "hidden") &&
            p.scrollWidth > p.clientWidth + 1
          ) {
            return (
              p.tagName.toLowerCase() +
              (p.className && typeof p.className === "string"
                ? "." + p.className.trim().split(/\s+/).slice(0, 2).join(".")
                : "")
            );
          }
          p = p.parentElement;
        }
        return null;
      };

      for (const el of Array.from(document.querySelectorAll("*"))) {
        const r = el.getBoundingClientRect();
        if (r.width === 0 || r.height === 0) continue;
        if (r.right <= gorunur + 1) continue;

        suclular.push({
          etiket:
            el.tagName.toLowerCase() +
            (el.className && typeof el.className === "string"
              ? "." + el.className.trim().split(/\s+/).slice(0, 2).join(".")
              : ""),
          sag: Math.round(r.right),
          genislik: Math.round(r.width),
          kayanAta: kayanAtaBul(el),
        });
      }

      // En sağa taşan ilk 8, tekrarları eleyerek
      const gorulen = new Set<string>();
      const ilkler = suclular
        .sort((a, b) => b.sag - a.sag)
        .filter((s) => {
          if (gorulen.has(s.etiket)) return false;
          gorulen.add(s.etiket);
          return true;
        })
        .slice(0, 8);

      /*
       * 631'İN KAYNAĞI — ATA ZİNCİRİ VE HESAPLANAN STİL.
       *
       * Taşan elemanı bulmak "neyi düzelteceğim"i söylemiyor. En sağa
       * taşan elemanın atalarını yukarı doğru yürüyüp genişliği
       * ÇİVİLEYEN ilk kuralı arıyoruz: sabit width, min-width, ya da
       * küçülmeyi reddeden bir ızgara/flex tanımı.
       */
      const zincir: string[] = [];
      const enSagdaki = suclular.sort((a, b) => b.sag - a.sag)[0];
      if (enSagdaki) {
        let el: Element | null = Array.from(document.querySelectorAll("*")).find(
          (x) => {
            const r = x.getBoundingClientRect();
            return Math.round(r.right) === enSagdaki.sag && Math.round(r.width) === enSagdaki.genislik;
          }
        ) ?? null;

        while (el && el !== document.documentElement) {
          const st = getComputedStyle(el);
          const r = el.getBoundingClientRect();
          const ad =
            el.tagName.toLowerCase() +
            (el.className && typeof el.className === "string"
              ? "." + el.className.trim().split(/\s+/).slice(0, 2).join(".")
              : "");
          zincir.push(
            `${ad.padEnd(44)} w=${Math.round(r.width).toString().padStart(4)}` +
              ` display=${st.display}` +
              ` width=${st.width}` +
              ` min-width=${st.minWidth}` +
              (st.display.includes("grid") ? ` cols=${st.gridTemplateColumns}` : "") +
              (st.flexShrink !== "1" ? ` flex-shrink=${st.flexShrink}` : "")
          );
          el = el.parentElement;
        }
      }

      return { gorunur, belge, tasma: Math.max(0, belge - gorunur), ilkler, zincir };
    });

    console.log(`\n=== ${yol} ===`);
    console.log(
      `belge scrollWidth ${rapor.belge} · görünür ${rapor.gorunur} · SAYFA TAŞMASI ${rapor.tasma}`
    );
    if (rapor.ilkler.length === 0) {
      console.log("  görünür alanın sağına taşan eleman YOK");
    }
    if (rapor.zincir.length) {
      console.log("  --- ata zinciri (en sağdaki elemandan yukarı) ---");
      for (const z of rapor.zincir) console.log("    " + z);
    }
    for (const s of rapor.ilkler) {
      const not = s.kayanAta
        ? `kendi kabında kayıyor (ata: ${s.kayanAta}) — KUSUR DEĞİL`
        : "KAYAN ATASI YOK -> SAYFAYI TAŞIRIYOR";
      console.log(
        `  ${s.etiket.padEnd(38)} sağ=${String(s.sag).padStart(5)} genişlik=${String(s.genislik).padStart(5)}  ${not}`
      );
    }
  }
  }

  /*
   * HÜKÜM — sondanın kapsamı kendi kendini beyan eder.
   * Rapor satırları göz içindir; bu satır makinenin okuduğudur.
   */
  console.log(
    `\n=== K5 KAPSAM: ${olculenGenislikler.length}/${BEKLENEN_OLCEK_SAYISI} genişlik ölçüldü (${olculenGenislikler.join(", ")}) ===`
  );
  expect(
    olculenGenislikler.length,
    `K5 yalnız ${olculenGenislikler.length} genişlik ölçtü; ${BEKLENEN_OLCEK_SAYISI} bekleniyor. `
      + `Ölçülenler: ${olculenGenislikler.join(", ") || "(hiç)"}. `
      + "Eksik ölçüm, temiz ölçüm değildir (Kural 90)."
  ).toBe(BEKLENEN_OLCEK_SAYISI);
});
