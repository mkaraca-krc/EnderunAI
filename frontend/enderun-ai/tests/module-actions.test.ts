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

/**
 * R2/3 kapsamı: hakediş + satın alma.
 *
 * Bu grup YIKICI aksiyonlarla dolu (iptal, red, silme) ve tam da
 * burada uçtan türetmenin değeri görülüyor — aşağıdaki teste bak.
 */
const R2_3: [string, string][] = [
  ["metrajlar/[id]", "hakedis"],
  ["hakedis/dosyalar", "hakedis"],
  ["satin-alma/siparis/[id]", "purchasing-orders"],
  ["satin-alma/rfq/[id]", "purchasing-rfq"],
  ["satin-alma/rfq/[id]/karsilastirma", "purchasing-rfq"],
  ["depo-stok/mal-kabul/[id]", "purchasing-receipts"],
];

/**
 * R2/4a kapsamı: personel ailesi.
 *
 * İşe alım dört varlığı (ilan, aday, başvuru, görüşme) tek ekranda
 * topluyor ve dördü de aynı izin ailesini paylaşıyor — uçlar öyle
 * kurulmuş, ekran onu izliyor.
 */
const R2_4A = ["ise-alim", "zimmetler", "organizasyon", "kariyer"];

/**
 * R2/4b kapsamı: sekreterya, sistem yönetimi, taşeronlar.
 *
 * Bu ailelerde create/edit/delete ayrımı YOK; sekreterya ve taşeron
 * uçları tek "manage" anahtarında toplanmış. Ekran onu izliyor —
 * daha ince bir ayrım istenirse önce UÇ bölünmeli.
 */
const R2_4B: [string, string][] = [
  ["sekreterya/kargo", "secretariat"],
  ["sekreterya/ziyaretciler", "secretariat"],
  ["sistem-yonetimi/kullanicilar", "user-management"],
  ["sistem-yonetimi/erisim-talepleri", "user-management"],
  ["taseronlar", "subcontractor"],
];

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

  it("hakediş ve satın alma ekranları kapılı", () => {
    const missing = R2_3.filter(([screen, module]) => {
      const path = join(ROOT, "app", ...screen.split("/"), "page.tsx");
      const text = readFileSync(path, "utf8");

      return !text.includes(`useModuleActions("${module}")`);
    }).map(([screen]) => screen);

    expect(missing).toEqual([]);
  });

  /**
   * YIKICI AKSİYONLAR UCUN DEDİĞİ İZNE BAĞLI — TAHMİNE DEĞİL.
   *
   * SIRALAMA ÖNEMLİ, ÇÜNKÜ DERSİ O TAŞIYOR:
   *
   * 1. Ölçüm, iptal uçlarının EDIT istediğini gösterdi (beklenti
   *    delete'ti). Arayüz o an ucu izledi — delete'e bağlamak
   *    "gizli ama izinli" sapması üretirdi: edit yetkili kullanıcı
   *    düğmeyi göremez ama API'den yine iptal edebilirdi.
   * 2. Sonra UÇLAR daraltıldı (A tipi): dokuz yıkıcı uç
   *    Edit/Manage -> Delete. Etki ölçüldü, canlıda etkilenen yoktu.
   * 3. Arayüz kapıları KENDİLİĞİNDEN takip etti — çünkü izin tek
   *    kaynaktan geliyor.
   *
   * Yani düzeltme UI'da değil UÇTA yapıldı ve arayüz onu izledi.
   * Tersini yapmak arayüzü backend'den koparırdı.
   *
   * BU TEST O ZİNCİRİ KİLİTLİYOR: kapı ile ucun izni ayrışırsa kırılır.
   */
  it("yıkıcı aksiyonlar ucun izniyle kapılı, tahminle değil", () => {
    // İşaretçiler ÇAĞRI YERİNİN kendisi; "cancel" gibi genel bir
    // kelime yorumlarda da geçiyor ve yanlış yeri bulurdu.
    const cases: [string, string, string][] = [
      // İptal artık delete istiyor (A tipi daraltma sonrası).
      ["metrajlar/[id]", 'setPendingAction("cancel")', "delete"],
      ["satin-alma/siparis/[id]", 'setPendingAction("cancel")', "delete"],
      ["depo-stok/mal-kabul/[id]", 'setConfirming("iptal")', "delete"],
      // Gerçek SİLME uçları delete istiyor; ayrım korunmalı.
      ["metrajlar/[id]", 'setPendingAction("remove")', "delete"],
      ["hakedis/dosyalar", "setPendingDelete(file)", "delete"],
    ];

    for (const [screen, marker, action] of cases) {
      const path = join(ROOT, "app", ...screen.split("/"), "page.tsx");
      const text = readFileSync(path, "utf8");

      const index = text.indexOf(marker);
      expect(index, `${screen}: "${marker}" bulunamadı`).toBeGreaterThan(0);

      // İşaretçiden geriye doğru en yakın kapı bu eylem olmalı.
      const before = text.slice(Math.max(0, index - 400), index);
      const gates = [...before.matchAll(/actions\.can\("(\w+)"\)/g)];
      const nearest = gates.at(-1)?.[1];

      expect(
        nearest,
        `${screen} / ${marker}: kapı "${action}" olmalıydı, "${nearest}" bulundu`,
      ).toBe(action);
    }
  });

  it("personel ailesi ekranları kapılı", () => {
    const missing = R2_4A.filter((name) => {
      const screen = hr.find(
        (x) => x.path === join("app", "insan-kaynaklari", name, "page.tsx"),
      );
      return !screen || !screen.text.includes('useModuleActions("personnel")');
    });

    expect(missing).toEqual([]);
  });

  it("sekreterya, sistem yönetimi ve taşeron ekranları kapılı", () => {
    const missing = R2_4B.filter(([screen, module]) => {
      const path = join(ROOT, "app", ...screen.split("/"), "page.tsx");
      return !readFileSync(path, "utf8").includes(`useModuleActions("${module}")`);
    }).map(([screen]) => screen);

    expect(missing).toEqual([]);
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
