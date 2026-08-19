import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * HAM `<table>` KULLANAN LİSTE EKRANI SAYISI — CIRCIR.
 *
 * NEDEN VAR: denetimde 143 liste ekranının hiçbirinde sayfalama yoktu,
 * 3'ü dosya indirebiliyordu, her ekran kendi tablosunu ayrı yazmıştı.
 * Standart bileşen (`components/ui/data-table.tsx`) o boşluğu
 * kapatıyor ama 60 ekranı bir fazda taşımak riskli — bu test taşınma
 * borcunu GÖRÜNÜR ve SAYILABİLİR tutuyor.
 *
 * Kural cırcır: sayı ARTAMAZ. Yeni bir ekran ham `<table>` ile
 * yazılırsa test düşer; bir faz ekran taşıyınca sınır ELLE düşürülür
 * ve bir daha yükselemez.
 *
 * NEDEN 60 MADDELİK GEREKÇE LİSTESİ DEĞİL: bu programda istisna
 * listeleri (bkz. `DataScopeSeamTests`) her maddenin ayrı bir KARAR
 * olduğu yerlerde işe yarıyor. Burada karar tek: "henüz taşınmadı".
 * Altmış kez aynı gerekçeyi yazmak listeyi okunmaz yapardı; sayı
 * daha dürüst.
 *
 * KAPSAM DIŞI (tablo var ama bileşen yanlış araç):
 * - AĞAÇ ekranları (hesap planı): sayfalama üst-alt ilişkisini kırar.
 * - IZGARA ekranları (puantaj takvimi): satır sayısı personel kadar,
 *   sütunlar gün.
 * - YAZDIRMA sayfaları: zaten çıktı, kabuk yok.
 * - DETAY alt tabloları (`[id]` altındakiler): birkaç satırlık alt
 *   liste; sayfalama gürültü olurdu.
 */

const ROOT = join(__dirname, "..");
const APP = join(ROOT, "app");

/**
 * Bu sayı yalnızca AŞAĞI iner. Bir faz ekran taşıdığında elle
 * düşürülür; yükseltmek için gerekçe yazılmalıdır.
 */
const HAM_TABLO_UST_SINIRI = 63;

function pages(dir: string): string[] {
  const found: string[] = [];

  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);

    if (statSync(path).isDirectory()) {
      found.push(...pages(path));
      continue;
    }

    if (entry === "page.tsx") found.push(path);
  }

  return found;
}

function hamTabloEkranlari(): string[] {
  return pages(APP)
    .filter((path) => {
      const code = readFileSync(path, "utf8");
      const rel = relative(APP, path).replace("/page.tsx", "");

      if (!code.includes("<table")) return false;

      /*
       * ÖLÇÜT: BİLEŞENİN KULLANILMASI, adının geçmesi değil.
       *
       * İlk sürüm `code.includes("DataTable")` diyordu ve SONDA
       * KAÇIRDI: bir ekran ham `<table>`a geri döndüğünde bile
       * `import` satırı ve `DataTableColumn` tipi dosyada kalıyor,
       * ekran sayımdan düşüyordu. Yani cırcır korumuş gibi görünüp
       * hiçbir şey korumuyordu.
       */
      if (/<DataTable[\s/>]/.test(code)) return false;

      // Kapsam dışı türler.
      if (/expandedIds|TreeNode/.test(code)) return false;
      if (code.includes("@media print")) return false;
      if (rel.endsWith("/yazdir") || rel.endsWith("/tutanak")) return false;
      if (rel.includes("[")) return false;

      return true;
    })
    .map((path) => relative(APP, path).replace("/page.tsx", ""));
}

describe("liste bileşeni cırcırı", () => {
  it("ham <table> kullanan liste ekranı sayısı artmıyor", () => {
    const ekranlar = hamTabloEkranlari();

    expect(
      ekranlar.length,
      `Ham <table> kullanan liste ekranı sayısı ${HAM_TABLO_UST_SINIRI} ` +
        `sınırını aştı (${ekranlar.length}). Yeni liste ekranları ` +
        `components/ui/data-table.tsx kullanmalı — sayfalama, dışa ` +
        `aktarma ve yazdırma oradan geliyor.\n\nEkranlar:\n` +
        ekranlar.join("\n")
    ).toBeLessThanOrEqual(HAM_TABLO_UST_SINIRI);
  });

  it("sınır gerçek sayıya yakın kalıyor", () => {
    /*
     * Cırcır ancak SIKI olduğunda işe yarar. Sınır gerçek sayının çok
     * üstünde kalırsa yeni ham tablolar sessizce eklenir ve test hiç
     * düşmez — koruma görüntüsü, koruma değil.
     */
    const ekranlar = hamTabloEkranlari();

    expect(
      HAM_TABLO_UST_SINIRI - ekranlar.length,
      `Sınır (${HAM_TABLO_UST_SINIRI}) gerçek sayıdan ` +
        `(${ekranlar.length}) fazla yüksek. Sınırı düşürün.`
    ).toBeLessThanOrEqual(2);
  });

  it("taşınmış ekranlar geri dönmüyor", () => {
    // F2/F3'te taşınanlar. Ham tabloya dönerlerse sayfalamayı
    // kaybederler ve kimse fark etmez.
    const tasinanlar = [
      // F2/F3
      "depo-stok/hareketler",
      "hakedis",
      "muhasebe/faturalar",
      "muhasebe/satis-faturalari",
      "muhendislik/pozlar",
      "sistem-yonetimi/denetim-kayitlari",
      // F4a
      "sirketler",
      "subeler",
      "kesifler",
      "metrajlar",
      "muhasebe/fisler",
      "depo-stok/depolar",
      "depo-stok/iadeler",
    ];

    const ham = new Set(hamTabloEkranlari());

    for (const ekran of tasinanlar) {
      expect(ham.has(ekran), `${ekran} ham <table>'a geri dönmüş`).toBe(false);
    }
  });
});
