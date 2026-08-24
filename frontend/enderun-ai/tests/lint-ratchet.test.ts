import { execFileSync } from "node:child_process";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * LINT CIRCIRI — `react-hooks/set-state-in-effect`.
 *
 * NEDEN CIRCIR, NEDEN TEMİZLİK DEĞİL: kural, efektin çağırdığı
 * fonksiyonun İÇİNE bakıyor; setState'in senkron olup olmaması fark
 * etmiyor. Ölçüldü — senkron çağrılar efekt yolundan çıkarıldı ve
 * ihlal 1'den 1'e kaldı, DÜŞMEDİ. Efektten veri çekip durum yazmanın
 * bu kuralla uyumlu bir biçimi yok. Düzgün çözüm bir veri çekme
 * katmanı ya da ilk veriyi sunucu bileşeninden geçirmek; ikisi de
 * mimari değişiklik ve 110 dosya bu desende.
 *
 * SUSTURMA İHLAL SAYILIR. `eslint-disable` yorumu ihlali ortadan
 * kaldırmaz, yalnız gizler. Sayılmasaydı çizgi bir gün "0" görünür ve
 * hiçbir şey ölçmezdi — kaçış yolu olmasın diye ikisi toplanıyor.
 *
 * ESLINT BURADAN ÇAĞRILIYOR (lint ayrı bir adım değil): safe-deploy
 * `npm run test` koşuyor ama `npm run lint` KOŞMUYOR. Cırcır lint
 * adımında dursaydı otomatik kapı olmazdı.
 */

const KOK = join(__dirname, "..");
const KURAL = "react-hooks/set-state-in-effect";
const DIZINLER = ["app", "components", "services", "lib"];

type EslintDosya = {
  filePath: string;
  messages: { ruleId: string | null }[];
};

function eslintIhlalleri(): Map<string, number> {
  /*
   * ESLINT HATA BULUNCA ÇIKIŞ KODU 1 DÖNER ve execFileSync FIRLATIR.
   * Çıktı yine de `stdout`'ta durur — fırlatmayı yutmak değil,
   * çıktıyı oradan almak gerekiyor. Yutulsaydı ve boş dizi
   * dönseydi cırcır her koşuda "0 ihlal" görüp sessizce yeşil
   * kalırdı; tam da bu testin yakalamak istediği durum.
   */
  let ciktı: string;

  try {
    ciktı = execFileSync(
      "npx",
      ["eslint", ...DIZINLER, "-f", "json"],
      { cwd: KOK, encoding: "utf8", maxBuffer: 64 * 1024 * 1024 }
    );
  } catch (hata) {
    const stdout = (hata as { stdout?: string }).stdout;

    if (typeof stdout !== "string" || stdout.trim().length === 0) {
      throw new Error(
        "eslint çalıştırılamadı ve çıktı da yok. Cırcır ölçemediği " +
          "için YEŞİL KALMAMALI:\n" + String(hata)
      );
    }

    ciktı = stdout;
  }

  const sonuc: EslintDosya[] = JSON.parse(ciktı);
  const sayac = new Map<string, number>();

  for (const dosya of sonuc) {
    const adet = dosya.messages.filter((m) => m.ruleId === KURAL).length;
    if (adet > 0) {
      sayac.set(relative(KOK, dosya.filePath), adet);
    }
  }

  return sayac;
}

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

    if (/\.(tsx|ts)$/.test(girdi)) bulunan.push(yol);
  }

  return bulunan;
}

function susturmalar(): Map<string, number> {
  const sayac = new Map<string, number>();

  for (const d of DIZINLER) {
    for (const yol of kaynaklar(join(KOK, d))) {
      const adet = readFileSync(yol, "utf8")
        .split("\n")
        .filter((s) => s.includes("eslint-disable") && s.includes(KURAL))
        .length;

      if (adet > 0) sayac.set(relative(KOK, yol), adet);
    }
  }

  return sayac;
}

/** İhlal + susturma. */
function suanki(): Map<string, number> {
  const toplam = new Map(eslintIhlalleri());

  for (const [dosya, adet] of susturmalar()) {
    toplam.set(dosya, (toplam.get(dosya) ?? 0) + adet);
  }

  return toplam;
}

function cizgi(): Map<string, number> {
  const m = new Map<string, number>();

  const satirlar = readFileSync(join(KOK, "tests", "bekci", "lint-cizgi.txt"), "utf8")
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

// eslint çağrısı ~90 saniye sürüyor; tek seferde ölçülüp paylaşılıyor.
describe("lint cırcırı", () => {
  const olculen = suanki();
  const temel = cizgi();

  it("tarama boşa düşmüyor", () => {
    expect(olculen.size).toBeGreaterThan(50);
    expect(toplam(olculen)).toBeGreaterThan(100);
  });

  it("ihlal sayısı çizgiyi aşmıyor", () => {
    const artanlar = [...olculen.entries()]
      .filter(([d, n]) => n > (temel.get(d) ?? 0))
      .map(([d, n]) => `${d}: ${temel.get(d) ?? 0} -> ${n}`);

    expect(
      artanlar,
      `set-state-in-effect toplamı ${toplam(olculen)}, çizgi ${toplam(temel)}.\n` +
        "Bu dosyalarda arttı:\n" + artanlar.join("\n") +
        "\n\nSusturma da ihlal sayılır — `eslint-disable` yazmak çizgiyi " +
        "düşürmez. Efektten veri çekmek yerine mevcut desenlerden birini " +
        "kullanın ya da katman paketini bekleyin."
    ).toEqual([]);

    expect(toplam(olculen)).toBeLessThanOrEqual(toplam(temel));
  });

  it("çizgide artık ihlal taşımayan dosya kalmıyor", () => {
    const olmayan = [...temel.keys()].filter((d) => !olculen.has(d));

    expect(
      olmayan,
      "Bu dosyalar çizgide ama artık ihlal taşımıyorlar. SİLİN — " +
        "çizgi küçülmeli:\n" + olmayan.join("\n")
    ).toEqual([]);
  });
}, 300_000);
