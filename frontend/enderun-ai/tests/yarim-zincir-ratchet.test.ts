import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * YARIM İSTEĞE BAĞLI ZİNCİR CIRCIRI.
 *
 * DESEN: `a?.b.c` ya da `a?.b[0]` — DIŞ nesne korunmuş, İÇ ALAN
 * korumasız. Yazan kişi "a henüz yüklenmemiş olabilir" diye
 * düşünmüş, "a geldi ama b gelmemiş olabilir" diye düşünmemiş.
 *
 * TYPESCRIPT BUNU YAKALAMAZ ve yakalamaması doğru: tip `b`yi zorunlu
 * ilan ediyorsa derleyicinin şüphelenmesi için sebep yok. Kusur
 * TİPİN KENDİSİNDE — sunucu sözleşmesi ile tip tanımı ayrıştığında
 * tip yalan söyler ve yalan çalışma anında ortaya çıkar.
 *
 * SOMUT OLAY: kabukta `currentUser?.roles[0]` vardı. Oturum beklenen
 * şekilde gelmediğinde YAN MENÜDEKİ TEK SATIR bütün uygulamayı
 * düşürüyordu — kabuk her ekranı sardığı için açık kalan tek bir
 * sayfa bile olmuyordu. Düzeltildi; bu yüzden çizgide YOK.
 *
 * NEDEN CIRCIR, NEDEN TOPLU DÜZELTME DEĞİL: 28 dosyada 64 yer var ve
 * hepsi aynı ölçüde riskli değil. Bir kısmı `Promise.all` çıktısı
 * gibi yapı gereği güvenli. Hepsini tek turda değiştirmek, gerçek
 * riskli olanları da gürültünün içinde kaybederdi. Cırcır borcu
 * GÖRÜNÜR ve SAYILABİLİR tutuyor; düzeltme ayrı bir işin konusu.
 *
 * HATA SINIRI BU CIRCIRIN YERİNE GEÇMEZ. Sınır çöküşü EKRANA
 * çeviriyor, çöküşü ortadan kaldırmıyor. İkisi ayrı katman.
 */

const KOK = join(__dirname, "..");
const DIZINLER = ["app", "components", "lib", "services", "hooks"];

/** `a?.b.c` ve `a?.b[` — iç alan korumasız. */
const DESEN = /\?\.[A-Za-z_][A-Za-z0-9_]*(?:\[|\.[A-Za-z_])/g;

/** `a?.b?.c` — tam korumalı, sayılmaz. */
const TAM_KORUMALI = /^\?\.[A-Za-z_][A-Za-z0-9_]*\?\./;

function kaynaklar(dizin: string): string[] {
  const bulunan: string[] = [];

  let girdiler: string[];
  try {
    girdiler = readdirSync(dizin);
  } catch {
    return bulunan;
  }

  for (const girdi of girdiler) {
    if (girdi === "node_modules" || girdi === ".next") continue;

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...kaynaklar(yol));
      continue;
    }

    if (yol.endsWith(".ts") || yol.endsWith(".tsx")) bulunan.push(yol);
  }

  return bulunan;
}

function suanki(): Map<string, number> {
  const sayac = new Map<string, number>();

  for (const dizin of DIZINLER) {
    for (const yol of kaynaklar(join(KOK, dizin))) {
      const metin = readFileSync(yol, "utf8");
      let adet = 0;

      for (const satir of metin.split("\n")) {
        for (const eslesme of satir.matchAll(DESEN)) {
          const parca = satir.slice(eslesme.index ?? 0, (eslesme.index ?? 0) + 40);
          if (TAM_KORUMALI.test(parca)) continue;
          adet += 1;
        }
      }

      if (adet > 0) sayac.set(relative(KOK, yol), adet);
    }
  }

  return sayac;
}

function cizgi(): Map<string, number> {
  const m = new Map<string, number>();

  const satirlar = readFileSync(
    join(KOK, "tests", "bekci", "yarim-zincir-cizgi.txt"),
    "utf8",
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

describe("yarım isteğe bağlı zincir cırcırı", () => {
  const olculen = suanki();
  const temel = cizgi();

  /**
   * TARAMA BOŞA DÜŞMÜYOR.
   *
   * Sayaç bozulup boş dönerse "sayı artmadı" testi YEŞİL kalırdı —
   * boş küme her "artmadı" iddiasını doğrular. Bu programda aynı
   * tuzağa daha önce düşüldü (7a Sonda 5, ve "sıfır sonuç yokluğun
   * kanıtı değil" kuralı). Pozitif kontrol şart.
   */
  it("tarama boşa düşmüyor", () => {
    expect(olculen.size).toBeGreaterThan(15);
    expect(toplam(olculen)).toBeGreaterThan(30);
  });

  it("yarım zincir sayısı çizgiyi aşmıyor", () => {
    const artanlar = [...olculen.entries()]
      .filter(([d, n]) => n > (temel.get(d) ?? 0))
      .map(([d, n]) => `${d}: ${temel.get(d) ?? 0} -> ${n}`);

    expect(
      artanlar,
      `Yarım isteğe bağlı zincir toplamı ${toplam(olculen)}, ` +
        `çizgi ${toplam(temel)}.\nBu dosyalarda arttı:\n` +
        artanlar.join("\n") +
        "\n\n`a?.b.c` yerine `a?.b?.c` yazın: dış nesneyi korumak " +
        "yetmiyor, iç alan da gelmemiş olabilir.",
    ).toEqual([]);

    expect(toplam(olculen)).toBeLessThanOrEqual(toplam(temel));
  });

  /**
   * DÜZELTİLEN DOSYA ÇİZGİDEN SİLİNİR.
   *
   * Silinmeseydi çizgi gerçek borçtan büyük kalır ve bir gün yeni bir
   * ihlal o boşluğa sessizce sığardı.
   */
  it("çizgide artık yarım zincir taşımayan dosya kalmıyor", () => {
    const olmayan = [...temel.keys()].filter((d) => !olculen.has(d));

    expect(
      olmayan,
      "Bu dosyalar çizgide ama artık yarım zincir taşımıyorlar. SİLİN:\n" +
        olmayan.join("\n"),
    ).toEqual([]);
  });

  /**
   * KABUK TEMİZ KALIYOR.
   *
   * Cırcır toplamı koruyor ama tek bir dosyanın geri kayması toplamda
   * kaybolabilir (başka bir yerde iki düzeltme yapılmışsa). Kabuk her
   * ekranı sardığı için orada bir geri adım diğerlerinden ağırdır ve
   * ayrıca sınanıyor.
   */
  it("erp-shell yarım zincir taşımıyor", () => {
    expect(olculen.get("components/erp/erp-shell.tsx")).toBeUndefined();
  });
});
