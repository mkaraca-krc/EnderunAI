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
  /**
   * GRAFİK SERİLERİ BİRBİRİNE KARIŞMAMALI.
   *
   * Yakalanmak istenen hata: dört seriden ikisinin aynı ya da neredeyse
   * aynı değere düşmesi. Bu, gözle bakılmadan fark edilmez — çizelge
   * yine "çalışıyor" görünür, sadece iki çubuk ayırt edilemez olur.
   *
   * ÖLÇÜ KONTRAST ORANI DEĞİL, RGB UZAKLIĞI. Kontrast yalnızca
   * PARLAKLIĞA bakıyor; kırmızı ile aynı parlaklıktaki bir griyi
   * "ayırt edilemez" sayar, oysa gözle bakan ikisini anında ayırır.
   * Serileri ayıran şey çoğu zaman ton, parlaklık değil.
   *
   * Bu ayrımı ilk sürümde ters kurmuştum ve test, aslında sorunsuz olan
   * kehribar/turkuaz çiftini kırdı.
   */
  it("grafik serileri birbirine karışmıyor", () => {
    const tokens = ["chart-1", "chart-2", "chart-3", "chart-4"];
    const series = tokens.map((token) => pair(`color-${token}`));

    const rgb = (hex: string) => {
      let h = hex.replace("#", "");
      if (h.length === 3) h = [...h].map((c) => c + c).join("");
      return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16));
    };

    const distance = (a: string, b: string) => {
      const [x, y] = [rgb(a), rgb(b)];
      return Math.hypot(x[0] - y[0], x[1] - y[1], x[2] - y[2]);
    };

    for (const theme of ["light", "dark"] as const) {
      for (let i = 0; i < series.length; i += 1) {
        for (let j = i + 1; j < series.length; j += 1) {
          expect(
            distance(series[i][theme], series[j][theme]),
            `${theme}: ${tokens[i]} ile ${tokens[j]} birbirine çok yakın`,
          ).toBeGreaterThan(40);
        }
      }
    }
  });

  /**
   * TAILWIND SKALALARI DA TEMA DUYARLI OLMALI.
   *
   * Bu testin ilk hâli yalnızca `--erp-*` ve `--color-semantic-*`
   * tokenlarına bakıyordu ve koyu temayı iki kez "geçti" diye
   * onayladı. Oysa ekranlar rengi çoğunlukla o tokenlarla değil,
   * Tailwind sınıflarıyla yazıyor: `text-slate-900`, `bg-slate-50`.
   * 160 Redwood ekranının 67'sinde 2997 kullanım vardı ve hepsi sabit
   * değerdi — koyu temada siyah metin, beyaz kart.
   *
   * Testin kapsamı ekrandaki renk YAZMA BİÇİMİNİ takip etmezse,
   * ölçtüğü şey gerçekte görülen şey olmuyor.
   */
  it("kullanılan Tailwind skalaları light-dark tanımlı", () => {
    const families = ["slate", "red", "emerald", "amber", "blue", "cyan"];
    const steps = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950];

    const missing: string[] = [];

    for (const family of families) {
      for (const step of steps) {
        const token = `--color-${family}-${step}`;

        if (!new RegExp(`${token}:\\s*light-dark\\(`).test(CSS)) {
          missing.push(`${token}`);
        }
      }
    }

    expect(
      missing,
      "Skala tokenı sabit değerde kalmış; o sınıfı kullanan her ekran " +
        "koyu temada açık renk basar.",
    ).toEqual([]);
  });

  /**
   * En yoğun kullanılan slate adımları koyu KARTTA da okunmalı.
   *
   * Zemin değil kart seçildi çünkü kart daha açık ve zor olan o.
   * `text-slate-500` (531 kullanım) saf aynalamada 500'de kalıyor ve
   * kartta 4.09 veriyordu — bu yüzden koyu değeri bir adım açıldı.
   */
  it("slate metin adımları koyu kartta okunuyor", () => {
    const panel = pair("erp-panel").dark;

    for (const step of [500, 600, 700, 800, 900, 950]) {
      const { dark } = pair(`color-slate-${step}`);

      expect(
        Number(contrast(dark, panel).toFixed(2)),
        `text-slate-${step} koyu kartta okunmuyor`,
      ).toBeGreaterThanOrEqual(MINIMUM);
    }
  });

  /**
   * Rampa gerçekten TERS ÇEVRİLMİŞ olmalı.
   *
   * Bir adımın koyu değeri yanlışlıkla açık değerinin kopyası olarak
   * bırakılırsa yukarıdaki testler bunu yakalamayabilir; burada yön
   * kontrol ediliyor: en açık uç koyulaşmış, en koyu uç açılmış.
   */
  it("slate rampası koyu temada ters çevrilmiş", () => {
    const lightEnd = pair("color-slate-50");
    const darkEnd = pair("color-slate-900");

    expect(luminance(lightEnd.dark)).toBeLessThan(luminance(lightEnd.light));
    expect(luminance(darkEnd.dark)).toBeGreaterThan(luminance(darkEnd.light));
  });

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
