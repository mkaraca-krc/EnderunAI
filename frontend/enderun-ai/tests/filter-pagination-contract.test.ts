import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * FİLTRESİ OLAN HER SAYFALI LİSTE, FİLTRE DEĞİŞİNCE SAYFA 1'E DÖNER.
 *
 * NEDEN VAR — ve bu boşluğu BU PROGRAM AÇTI: F4'te on ekrana
 * sayfalama eklendi ama `resetKey` bağlanmadı. Sayfalamadan önce
 * böyle bir hata yoktu; ekleyince ortaya çıktı.
 *
 * Belirti sinsi: `DataTable` sayfa numarasını sayfa sayısına
 * sıkıştırdığı için kullanıcı BOŞ ekran görmüyor — filtrelenmiş
 * sonucun SON sayfasını görüyor. Yani ekran çalışıyor, sadece yanlış
 * yerde duruyor ve kullanıcı "aradığım kayıt yok" diye düşünüyor.
 *
 * Bu test, filtre durumu taşıyan her `DataTable` ekranının `resetKey`
 * geçirmesini zorunlu kılar.
 */

const ROOT = join(__dirname, "..");
const APP = join(ROOT, "app");

/**
 * Filtre durumu sayılan `useState` adları.
 *
 * SEKME DE FİLTREDİR. İlk sürüm `tab` ve `view` adlarını dışarıda
 * bırakıyordu; `teklifler/takip` gibi sekmeyle süzen ekranlar
 * (açık / kazanılan / kaybedilen) bu yüzden zorunluluğun DIŞINDA
 * kalıyordu — sekme değiştirmek de listeyi daraltır ve sayfa 1'e
 * dönmesi gerekir.
 */
const FILTRE_DESENI =
  /const \[\s*(search|query|status\w*|companyId|projectId|entityType|direction\w*|filter\w+|personnelId|startDate|endDate|from|to|type|month|year|siteId|discipline|source|tab|activeTab|view|mode|kind|period)\s*,/;

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

describe("filtre ↔ sayfalama sözleşmesi", () => {
  it("filtresi olan her DataTable ekranı resetKey geçiriyor", () => {
    const eksik: string[] = [];

    for (const path of pages(APP)) {
      const code = readFileSync(path, "utf8");

      if (!/<DataTable[\s/>]/.test(code)) continue;
      if (!FILTRE_DESENI.test(code)) continue;
      if (code.includes("resetKey")) continue;

      eksik.push(relative(APP, path).replace("/page.tsx", ""));
    }

    expect(
      eksik,
      "Bu ekranlarda filtre var ama DataTable'a resetKey geçilmiyor. " +
        "Kullanıcı 7. sayfadayken filtreyi daraltınca sayfa 1'e DÖNMEZ; " +
        "filtrelenmiş sonucun son sayfasında kalır ve aradığı kaydı " +
        "bulamaz.\n\n" +
        eksik.join("\n")
    ).toEqual([]);
  });

  it("sunucu kipindeki ekranlar sayfayı da 1'e çekiyor", () => {
    /*
     * SUNUCU KİPİNDE İKİ AYRI SIFIRLAMA GEREKİYOR:
     * (a) DataTable'ın görünümü — `resetKey` ile,
     * (b) İSTEĞİN kendisi — ekran `page` durumunu 1'e çekmeli.
     *
     * Yalnız (a) yapılırsa uca hâlâ eski sayfa gider ve BOŞ sayfa
     * döner; görünüm 1. sayfayı gösterirken içerik 7. sayfanın
     * (boş) sonucudur.
     */
    const sunucuKipli: string[] = [];

    for (const path of pages(APP)) {
      const code = readFileSync(path, "utf8");
      if (!/<DataTable[\s/>]/.test(code)) continue;
      if (!code.includes("server={{")) continue;

      sunucuKipli.push(path);

      const rel = relative(APP, path).replace("/page.tsx", "");

      /*
       * İKİ MEŞRU DESEN VAR:
       *  - sayfa bileşen durumunda tutuluyorsa `setPage(1)`,
       *  - sayfa URL'de tutuluyorsa sorgu parametresi 1'e çekilir
       *    (`sayfa: 1`). URL deseni daha güçlü: yenilemede ve
       *    paylaşılan bağlantıda da aynı yerde kalınır.
       *
       * Ölçüt "hangi yazım" değil, "istek 1. sayfaya dönüyor mu".
       */
      const sayfaSifirlaniyor =
        /setPage\(1\)/.test(code) || /sayfa:\s*1\b/.test(code);

      expect(
        sayfaSifirlaniyor,
        `${rel} sunucu kipinde; filtre değişince İSTEĞİN sayfasını da ` +
          `1'e çekmeli — durum bileşendeyse setPage(1), URL'deyse ` +
          `sorgu parametresi (sayfa: 1).`
      ).toBe(true);
    }

    // Testin boşa dönmediğini garanti et.
    expect(sunucuKipli.length).toBeGreaterThan(0);
  });

  /*
   * GERÇEK LİSTE EKRANLARI SUNUCU KİPİNDE KALMALI.
   *
   * Yukarıdaki test yalnız `server={{` BİLDİREN ekranlara bakıyor;
   * sunucu kipini tamamen bırakan bir ekran kuralın dışına çıkıyordu.
   * Sonda gösterdi: mal kabul ekranından `server` bloğunu sildim,
   * iki sözleşme testi de geçmeye devam etti.
   *
   * "Gerçek liste" = kayıt sayısı zamanla büyüyen küme. Bu ekranlarda
   * tümünü çekip ön yüzde dilimlemek, listeyi sessizce kırpar ya da
   * tarayıcıyı kilitler — poz kütüphanesinde (23.531 kayıt) tam olarak
   * bu yaşandı.
   *
   * Liste F4 ilerledikçe UZAR; kısalması ancak bir ekranın gerçekten
   * liste olmadığına karar verilirse ve gerekçesi yazılırsa meşrudur.
   */
  const SUNUCU_KIPI_ZORUNLU = [
    "depo-stok/mal-kabul",
  ];

  it("gerçek liste ekranları sunucu kipini bırakmıyor", () => {
    const eksik: string[] = [];

    for (const rel of SUNUCU_KIPI_ZORUNLU) {
      const code = readFileSync(join(APP, rel, "page.tsx"), "utf8");

      const sunucuKipi =
        /<DataTable[\s/>]/.test(code) && code.includes("server={{");

      if (!sunucuKipi) eksik.push(rel);
    }

    expect(
      eksik,
      "Bu ekranlar GERÇEK LİSTE ve sunucu kipinde olmak zorunda: " +
        eksik.join(", ") +
        ". Tümünü çekip ön yüzde dilimlemek listeyi sessizce kırpar. " +
        "Ekran gerçekten liste değilse listeden GEREKÇESİYLE çıkarın."
    ).toEqual([]);
  });
});
