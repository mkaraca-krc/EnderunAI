import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * YAZDIRMA STİLİ TEK KAYNAKTAN GELİR.
 *
 * NEDEN VAR: denetimde `globals.css` içinde TEK BİR `@media print`
 * kuralı yoktu. İhtiyacı olan sayfalar stilini kendi içine yazmıştı ve
 * üçünde unutulmuştu — `gunluk-puantaj` ve `zimmetler` Ctrl+P'de menüyü
 * ve kenar çubuğunu kâğıda basıyordu, `hakedis/[id]/yazdir` ise kendi
 * yazdır düğmesini basıyordu.
 *
 * Bu test o kuralın var olduğunu ve gezinme öğelerini kapsadığını
 * sabitler. Kural silinirse ya da kabuk sınıfı eklenip yazdırmadan
 * gizlenmesi unutulursa burada düşer.
 */

const ROOT = join(__dirname, "..");
const CSS = readFileSync(join(ROOT, "app/globals.css"), "utf8");

function printBlock(): string {
  const start = CSS.indexOf("@media print");
  expect(start, "globals.css içinde @media print bloğu yok").toBeGreaterThan(-1);

  let depth = 0;
  for (let i = CSS.indexOf("{", start); i < CSS.length; i++) {
    if (CSS[i] === "{") depth++;
    else if (CSS[i] === "}") {
      depth--;
      if (depth === 0) return CSS.slice(start, i + 1);
    }
  }

  throw new Error("@media print bloğu kapanmamış");
}

describe("yazdırma sözleşmesi", () => {
  const block = printBlock();

  it("kabuk gezinmesi kâğıda basılmaz", () => {
    // ErpShell'in kâğıtta işi olmayan parçaları.
    for (const selector of [
      ".erp-sidebar",
      ".erp-topbar",
      ".erp-breadcrumb",
      ".erp-mobile-menu-button",
    ]) {
      expect(block, `${selector} yazdırmada gizlenmeli`).toContain(selector);
    }
  });

  it("eylem düğmeleri kâğıda basılmaz", () => {
    // Kâğıtta düğme tıklanamaz bir dikdörtgendir.
    expect(block).toMatch(/\bbutton\b/);
  });

  it("no-print ve print-only kancaları tanımlı", () => {
    expect(block).toContain(".no-print");
    expect(block).toContain(".print-only");
    // print-only ekranda GİZLİ olmalı, yoksa her sayfada görünür.
    expect(CSS).toMatch(/\.print-only\s*\{\s*display:\s*none/);
  });

  it("yatay kayan tablo kâğıtta kırpılmaz", () => {
    expect(block).toContain("overflow: visible");
  });

  it("tablo başlığı her sayfada tekrarlanır", () => {
    expect(block).toContain("table-header-group");
  });
});
