import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * HER İÇERİK SAYFASI ERP KABUĞUNUN İÇİNDE YAŞAR.
 *
 * ═══ DOĞURAN ARIZA (2026-09-06) ═══
 *
 * Mesaj paneli kabuğa takıldı ve "panel her ekranda açık kalır" diye
 * yazıldı. Mehmet ölçtü: `/gorevler`de kenar çubuğu, üst çubuk, Hızır
 * ve mesaj baloncuğu vardı; **`/yapilacaklar`da hiçbiri yoktu.**
 * Erişilebilirlik ağacı yalnız sayfa içeriğini döndürüyordu.
 *
 * İddia yanlıştı ve bunu hiçbir test yakalayamazdı: yapı testi
 * *"baloncuk yalnız `erp-shell`de"* diyordu ama **hiçbir test "her
 * sayfa `erp-shell` kullanır" demiyordu.** Boşluk tam oradaydı.
 *
 * Ölçümde iki ekran çıktı: `/yapilacaklar` ve `/mesajlar`.
 *
 * ═══ NEDEN BU KADAR KOLAY OLDU ═══
 *
 * Ortak bir `layout.tsx` yok; kabuk her sayfaya TEK TEK ekleniyor
 * (189 sayfanın 172'sinde). Eklemeyi unutmak sessizce geçiyor ve
 * ekran yine çalışıyor — yalnız menüsüz.
 *
 * ═══ DEVİR SAYILIR ═══
 *
 * Bazı sayfalar iki satır: kabuğu KULLANAN bir bileşene devrediyorlar
 * (`inventory-movement-form`, `secretariat-registry-page`). Onları
 * ihlal saymak, çalışan bir deseni bozmak olurdu. Tarama bir kademe
 * devri izliyor — ölçüldü, gerçekte kullanılan derinlik bu.
 */

const ROOT = join(__dirname, "..");

/**
 * MUAFİYET LİSTESİ — HER SATIRIN GEREKÇESİ VAR.
 *
 * Gerekçesiz muafiyet, bir süre sonra kimsenin neden orada olduğunu
 * bilmediği bir satırdır (aynı disiplin: `MuafUclar.txt`, açık
 * veritabanı beyaz listesi).
 */
const MUAF: Record<string, string> = {
  "app/page.tsx":
    "Kök sayfa; yalnız yönlendirme yapıyor, içerik render etmiyor.",
  "app/login/page.tsx":
    "Giriş ekranı. Kabuk oturum gerektiriyor; giriş öncesinde menü olamaz.",
  "app/yetkisiz/page.tsx":
    "Yetkisiz sayfası. Menüyü göstermek, erişilemeyen yerleri listelemek olurdu.",
  "app/portal/[token]/page.tsx":
    "Dış portal: oturumu olmayan tedarikçi/taşeron açıyor. ERP menüsü gösterilemez.",
  "app/hakedis/[id]/yazdir/page.tsx":
    "Yazdırma görünümü. Menü ve baloncuklar kâğıda basılırdı.",
  "app/satin-alma/siparis/[id]/yazdir/page.tsx":
    "Yazdırma görünümü. Aynı gerekçe.",
  "app/teklifler/[id]/yazdir/page.tsx":
    "Yazdırma görünümü; gerekçe dosyanın kendi başlığında yazılı (antet kâğıda basılmamalı).",
  "app/insan-kaynaklari/zimmetler/[id]/tutanak/page.tsx":
    "Tutanak çıktısı; gerekçe dosyanın kendi başlığında yazılı (menü kâğıda basılmamalı).",
  "app/depo/page.tsx": "Yalnız yönlendirme.",
  "app/onay-merkezi/page.tsx": "Yalnız yönlendirme.",
  "app/insan-kaynaklari/egitimler/page.tsx": "Yalnız yönlendirme.",
  "app/insan-kaynaklari/sertifikalar/page.tsx": "Yalnız yönlendirme.",
};

function sayfalar(dizin: string): string[] {
  const bulunan: string[] = [];

  for (const girdi of readdirSync(dizin)) {
    if (girdi === "node_modules" || girdi === ".next") continue;

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...sayfalar(yol));
      continue;
    }

    if (girdi === "page.tsx") bulunan.push(yol);
  }

  return bulunan;
}

/**
 * Dosya kabuğu doğrudan mı KULLANIYOR.
 *
 * `<ErpShell` aranıyor, yalnız "ErpShell" değil — ÖLÇÜLDÜ (2026-09-06):
 * ilk sürüm adı dosyanın HER YERİNDE arıyordu ve sonda ısırmadı.
 * `/gorevler`den `import` satırını silmek yetmiyordu; JSX gövdesindeki
 * kullanım hâlâ dizgiyi taşıyordu. Aynı gevşeklik gerçek bir zayıflıktı:
 * adı yalnız YORUMDA geçen bir sayfa da testi geçerdi.
 *
 * Kural 81: sonda ısırmıyorsa önce düzeneği sorgula.
 */
function kabukVar(yol: string): boolean {
  return /<ErpShell[\s>]/.test(readFileSync(yol, "utf8"));
}

/** Bir kademe devir: sayfanın çağırdığı yerel bileşen kabuğu kullanıyor mu. */
function devrettigiKabukVar(yol: string): boolean {
  const kod = readFileSync(yol, "utf8");

  for (const eslesme of kod.matchAll(/from "@\/(components\/[^"]+)"/g)) {
    for (const uzanti of [".tsx", ".ts"]) {
      const hedef = join(ROOT, eslesme[1] + uzanti);

      try {
        if (/<ErpShell[\s>]/.test(readFileSync(hedef, "utf8"))) return true;
      } catch {
        // Dosya yoksa geç: burada aranan şey kanıt, yokluk değil.
      }
    }
  }

  return false;
}

describe("sayfa ↔ kabuk sözleşmesi", () => {
  it("taranan sayfa sayısı beklenen mertebede (POZİTİF KONTROL)", () => {
    // Tarama bozulursa aşağıdaki test boş kümede yeşil döner ve
    // hiçbir şey ölçmez (Kural 48).
    expect(sayfalar(join(ROOT, "app")).length).toBeGreaterThan(150);
  });

  it("her içerik sayfası ERP kabuğunun içinde", () => {
    const kabuksuz = sayfalar(join(ROOT, "app"))
      .map((yol) => ({ yol, goreli: yol.replace(ROOT + "/", "") }))
      .filter(({ goreli }) => !(goreli in MUAF))
      .filter(({ yol }) => !kabukVar(yol) && !devrettigiKabukVar(yol))
      .map(({ goreli }) => goreli);

    expect(
      kabuksuz,
      "Bu sayfalar ERP kabuğunun DIŞINDA: kenar çubuğu, üst çubuk, " +
        "Hızır ve mesaj baloncuğu orada görünmez. Kabuğa alın ya da " +
        "gerekçesiyle MUAF listesine yazın.\n  - " + kabuksuz.join("\n  - ")
    ).toEqual([]);
  });

  it("muafiyet listesinin her satırının gerekçesi ve karşılığı var", () => {
    for (const [yol, gerekce] of Object.entries(MUAF)) {
      expect(gerekce.trim().length, `Gerekçesiz muafiyet: ${yol}`).toBeGreaterThan(10);

      // ÇÜRÜMÜŞ MUAFİYET: silinmiş bir sayfa listede kalmasın.
      expect(() => statSync(join(ROOT, yol)), `Muaf sayfa yok: ${yol}`).not.toThrow();
    }
  });
});
