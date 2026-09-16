import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";

/**
 * QUICK-GRID TEK TANIM MUHAFIZI (K5-ÜST/1, 2026-09-16).
 *
 * ═══ ÖLÇÜLEN KUSUR ═══
 *
 * `globals.css` içinde `.erp-quick-grid` İKİ KEZ taban tanımlıydı:
 *   satır  664 : repeat(2, minmax(0, 1fr))   ← doğru
 *   satır 1092 : repeat(4, 1fr)              ← sonra, KAZANIYORDU
 *
 * Dashboard 1280 px'te sayfayı 72 px taşırıyordu (rig), Mehmet Bey
 * tarayıcıdan 70 px ölçtü. İki bağımsız ölçüm aynı yeri gösterdi.
 *
 * Düzeltme "4'ü minmax yapmak" DEĞİLDİ — kopyanın kendisi kaldırıldı.
 * İki kopyadan birini düzeltmek, ikinci davranışı korumaktı. Bu, fiş
 * tipi eşlemesiyle aynı aile; muhafız da aynı şekli alıyor.
 *
 * ═══ NE SAYILIR, NE SAYILMAZ ═══
 *
 * SAYILIR   : `.erp-quick-grid{` — taban TANIM.
 * SAYILMAZ  : `@media` içindeki override (meşru duyarlı davranış;
 *             760 px altında tek sütuna düşmek istenen şeydir).
 * SAYILMAZ  : `.erp-quick-grid a{` — farklı seçici; bağlantıları
 *             biçimlendiren kural odur ve işaretlemede sınıf yoktur.
 */
const CSS = readFileSync(join(__dirname, "..", "app", "globals.css"), "utf8");

/** @media bloklarını çıkarır; geriye taban seviye kalır. */
function tabanSeviye(css: string): string {
  let sonuc = "";
  let i = 0;
  while (i < css.length) {
    const m = css.indexOf("@media", i);
    if (m === -1) {
      sonuc += css.slice(i);
      break;
    }
    sonuc += css.slice(i, m);
    // @media'nın açılış süslüsünü bul, eşleşen kapanışa kadar atla
    let j = css.indexOf("{", m);
    if (j === -1) break;
    let derinlik = 1;
    j++;
    while (j < css.length && derinlik > 0) {
      if (css[j] === "{") derinlik++;
      else if (css[j] === "}") derinlik--;
      j++;
    }
    i = j;
  }
  return sonuc;
}

function tabanTanimSayisi(css: string): number {
  return (tabanSeviye(css).match(/\.erp-quick-grid\s*\{/g) ?? []).length;
}

describe("quick-grid tek tanım", () => {
  it("tarama sağlığı: CSS okundu ve seçici bulundu (boş küme kanıt değil)", () => {
    expect(CSS.length).toBeGreaterThan(5000);
    expect(CSS).toContain(".erp-quick-grid");
  });

  it("SONDA_ISIRIYOR — ikinci taban tanım yakalanır, media ve `a` yakalanmaz", () => {
    // Kural 93: yeşil, sondanın ısırabildiği gösterilmeden rapora girmez.
    expect(tabanTanimSayisi(".erp-quick-grid{a:b}")).toBe(1);
    expect(tabanTanimSayisi(".erp-quick-grid{a:b}.erp-quick-grid{c:d}")).toBe(2);
    // @media içindeki override SAYILMAZ:
    expect(
      tabanTanimSayisi(".erp-quick-grid{a:b}@media(max-width:760px){.erp-quick-grid{c:d}}")
    ).toBe(1);
    // `a` seçicisi TANIM DEĞİL:
    expect(tabanTanimSayisi(".erp-quick-grid a{border:1px}")).toBe(0);
  });

  it("TAM BİR KEZ tanımlı — ikinci taban tanım KIRMIZI", () => {
    const sayi = tabanTanimSayisi(CSS);
    expect(
      sayi,
      `.erp-quick-grid taban tanımı ${sayi} kez bulundu; TAM 1 olmalı. `
        + "İkinci tanım kaynak sırasıyla kazanır ve sessizce ikinci bir davranış yaratır "
        + "(2026-09-16: dashboard 1280 px'te 72 px taştı). Yeni davranış gerekiyorsa "
        + "TEK tanımı değiştirin, ikincisini eklemeyin."
    ).toBe(1);
  });

  it("tek tanım küçülmeye izin veriyor — `1fr` değil `minmax(0, 1fr)`", () => {
    const taban = tabanSeviye(CSS);
    const blok = /\.erp-quick-grid\s*\{([^}]*)\}/.exec(taban)?.[1] ?? "";
    expect(blok).toContain("minmax(0, 1fr)");
    // `1fr` = `minmax(auto, 1fr)`; asgarisi auto olduğu için sütun
    // içeriğin altına inmez — taşmanın mekanizması tam budur.
    expect(blok).not.toMatch(/repeat\(\s*\d+\s*,\s*1fr\s*\)/);
  });
});
