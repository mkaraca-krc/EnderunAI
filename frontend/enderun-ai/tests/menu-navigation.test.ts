import { describe, expect, it } from "vitest";

import {
  MENU_GROUPS,
  findMenuEntry,
  foldTurkish,
  searchMenu,
  visibleMenuGroups,
} from "@/lib/navigation/menu";

/**
 * MENÜ AĞACI — kabuk, komut paleti ve kırıntı yolunun ortak kaynağı.
 *
 * Bu testler üç şeyi sabitler: arama Türkçe yazıma takılmıyor, arama
 * kullanıcının GÖREMEDİĞİ sayfayı asla göstermiyor, ve bir yolun
 * menüdeki karşılığı doğru bulunuyor (kırıntı yolu buna dayanıyor).
 */
describe("Türkçe katlama", () => {
  /**
   * Kullanıcı arama kutusuna Türkçe karakter yazmak için klavye
   * değiştirmez: "hakedis" yazan "Hakediş"i bulmalı.
   */
  it("Türkçe harfleri ASCII karşılığına indirir", () => {
    expect(foldTurkish("Hakediş")).toBe("hakedis");
    expect(foldTurkish("ŞUBELER")).toBe("subeler");
    expect(foldTurkish("Ödeme Günlüğü")).toBe("odeme gunlugu");
  });

  /**
   * TÜRKÇE KİPTE KÜÇÜLTME YAPILMIYOR: "I" harfi Türkçe kipte noktasız
   * "ı"ya döner ve marka adları aranamaz hale gelirdi.
   */
  it("büyük I harfini noktasız ı'ya çevirmez", () => {
    expect(foldTurkish("SCHNEIDER")).toBe("schneider");
    expect(foldTurkish("SIEMENS")).toBe("siemens");
  });
});

describe("menü araması", () => {
  it("sayfa adına göre bulur", () => {
    const results = searchMenu("hakedis", MENU_GROUPS);

    expect(results.length).toBeGreaterThan(0);
    expect(
      results.some((result) => foldTurkish(result.item.label).includes("hakedis")),
    ).toBe(true);
  });

  /** Aranan metinle BAŞLAYAN sayfa listenin başında olmalı. */
  it("baştan eşleşeni öne alır", () => {
    const results = searchMenu("kasa", MENU_GROUPS);

    expect(foldTurkish(results[0].item.label).startsWith("kasa")).toBe(true);
  });

  it("bölüm adıyla da bulur", () => {
    const results = searchMenu("finans", MENU_GROUPS);

    expect(results.length).toBeGreaterThan(0);
  });

  it("boş sorguda liste döner, hata vermez", () => {
    expect(searchMenu("", MENU_GROUPS).length).toBeGreaterThan(0);
  });

  it("eşleşme yoksa boş döner", () => {
    expect(searchMenu("zzzzzz-boyle-bir-sayfa-yok", MENU_GROUPS)).toEqual([]);
  });

  /**
   * ASIL GÜVENCE: palet yalnızca kabuğun süzdüğü listeden besleniyor.
   * Süzülmüş liste verildiğinde, izni olmayan sayfa arama sonucunda
   * ÇIKMAZ — palet yeni bir görünürlük yolu açmıyor.
   */
  it("süzülmüş listede olmayan sayfa sonuçlarda çıkmaz", () => {
    const onlyDashboard = visibleMenuGroups(new Set(["dashboard.view"]), false);

    const results = searchMenu("bordro", onlyDashboard);

    expect(results.every((result) => result.item.href !== "/insan-kaynaklari/bordro")).toBe(
      true,
    );
  });
});

describe("görünür menü", () => {
  /**
   * Oturum gelmeden menü boş: dolu menüyü gösterip sonra öğe
   * kaybetmek, kullanıcıya olmayan yetkiyi bir an göstermek olurdu.
   */
  it("izin kümesi yoksa menü boş", () => {
    expect(visibleMenuGroups(null, false)).toEqual([]);
  });

  it("her izne sahip kullanıcı tüm bölümleri görür", () => {
    const all = visibleMenuGroups(new Set<string>(), true);

    expect(all.length).toBe(MENU_GROUPS.length);
  });

  /** Görünür öğesi kalmayan bölüm başlığı da düşer. */
  it("boş kalan bölüm başlığı da çıkmaz", () => {
    const limited = visibleMenuGroups(new Set(["dashboard.view"]), false);

    expect(limited.every((group) => group.items.length > 0)).toBe(true);
    expect(limited.length).toBeLessThan(MENU_GROUPS.length);
  });
});

describe("yol → menü karşılığı", () => {
  it("tam eşleşmeyi bulur", () => {
    const entry = findMenuEntry("/muhasebe/fisler");

    expect(entry?.item.href).toBe("/muhasebe/fisler");
  });

  /**
   * EN UZUN EŞLEŞME KAZANIR: /muhasebe/fisler/yeni hem "Muhasebe
   * Fişleri" hem "Yeni Muhasebe Fişi" ile eşleşiyor; kullanıcının
   * gerçekten durduğu yer daha uzun olanı.
   */
  it("iç içe yollarda en uzun eşleşmeyi seçer", () => {
    const entry = findMenuEntry("/muhasebe/fisler/yeni");

    expect(entry?.item.href).toBe("/muhasebe/fisler/yeni");
  });

  it("alt yolu üst sayfaya bağlar", () => {
    const entry = findMenuEntry("/muhasebe/fisler/8f2c-detay");

    expect(entry?.item.href).toBe("/muhasebe/fisler");
  });

  /**
   * Menüde karşılığı olmayan yol için UYDURMA bir üst seviye
   * gösterilmez; kırıntı yolu yalnızca sayfa başlığıyla kalır.
   */
  it("menüde olmayan yol için karşılık dönmez", () => {
    expect(findMenuEntry("/boyle-bir-sayfa-yok")).toBeNull();
  });
});
