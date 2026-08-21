import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
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
  /**
   * R2/4c kapsamı: finans, şantiye, görev ve ayar ekranları.
   *
   * Her satır BİR EKRAN ve o ekranda kapılanması gereken eylemler.
   * Eylem adları uçların RequirePermission'ından geldi, düğme
   * adlarından değil.
   */
  const R2_4C: Array<[string, string[]]> = [
    ["app/finans/cekler/page.tsx", ["create", "edit", "approve"]],
    ["app/finans/kasa-banka/page.tsx", ["create"]],
    ["app/finans/piyasa/page.tsx", ["manage"]],
    ["app/finans/vergi/page.tsx", ["edit", "delete", "manage"]],
    ["app/gorevler/page.tsx", ["manage"]],
    ["app/sekreterya/evrak/page.tsx", ["create", "delete"]],
    ["app/sistem-yonetimi/sirket-ayarlari/page.tsx", ["edit"]],
    ["app/projeler/[id]/santiyeler/[siteId]/page.tsx",
      ["create", "edit", "delete", "approve"]],
    ["app/projeler/[id]/page.tsx", ["create", "delete", "edit"]],
    ["app/insan-kaynaklari/organizasyon/page.tsx", ["edit", "delete"]],
    ["app/muhasebe/hesap-plani/[id]/page.tsx", ["edit", "delete"]],
  ];

  it.each(R2_4C)("R2/4c — %s aksiyonları kapılı", (relative, expected) => {
    const text = readFileSync(join(ROOT, relative), "utf8");
    const gated = new Set(
      [...text.matchAll(/[Aa]ctions\.can\("([^"]+)"\)/g)].map((m) => m[1]),
    );

    for (const action of expected) {
      expect(gated.has(action), `${relative}: ${action} kapısı yok`).toBe(true);
    }
  });

  /**
   * YIKICI AKSİYON, UCUN İSTEDİĞİ AĞIR YETKİDE.
   *
   * Çekte "Son Durumu Geri Al" ve "Çeki İptal Et" muhasebe fişine
   * ters kayıt üretiyor; uç finance.approve istiyor — düzenlemeden
   * daha ağır. Vergide "Geri Al" ödeme kaydını siliyor:
   * accounting.delete. Bunlar edit'e kaysa "görünür ama reddedilir"
   * olurdu.
   */
  it("yıkıcı finans aksiyonları edit'e değil approve/delete'e bağlı", () => {
    const cheques = readFileSync(join(ROOT, "app/finans/cekler/page.tsx"), "utf8");

    for (const label of ["Son Durumu Geri Al", "Çeki İptal Et"]) {
      const at = cheques.indexOf(label);
      expect(at, `${label} bulunamadı`).toBeGreaterThan(-1);

      const before = cheques.slice(0, at);
      const gate = before.lastIndexOf('actions.can("');
      const action = /actions\.can\("([^"]+)"\)/.exec(before.slice(gate))?.[1];

      expect(action, `${label} yanlış yetkide`).toBe("approve");
    }

    const tax = readFileSync(join(ROOT, "app/finans/vergi/page.tsx"), "utf8");
    const undo = tax.indexOf("Geri Al");
    const gate = tax.slice(0, undo).lastIndexOf('actions.can("');
    expect(/actions\.can\("([^"]+)"\)/.exec(tax.slice(gate))?.[1]).toBe("delete");
  });

  /**
   * ARAYÜZ DARALTMASI TEK BAŞINA YAPILMAZ.
   *
   * Vergi ekranındaki "Geri Al" accounting.delete'e bağlandı; uç hâlâ
   * accounting.edit isterse "gizli ama izinli" doğar — yetkisi olan
   * kullanıcı düğmeyi göremez ama API'den işlemi yine yapar. Bu test
   * ucun da daraltıldığını doğruluyor.
   */
  it("vergi ödemesi geri alma ucu delete yetkisinde", () => {
    const controller = readFileSync(
      join(ROOT, "..", "..", "backend", "EnderunAI.Api", "Controllers", "TaxController.cs"),
      "utf8",
    );
    const at = controller.indexOf('[HttpDelete("payments")]');
    expect(at).toBeGreaterThan(-1);

    const attribute = controller.slice(at, at + 200);
    expect(attribute).toContain("AccountingDelete");
    expect(attribute).not.toContain("AccountingEdit");
  });

  /**
   * OTURUM İSTEĞİ PAYLAŞILIYOR.
   *
   * useCurrentUser her örnekte kendi `auth/me` isteğini atıyordu. R2
   * yayıldıkça bir sayfada üç dört örnek olması normalleşti; ekranın
   * modülü dışında izin isteyen her düğme ikinci bir çağrı doğuruyor.
   *
   * Önbellek YALNIZCA BAŞARILI yanıtı tutmalı ve giriş/çıkış onu
   * temizlemeli: ikisi de router.push ile çalışıyor, yani modül
   * durumu kendiliğinden sıfırlanmıyor. 401 önbellekte kalsaydı
   * giriş sonrası kullanıcı hâlâ oturumsuz görünürdü.
   */
  it("oturum isteği paylaşılıyor ve giriş/çıkışta temizleniyor", () => {
    const hook = readFileSync(join(ROOT, "lib", "use-current-user.ts"), "utf8");

    expect(hook).toMatch(/let sessionRequest/);
    expect(hook).toContain("clearCurrentUserCache");
    // başarısız istek önbellekte kalmıyor
    expect(hook).toMatch(/catch\([\s\S]{0,200}sessionRequest = null/);

    for (const consumer of ["app/login/page.tsx", "components/logout-button.tsx"]) {
      expect(
        readFileSync(join(ROOT, consumer), "utf8"),
        `${consumer} önbelleği temizlemiyor`,
      ).toContain("clearCurrentUserCache()");
    }
  });

  /**
   * BÜTÇE KAPISI UCUN İSTEDİĞİNDEN GENİŞ OLAMAZ.
   *
   * `purchasing.approve || finance.approve` idi; uç yalnızca
   * purchasing.approve istiyor. Yalnız finance.approve'u olan
   * kullanıcı formu doldurup reddi KAYDEDERKEN yiyordu.
   */
  it("bütçe onay ekranı ucun istediği izne eşit", () => {
    const text = readFileSync(join(ROOT, "app/satin-alma/butce-onay/page.tsx"), "utf8");
    const code = text.replace(/\/\*[\s\S]*?\*\//g, " ").replace(/\/\/[^\n]*/g, " ");

    expect(code).toContain('hasPermission(session, "purchasing.approve")');
    expect(code).not.toContain('hasPermission(session, "finance.approve")');
  });

  /**
   * TAM SAYFA AKSİYON EKRANI ROTA KAPISIYLA KORUNUR.
   *
   * "yeni" ve "duzenle" ekranlarının tek işi bir yazma çağrısı.
   * Düğmeyi gizlemek yetmez: yalnız görüntüleme yetkisi olan biri
   * uzun formu doldurup reddi ancak kaydederken yiyor.
   */
  it("yeni/duzenle ekranları rota seviyesinde kapılı", () => {
    const rules = readFileSync(join(ROOT, "lib", "auth", "route-permissions.ts"), "utf8");

    for (const fragment of [
      "hakedis\\/yeni",
      "hakedis\\/[^/]+\\/duzenle",
      "fiyat-farki\\/[^/]+\\/yeni",
      "metrajlar\\/yeni",
      "mal-kabul\\/yeni",
      "malzeme-talepleri\\/yeni",
      "santiyeler\\/yeni",
      "teklifler\\/yeni",
    ]) {
      expect(rules, `${fragment} rota kuralı yok`).toContain(fragment);
    }
  });
  /**
   * DÜĞME BAZINDA KAPI — eylem adının dosyada geçmesi yetmez.
   *
   * Önceki sürüm "bu ekranda manage kapısı var mı" diye soruyordu;
   * dört düğmeden birinin kapısı silinse test yine geçiyordu. Sonda
   * (probe) bunu yakaladı. Bu sürüm her düğmenin ETİKETİNDEN geriye
   * gidip EN YAKIN kapıyı okuyor.
   */
  const BUTTON_GATES: Array<[string, string, string]> = [
    // [ekran, düğme etiketi, beklenen eylem]
    ["app/gorevler/page.tsx", "+ Yeni Görev", "manage"],
    ["app/gorevler/page.tsx", "Görevi Kaydet", "manage"],
    ["app/gorevler/page.tsx", "Başlat", "manage"],
    ["app/gorevler/page.tsx", "Tamamla", "manage"],
    ["app/finans/kasa-banka/page.tsx", "+ Yeni Hesap", "create"],
    ["app/finans/kasa-banka/page.tsx", "+ Tahsilat / Ödeme", "create"],
    ["app/finans/piyasa/page.tsx", "Şimdi güncelle", "manage"],
    ["app/finans/vergi/page.tsx", "Fiş Kes", "manage"],
    ["app/finans/vergi/page.tsx", "Ödendi", "edit"],
    ["app/sekreterya/evrak/page.tsx", "Evrakı Kaydet", "create"],
    ["app/sistem-yonetimi/sirket-ayarlari/page.tsx", "+ Ekle", "edit"],
    ["app/sistem-yonetimi/sirket-ayarlari/page.tsx", "Finans Ayarlarını Kaydet", "edit"],
    ["app/sistem-yonetimi/sirket-ayarlari/page.tsx", "Test E-postası Gönder", "edit"],
    ["app/projeler/[id]/santiyeler/[siteId]/page.tsx", "Onayla", "approve"],
    ["app/projeler/[id]/santiyeler/[siteId]/page.tsx", "Depoyu Kaydet", "create"],
    ["app/projeler/[id]/santiyeler/[siteId]/page.tsx", "Personeli Ata", "edit"],
    ["app/projeler/[id]/santiyeler/[siteId]/page.tsx", "Atamayı Kapat", "edit"],
    ["app/projeler/[id]/page.tsx", "Portal Linki Oluştur", "create"],
    ["app/projeler/[id]/page.tsx", "İptal Et", "delete"],
    ["app/projeler/[id]/page.tsx", "E-posta ile Gönder", "edit"],
    ["app/finans/cekler/page.tsx", "Durumu Güncelle", "edit"],
    /*
     * ÇEK DÜZENLEME AYRI ANAHTARDA: uç artık `cheque.edit` istiyor,
     * `finance.edit` DEĞİL — düzeltme, çeki görebilen herkesin işi
     * değil. Beklenti kanca adıyla birlikte yazılıyor ki düğme
     * sessizce `finance.edit` kapısına geri kaymasın.
     */
    ["app/finans/cekler/page.tsx", "Çeki Düzenle", "chequeActions.edit"],
  ];

  it.each(BUTTON_GATES)("%s -> \"%s\" düğmesi %s kapısında", (relative, label, action) => {
    const text = readFileSync(join(ROOT, relative), "utf8");

    /*
     * Etiket dosyada BİRDEN FAZLA yerde geçebiliyor — çoğu ekranda
     * aynı kelime bir durum etiketi olarak da duruyor ("Ödendi",
     * "Tamamla"). Bu yüzden yalnızca DÜĞME bağlamındaki geçişler
     * sayılıyor: hemen öncesinde <button/<Button açılışı olanlar.
     */
    const occurrences: number[] = [];
    for (let at = text.indexOf(label); at > -1; at = text.indexOf(label, at + 1)) {
      const window = text.slice(Math.max(0, at - 1200), at);
      /*
       * "Bu etiket açık bir düğmenin İÇİNDE mi" sorusu: penceredeki
       * son açılış etiketi, son kapanış etiketinden sonra geliyorsa
       * evet. Yalnızca "</button> yok" demek yetmiyordu — yan yana
       * duran iki düğmede ikincisinin etiketi de elenirdi.
       */
      const open = Math.max(window.lastIndexOf("<button"), window.lastIndexOf("<Button"));
      const close = Math.max(window.lastIndexOf("</button>"), window.lastIndexOf("</Button>"));

      // Kendini kapatan `<button ... />` açık sayılmaz (bkz. aşağıdaki
      // aynı kontrol); ondan sonraki başlık metni düğme sanılıyordu.
      const selfClosed = open > -1 && window.slice(open).includes("/>");

      if (open > -1 && open > close && !selfClosed) {
        occurrences.push(at);
      }
    }

    expect(occurrences.length, `"${label}" düğme olarak bulunamadı`).toBeGreaterThan(0);

    /*
     * DÜĞME BAĞLAMINDAKİ HER GEÇİŞ kapılı olmalı. "en az biri" demek,
     * dört düğmeden birinin kapısı silindiğinde testi kör bırakırdı —
     * sonda (probe) tam bunu yakaladı.
     */
    for (const at of occurrences) {
      const before = text.slice(0, at);
      const gate = before.lastIndexOf('.can("');

      expect(gate, `"${label}" için kapı yok`).toBeGreaterThan(-1);
      expect(
        at - gate,
        `"${label}" en yakın kapıdan ${at - gate} karakter uzakta — kapı başka düğmenin olabilir`,
      ).toBeLessThan(900);

      /*
       * KANCA ADI DA KARŞILAŞTIRILIYOR.
       *
       * Önce yalnız eylem adına bakılıyordu; `rfqActions.can("create")`
       * yerine `actions.can("create")` yazılsa test geçiyordu — yani
       * çapraz modül kapısının yanlış modüle kayması yakalanmıyordu.
       * Sonda (probe) bunu gösterdi.
       *
       * Beklenen değer "create" ise yalnız eylem, "rfqActions.create"
       * ise kanca + eylem karşılaştırılır.
       */
      // Dilim `.can("` ile başlıyor; kanca adı ONDAN ÖNCE, o yüzden
      // pencere geriye açılıyor ve en SON eşleşme alınıyor.
      const gateText = before.slice(Math.max(0, gate - 40));
      const found = [...gateText.matchAll(/(\w+)\.can\("([^"]+)"\)/g)];
      const match = found[found.length - 1];
      const actual = action.includes(".")
        ? `${match?.[1]}.${match?.[2]}`
        : match?.[2];

      expect(actual, `"${label}" yanlış kapıda`).toBe(action);
    }
  });
  /**
   * R2/4d yığın 1 — muhasebe ve hakediş belge ekranları.
   *
   * Bu ailede iki ayrım kritik:
   *   RED = ONAY yetkisinde (ikisi de onay makamının kararı, yıkıcı değil)
   *   İPTAL = DELETE yetkisinde (fişi ters kayıtla dengeliyor, defter izi)
   */
  const R2_4D1: Array<[string, string, string]> = [
    ["app/muhasebe/faturalar/[id]/page.tsx", "Onaya Gönder", "edit"],
    ["app/muhasebe/faturalar/[id]/page.tsx", "Onayla ve Fişleştir", "approve"],
    ["app/muhasebe/faturalar/[id]/page.tsx", "Reddet", "approve"],
    ["app/muhasebe/faturalar/[id]/page.tsx", "İptal Et", "delete"],
    ["app/muhasebe/faturalar/[id]/page.tsx", "İade Faturasını Oluştur", "create"],
    ["app/muhasebe/satis-faturalari/[id]/page.tsx", "Kesinleştir ve Fiş Oluştur", "edit"],
    ["app/muhasebe/satis-faturalari/[id]/page.tsx", "İptal Et", "delete"],
    ["app/muhasebe/satis-faturalari/[id]/page.tsx", "İade Faturasını Oluştur", "create"],
    ["app/muhasebe/e-fatura-ice-aktar/page.tsx", "Dosyaları Oku", "create"],
    ["app/hakedis/[id]/page.tsx", "Sil", "delete"],
    ["app/hakedis/[id]/page.tsx", "Onaya Gönder", "edit"],
    ["app/hakedis/[id]/page.tsx", "Onayla", "approve"],
    ["app/hakedis/[id]/page.tsx", "Kesinleştir ve Fişleştir", "approve"],
    ["app/hakedis/[id]/page.tsx", "Hakedişi İptal Et", "delete"],
    ["app/projeler/[id]/metraj-takip/page.tsx", "İlave İşi Kaydet", "create"],
  ];

  it.each(R2_4D1)("%s -> \"%s\" %s kapısında", (relative, label, action) => {
    const text = readFileSync(join(ROOT, relative), "utf8");

    const occurrences: number[] = [];
    for (let at = text.indexOf(label); at > -1; at = text.indexOf(label, at + 1)) {
      const window = text.slice(Math.max(0, at - 1200), at);
      const open = Math.max(window.lastIndexOf("<button"), window.lastIndexOf("<Button"));
      const close = Math.max(window.lastIndexOf("</button>"), window.lastIndexOf("</Button>"));

      /*
       * KENDİNİ KAPATAN DÜĞME AÇIK SAYILMAZ. `<button ... />` biçiminde
       * bir eleman (örneğin panel kapatma katmanı) kapanış etiketi
       * taşımadığı için "açık" görünüyordu; ondan sonraki bir BAŞLIK
       * metni yanlışlıkla düğme etiketi sayılıyordu.
       */
      const selfClosed = open > -1 && window.slice(open).includes("/>");

      if (open > -1 && open > close && !selfClosed) occurrences.push(at);
    }

    expect(occurrences.length, `"${label}" düğme olarak bulunamadı`).toBeGreaterThan(0);

    for (const at of occurrences) {
      const before = text.slice(0, at);
      const gate = before.lastIndexOf('.can("');

      expect(gate, `"${label}" için kapı yok`).toBeGreaterThan(-1);
      expect(at - gate, `"${label}" kapıdan çok uzak`).toBeLessThan(900);

      /*
       * KANCA ADI DA KARŞILAŞTIRILIYOR.
       *
       * Önce yalnız eylem adına bakılıyordu; `rfqActions.can("create")`
       * yerine `actions.can("create")` yazılsa test geçiyordu — yani
       * çapraz modül kapısının yanlış modüle kayması yakalanmıyordu.
       * Sonda (probe) bunu gösterdi.
       *
       * Beklenen değer "create" ise yalnız eylem, "rfqActions.create"
       * ise kanca + eylem karşılaştırılır.
       */
      // Dilim `.can("` ile başlıyor; kanca adı ONDAN ÖNCE, o yüzden
      // pencere geriye açılıyor ve en SON eşleşme alınıyor.
      const gateText = before.slice(Math.max(0, gate - 40));
      const found = [...gateText.matchAll(/(\w+)\.can\("([^"]+)"\)/g)];
      const match = found[found.length - 1];
      const actual = action.includes(".")
        ? `${match?.[1]}.${match?.[2]}`
        : match?.[2];

      expect(actual, `"${label}" yanlış kapıda`).toBe(action);
    }
  });

  /**
   * SATIR BİLEŞENİNE İZİN PROP OLARAK GEÇER.
   *
   * `ExtraWorkRow` satır başına render ediliyor. İçinde
   * `useModuleActions` çağrılsa her satır kendi izin okumasını yapardı;
   * `useCurrentUser` örnek başına istek attığı için bu satır sayısı
   * kadar `auth/me` demek olurdu.
   *
   * Ayrıca `canTransferWork` (yetki) ile `canTransfer` (iş kuralı: uç
   * bu işi aktarılabilir saydı mı) AYRI kalmalı; birleştirilirse
   * kuralın biri diğerini sessizce yutar.
   */
  it("satır bileşeni izni prop olarak alıyor, kancayı içinde çağırmıyor", () => {
    const text = readFileSync(
      join(ROOT, "app/projeler/[id]/metraj-takip/page.tsx"),
      "utf8",
    );
    const row = text.slice(text.indexOf("function ExtraWorkRow"));

    expect(row).toContain("canApprove: boolean;");
    expect(row).toContain("canTransferWork: boolean;");
    expect(row).not.toContain("useModuleActions(");
    // iş kuralı propu ayrı duruyor
    expect(row).toContain("canTransfer: boolean;");
  });

  /**
   * FİYAT FARKI HESAP PANELİ KALDIRILDI — hayalet arayüz bırakılmadı.
   *
   * Panel `POST price-difference-calculations/calculate` çağırıyordu;
   * o uç backend'de YOK ve PDF üreten bir kütüphane de yok. Elle
   * girilen `priceDifferenceAmount` çalışmaya devam ediyor: hakediş
   * formunda giriliyor, Excel çıktısına, finans panosuna ve kâr
   * hesabına akıyor. Yani kaldırılan şey yetenek değil, çalışmayan
   * otomatik hesap.
   *
   * ANA VERİ EKRANLARI (profiller, endeksler) KASITLI OLARAK DURUYOR:
   * formül yazıldığında ihtiyaç duyulacak veri onlarda.
   */
  it("hakediş detayında ölü fiyat farkı hesabı kalmadı", () => {
    const text = readFileSync(join(ROOT, "app/hakedis/[id]/page.tsx"), "utf8");

    expect(text).not.toContain("priceDifferenceService");
    expect(text).not.toContain("Fiyat Farkı Hesabı");
    expect(text).not.toContain("calculatePriceDifference");
    // elle girilen tutarın GÖSTERİMİ kalmalı
    expect(text).toContain("item.priceDifferenceAmount");

    // ana veri ekranları silinmedi
    const rules = readFileSync(join(ROOT, "lib", "auth", "route-permissions.ts"), "utf8");
    expect(rules).toContain("fiyat-farki");
  });
  /**
   * R2/4d yığın 2 — satın alma, depo, demirbaş, filo.
   *
   * Bu yığının dersi: EKRAN ADI MODÜLÜ BELİRLEMİYOR.
   *   demirbaş ekranları  -> personnel.*        (zimmet personele bağlı)
   *   "Yerine Talep Aç"   -> purchasing-requests.create (satın alma talebi açıyor)
   *   "RFQ Oluştur"       -> purchasing-rfq.create      (talep ekranında ama RFQ modülü)
   *   malzeme ihtiyacı    -> purchasing-requests.create (proje ekranında)
   */
  const R2_4D2: Array<[string, string, string]> = [
    ["app/satin-alma/[id]/page.tsx", "Onaya Gönder", "edit"],
    ["app/satin-alma/[id]/page.tsx", "Onayla", "approve"],
    ["app/satin-alma/[id]/page.tsx", "Düzeltmeye İade Et", "approve"],
    ["app/satin-alma/[id]/page.tsx", "Reddet", "approve"],
    ["app/satin-alma/[id]/page.tsx", "İptal Et", "delete"],
    ["app/satin-alma/[id]/page.tsx", "RFQ Oluştur", "rfqActions.create"],
    ["app/satin-alma/rfq/[id]/tedarikci/[supplierId]/page.tsx", "Teklifi Kaydet", "edit"],
    ["app/depo-stok/malzeme-talepleri/[id]/page.tsx", "Onaya Gönder", "edit"],
    ["app/depo-stok/malzeme-talepleri/[id]/page.tsx", "Talebi Onayla", "approve"],
    ["app/depo-stok/malzeme-talepleri/[id]/page.tsx", "Talebi İptal Et", "delete"],
    ["app/depo-stok/iadeler/page.tsx", "Tedarikçiye Gönderildi", "edit"],
    ["app/projeler/[id]/malzeme-ihtiyaci/page.tsx", "Taslak Talep Oluştur", "actions.create"],
    ["app/demirbas/page.tsx", "Yeni Alet", "create"],
    ["app/demirbas/page.tsx", "Düzenle", "edit"],
    ["app/demirbas/[id]/page.tsx", "İade Al", "edit"],
    ["app/demirbas/servis/page.tsx", "Talep Aç", "create"],
    ["app/demirbas/servis/page.tsx", "Kararı Kaydet", "edit"],
    ["app/demirbas/servis/page.tsx", "Karar Ver", "edit"],
    ["app/demirbas/servis/page.tsx", "Yerine Talep Aç", "purchasingActions.create"],
    ["app/filo/page.tsx", "+ Yeni Araç", "manage"],
    ["app/filo/[id]/page.tsx", "Atama Yap", "manage"],
    ["app/filo/[id]/page.tsx", "Ata", "manage"],
  ];

  it.each(R2_4D2)("%s -> \"%s\" %s kapısında", (relative, label, action) => {
    const text = readFileSync(join(ROOT, relative), "utf8");

    const occurrences: number[] = [];
    for (let at = text.indexOf(label); at > -1; at = text.indexOf(label, at + 1)) {
      const window = text.slice(Math.max(0, at - 1200), at);
      const open = Math.max(window.lastIndexOf("<button"), window.lastIndexOf("<Button"));
      const close = Math.max(window.lastIndexOf("</button>"), window.lastIndexOf("</Button>"));

      /*
       * KENDİNİ KAPATAN DÜĞME AÇIK SAYILMAZ. `<button ... />` biçiminde
       * bir eleman (örneğin panel kapatma katmanı) kapanış etiketi
       * taşımadığı için "açık" görünüyordu; ondan sonraki bir BAŞLIK
       * metni yanlışlıkla düğme etiketi sayılıyordu.
       */
      const selfClosed = open > -1 && window.slice(open).includes("/>");

      if (open > -1 && open > close && !selfClosed) occurrences.push(at);
    }

    expect(occurrences.length, `"${label}" düğme olarak bulunamadı`).toBeGreaterThan(0);

    for (const at of occurrences) {
      const before = text.slice(0, at);
      const gate = before.lastIndexOf('.can("');

      expect(gate, `"${label}" için kapı yok`).toBeGreaterThan(-1);
      expect(at - gate, `"${label}" kapıdan çok uzak`).toBeLessThan(900);

      /*
       * KANCA ADI DA KARŞILAŞTIRILIYOR.
       *
       * Önce yalnız eylem adına bakılıyordu; `rfqActions.can("create")`
       * yerine `actions.can("create")` yazılsa test geçiyordu — yani
       * çapraz modül kapısının yanlış modüle kayması yakalanmıyordu.
       * Sonda (probe) bunu gösterdi.
       *
       * Beklenen değer "create" ise yalnız eylem, "rfqActions.create"
       * ise kanca + eylem karşılaştırılır.
       */
      // Dilim `.can("` ile başlıyor; kanca adı ONDAN ÖNCE, o yüzden
      // pencere geriye açılıyor ve en SON eşleşme alınıyor.
      const gateText = before.slice(Math.max(0, gate - 40));
      const found = [...gateText.matchAll(/(\w+)\.can\("([^"]+)"\)/g)];
      const match = found[found.length - 1];
      const actual = action.includes(".")
        ? `${match?.[1]}.${match?.[2]}`
        : match?.[2];

      expect(actual, `"${label}" yanlış kapıda`).toBe(action);
    }
  });

  /**
   * ÇAPRAZ MODÜL KAPILARI İKİNCİ KANCADAN GELİYOR.
   *
   * Bir düğme ekranın modülü dışında bir izin istiyorsa, o izin ayrı
   * bir `useModuleActions` çağrısıyla alınmalı. Ekranın kancasına
   * bağlamak (örneğin "RFQ Oluştur"u purchasing-requests'e) sessizce
   * yanlış yetkiye bakardı.
   */
  it("çapraz modül aksiyonları ayrı kanca kullanıyor", () => {
    const cases: Array<[string, string]> = [
      ["app/satin-alma/[id]/page.tsx", 'useModuleActions("purchasing-rfq")'],
      ["app/demirbas/servis/page.tsx", 'useModuleActions("purchasing-requests")'],
      ["app/projeler/[id]/malzeme-ihtiyaci/page.tsx", 'useModuleActions("purchasing-requests")'],
    ];

    for (const [relative, expected] of cases) {
      expect(
        readFileSync(join(ROOT, relative), "utf8"),
        `${relative}: ${expected} yok`,
      ).toContain(expected);
    }
  });

  /**
   * DEMİRBAŞ EKRANLARI personnel.* KULLANIYOR, inventory.* DEĞİL.
   *
   * Ekran adına bakan biri inventory derdi; uçlar personnel zorluyor
   * çünkü alet zimmeti personel kaydına bağlı. Bu test o türetmenin
   * geri kaymasını engelliyor.
   */
  it("demirbaş kapıları personel modülünde", () => {
    for (const relative of [
      "app/demirbas/page.tsx",
      "app/demirbas/[id]/page.tsx",
      "app/demirbas/servis/page.tsx",
    ]) {
      const text = readFileSync(join(ROOT, relative), "utf8");
      expect(text, `${relative}`).toContain('useModuleActions("personnel")');
      expect(text, `${relative} inventory kullanmamalı`).not.toContain(
        'useModuleActions("inventory")',
      );
    }
  });

  /**
   * ZİMMET VERMEK create, İADE ALMAK edit.
   *
   * Sezgiye ters: "iade = geri alma = delete" denirdi. Uç öyle
   * kurmamış — iade MEVCUT zimmet kaydını güncelliyor, yeni kayıt
   * açmıyor. Düğme adından değil uçtan türetmenin örneği.
   */
  it("zimmet ver create, iade al edit yetkisinde", () => {
    const text = readFileSync(join(ROOT, "app/demirbas/[id]/page.tsx"), "utf8");

    const returnAt = text.indexOf("İade Al");
    const returnGate = text.slice(0, returnAt).lastIndexOf('.can("');
    expect(/\.can\("([^"]+)"\)/.exec(text.slice(returnGate))?.[1]).toBe("edit");

    // zimmet verme düğmesi ("Zimmet Ver" / "Devret") create kapısında
    const assignAt = text.indexOf("Zimmet Ver");
    expect(assignAt).toBeGreaterThan(-1);
    const assignGate = text.slice(0, assignAt).lastIndexOf('.can("');
    expect(/\.can\("([^"]+)"\)/.exec(text.slice(assignGate))?.[1]).toBe("create");
  });
  /**
   * AYNI DÜĞME İKİ AYRI UCA GİDİYORSA KAPI DA İKİ DALLI OLMALI.
   *
   * Bir form hem yeni kayıt (POST -> create) hem düzenleme
   * (PUT -> edit) yapıyorsa tek anahtara indirgemek iki yönde de
   * bozuk: `create`e bağlanırsa düzenleme yetkisi olan kaydedemez,
   * `edit`e bağlanırsa yeni kayıt açan göremez.
   *
   * Sonda bu testin yokluğunda dalın sessizce silinebildiğini
   * gösterdi: ternary'yi tek çağrıya indirince hiçbir test düşmedi.
   */
  const DUAL_ENDPOINT_BUTTONS: Array<[string, string]> = [
    ["app/demirbas/page.tsx", "Kaydediliyor..."],
    ["app/projeler/[id]/santiyeler/[siteId]/page.tsx", "Raporu Güncelle"],
  ];

  it.each(DUAL_ENDPOINT_BUTTONS)("%s -> \"%s\" iki dallı kapıda", (relative, label) => {
    const text = readFileSync(join(ROOT, relative), "utf8");
    const at = text.indexOf(label);
    expect(at, `"${label}" bulunamadı`).toBeGreaterThan(-1);

    const before = text.slice(Math.max(0, at - 400), at);

    expect(before, `"${label}": create dalı yok`).toContain('can("create")');
    expect(before, `"${label}": edit dalı yok`).toContain('can("edit")');
  });
  /**
   * R2/4d yığın 3 — İK, teklif, yönetim.
   *
   * Bu yığının hassasiyeti: ekranların dördü ELDEN ÖDEME ve MAAŞ
   * taşıyor. Kapı eklemek maskeyi değiştirmez — maske "görebilir mi",
   * kapı "yazabilir mi" sorusu. Aşağıdaki ayrı test maskelerin yerinde
   * durduğunu doğruluyor.
   */
  const R2_4D3: Array<[string, string, string]> = [
    ["app/insan-kaynaklari/cikis-tazminat/page.tsx", "Çıkış Kaydı Oluştur", "salaryActions.manage"],
    ["app/insan-kaynaklari/cikis-tazminat/page.tsx", "Kesinleştir", "payrollActions.approve"],
    ["app/insan-kaynaklari/ek-odemeler/page.tsx", "Kaydediliyor...", "manage"],
    ["app/insan-kaynaklari/veri-eksikleri/page.tsx", "alanı doldur", "edit"],
    ["app/personel/page.tsx", "Personeli Kaydet", "create"],
    ["app/insan-kaynaklari/ucret-kartlari/page.tsx", "Yeni Maaş Kartı", "manage"],
    ["app/insan-kaynaklari/personeller/page.tsx", "+ Yeni Personel", "create"],
    ["app/insan-kaynaklari/personeller/page.tsx", "Personeli Düzenle", "edit"],
    ["app/insan-kaynaklari/personeller/page.tsx", "Görev Yeri", "edit"],
    ["app/insan-kaynaklari/personeller/page.tsx", "Oluştur ve seç", "siteActions.create"],
    ["app/teklifler/[id]/page.tsx", "İcmale Aktar", "manage"],
    ["app/teklifler/takip/page.tsx", "Sözleşmeyi Aç", "manage"],
    ["app/sistem-yonetimi/yetki-matrisi/page.tsx", "Rolü oluştur", "create"],
    ["app/muhendislik/pozlar/[id]/page.tsx", "Değişiklikleri Kaydet", "manage"],
    ["app/taseronlar/[id]/page.tsx", "Yeni Hakediş", "actions.manage"],
    ["app/taseronlar/[id]/page.tsx", "Ekibe Ekle", "actions.manage"],
    ["app/onay-merkezi/page.tsx", "Reddet", "orderActions.approve"],
  ];

  it.each(R2_4D3)("%s -> \"%s\" %s kapısında", (relative, label, action) => {
    const text = readFileSync(join(ROOT, relative), "utf8");

    const occurrences: number[] = [];
    for (let at = text.indexOf(label); at > -1; at = text.indexOf(label, at + 1)) {
      const window = text.slice(Math.max(0, at - 1200), at);
      const open = Math.max(window.lastIndexOf("<button"), window.lastIndexOf("<Button"));
      const close = Math.max(window.lastIndexOf("</button>"), window.lastIndexOf("</Button>"));

      /*
       * KENDİNİ KAPATAN DÜĞME AÇIK SAYILMAZ. `<button ... />` biçiminde
       * bir eleman (örneğin panel kapatma katmanı) kapanış etiketi
       * taşımadığı için "açık" görünüyordu; ondan sonraki bir BAŞLIK
       * metni yanlışlıkla düğme etiketi sayılıyordu.
       */
      const selfClosed = open > -1 && window.slice(open).includes("/>");

      if (open > -1 && open > close && !selfClosed) occurrences.push(at);
    }

    expect(occurrences.length, `"${label}" düğme olarak bulunamadı`).toBeGreaterThan(0);

    for (const at of occurrences) {
      const before = text.slice(0, at);
      const gate = before.lastIndexOf('.can("');

      expect(gate, `"${label}" için kapı yok`).toBeGreaterThan(-1);
      expect(at - gate, `"${label}" kapıdan çok uzak`).toBeLessThan(900);

      const gateText = before.slice(Math.max(0, gate - 40));
      const found = [...gateText.matchAll(/(\w+)\.can\("([^"]+)"\)/g)];
      const match = found[found.length - 1];
      const actual = action.includes(".")
        ? `${match?.[1]}.${match?.[2]}`
        : match?.[2];

      expect(actual, `"${label}" yanlış kapıda`).toBe(action);
    }
  });

  /**
   * MASKELER YERİNDE — kapı eklemek görünürlük mantığını değiştirmedi.
   *
   * Elden ödeme ve maaş taşıyan ekranlarda iki ayrı mekanizma var ve
   * İKİSİ DE gerekli:
   *   maske  -> tutarı GÖREBİLİR Mİ  (backend projeksiyonu + view izni)
   *   kapı   -> YAZABİLİR Mİ         (extra_payment.manage)
   *
   * Bu test maskenin silinip yerine kapı konmadığını doğruluyor. Maske
   * güvenlik sınırı; kapı yalnızca arayüz kolaylığı.
   */
  it("elden ödeme maskeleri kapı eklendikten sonra da yerinde", () => {
    const p360 = readFileSync(
      join(ROOT, "app/insan-kaynaklari/personel-360/page.tsx"),
      "utf8",
    );
    // backend projeksiyonundan gelen maske bayrağı duruyor
    expect(p360).toContain("financial.extraPaymentHidden");
    // ve kapı ONUN ÜSTÜNE eklendi, yerine geçmedi
    expect(p360).toContain("!financial.extraPaymentHidden && canManage");

    const personnel = readFileSync(
      join(ROOT, "app/insan-kaynaklari/personeller/page.tsx"),
      "utf8",
    );
    expect(personnel).toContain('permissions.has("salary.view")');
    expect(personnel).toContain('permissions.has("extra_payment.manage")');
  });

  /**
   * ELDEN CARİ KAYDI TAŞERON YETKİSİYLE AÇILAMAZ.
   *
   * `subcontractor-ledger/cash` ucu extra_payment.manage istiyor;
   * faturalı kayıt (`subcontractor-ledger`) subcontractor.manage.
   * Aynı düğme, seçime göre iki ayrı modül. Tek anahtara indirgemek
   * elden izolasyonunu taşeron modülünden delerdi.
   *
   * Ayrıca "Elden" SEÇENEĞİ de yetkisiz kullanıcıya sunulmuyor:
   * seçip formu doldurup reddi kaydederken yemek kötü deneyim.
   */
  it("taşeronda elden kayıt ayrı yetkide", () => {
    const text = readFileSync(join(ROOT, "app/taseronlar/[id]/page.tsx"), "utf8");

    expect(text).toContain('useModuleActions("extra_payment")');
    expect(text).toContain("entryIsCash");
    // kaydet düğmesi seçime göre modül değiştiriyor
    expect(text).toMatch(
      /entryIsCash[\s\S]{0,120}extraPaymentActions\.can\("manage"\)[\s\S]{0,120}actions\.can\("manage"\)/,
    );
    // elden seçeneği yetkisizde listede yok
    expect(text).toMatch(
      /extraPaymentActions\.can\("manage"\)[\s\S]{0,120}value="cash"/,
    );
  });

  /**
   * ONAY MERKEZİ TEK KAPIYA BAĞLANAMAZ.
   *
   * Dört modülün onayını topluyor. Ekranı tek anahtara bağlamak,
   * yalnız satın alma onayı olan kullanıcıya hakediş bölümünü de
   * gösterirdi (ya da tersine sipariş bölümünü gizlerdi).
   */
  it("onay merkezi bölüm bölüm kapılı", () => {
    const text = readFileSync(join(ROOT, "app/onay-merkezi/page.tsx"), "utf8");

    for (const hook of [
      'useModuleActions("hakedis")',
      'useModuleActions("purchasing-orders")',
      'useModuleActions("purchasing-requests")',
      'useModuleActions("site-reports")',
    ]) {
      expect(text, `${hook} yok`).toContain(hook);
    }

    // iptaller delete'te, onaylar approve'da
    expect(text).toContain('hakedisActions.can("delete")');
    expect(text).toContain('requestActions.can("delete")');
    expect(text).toContain('hakedisActions.can("approve")');
  });

  /**
   * YETKİ MATRİSİ HÜCRELERİ GİZLENMİYOR, PASİFLEŞTİRİLİYOR.
   *
   * Matris bir TABLO: hücreyi kaldırmak satırı bozar ve okuma yetkisi
   * olan kullanıcı mevcut yetki dağılımını göremez. Okuma korunuyor,
   * yazma kapanıyor.
   */
  it("yetki matrisinde hücreler pasifleşiyor, kaybolmuyor", () => {
    const text = readFileSync(
      join(ROOT, "app/sistem-yonetimi/yetki-matrisi/page.tsx"),
      "utf8",
    );

    /*
     * İKİ AYRI DÜĞME, İKİSİ DE KONTROL EDİLİYOR:
     *   veri kapsamı düğmesi (role.name === "Admin" kontrolüyle)
     *   izin hücresi        (isPending kontrolüyle)
     * Önce tek bir `disabled={... !can("edit")` deseni aranıyordu;
     * hücrenin kapısı silinse kapsam düğmesi eşleşiyor ve test
     * geçiyordu. Sonda bunu gösterdi.
     */
    expect(text, "izin hücresi kapısı yok").toMatch(
      /isPending[\s\S]{0,80}!actions\.can\("edit"\)/,
    );
    expect(text, "veri kapsamı düğmesi kapısı yok").toMatch(
      /role\.name === "Admin" \|\| !actions\.can\("edit"\)/,
    );
  });
  /**
   * SUNUCU TARAFI PDF HİÇ YAZILMAMIŞ — ÖLÜ DÜĞMELER KALDIRILDI.
   *
   * `report.service.ts` beş PDF ucu tanımlıyordu; backend'de `api/reports`
   * rotası YOK ve PDF üretebilecek kütüphane de yok (QuestPDF, iText,
   * DinkToPdf, Puppeteer, wkhtmltopdf — hiçbiri projede değil).
   *
   * Kaldırmanın gerekçesi "kullanılmıyor" değil: her iki ekranda da
   * ÇALIŞAN bir yazdırma sayfası ölü düğmenin YANINDA duruyor
   * (`/hakedis/{id}/yazdir`, `/satin-alma/siparis/{id}/yazdir`).
   * PDF yeteneği tarayıcının yazdırma penceresiyle zaten sağlanıyor;
   * ölü düğme yalnızca kullanıcıyı yanıltıyordu.
   *
   * Bu test yazdırma yolunun DURDUĞUNU da doğruluyor — düğme
   * kaldırıldıktan sonra o link tek çıkış yolu.
   */
  it("ölü PDF düğmeleri kalktı, yazdırma yolu duruyor", () => {
    const hakedis = readFileSync(join(ROOT, "app/hakedis/[id]/page.tsx"), "utf8");
    const siparis = readFileSync(
      join(ROOT, "app/satin-alma/siparis/[id]/page.tsx"),
      "utf8",
    );

    for (const [text, name] of [[hakedis, "hakedis"], [siparis, "siparis"]] as const) {
      /*
       * YORUMLAR SOYULUYOR. Kaldırma gerekçesi kodda yorum olarak
       * yazılı ve içinde eski düğme adı geçiyor; ham metinde arayan
       * bir tarama kendi açıklamamızı bulgu sanıyordu.
       */
      const code = text
        .replace(/\/\*[\s\S]*?\*\//g, " ")
        .replace(/\{\/\*[\s\S]*?\*\/\}/g, " ")
        .replace(/\/\/[^\n]*/g, " ");

      expect(code, `${name}: reportService hâlâ kullanılıyor`).not.toContain(
        "reportService",
      );
      expect(code, `${name}: ölü PDF düğmesi hâlâ var`).not.toMatch(/PDF İndir/);
    }

    // çalışan çıkış yolu duruyor
    expect(hakedis).toContain("/yazdir");
    expect(siparis).toContain("/yazdir");

    // Excel yolu da duruyor (hakedişte ikinci alternatif)
    expect(hakedis).toContain("hakedis-export");

    // servis dosyası silindi
    expect(existsSync(join(ROOT, "services", "report.service.ts"))).toBe(false);
  });
  /**
   * R2/4 KAPANIŞ — son iki ekran.
   *
   * İkisinde de rota kapısı yetmiyordu: ekranın asıl işi listeyi
   * GÖSTERMEK, o yüzden rota okuma izniyle açılıyor. Ama proje açmak ve
   * talep açmak ayrı yetkiler.
   */
  const R2_KAPANIS: Array<[string, string, string]> = [
    ["app/projeler/page.tsx", "+ Yeni Proje", "create"],
    ["app/projeler/page.tsx", "Düzenle", "edit"],
    ["app/satin-alma/page.tsx", "Taslak Kaydet", "create"],
    ["app/insan-kaynaklari/puantaj-cetveli/page.tsx", "Takvimden Doldur", "create"],
    ["app/insan-kaynaklari/puantaj-cetveli/page.tsx", "Ayı Onayla", "approve"],
    ["app/insan-kaynaklari/tatil-takvimi/page.tsx", "Sabit Tatilleri Ekle", "manage"],
    ["app/insan-kaynaklari/tatil-takvimi/page.tsx", "Bayramı Ekle", "manage"],
    ["app/insan-kaynaklari/tatil-takvimi/page.tsx", "Günü Ekle", "manage"],
    ["app/insan-kaynaklari/tatil-takvimi/page.tsx", "Kaldır", "manage"],
    ["app/insan-kaynaklari/tatil-takvimi/page.tsx", "Takvimi Doğrula", "manage"],
  ];

  it.each(R2_KAPANIS)("%s -> \"%s\" %s kapısında", (relative, label, action) => {
    const text = readFileSync(join(ROOT, relative), "utf8");

    const occurrences: number[] = [];
    for (let at = text.indexOf(label); at > -1; at = text.indexOf(label, at + 1)) {
      const window = text.slice(Math.max(0, at - 1200), at);
      const open = Math.max(window.lastIndexOf("<button"), window.lastIndexOf("<Button"));
      const close = Math.max(window.lastIndexOf("</button>"), window.lastIndexOf("</Button>"));
      const selfClosed = open > -1 && window.slice(open).includes("/>");
      if (open > -1 && open > close && !selfClosed) occurrences.push(at);
    }

    expect(occurrences.length, `"${label}" düğme olarak bulunamadı`).toBeGreaterThan(0);

    for (const at of occurrences) {
      const before = text.slice(0, at);
      const gate = before.lastIndexOf('.can("');

      expect(gate, `"${label}" için kapı yok`).toBeGreaterThan(-1);
      expect(at - gate, `"${label}" kapıdan çok uzak`).toBeLessThan(900);

      const gateText = before.slice(Math.max(0, gate - 40));
      const found = [...gateText.matchAll(/(\w+)\.can\("([^"]+)"\)/g)];
      const match = found[found.length - 1];
      const actual = action.includes(".")
        ? `${match?.[1]}.${match?.[2]}`
        : match?.[2];

      expect(actual, `"${label}" yanlış kapıda`).toBe(action);
    }
  });

  /**
   * KAPANIŞ SÖZLEŞMESİ — YENİ KAPISIZ EKRAN EKLENEMEZ.
   *
   * R2/4 kapandığında yazan aksiyonu olan 97 ekranın hepsi üç yoldan
   * biriyle korunuyordu:
   *   1. düğme kapısı  (useModuleActions)      64 ekran
   *   2. rota kapısı   (tam sayfa aksiyon ekranı) 16 ekran
   *   3. satır içi izin (önceden var olan, uçlarla eşleştiği ölçüldü) 15 ekran
   *
   * Bu test yeni bir ekran YAZAN bir uç çağırıp hiçbir kapı taşımazsa
   * düşer. Amacı kapsamı dondurmak değil — yeni ekranın hangi yolu
   * seçtiğini BİLİNÇLİ bir karar yapmak. Yeni ekran eklerken listeye
   * eklemek gerekiyorsa, o eklemenin kendisi kararın kaydı olur.
   *
   * ARAYÜZ GÜVENLİK SINIRI DEĞİL: uçlardaki RequirePermission sınır.
   * Buradaki bir eksik veriyi açığa çıkarmaz, kullanıcıya yapamayacağı
   * işi gösterir.
   */
  it("yazan aksiyonu olan her ekran bir kapı taşıyor", () => {
    /*
     * ROTA KAPISIYLA KORUNAN TAM SAYFA AKSİYON EKRANLARI.
     * Tek işi bir yazma çağrısı olan ekranlarda düğmeyi gizlemek
     * yetmiyor: yalnız görüntüleme yetkisi olan biri uzun formu
     * doldurup reddi ancak kaydederken yiyor. Bunlar
     * lib/auth/route-permissions.ts içinde yazma izniyle kapılı ve
     * tests/route-permissions.test.ts bunu doğruluyor.
     */
    const ROTA_KAPILI = [
      "app/depo-stok/mal-kabul/yeni/page.tsx",
      "app/depo-stok/malzeme-talepleri/yeni/page.tsx",
      "app/fiyat-farki/endeksler/yeni/page.tsx",
      "app/fiyat-farki/profiller/yeni/page.tsx",
      "app/hakedis/[id]/duzenle/page.tsx",
      "app/hakedis/yeni/page.tsx",
      "app/kesifler/yeni/page.tsx",
      "app/metrajlar/yeni/page.tsx",
      "app/muhasebe/fisler/[id]/duzenle/page.tsx",
      "app/muhasebe/fisler/yeni/page.tsx",
      "app/muhasebe/hesap-plani/yeni/page.tsx",
      "app/muhasebe/faturalar/yeni/page.tsx",
      "app/muhasebe/satis-faturalari/yeni/page.tsx",
      "app/muhendislik/pozlar/ice-aktar/page.tsx",
      "app/muhendislik/pozlar/ozel/page.tsx",
      "app/muhendislik/pozlar/yeni/page.tsx",
      "app/muhendislik/receteler/ice-aktar/page.tsx",
      "app/depo-stok/yeni/page.tsx",
      "app/perakende/fiyatlar/page.tsx",
      "app/projeler/[id]/santiyeler/yeni/page.tsx",
      "app/teklifler/yeni/page.tsx",
    ];

    const kapisiz: string[] = [];

    for (const path of screens(join(ROOT, "app"))) {
      const text = readFileSync(path, "utf8");
      if (!text.includes('design="redwood"')) continue;

      // yazan bir servis çağrısı var mı (kaba ama yeterli sinyal)
      const yazan = /(Service)\.(create|update|delete|remove|approve|reject|submit|cancel|post|assign|save|toggle|advance|decide|finalize|replace|upload|commit|refresh|void|revoke)/.test(
        text,
      );
      if (!yazan) continue;

      const kapili =
        text.includes("useModuleActions") ||
        text.includes("usePermissions") ||
        text.includes("hasPermission(session") ||
        text.includes('apiClient<{ permissions');

      const rel = path.slice(path.indexOf("app/")).split("\\").join("/");

      if (!kapili && !ROTA_KAPILI.includes(rel)) kapisiz.push(rel);
    }

    expect(
      kapisiz,
      `Bu ekranlar yazan uç çağırıyor ama kapı taşımıyor. Ya düğme ` +
        `kapısı ekleyin (useModuleActions, izni ucun ` +
        `RequirePermission'ından türetin) ya da tam sayfa aksiyon ` +
        `ekranıysa route-permissions.ts'e yazma izni kuralı ekleyip ` +
        `yukarıdaki ROTA_KAPILI listesine alın.`,
    ).toEqual([]);
  });
  /**
   * ÖLÜ EKRAN KALDIRILDI: proje kesinti politikası.
   *
   * `projeler/[id]/kesintiler` (463 satır) hem listesini hem kaydını
   * `progress-payment-deduction-rules` ucuna yapıyordu. O uç backend'de
   * HİÇ YOK: controller yok, rota yok, `ProgressPaymentDeductionRule`
   * modeli bile yok. Ekran açılıyor, liste yüklenmiyor, kaydet her
   * zaman hata veriyordu.
   *
   * Kesintiler pratikte ÇALIŞIYOR ama başka yolla: hakediş
   * oluşturulurken/düzenlenirken belge başına giriliyor
   * (`ProgressPaymentsController.ApplyDeductions`). Fiyat farkı ve PDF
   * ile aynı desen — elle giriş çalışıyor, otomasyon katmanı hiç
   * yazılmamış.
   *
   * İKİ GİRİŞ NOKTASI vardı ve ikisi de kaldırıldı: proje detayındaki
   * "Kesinti Politikası" modül kartı ve "Finansal Sözleşme Oranları"
   * panelindeki bağlantı.
   */
  it("ölü kesinti politikası ekranı ve girişleri kalmadı", () => {
    expect(
      existsSync(join(ROOT, "app", "projeler", "[id]", "kesintiler")),
      "ölü ekran hâlâ duruyor",
    ).toBe(false);

    expect(
      existsSync(join(ROOT, "services", "deduction-rule.service.ts")),
      "ölü servis hâlâ duruyor",
    ).toBe(false);

    const projeDetay = readFileSync(join(ROOT, "app/projeler/[id]/page.tsx"), "utf8");
    const code = projeDetay
      .replace(/\{\/\*[\s\S]*?\*\/\}/g, " ")
      .replace(/\/\*[\s\S]*?\*\//g, " ")
      .replace(/\/\/[^\n]*/g, " ");

    expect(code, "modül kartı ya da bağlantı hâlâ var").not.toContain("kesintiler");

    /*
     * ÇALIŞAN AKIŞ DURUYOR: kesinti türleri ve varsayılan oranları
     * hakediş hesabında tanımlı ve sunucudaki HakedisDeductionType ile
     * eşleşiyor. Ölü servisin kendi enum'u vardı; silinen o.
     */
    const hesap = readFileSync(join(ROOT, "lib", "hakedis", "calculation.ts"), "utf8");
    expect(hesap).toContain("DeductionType");
  });
  /**
   * DEĞERİ DE GÖSTEREN DÜĞME GİZLENMEZ, DÜZ METNE DÜŞER.
   *
   * İki yerde aynı desen var: piyasa ekranındaki tonaj hücresi ve
   * depo-stok'taki asgari stok hücresi. Düğme aynı zamanda DEĞERİ
   * gösteriyor; gizlemek okuma yetkisi olan kullanıcıdan veriyi de
   * saklardı. Sadece düzenleme girişi kapanıyor.
   *
   * Bu test yedeğin silinip düğmenin tamamen gizlenmesini yakalar.
   */
  /*
   * DEPO-STOK ARTIK BU LİSTEDE DEĞİL (S8) — kural gevşetilmedi,
   * KONUSU KALMADI.
   *
   * Buradaki madde kart listesindeki satır içi asgari stok hücresiydi:
   * hem değeri gösteren hem düzenleten bir düğme. Asgari seviye artık
   * karta değil DEPOYA ait (`warehouse_stock_levels`) ve bir kartın
   * birden çok deposu olabildiği için tek hücreye sığmıyordu; hücre
   * kaldırıldı. Değer o sütunda artık HER kullanıcıya düz metin olarak
   * basılıyor — yani kuralın istediğinden daha fazlası sağlanıyor,
   * gizlenecek bir düğme yok. Tanım ekranı ayrı:
   * /depo-stok/stok-seviyeleri (POST api/stock-levels -> inventory.edit).
   *
   * Piyasa ekranındaki tonaj hücresi duruyor ve kural onu korumaya
   * devam ediyor.
   */
  const DEGER_GOSTEREN_HUCRELER: Array<[string, string]> = [
    ["app/finans/piyasa/page.tsx", 'actions.can("manage") ? ('],
  ];

  it.each(DEGER_GOSTEREN_HUCRELER)("%s değer yedeği duruyor", (relative, marker) => {
    const text = readFileSync(join(ROOT, relative), "utf8");

    const at = text.indexOf(marker);
    expect(at, `${relative}: yetki dalı yok`).toBeGreaterThan(-1);

    /*
     * YETKİSİZ DALDA DEĞER YİNE BASILIYOR. İşaretçiden sonraki blokta
     * hem bir <span> hem de değeri biçimleyen çağrı olmalı; düğme
     * tamamen gizlenirse ikisi de kaybolur.
     */
    const dal = text.slice(at, at + 1500);
    expect(dal, `${relative}: düz metin yedeği yok`).toContain("<span");
    expect(dal, `${relative}: değer basılmıyor`).toContain("formatNumber(");
  });
});
