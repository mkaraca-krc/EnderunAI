import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import {
  ONYUZ_KOK,
  backendDosyaSayisi,
  cagrilar,
  cozuluyorMu,
  onyuzDosyaSayisi,
  uclar,
} from "./bekci/uc-envanteri";

/**
 * SERVİS ÇAĞRISI BEKÇİSİ — rota bekçisinin API karşılığı.
 *
 * NEDEN VAR: 7a'da rota bekçisi yazıldı ama API uçları BİLEREK
 * kapsam dışı bırakıldı ve bu, aynı gün TEMIZLIK-TARAMASI.md'ye
 * "sonraki tur" kalemi olarak yazıldı. Birkaç saat sonra
 * `/yapilacaklar` ekranı canlıda `project-sites/daily-reports/
 * pending-approval` çağırırken 404 aldı; doğrusu
 * `site-reports/pending-approval` idi. Kapsam dışı bırakma kararı
 * teorik bir borç değil, aynı gün gerçek bir arıza olarak döndü.
 *
 * UÇLAR BACKEND KAYNAĞINDAN TÜRETİLİYOR — Swagger'dan ya da çalışan
 * sunucudan değil. Çalışan sunucuya bakan bir bekçi CI'da sessizce
 * boşa düşerdi.
 */

const envanter = uclar();
const tumCagrilar = cagrilar();

function cizgiSatirlari(dosya: string): string[] {
  return readFileSync(join(ONYUZ_KOK, "tests", "bekci", dosya), "utf8")
    .split("\n")
    .map((s) => s.trim())
    .filter((s) => s.length > 0 && !s.startsWith("#"));
}

/** `dosya|yol` — gerekçe karşılaştırmaya girmez. */
function anahtar(satir: string): string {
  const p = satir.split("|");
  return `${p[0]}|${p[1]}`;
}

describe("servis çağrısı bekçisi — tarama gerçekten çalışıyor", () => {
  /**
   * SONDA 5 DERSİ (7a): tarayıcı bozulunca "hepsi çözülüyor" testi
   * YEŞİL kalır — boş küme her iddiayı doğrular. Bu yüzden taranan
   * ve bulunan sayıların alt sınırı ayrıca sınanıyor.
   */
  it("backend uç envanteri ve ön yüz çağrıları bulundu", () => {
    expect(backendDosyaSayisi()).toBeGreaterThan(100);
    expect(envanter.size).toBeGreaterThan(400);
    expect(onyuzDosyaSayisi()).toBeGreaterThan(300);
    expect(tumCagrilar.length).toBeGreaterThan(500);
  });
});

describe("servis çağrısı bekçisi — yollar çözülüyor", () => {
  it("her servis çağrısı gerçek bir uca çözülüyor", () => {
    const cizgi = new Set(cizgiSatirlari("uc-cizgi.txt").map(anahtar));

    const kirik = tumCagrilar
      .filter((c) => !c.hesaplanmis)
      .filter((c) => !cozuluyorMu(c, envanter))
      .map((c) => `${c.dosya}|${c.normal}`)
      .filter((a) => !cizgi.has(a));

    expect(
      [...new Set(kirik)],
      "Bu çağrılar var olmayan bir uca gidiyor — kullanıcı ekranda " +
        "404 alır:\n" + [...new Set(kirik)].join("\n")
    ).toEqual([]);
  });

  it("çizgideki her satır hâlâ gerçekten çözülmüyor", () => {
    const suanki = new Set(
      tumCagrilar
        .filter((c) => !c.hesaplanmis && !cozuluyorMu(c, envanter))
        .map((c) => `${c.dosya}|${c.normal}`)
    );

    const olmayan = cizgiSatirlari("uc-cizgi.txt")
      .map(anahtar)
      .filter((a) => !suanki.has(a));

    expect(
      olmayan,
      "Bu satırlar uc-cizgi.txt içinde ama artık kırık değiller. " +
        "Düzeltilmişlerse SİLİN — çizgi küçülmeli:\n" + olmayan.join("\n")
    ).toEqual([]);
  });
});

describe("servis çağrısı bekçisi — hesaplanmış önek cırcırı", () => {
  function cizgiHaritasi(): Map<string, number> {
    const m = new Map<string, number>();

    for (const satir of cizgiSatirlari("uc-hesaplanmis-cizgi.txt")) {
      const i = satir.lastIndexOf(":");
      m.set(satir.slice(0, i), Number(satir.slice(i + 1)));
    }

    return m;
  }

  function suankiHarita(): Map<string, number> {
    const m = new Map<string, number>();

    for (const c of tumCagrilar) {
      if (c.hesaplanmis) m.set(c.dosya, (m.get(c.dosya) ?? 0) + 1);
    }

    return m;
  }

  function toplam(m: Map<string, number>): number {
    return [...m.values()].reduce((a, b) => a + b, 0);
  }

  it("hesaplanmış önekli çağrı sayısı çizgiyi aşmıyor", () => {
    const suanki = toplam(suankiHarita());
    const temel = toplam(cizgiHaritasi());

    expect(
      suanki,
      `Doğrulanamayan servis çağrısı ${suanki}, çizgi ${temel}. ` +
        "Yolu sabit yazın; değişkeni yalnız SEGMENT olarak kullanın."
    ).toBeLessThanOrEqual(temel);
  });

  it("çizgide artık hesaplanmış çağrı taşımayan dosya kalmıyor", () => {
    const suanki = suankiHarita();

    const olmayan = [...cizgiHaritasi().keys()].filter((d) => !suanki.has(d));

    expect(
      olmayan,
      "Bu dosyalar çizgide ama artık hesaplanmış önekli çağrı " +
        "taşımıyorlar. SİLİN:\n" + olmayan.join("\n")
    ).toEqual([]);
  });
});
