import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * PANEL KABUKTA TAKILI, SAYFADA DEĞİL — VE KENDİ HATA SINIRINDA.
 *
 * Davranış testi (`mesaj-paneli-kabukta.test.tsx`) panelin kabukta
 * OLMASI HÂLİNDE taslağın korunduğunu gösteriyor. Bu test, panelin
 * GERÇEKTEN orada olduğunu tutuyor. İkisi ayrı sorular:
 * biri "böyle olursa ne olur", diğeri "böyle mi".
 *
 * ═══ İKİ AYRI ŞART ═══
 *
 * 1. `<MesajBaloncugu` YALNIZ `erp-shell.tsx` içinde geçer. Bir sayfa
 *    bileşenine eklenirse rota değişiminde sökülür.
 * 2. Kabukta KENDİ hata sınırında sarılıdır. `nerede="içerik"` sınırı
 *    yalnız `children`'ı kapsıyor; panel onun dışında olduğu için
 *    sarılmazsa bir render hatası TÜM ERP KABUĞUNU düşürürdü.
 */

const ROOT = join(__dirname, "..");
const KOK_LAYOUT = join(ROOT, "app", "layout.tsx");
const KABUK = join(ROOT, "components", "erp", "erp-shell.tsx");

function tsxDosyalari(dizin: string): string[] {
  const bulunan: string[] = [];

  for (const girdi of readdirSync(dizin)) {
    if (girdi === "node_modules" || girdi === ".next") continue;

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...tsxDosyalari(yol));
      continue;
    }

    if (/\.tsx$/.test(girdi)) bulunan.push(yol);
  }

  return bulunan;
}

describe("mesaj paneli yerleşim sözleşmesi", () => {
  it("kök layout baloncuğu takıyor (POZİTİF KONTROL)", () => {
    // Tarama bozulursa aşağıdaki testler boş kümede yeşil döner
    // ve hiçbir şey ölçmezler (Kural 48).
    expect(readFileSync(KOK_LAYOUT, "utf8")).toContain("<MesajBaloncugu");
  });

  it("baloncuk YALNIZ kök layout'ta — kabukta ve sayfalarda YOK", () => {
    /*
     * YÖN TERSİNE ÇEVRİLDİ (2026-09-07).
     *
     * Önceki sürüm *"baloncuk yalnız erp-shell'de"* diyordu ve o iddia
     * YANLIŞ BİR TEMELE dayanıyordu: kabuğun rota değişiminde ayakta
     * kaldığı sanılıyordu. Ölçüldü — kalmıyor; her sayfa kendi
     * `<ErpShell>`'ini kuruyor ve alt ağaç tamamen sökülüyor.
     *
     * İKİ BALONCUK TEHLİKESİ: kabuktaki çağrı kaldırılmazsa 173
     * sayfanın her birinde ikinci bir baloncuk doğar.
     */
    const suclular = [
      ...tsxDosyalari(join(ROOT, "app")),
      KABUK,
    ]
      .filter((yol) => yol !== KOK_LAYOUT)
      .filter((yol) => readFileSync(yol, "utf8").includes("<MesajBaloncugu"))
      .map((yol) => yol.replace(ROOT + "/", ""));

    expect(
      suclular,
      "Baloncuk yalnız `app/layout.tsx`'te takılabilir. Başka bir yerde:\n" +
        "  · sayfa ağacındaysa rota değişiminde SÖKÜLÜR (taslak gider,\n" +
        "    canlı bağlantı kopar),\n" +
        "  · kabukta da kalırsa 173 sayfada İKİNCİ BALONCUK doğar.\n  - " +
        suclular.join("\n  - ")
    ).toEqual([]);
  });

  it("baloncuk kök layout'ta kendi hata sınırında sarılı", () => {
    const kok = readFileSync(KOK_LAYOUT, "utf8");

    const sinir = kok.indexOf('nerede="mesaj-paneli"');
    const baloncuk = kok.indexOf("<MesajBaloncugu");

    expect(sinir, 'nerede="mesaj-paneli" hata sınırı bulunamadı').toBeGreaterThan(-1);
    expect(baloncuk).toBeGreaterThan(sinir);

    // Kökte başka bir hata sınırı yok; sarılmazsa panel çökünce
    // uygulamanın TAMAMI düşer.
    const arada = kok.slice(sinir, baloncuk);
    expect(
      arada.includes("<HataSiniri"),
      "Baloncuk ile kendi hata sınırı arasında başka bir sınır açılmış."
    ).toBe(false);
  });

  it("oturum ve /portal kapıları baloncuğun İÇİNDE", () => {
    /*
     * Kapılar kök layout'a değil bileşenin içine kondu: kök layout bir
     * sunucu bileşeni ve orada rota listesi tutmak, unutulacak bir şey
     * daha demekti.
     */
    const kaynak = readFileSync(
      join(ROOT, "components", "mesajlar", "mesaj-baloncugu.tsx"),
      "utf8"
    );

    expect(kaynak, "oturum yüklenirken render engellenmiyor").toContain(
      "if (oturumYukleniyor) return null"
    );
    expect(kaynak, "oturumsuz kullanıcıya panel gösteriliyor").toContain(
      "if (!user) return null"
    );
    expect(kaynak, "/portal istisnası yok").toContain("PANEL_YASAK_ONEK");
  });
});
