import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * TEMA KONTRASTI — AÇIK VE KOYU, İKİSİ BİRDEN.
 *
 * Paletteki her token `light-dark(açık, koyu)` biçiminde tek satırda
 * iki değer taşıyor. Bu test o satırları OKUYUP kontrast oranını
 * hesaplıyor; sayılar burada elle yazılmıyor, palet değişince test de
 * yeni değerle çalışıyor.
 *
 * NEDEN GEREKLİ: koyu temayı eklerken açık temada zaten var olan bir
 * sorun ortaya çıktı — uyarı rengi (#b7791f) kendi ton zemininde 3.38
 * kontrast veriyordu, metin eşiğinin altında. Gözle bakınca "sarımsı
 * yazı" gibi duruyor ve kimse fark etmiyor; ölçünce görünüyor.
 *
 * EŞİK 4.5: WCAG AA, normal boyutlu metin. Kenarlık ve ayraç gibi
 * metin olmayan öğeler bu testin kapsamında değil — onların eşiği
 * farklı ve bilinçli olarak silik olabiliyorlar.
 */

const CSS = readFileSync(join(__dirname, "..", "app", "globals.css"), "utf8");

/** `--token: light-dark(#aaa, #bbb);` satırından iki değeri çeker. */
function pair(token: string): { light: string; dark: string } {
  const match = CSS.match(
    new RegExp(`--${token}:\\s*light-dark\\(\\s*(#[0-9a-fA-F]{3,8})\\s*,\\s*(#[0-9a-fA-F]{3,8})\\s*\\)`),
  );

  if (!match) {
    throw new Error(
      `--${token} paletde light-dark() olarak bulunamadı. ` +
        "Token kaldırıldıysa bu testten de çıkarılmalı.",
    );
  }

  return { light: match[1], dark: match[2] };
}

function channel(value: string): number {
  const n = parseInt(value, 16) / 255;
  return n <= 0.03928 ? n / 12.92 : ((n + 0.055) / 1.055) ** 2.4;
}

function luminance(hex: string): number {
  let h = hex.replace("#", "");
  if (h.length === 3) h = [...h].map((c) => c + c).join("");

  return (
    0.2126 * channel(h.slice(0, 2)) +
    0.7152 * channel(h.slice(2, 4)) +
    0.0722 * channel(h.slice(4, 6))
  );
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** Metin tokenı × zemin tokenı — her ikisi de light-dark çifti. */
const TEXT_ON_SURFACE: [string, string, string][] = [
  ["Gövde metni / sayfa zemini", "erp-text", "erp-bg"],
  ["Gövde metni / kart", "erp-text", "erp-panel"],
  ["İkincil metin / kart", "erp-muted", "erp-panel"],
  ["Marka rengi / kart", "erp-primary", "erp-panel"],
  ["Başarı / başarı tonu", "color-semantic-success", "color-semantic-success-tint"],
  ["Uyarı / uyarı tonu", "color-semantic-warning", "color-semantic-warning-tint"],
  ["Hata / hata tonu", "color-semantic-danger", "color-semantic-danger-tint"],
  ["Bilgi / bilgi tonu", "color-semantic-info", "color-semantic-info-tint"],
];

const MINIMUM = 4.5;

describe("tema kontrastı", () => {
  it.each(TEXT_ON_SURFACE)("%s — açık tema", (_name, fg, bg) => {
    const ratio = contrast(pair(fg).light, pair(bg).light);
    expect(Number(ratio.toFixed(2))).toBeGreaterThanOrEqual(MINIMUM);
  });

  it.each(TEXT_ON_SURFACE)("%s — koyu tema", (_name, fg, bg) => {
    const ratio = contrast(pair(fg).dark, pair(bg).dark);
    expect(Number(ratio.toFixed(2))).toBeGreaterThanOrEqual(MINIMUM);
  });

  /**
   * Marka düğmesinin üzerindeki yazı ayrı bir token: koyu temada zemin
   * AÇILDIĞI için yazının koyulaşması gerekiyor. Kart yüzeyiyle aynı
   * tokena bağlansaydı düğme koyu temada okunmaz olurdu — yayma
   * sırasında bu hata üç ayrı modülde çıktı.
   */
  it("marka düğmesinin yazısı iki temada da okunuyor", () => {
    const text = pair("color-on-brand");
    const surface = pair("color-brand-primary");

    expect(contrast(text.light, surface.light)).toBeGreaterThanOrEqual(MINIMUM);
    expect(contrast(text.dark, surface.dark)).toBeGreaterThanOrEqual(MINIMUM);
  });

  /**
   * Koyu tema gerçekten KOYU olmalı: zemin, açık temadaki zeminden
   * belirgin biçimde daha karanlık. Bir tokenın koyu değeri yanlışlıkla
   * açık değerin kopyası olarak bırakılırsa bu test yakalar.
   */
  it("koyu tema zeminleri gerçekten koyu", () => {
    for (const token of ["erp-bg", "erp-panel", "color-surface-bg", "color-surface-card"]) {
      const { light, dark } = pair(token);

      expect(luminance(dark), `--${token} koyu değeri açık kalmış`).toBeLessThan(
        luminance(light),
      );
      expect(luminance(dark), `--${token} yeterince koyu değil`).toBeLessThan(0.1);
    }
  });
});
