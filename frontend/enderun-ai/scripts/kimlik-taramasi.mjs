#!/usr/bin/env node
/*
 * Kurumsal kimlik taraması.
 *
 * Build'den önce çalışır (package.json -> prebuild), bulgu varsa build
 * durur. Amacı tek bir şeyi engellemek: tıklanabilir bir öğenin marka
 * turkuazı yerine nötr koyu griyle çıkması.
 *
 * Bu projede Tailwind'in "slate" paleti turkuaz tonlu NÖTR gri olarak
 * yeniden tanımlı (globals.css @theme). Metin, kenarlık ve zemin için
 * doğru; ama butonda kullanıldığında ekran kimliğe yabancı görünüyor.
 * Marka rengi "brand" ölçeğinde: brand-700 (#18797c) = --erp-primary.
 *
 * Modal perdeleri (bg-slate-950/40) ve yazdırma sayfaları hariç.
 */

import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";

const ROOT = process.cwd();
const SCAN_DIRS = ["app", "components"];

/** Yazdırma ekranları kağıt kontrastı için koyu kalır. */
const EXEMPT = [/\/yazdir\//, /\/portal\//];

/**
 * Buton kalıbı: koyu zemin + yatay/iç padding. Perdelerde padding
 * bulunmaz ve renk her zaman saydamlık ekiyle yazılır (/40 gibi), bu
 * yüzden kalıba takılmazlar.
 */
const PATTERNS = [
  {
    regex: /bg-slate-(?:800|900|950)\s+(?:px-|p-)\d/g,
    message:
      "Koyu zeminli buton. Marka turkuazı kullanın: bg-brand-700 " +
      "(hover:bg-brand-600).",
  },
  {
    regex: /file:bg-slate-(?:800|900|950)/g,
    message: "Dosya seçme butonu koyu. file:bg-brand-700 kullanın.",
  },
  {
    regex: /hover:bg-slate-(?:800|900)\b/g,
    message: "Buton hover rengi nötr gri. hover:bg-brand-600 kullanın.",
  },
];

function walk(dir, files = []) {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);

    if (entry === "node_modules" || entry === ".next") continue;

    if (statSync(full).isDirectory()) {
      walk(full, files);
    } else if (full.endsWith(".tsx")) {
      files.push(full);
    }
  }

  return files;
}

const findings = [];

for (const dir of SCAN_DIRS) {
  const base = join(ROOT, dir);

  for (const file of walk(base)) {
    const relativePath = relative(ROOT, file);

    if (EXEMPT.some((pattern) => pattern.test(`/${relativePath}`))) continue;

    const content = readFileSync(file, "utf8");
    const lines = content.split("\n");

    for (const { regex, message } of PATTERNS) {
      lines.forEach((line, index) => {
        regex.lastIndex = 0;
        if (regex.test(line)) {
          findings.push(`${relativePath}:${index + 1} — ${message}`);
        }
      });
    }
  }
}

if (findings.length > 0) {
  console.error("\nKurumsal kimlik taraması başarısız:\n");
  for (const finding of findings) console.error(`  ${finding}`);
  console.error(
    `\n${findings.length} bulgu. Marka ölçeği: brand-50 ... brand-950 ` +
      "(brand-700 = #18797c, --erp-primary ile aynı).\n"
  );
  process.exit(1);
}

console.log("Kurumsal kimlik taraması temiz.");
