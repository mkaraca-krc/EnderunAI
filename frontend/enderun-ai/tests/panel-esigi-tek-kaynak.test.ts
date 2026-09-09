import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { PANEL_DAR_EKRAN_ESIGI } from "@/lib/mesajlasma/panel-esigi";

/**
 * PANEL EŞİĞİ İKİ YERDE DURUYOR — AMA SESSİZCE AYRIŞAMAZ.
 *
 * CSS medya sorgusu `var()` okuyamaz; `@media (max-width: var(--x))`
 * geçersizdir. Yani CSS'teki sayı literal kalmak zorunda ve "tek
 * dosyada tut" mümkün değil.
 *
 * **Mehmet, 2026-09-07:** *"Bu hafta üç kez ödediğimiz bedel 'iki
 * yerde olması' değil, 'ayrıştığını kimsenin görmemesi'ydi."*
 *
 * Bu test o görmeyi sağlıyor: TS sabiti kanonik, CSS literali ona eşit
 * olmalı. Biri değişip diğeri unutulursa kırmızı yanar.
 */

const CSS = readFileSync(join(__dirname, "..", "app", "globals.css"), "utf8");

/** `.mesaj-panel { display: none }` kuralını içeren medya sorgusunun eşiği. */
function cssEsigi(): number {
  // Panel gizleme kuralını içeren bloğu bul; başka 900px blokları da
  // var (`.mesaj-duzen`), onlara bakmıyoruz.
  const bloklar = [
    ...CSS.matchAll(/@media\s*\(max-width:\s*([\d.]+)px\)\s*\{([\s\S]*?)\n\}/g),
  ];

  const panelBloklari = bloklar.filter((b) => b[2].includes(".mesaj-panel"));

  // POZİTİF KONTROL: kural bulunamazsa test sessizce geçmemeli.
  // Bulunamayan literal, "eşitler" demek değildir (Kural 48).
  expect(
    panelBloklari.length,
    "globals.css'te `.mesaj-panel` gizleyen bir medya sorgusu bulunamadı. " +
      "Ayıklama bozulmuş olabilir; bu hâlde test hiçbir şey ölçmüyor."
  ).toBeGreaterThan(0);

  return Number(panelBloklari[0][1]);
}

describe("panel genişlik eşiği tek kaynak", () => {
  it("CSS'teki eşik okunabiliyor ve makul (POZİTİF KONTROL)", () => {
    const esik = cssEsigi();

    /*
     * `isInteger` DEĞİL `isFinite`: eşik artık 899.98 (dışlayıcı
     * sınır). Tam sayı olması, bu pozitif kontrolün koruduğu şey
     * DEĞİLDİ — koruduğu şey ayıklamanın NaN/boş üretmemesi.
     * Tam sayı iddiası eşiğin tam sayı olduğu dönemden kalmıştı.
     */
    expect(Number.isFinite(esik)).toBe(true);
    expect(esik).toBeGreaterThan(300);
  });

  /*
   * SAYI DEĞİL, SINIR SINANIYOR (PN4-3, 2026-09-09).
   *
   * Eski hâli `toBe(PANEL_DAR_EKRAN_ESIGI)` idi ve GEÇİYORDU: CSS
   * `max-width: 900px`, sabit 900, sayılar eşit. Ama JS `< 900`
   * (dışlayıcı), CSS `<= 900` (kapsayıcı) demekti ve TAM 900'de panel
   * açılıp CSS tarafından gizleniyordu — testin kendi yorumunun
   * uyardığı durum, testin ölçmediği yerden geçti.
   *
   * Sayı eşitliği ayrışmayı yakalar, SINIR HATASINI yakalamaz. İki
   * iddia birlikte gerekiyor.
   */
  it("CSS eşiği DIŞLAYICI: sabitin altında gizler, sabitte gizlemez", () => {
    expect(
      cssEsigi(),
      "CSS eşiği sabite eşit ya da büyük. `max-width: N` N'i DAHİL " +
        "eder; JS ise `< N` diyor. Tam N pikselde baloncuk paneli açar, " +
        "CSS gizler ve kullanıcı panelsiz KALIR."
    ).toBeLessThan(PANEL_DAR_EKRAN_ESIGI);
  });

  it("CSS eşiği sabitten UZAKLAŞMAMIŞ (1 px'ten yakın)", () => {
    expect(
      PANEL_DAR_EKRAN_ESIGI - cssEsigi(),
      "globals.css'teki eşik ile PANEL_DAR_EKRAN_ESIGI ayrışmış. " +
        "Baloncuk bir genişlikte yönlendirirken CSS başka bir " +
        "genişlikte gizler; arada kullanıcı panelsiz ve yönlendirmesiz kalır."
    ).toBeLessThanOrEqual(1);
  });
});
