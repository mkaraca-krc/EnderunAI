import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * TARAYICI DİYALOGLARI — ŞİMDİLİK SAYIM MODUNDA.
 *
 * window.confirm / window.prompt / window.alert uygulama içi diyalogla
 * değiştirilecek: tarayıcı penceresi biçimlendirilemiyor, gerekçeyi
 * zorunlu tutamıyor, hata mesajını aynı yerde gösteremiyor ve
 * erişilebilir değil. Uygulamada bunların yerine Modal, ConfirmDialog
 * ve Drawer var.
 *
 * BU TEST HENÜZ YASAK KOYMUYOR. Bugün 50 dosyada 98 kullanım var
 * (72 confirm, 26 prompt); hepsini tek pakette değiştirmek uygulamanın
 * yarısına aynı anda dokunmak olurdu. Test şimdilik sayıyor ve sayının
 * ARTMAMASINI garanti ediyor: yeni kod eklerken tarayıcı diyaloğu
 * kullanılırsa tavan aşılır ve test kırmızıya döner.
 *
 * Yayma adımları bittiğinde tavan sıfıra çekilecek ve bu test gerçek
 * bir yasağa dönüşecek.
 */

const ROOTS = ["app", "components", "lib", "services"];

/**
 * Bugünkü sayım. Yayma adımlarında DÜŞECEK; hiçbir zaman artmamalı.
 * Sayı düştüğünde bu tavan da düşürülür (test bunu ayrıca söylüyor).
 */
const CURRENT_CEILING = 60;

const PATTERN = /(?<![.\w])(?:window\.)?(confirm|prompt|alert)\s*\(/g;

function collectFiles(dir: string): string[] {
  const entries = readdirSync(dir);
  const files: string[] = [];

  for (const entry of entries) {
    const full = join(dir, entry);

    if (statSync(full).isDirectory()) {
      files.push(...collectFiles(full));
      continue;
    }

    if (/\.(tsx?|jsx?)$/.test(entry)) files.push(full);
  }

  return files;
}

function scan() {
  const hits: { file: string; count: number }[] = [];

  for (const root of ROOTS) {
    for (const file of collectFiles(root)) {
      // Bileşenin KENDİSİ tarayıcı diyaloğunun yerine geçiyor; içindeki
      // "confirm" sözcükleri (confirmLabel, onConfirm) sayılmamalı.
      if (file.includes(join("components", "ui"))) continue;

      const source = readFileSync(file, "utf8");
      const matches = source.match(PATTERN);

      if (matches?.length) hits.push({ file, count: matches.length });
    }
  }

  return hits;
}

describe("tarayıcı diyalogları", () => {
  it("kullanım sayısı tavanı aşmıyor", () => {
    const hits = scan();
    const total = hits.reduce((sum, hit) => sum + hit.count, 0);

    expect(
      total,
      `Tarayıcı diyaloğu kullanımı arttı (${total} > ${CURRENT_CEILING}). ` +
        "Yeni kodda window.confirm/prompt/alert yerine ConfirmDialog, " +
        "Modal ya da Drawer kullanın.",
    ).toBeLessThanOrEqual(CURRENT_CEILING);
  });

  /**
   * Sayı düştüğünde tavanı da düşürmeyi hatırlatır: tavan gerçek
   * sayının çok üstünde kalırsa koruma gevşer ve yeni kullanımlar
   * sessizce sızar.
   */
  it("tavan gerçek sayıya yakın kalıyor", () => {
    const hits = scan();
    const total = hits.reduce((sum, hit) => sum + hit.count, 0);

    expect(
      CURRENT_CEILING - total,
      `Tavan (${CURRENT_CEILING}) gerçek sayının (${total}) çok üstünde; ` +
        "CURRENT_CEILING değerini güncel sayıya çekin.",
    ).toBeLessThanOrEqual(10);
  });
});
