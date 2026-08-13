import { describe, expect, it } from "vitest";

import {
  canAccessRoute,
  routePermission,
} from "@/lib/auth/route-permissions";

/**
 * YOL → İZİN HARİTASI: TEK KAYNAK.
 *
 * NEDEN VAR: bu harita önce İKİ KOPYAYDI (middleware + menü) ve
 * ayrışmıştı. Menüde gizlenen dokuz ekran — elden ödemeler ve gider
 * merkezi dahil — adres çubuğuna yazan kullanıcıya açılıyordu; filo
 * ise menüde herkese görünüp tıklanınca yetkisiz sayfasına düşüyordu.
 * Bu testler kopyanın geri gelmesini değil, kuralların doğruluğunu
 * korur.
 */
describe("yol izinleri", () => {
  /**
   * Ayrışmanın kapandığı dokuz yol: menüde gizliydi ama URL ile
   * açılabiliyordu. Artık ikisi de aynı kuraldan geçiyor.
   */
  it.each([
    ["/insan-kaynaklari/ek-odemeler", "extra_payment.view"],
    ["/finans/gider-merkezi", "expense.view"],
    ["/taseronlar", "subcontractor.view"],
    ["/isg/kazalar", "isg.incident.view"],
    ["/is-programi", "schedule.view"],
  ])("%s → %s", (path, permission) => {
    expect(routePermission(path)).toBe(permission);
  });

  it("filo aracı görme iznine bağlı", () => {
    expect(routePermission("/filo")).toBe("vehicle.view");
    expect(routePermission("/filo/abc-123")).toBe("vehicle.view");
  });

  /**
   * SPESİFİK KALIP GENELDEN ÖNCE: "bordro-on-kontrol" hem kendi
   * kalıbına hem genel "bordro" kalıbına uyuyor. Sıra bozulursa
   * kullanıcı ekranı açar, sonra uçtan 403 yer.
   */
  it("bordro ön kontrolü kendi anahtarını ister", () => {
    expect(routePermission("/insan-kaynaklari/bordro-on-kontrol")).toBe(
      "attendance-payroll.view",
    );

    expect(routePermission("/insan-kaynaklari/bordro")).toBe("payroll.view");
  });

  it("içe aktarma ekranları YAZMA yetkisi ister", () => {
    expect(routePermission("/muhendislik/receteler/ice-aktar")).toBe(
      "engineering.manage",
    );

    expect(routePermission("/muhendislik/receteler")).toBe("engineering.view");
  });

  it("proje alt ekranları kendi izinlerinde", () => {
    expect(routePermission("/projeler/1/maliyet-analizi")).toBe("hakedis.view");
    expect(routePermission("/projeler/1/malzeme-ihtiyaci")).toBe(
      "purchasing-requests.view",
    );
    expect(routePermission("/projeler/1")).toBe("projects.view");
  });

  /**
   * Personelin KENDİ İSG belgeleri bilinçli olarak açık: uç zaten
   * yalnız kendi kaydını döndürüyor. Kural null olmasaydı belgesini
   * görmek için İSG yetkisi gerekirdi.
   */
  it("personelin kendi belgeleri izin istemez", () => {
    expect(routePermission("/isg/benim")).toBeNull();
    expect(routePermission("/isg")).toBe("isg.view");
  });

  it("kuralı olmayan yol açıktır", () => {
    expect(routePermission("/dashboard")).toBeNull();
  });
});

describe("erişim kararı", () => {
  it("izni olan geçer", () => {
    expect(canAccessRoute("/finans", ["finance.view"], false)).toBe(true);
  });

  it("izni olmayan geçemez", () => {
    expect(canAccessRoute("/finans", ["projects.view"], false)).toBe(false);
  });

  /**
   * SÜPER KULLANICI ROL ADINDAN DEĞİL BAYRAKTAN: rol yeniden
   * adlandırılsa ya da başka bir role tüm izinler verilse ad kontrolü
   * yanlış cevap verirdi.
   */
  it("tüm izinleri olan her yeri görür", () => {
    expect(canAccessRoute("/sistem-yonetimi", [], true)).toBe(true);
    expect(canAccessRoute("/insan-kaynaklari/ek-odemeler", [], true)).toBe(true);
  });

  it("çoklu izinde herhangi biri yeter", () => {
    expect(
      canAccessRoute("/satin-alma/butce-onay", ["finance.view"], false),
    ).toBe(true);

    expect(
      canAccessRoute("/satin-alma/butce-onay", ["purchasing.view"], false),
    ).toBe(true);

    expect(canAccessRoute("/satin-alma/butce-onay", ["projects.view"], false)).toBe(
      false,
    );
  });

  /**
   * ROL BAZLI SENARYOLAR — gerçek rollerin izin kümesinden örnekler.
   * Teknik Ofis'in gider yetkisi yok: gider merkezini görmemeli.
   */
  it("Teknik Ofis gider merkezini görmez, mühendisliği görür", () => {
    const teknikOfis = ["engineering.view", "projects.view", "hakedis.view"];

    expect(canAccessRoute("/finans/gider-merkezi", teknikOfis, false)).toBe(false);
    expect(canAccessRoute("/muhendislik", teknikOfis, false)).toBe(true);
  });

  it("Formen yalnız iş programını görür, bordroyu görmez", () => {
    const formen = ["schedule.view", "site-reports.create"];

    expect(canAccessRoute("/is-programi", formen, false)).toBe(true);
    expect(canAccessRoute("/insan-kaynaklari/bordro", formen, false)).toBe(false);
  });

  /**
   * ELDEN MASKESİ BOZULMADI: ek ödemeler ekranı hâlâ kendi dar
   * izninde; bordro yetkisi olan ama ek ödeme yetkisi olmayan
   * kullanıcı giremez.
   */
  it("bordro yetkisi ek ödemeleri açmaz", () => {
    const payrollOnly = ["payroll.view", "personnel.view"];

    expect(
      canAccessRoute("/insan-kaynaklari/ek-odemeler", payrollOnly, false),
    ).toBe(false);

    expect(
      canAccessRoute("/insan-kaynaklari/ek-odemeler", ["extra_payment.view"], false),
    ).toBe(true);
  });
});
