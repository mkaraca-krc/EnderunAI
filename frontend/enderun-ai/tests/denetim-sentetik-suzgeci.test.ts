import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { sentetikSatir, sentetikSayisi } from "@/lib/denetim/sentetik-satir";

/**
 * DENETİM EKRANI SENTETİK SÜZGECİ (2026-09-16).
 *
 * Gürültü KAYDI değiştirerek değil EKRANI değiştirerek çözülüyor.
 * Denetim yazıcısına hiçbir istisna eklenmedi (Kural 96: istisnanın
 * anahtarı saldırganın eline geçer).
 */
describe("sentetik satır ayrımı", () => {
  it("ısıtma ve sonda satırlarını tanır", () => {
    expect(sentetikSatir("isitma-yok-1789512488-6676")).toBe(true);
    expect(sentetikSatir("sonda-vekil-1789512512-a")).toBe(true);
    expect(sentetikSatir("sonda-k5")).toBe(true);
  });

  it("GERÇEK KULLANICIYI GİZLEMEZ — en önemli iddia", () => {
    expect(sentetikSatir("mehmet")).toBe(false);
    expect(sentetikSatir("test.admin")).toBe(false);
    expect(sentetikSatir(null)).toBe(false);
    expect(sentetikSatir("")).toBe(false);
    // Önek ORTADA geçerse sentetik SAYILMAZ; yalnız başlangıç sayılır.
    expect(sentetikSatir("ali-sonda-bey")).toBe(false);
  });

  it("sayım doğru", () => {
    const satirlar = [
      { actorUsername: "mehmet" },
      { actorUsername: "isitma-yok-1" },
      { actorUsername: "sonda-x" },
      { actorUsername: null },
    ];
    expect(sentetikSayisi(satirlar)).toBe(2);
  });
});

describe("ekran muhafızı", () => {
  const sayfa = readFileSync(
    join(__dirname, "..", "app", "sistem-yonetimi", "denetim-kayitlari", "page.tsx"),
    "utf8"
  );

  it("tarama sağlığı: sayfa okundu", () => {
    expect(sayfa.length).toBeGreaterThan(1000);
  });

  it("VARSAYILAN KAPALI — denetim ekranı eksiksiz doğar", () => {
    expect(sayfa).toContain("useState(false)");
    expect(sayfa).toContain("sentetikGizle");
  });

  it("GİZLENEN SAYISI EKRANDA — sessizce düşürülmüyor", () => {
    expect(sayfa).toContain("{gizlenen}");
    expect(sayfa).toMatch(/Kayıt eksiksizdir/);
  });

  it("TOPLAM SAYI GİZLEMEDEN ETKİLENMİYOR", () => {
    // `total` sunucudaki gerçek sayı; gizlenen satırlar ondan düşülmez.
    expect(sayfa).not.toMatch(/setTotal\(\s*gorunenSatirlar/);
    expect(sayfa).toContain("rows={gorunenSatirlar}");
  });

  it("IP uyarısı ölçümle daraltılmış — tarih taşıyor", () => {
    expect(sayfa).toContain("22:48");
    expect(sayfa).not.toMatch(/IP adresi alanı şu an güvenilmez/);
  });
});
