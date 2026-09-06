import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * PANEL KABUKTA TAKILI, SAYFADA DEĞİL — VE KENDİ HATA SINIRINDA.
 *
 * Davranış testi (`mesaj-paneli-kabukta.test.tsx`) panelin kabukta
 * OLMASI HÂLİNDE taslağın korunduğunu gösteriyor. Bu test, panelin
 * GERÇEKTEN orada olduğunu tutuyor. İkisi ayrı sorular:
 * biri "böyle olursa ne olur", diğeri "böyle mi".
 *
 * ═══ İKİ AYRI ŞART ═══
 *
 * 1. `<MesajBaloncugu` YALNIZ `erp-shell.tsx` içinde geçer. Bir sayfa
 *    bileşenine eklenirse rota değişiminde sökülür.
 * 2. Kabukta KENDİ hata sınırında sarılıdır. `nerede="içerik"` sınırı
 *    yalnız `children`'ı kapsıyor; panel onun dışında olduğu için
 *    sarılmazsa bir render hatası TÜM ERP KABUĞUNU düşürürdü.
 */

const ROOT = join(__dirname, "..");
const KABUK = join(ROOT, "components", "erp", "erp-shell.tsx");

function tsxDosyalari(dizin: string): string[] {
  const bulunan: string[] = [];

  for (const girdi of readdirSync(dizin)) {
    if (girdi === "node_modules" || girdi === ".next") continue;

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...tsxDosyalari(yol));
      continue;
    }

    if (/\.tsx$/.test(girdi)) bulunan.push(yol);
  }

  return bulunan;
}

describe("mesaj paneli yerleşim sözleşmesi", () => {
  it("kabuk dosyası okunabiliyor ve baloncuğu takıyor (POZİTİF KONTROL)", () => {
    // Tarama bozulursa aşağıdaki testler boş kümede yeşil döner
    // ve hiçbir şey ölçmezler (Kural 48).
    const kabuk = readFileSync(KABUK, "utf8");
    expect(kabuk).toContain("<MesajBaloncugu");
  });

  it("hiçbir sayfa bileşeni baloncuğu kendi ağacına takmaz", () => {
    const suclular = tsxDosyalari(join(ROOT, "app"))
      .filter((yol) => readFileSync(yol, "utf8").includes("<MesajBaloncugu"))
      .map((yol) => yol.replace(ROOT + "/", ""));

    expect(
      suclular,
      "Bu sayfa bileşenleri mesaj baloncuğunu kendi ağaçlarına takıyor. " +
        "Rota değişiminde sökülür: açık konuşma kapanır, yazılmış taslak " +
        "gider.\n  - " + suclular.join("\n  - ")
    ).toEqual([]);
  });

  it("baloncuk kabukta kendi hata sınırında sarılı", () => {
    const kabuk = readFileSync(KABUK, "utf8");

    // Sınır etiketi ile baloncuk arasında başka bir HataSiniri açılışı
    // olmamalı; yani baloncuk DOĞRUDAN o sınırın içinde.
    const sinir = kabuk.indexOf('nerede="mesaj-paneli"');
    const baloncuk = kabuk.indexOf("<MesajBaloncugu");

    expect(sinir, "nerede=\"mesaj-paneli\" hata sınırı bulunamadı").toBeGreaterThan(-1);
    expect(baloncuk).toBeGreaterThan(sinir);

    const arada = kabuk.slice(sinir, baloncuk);
    expect(
      arada.includes("<HataSiniri"),
      "Baloncuk ile kendi hata sınırı arasında başka bir sınır açılmış."
    ).toBe(false);
  });
});
