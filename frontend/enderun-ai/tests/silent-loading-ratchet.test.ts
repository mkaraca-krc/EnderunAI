import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * "SESSİZ YÜKLENİYOR" CIRCIRI.
 *
 * DESEN: `useState(true)` ile açılan yükleme durumu + yükleyicinin
 * `setLoading(false)` çağrılmadan ÖNCE çıplak `return;` ile
 * dönebilmesi. O yoldan dönülürse ekran sonsuza kadar "Yükleniyor…"
 * der ve HATA GÖSTERMEZ — çünkü ortada bir hata yoktur.
 *
 * 2026-08-24'te `/yapilacaklar` canlıda kilitlendi. Orada asıl sebep
 * kararsız referans döngüsüydü, ama bu desen ikinci ve bağımsız bir
 * kilit yolu olarak duruyordu.
 *
 * DURUM.md §5 kural 26: bir sayfa yükleme durumundan çıkışı GARANTİ
 * etmelidir — erken çıkış ve hata yollarında da.
 */

const KOK = join(__dirname, "..");

function ekranlar(dizin: string): string[] {
  const bulunan: string[] = [];

  for (const girdi of readdirSync(dizin)) {
    if (girdi === "node_modules" || girdi === ".next") continue;

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...ekranlar(yol));
      continue;
    }

    if (/\.tsx$/.test(girdi)) bulunan.push(yol);
  }

  return bulunan;
}

function suanki(): Map<string, number> {
  const sayac = new Map<string, number>();

  for (const yol of ekranlar(join(KOK, "app"))) {
    const kod = readFileSync(yol, "utf8");

    const durum =
      /const \[(\w*(?:[Ll]oading|[Yy]ukleniyor)\w*)\s*,\s*(set\w+)\]\s*=\s*useState(?:<[^>]*>)?\(true\)/.exec(
        kod
      );

    if (!durum) continue;

    const setter = durum[2];
    const kapanis = new RegExp(`${setter}\\(\\s*false\\s*\\)`).exec(kod);

    // Kapanış hiç yoksa desen zaten en kötü hâlinde.
    if (!kapanis) {
      sayac.set(relative(KOK, yol), 1);
      continue;
    }

    let adet = 0;
    const ciplakReturn = /^[ \t]*return;[ \t]*$/gm;

    let m: RegExpExecArray | null;
    while ((m = ciplakReturn.exec(kod)) !== null) {
      if (m.index > durum.index && m.index < kapanis.index) adet++;
    }

    if (adet > 0) sayac.set(relative(KOK, yol), adet);
  }

  return sayac;
}

function cizgi(): Map<string, number> {
  const m = new Map<string, number>();

  const satirlar = readFileSync(
    join(KOK, "tests", "bekci", "sessiz-yukleme-cizgi.txt"),
    "utf8"
  )
    .split("\n")
    .map((s) => s.trim())
    .filter((s) => s.length > 0 && !s.startsWith("#"));

  for (const satir of satirlar) {
    const i = satir.lastIndexOf(":");
    m.set(satir.slice(0, i), Number(satir.slice(i + 1)));
  }

  return m;
}

function toplam(m: Map<string, number>): number {
  return [...m.values()].reduce((a, b) => a + b, 0);
}

describe("sessiz yükleniyor cırcırı", () => {
  const olculen = suanki();
  const temel = cizgi();

  it("tarama boşa düşmüyor", () => {
    expect(ekranlar(join(KOK, "app")).length).toBeGreaterThan(150);
  });

  it("yeni ekran bu desenle doğamaz", () => {
    const artanlar = [...olculen.entries()]
      .filter(([d, n]) => n > (temel.get(d) ?? 0))
      .map(([d, n]) => `${d}: ${temel.get(d) ?? 0} -> ${n}`);

    expect(
      artanlar,
      `"Sessiz yükleniyor" deseni ${toplam(olculen)}, çizgi ${toplam(temel)}.\n` +
        "Artan dosyalar:\n" + artanlar.join("\n") +
        "\n\nYükleyicinin HER çıkış yolunda yükleme durumu kapanmalı — " +
        "erken çıkış ve hata yolları dahil."
    ).toEqual([]);

    expect(toplam(olculen)).toBeLessThanOrEqual(toplam(temel));
  });

  it("çizgide artık deseni taşımayan dosya kalmıyor", () => {
    const olmayan = [...temel.keys()].filter((d) => !olculen.has(d));

    expect(
      olmayan,
      "Bu dosyalar çizgide ama desen artık yok. SİLİN:\n" + olmayan.join("\n")
    ).toEqual([]);
  });
});
