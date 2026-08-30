import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { MENU_GROUPS, visibleMenuGroups } from "@/lib/navigation/menu";

/*
 * İŞ EMRİ BULUNABİLİRLİĞİ — İŞEMRİ/1.
 *
 * NEDEN VAR: Genel Müdür /yapilacaklar'ı açtı ve "burda görev veya emir
 * yazılacak bir yer yok" dedi. Oluşturma formu 26 Temmuz'dan beri
 * /gorevler'de duruyordu; kullanıcı oraya gidileceğini bilmiyordu.
 *
 * Bu paket TEK KAPIYI korur: /yapilacaklar'a ikinci bir form
 * EKLENMEZ — ikinci form ikinci doğrulama, ikinci izin kapısı ve
 * ikinci hata yüzeyi demektir. /yapilacaklar yalnızca BAĞLANTI verir.
 */

const KOK = join(__dirname, "..");
const oku = (p: string) => readFileSync(join(KOK, p), "utf8");

describe("iş emri bulunabilirliği", () => {
  it("tarama boşa düşmüyor", () => {
    // POZİTİF KONTROL: dosyalar taşınırsa aşağıdaki iddialar boş
    // metinde sessizce yeşil kalırdı.
    expect(oku("app/gorevler/page.tsx").length).toBeGreaterThan(5000);
    expect(oku("app/yapilacaklar/page.tsx").length).toBeGreaterThan(5000);
    expect(MENU_GROUPS.flatMap((g) => g.items).length).toBeGreaterThan(50);
  });

  // ---------- B1 / B3: adlandırma ----------

  it("menüde İş Emirleri ile Bekleyen İşler ayrı ve yan yana", () => {
    const grup = MENU_GROUPS.find((g) =>
      g.items.some((i) => i.href === "/gorevler"),
    );

    expect(grup, "/gorevler menüde yok").toBeDefined();

    const etiketler = grup!.items.map((i) => i.label);
    const bekleyen = etiketler.indexOf("Bekleyen İşler");
    const isEmri = etiketler.indexOf("İş Emirleri");

    expect(bekleyen).toBeGreaterThanOrEqual(0);
    expect(isEmri).toBe(bekleyen + 1);
  });

  it("ekran metinleri menü etiketiyle çelişmiyor", () => {
    const gorevler = oku("app/gorevler/page.tsx");
    const yapilacaklar = oku("app/yapilacaklar/page.tsx");
    const detay = oku("app/gorevler/[id]/page.tsx");
    const pano = oku("components/tasks/work-task-dashboard-widget.tsx");

    expect(gorevler).toContain('title="İş Emirleri"');
    expect(yapilacaklar).toContain("<h1>Bekleyen İşler</h1>");

    // Kırıntı yolu iki adı da taşır: nereden geldiği ve nerede olduğu.
    expect(detay).toContain("Bekleyen İşler");
    expect(detay).toContain("İş Emirleri");

    // Pano varsayılan açılış sayfası — orada eski ad kalırsa ekran
    // kendi kendisiyle çelişir.
    expect(pano).not.toContain("Görev Yönetimi");
    expect(pano).toContain("İş Emirleri");
  });

  // ---------- B4: tek giriş ----------

  it("Onay Merkezi menüden kalktı ama rotası duruyor", () => {
    /*
     * YOLA BAKIYORUZ, ETİKETE DEĞİL.
     *
     * İlk yazdığımda etiketi aradım ve test kırmızı verdi: menüde
     * BAŞKA bir "Onay Merkezi" daha var —
     * `/insan-kaynaklari/onay-merkezi`, İK'nın kendi onay ekranı.
     * Ayrı ekran, aynı ad. B4'ün kaldırdığı giriş bu değil.
     *
     * Kaldırılan giriş kalkınca ad çakışması da bitti: menüde artık
     * tek bir "Onay Merkezi" var ve o gerçekten kendi ekranı.
     */
    const yollar = MENU_GROUPS.flatMap((g) => g.items).map((i) => i.href);
    expect(yollar).not.toContain("/onay-merkezi");

    // ROTA DURUR: yer imleri ve paylaşılmış bağlantılar kırılmasın.
    const sayfa = oku("app/onay-merkezi/page.tsx");
    expect(sayfa).toContain('redirect("/yapilacaklar")');
  });

  // ---------- B5: bağlantı, form değil ----------

  it("boş ekran İş Emirleri'ne bağlantı verir, form açmaz", () => {
    const s = oku("app/yapilacaklar/page.tsx");

    expect(s).toContain('<Link href="/gorevler"');
    expect(s).toContain("Yeni iş emri açmak için");

    /*
     * TEK KAPI: bu ekranda oluşturma formu OLMAMALI. `<form` ya da
     * `tasks` POST'u belirirse ikinci bir oluşturma yolu doğmuş
     * demektir ve bu paketin gerekçesi çöker.
     */
    expect(s).not.toContain("<form");
    expect(s).not.toMatch(/method:\s*"POST"/);
  });

  // ---------- S2a: düğme izin kapısının içinde ----------

  it("oluşturma düğmesi ve formu tasks.manage kapısının içinde", () => {
    const s = oku("app/gorevler/page.tsx");

    const dugme = s.indexOf("+ Yeni İş Emri");
    expect(dugme, "+ Yeni İş Emri düğmesi bulunamadı").toBeGreaterThan(-1);

    /*
     * DÜĞMEDEN GERİYE BAKIYORUZ.
     *
     * İlk yazdığımda dosyadaki İLK `can("manage")` çağrısını aldım ve
     * arayı 300 karakterle sınırladım; test 2845 ile kırmızı verdi.
     * Sebep koddaki bir kusur değildi: bu ekranda beş ayrı
     * `can("manage")` kapısı var (satır tablo eylemleri dahil) ve ilki
     * düğmeninki değil. Ölçüm yanlış yeri okuyordu.
     */
    const kapi = s.lastIndexOf('actions.can("manage") && (', dugme);
    expect(kapi, 'düğmeden önce can("manage") kapısı yok').toBeGreaterThan(-1);
    expect(dugme - kapi).toBeLessThan(300);

    // Form da aynı kapının içinde: düğme gizlenip form açık kalırsa
    // gizleme bir görüntüden ibaret olurdu.
    const form = s.indexOf("<h2>Yeni İş Emri</h2>");
    expect(form).toBeGreaterThan(dugme);
  });

  // ---------- S3a: menü izne bağlı ----------

  it("tasks.view olmayan kullanıcı menüde İş Emirleri görmez", () => {
    const yetkisiz = visibleMenuGroups(new Set(["projects.view"]), false);
    const etiketler = yetkisiz.flatMap((g) => g.items).map((i) => i.label);

    expect(etiketler).not.toContain("İş Emirleri");

    // POZİTİF KONTROL: izin verilince görünüyor — yoksa yukarıdaki
    // iddia menü tamamen boş olduğu için de yeşil kalırdı.
    const yetkili = visibleMenuGroups(new Set(["tasks.view"]), false);
    expect(
      yetkili.flatMap((g) => g.items).map((i) => i.label),
    ).toContain("İş Emirleri");
  });

  it("menüden gizlemek kapı değil: rota izni ayrıca tanımlı", () => {
    /*
     * S3'ün ikinci yarısı. Menüyü gizlemek bir güvenlik önlemi
     * DEĞİLDİR; adres çubuğuna elle yazan kullanıcıyı durduran şey
     * middleware.ts'teki rota kontrolüdür.
     */
    const rotalar = oku("lib/auth/route-permissions.ts");
    expect(rotalar).toContain('{ match: "/gorevler", permission: "tasks.view" }');

    const ara = oku("middleware.ts");
    expect(ara).toContain("routeErisimi");
    expect(ara).toContain('"/yetkisiz"');
  });
});
