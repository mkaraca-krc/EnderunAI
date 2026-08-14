import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * KULLANILAN HER SINIFIN BİR TANIMI OLMALI.
 *
 * NEDEN VAR: `erp-status orange` 11 dosyada kullanılıyordu ama CSS'te
 * hiç tanımlı değildi. Tanımsız sınıf hata vermez — rozet sessizce
 * biçimsiz düz yazıya döner. İş programındaki gecikme uyarısı,
 * bekleyen iade, serviste olan demirbaş ve bekleyen teklif aylardır
 * öyle görünüyordu ve kimse fark etmedi, çünkü ekran "çalışıyordu".
 *
 * Bu test o sessizliği bozar: yeni bir renk ya da yeni bir `rw-`
 * sınıfı yazıp CSS'e eklemeyi unutmak derleme değil, test hatasıdır.
 */

const ROOT = join(__dirname, "..");
const SOURCE_DIRECTORIES = ["app", "components"];
const CSS = readFileSync(join(ROOT, "app/globals.css"), "utf8");

function sourceFiles(directory: string): string[] {
  const full = join(ROOT, directory);
  const found: string[] = [];

  for (const entry of readdirSync(full)) {
    const path = join(full, entry);

    if (statSync(path).isDirectory()) {
      found.push(...sourceFiles(join(directory, entry)));
      continue;
    }

    if (entry.endsWith(".tsx") || entry.endsWith(".ts")) found.push(path);
  }

  return found;
}

const sources = SOURCE_DIRECTORIES.flatMap(sourceFiles).map((path) => ({
  path: path.slice(ROOT.length + 1),
  text: readFileSync(path, "utf8"),
}));

/**
 * CSS'teki seçicilerin listesi. Yorumlar önce çıkarılıyor: içlerinde
 * geçen sınıf adları seçici sanılırsa test hem yanlış yerde düşer hem
 * de olmayan bir tanımı varmış gibi gösterir.
 */
const SELECTORS = [
  ...CSS.replace(/\/\*[\s\S]*?\*\//g, "").matchAll(/([^{}]+)\{/g),
]
  .flatMap((match) => match[1].split(","))
  .map((selector) => selector.trim())
  .filter(Boolean);

describe("durum rozeti renkleri", () => {
  it("kullanılan her varyantın CSS karşılığı var", () => {
    const used = new Map<string, string[]>();

    for (const file of sources) {
      for (const match of file.text.matchAll(/erp-status ([a-z]+)/g)) {
        const variant = match[1];
        used.set(variant, [...(used.get(variant) ?? []), file.path]);
      }
    }

    expect(used.size).toBeGreaterThan(0);

    /*
      TANIM KAPSAMSIZ OLMALI. `.rw .erp-status.orange` varsa metin
      araması tatmin olurdu ama klasik ekranlardaki rozet yine
      biçimsiz kalırdı — yani testi geçen kod hâlâ hatalı olurdu.
      Bu yüzden seçicinin kendisine bakılıyor.
    */
    const defined = new Set(
      SELECTORS.filter((selector) =>
        /^\.erp-status\.[a-z]+$/.test(selector),
      ).map((selector) => selector.split(".")[2]),
    );

    const missing = [...used.entries()]
      .filter(([variant]) => !defined.has(variant))
      .map(([variant, files]) => `${variant} (${files.length} dosyada)`);

    expect(missing).toEqual([]);
  });
});

describe("Redwood sınıfları", () => {
  it("kullanılan her rw- sınıfı globals.css'te tanımlı", () => {
    const used = new Set<string>();

    for (const file of sources) {
      // className="..." içinde geçen rw- ile başlayan sınıf adları.
      for (const match of file.text.matchAll(/\brw-[a-z0-9-]+/g)) {
        used.add(match[0]);
      }
    }

    expect(used.size).toBeGreaterThan(0);

    const missing = [...used].filter(
      (className) => !CSS.includes(`.${className}`),
    );

    expect(missing).toEqual([]);
  });

  /**
   * KAPSAM KORUMASI: `.rw` altında olmayan bir Redwood kuralı,
   * tasarım dilini istemeden tüm uygulamaya sızdırır. Referans
   * ekranlar onaylanmadan 175 sayfanın görünümü değişmemeli.
   */
  it("rw- kuralları .rw kapsamının dışına taşmaz", () => {
    const leaked = SELECTORS.filter(
      (selector) =>
        selector.includes("rw-") &&
        !selector.startsWith("@") &&
        // Kapsam ya `.rw ` ile başlar ya da sınıfın kendisi bağımsız
        // bir düzen sarmalayıcısıdır (.rw-stats, .rw-filters gibi) —
        // onlar yalnızca Redwood ekranlarında yazılıyor.
        !selector.startsWith(".rw ") &&
        !selector.startsWith(".rw-"),
    );

    expect(leaked).toEqual([]);
  });
});
