import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * REDWOOD SÖZLEŞMESİ — bir kez temizlenen ekran temiz kalır.
 *
 * Yayma (A4) ekran ekran ilerliyor. Bir sayfa `design="redwood"`
 * yazdığı anda dört kuralı kabul etmiş olur. Bu kurallar testle
 * bağlanmasaydı, sıradaki geliştirme sırasında sayfaya yeniden
 * yerel bir Intl biçimleyici ya da bir window.confirm girer ve
 * kimse fark etmezdi — ekran "çalışmaya" devam ederdi.
 *
 * Kurallar yalnızca BAYRAĞI YAZAN sayfalara uygulanır: henüz
 * geçirilmemiş 145 sayfa bu testten etkilenmez, aksi halde yayma
 * tek hamlede yapılmak zorunda kalırdı.
 */

const ROOT = join(__dirname, "..");
const APP = join(ROOT, "app");

function pages(directory: string): string[] {
  const found: string[] = [];

  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);

    if (statSync(path).isDirectory()) {
      found.push(...pages(path));
      continue;
    }

    if (entry === "page.tsx") found.push(path);
  }

  return found;
}

const redwoodPages = pages(APP)
  .map((path) => ({
    path: path.slice(ROOT.length + 1),
    text: readFileSync(path, "utf8"),
  }))
  .filter((page) => page.text.includes('design="redwood"'));

describe("Redwood ekranları", () => {
  it("en az bir ekran geçirilmiş durumda", () => {
    // Bu olmadan aşağıdaki testler boş kümede koşup sessizce geçerdi.
    expect(redwoodPages.length).toBeGreaterThan(0);
  });

  /**
   * Sayfanın yükleme ve hata dalları da ayrı birer ErpShell açıyor.
   * Biri bayraksız kalırsa kullanıcı sayfa yüklenirken bir tasarımı,
   * yüklendikten sonra ötekini görür — göz kırpması gibi.
   */
  it("sayfadaki bütün ErpShell çağrıları bayraklı", () => {
    const mismatched = redwoodPages
      .map((page) => ({
        path: page.path,
        shells: (page.text.match(/<ErpShell/g) ?? []).length,
        flags: (page.text.match(/design="redwood"/g) ?? []).length,
      }))
      .filter((page) => page.shells !== page.flags)
      .map((page) => `${page.path} (${page.flags}/${page.shells})`);

    expect(mismatched).toEqual([]);
  });

  /**
   * Para biçimi tek yerden gelir. Sayfa kendi Intl.NumberFormat'ını
   * kurarsa ondalık sayısı ve simge konumu ekrandan ekrana değişir;
   * bugün düzeltilen "₺1.250,00" geri gelir.
   */
  it("kendi sayı biçimleyicisini kurmuyor", () => {
    const offenders = redwoodPages
      .filter((page) => page.text.includes("new Intl.NumberFormat"))
      .map((page) => page.path);

    expect(offenders).toEqual([]);
  });

  /**
   * Tarayıcı diyaloğu biçimlendirilemez, gerekçe zorunlu tutamaz ve
   * hata mesajını aynı yerde gösteremez. Geçirilen ekranlarda
   * Modal/ConfirmDialog/Drawer kullanılır.
   */
  it("tarayıcı diyaloğu kullanmıyor", () => {
    const pattern = /(?<![.\w])(?:window\.)?(?:confirm|prompt|alert)\s*\(/;

    const offenders = redwoodPages
      .filter((page) => pattern.test(page.text))
      .map((page) => page.path);

    expect(offenders).toEqual([]);
  });

  /**
   * Satır içi ham hex, tokendan kaçan renktir: marka rengi
   * değiştiğinde o hücre geride kalır. Anlam taşıyan renk için
   * `rw-value-danger` / `rw-value-success` gibi sınıflar var.
   */
  it("satır içi stilde ham hex renk taşımıyor", () => {
    const offenders = redwoodPages
      .filter((page) => /style=\{\{[^}]*#[0-9a-fA-F]{3,8}/.test(page.text))
      .map((page) => page.path);

    expect(offenders).toEqual([]);
  });
});
