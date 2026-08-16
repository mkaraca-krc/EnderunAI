import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * ELEMAN SEVİYESİ YETKİ — R2 sözleşmesi.
 *
 * R1 rota seviyesini kapattı; bu, ekranın İÇİNDEKİ düğmeleri
 * bağlıyor. Kural tek cümle: bir düğmenin kontrol ettiği izin,
 * çağırdığı ucun zorladığı izinle AYNI olmalı.
 *
 * Ayrı tutulursa iki sapma doğuyor:
 *   "görünür ama reddedilir" — kullanıcı basar, 403 yer
 *   "gizli ama izinli"       — yetkisi olan düğmeyi hiç göremez
 *
 * İkincisi daha sinsi: kimse şikâyet etmez, iş yapılmaz ve sebebi
 * bilinmez.
 *
 * BU TEST ARAYÜZÜ DENETLİYOR, GÜVENLİĞİ DEĞİL. Uçlardaki
 * RequirePermission güvenlik sınırı; buradaki bir hata veriyi açığa
 * çıkarmaz, yalnızca kullanıcıya yapamayacağı işi gösterir ya da
 * yapabileceğini gizler.
 */

const ROOT = join(__dirname, "..");

function screens(directory: string): string[] {
  const found: string[] = [];

  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);

    if (statSync(path).isDirectory()) {
      found.push(...screens(path));
      continue;
    }

    if (entry === "page.tsx") found.push(path);
  }

  return found;
}

/** R2/1 kapsamı: puantaj-bordro ailesi. */
const R2_1 = [
  "avanslar",
  "bordro",
  "ek-ucretler",
  "fazla-mesai",
  "gunluk-puantaj",
  "izinler",
  "onay-merkezi",
  "puantaj",
];

/**
 * R2/2 kapsamı: muhasebe ailesi.
 *
 * Buradaki "yeni" ve "duzenle" ekranları LİSTEDE YOK — onlar düğme
 * kapısıyla değil ROTA kapısıyla korunuyor. Tek işi bir aksiyon olan
 * ekranda düğmeyi gizlemek yetmez: kullanıcı uzun formu doldurur,
 * reddi ancak kaydederken yer.
 */
const R2_2 = [
  join("fisler", "[id]"),
  join("hesap-plani", "[id]"),
  join("kur-degerlemesi"),
  join("kesinti-hesaplari"),
];

function read(directory: string) {
  return screens(join(ROOT, "app", directory)).map((path) => ({
    path: path.slice(ROOT.length + 1),
    text: readFileSync(path, "utf8"),
  }));
}

const hr = read("insan-kaynaklari");
const accounting = read("muhasebe");

describe("eleman seviyesi yetki (R2/1)", () => {
  it("kapsamdaki sekiz ekranın hepsi kapılı", () => {
    const missing = R2_1.filter((name) => {
      const screen = hr.find((x) => x.path.includes(join("insan-kaynaklari", name, "page.tsx")));
      return !screen || !screen.text.includes("useModuleActions");
    });

    expect(
      missing,
      "R2/1 kapsamındaki ekran gating almamış. Aksiyon düğmeleri " +
        "useModuleActions üzerinden kapılanmalı.",
    ).toEqual([]);
  });

  it("muhasebe ailesindeki aksiyon ekranları kapılı", () => {
    const missing = R2_2.filter((name) => {
      // TAM eşleşme: "fisler/[id]" kalıbı "fisler/[id]/duzenle" ile de
      // eşleşiyordu ve yanlış dosyaya bakıyordu.
      const screen = accounting.find(
        (x) => x.path === join("app", "muhasebe", name, "page.tsx"),
      );
      return !screen || !screen.text.includes("useModuleActions");
    });

    expect(missing).toEqual([]);
  });

  /**
   * TEK İŞİ AKSİYON OLAN EKRAN ROTA KAPISINDA.
   *
   * "/muhasebe" yalnız accounting.view istiyordu; görüntüleme yetkisi
   * olan biri "Yeni Fiş" ekranını açıp formu doldurabiliyordu.
   */
  it("muhasebe yeni/duzenle ekranları rota kapısında", () => {
    const routes = readFileSync(
      join(ROOT, "lib", "auth", "route-permissions.ts"), "utf8",
    );

    expect(routes).toMatch(/muhasebe[\s\S]*yeni[\s\S]*accounting\.create/);
    expect(routes).toMatch(/muhasebe[\s\S]*duzenle[\s\S]*accounting\.edit/);
  });

  /**
   * İZİN ANAHTARI ÇAĞRI YERİNDE ELLE YAZILMAZ.
   *
   * `has("attendance-payroll.create")` yerine
   * `actions.can("create")` — modül adı tek yerde durur. Elle
   * yazılan anahtar, modül yeniden adlandırıldığında sessizce
   * yanlış izne bakar.
   */
  it("izin anahtarını çağrı yerinde birleştirmiyor", () => {
    const offenders = hr
      .concat(accounting)
      .filter((screen) => /has\(\s*["'`](attendance-payroll|accounting)\./.test(screen.text))
      .map((screen) => screen.path);

    expect(offenders).toEqual([]);
  });

  /**
   * YARDIMCI YÜKLENME DURUMUNU TAŞIMALI.
   *
   * İzinler gelmeden düğme gösterilirse kullanıcı olmayan yetkiyi bir
   * an için görür — üstelik tıklamaya da yetişebilir. R1'de menü için
   * aynı karar verilmişti.
   */
  it("yardımcı yükleme durumunu döndürüyor", () => {
    const helper = readFileSync(join(ROOT, "lib", "auth", "module-actions.ts"), "utf8");

    expect(helper).toContain("loading");
    expect(helper).toContain("usePermissions");
  });

  /**
   * YARDIMCI KENDİ İZİN LİSTESİ TUTMUYOR.
   *
   * R2'nin tek kuralı: izin uçtan türer. Yardımcının içine bir
   * anahtar listesi girerse ikinci bir kaynak doğar ve zamanla
   * uçlardan ayrışır.
   */
  it("yardımcı ikinci bir izin haritası taşımıyor", () => {
    const helper = readFileSync(join(ROOT, "lib", "auth", "module-actions.ts"), "utf8");
    const code = helper.replace(/\/\*[\s\S]*?\*\//g, " ").replace(/\/\/[^\n]*/g, "");

    // Kod tarafında sabit izin anahtarı olmamalı.
    expect(code).not.toMatch(/["'`][a-z-]+\.(create|edit|delete|approve|view)["'`]/);
  });
});
