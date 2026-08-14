import { describe, expect, it } from "vitest";

import { foldTurkish, matchesSearch } from "@/lib/search/fold";

/**
 * Ekranlardaki arama kutuları bu iki işlevle çalışıyor. Kural
 * bozulursa kullanıcı "hakedis" yazıp "Hakediş"i bulamaz; daha
 * kötüsü, marka adları aranamaz hale gelir.
 */
describe("Türkçe arama katlaması", () => {
  it("Türkçe harfleri ASCII karşılığına indirir", () => {
    expect(foldTurkish("Hakediş")).toBe("hakedis");
    expect(foldTurkish("ŞUBELER")).toBe("subeler");
    expect(foldTurkish("Ödeme Günlüğü")).toBe("odeme gunlugu");
    expect(foldTurkish("Çelik")).toBe("celik");
  });

  /**
   * REGRESYON KORUMASI: toLocaleLowerCase("tr") kullanılsaydı büyük
   * "I" noktasız "ı"ya düşer, "SCHNEIDER" → "schneıder" olurdu ve
   * malzeme listesindeki marka adları hiçbir aramada bulunmazdı.
   */
  it("büyük I harfini noktasız ı'ya çevirmez", () => {
    expect(foldTurkish("SCHNEIDER")).toBe("schneider");
    expect(foldTurkish("SIEMENS")).toBe("siemens");
  });
});

describe("matchesSearch", () => {
  it("boş sorguda her kaydı geçirir", () => {
    // Arama kutusu boşken listenin boşalması, kullanıcıya
    // "kayıt yok" derdi.
    expect(matchesSearch("", "Beton A.Ş.")).toBe(true);
    expect(matchesSearch("   ", "Beton A.Ş.")).toBe(true);
  });

  it("alanlardan herhangi birinde geçen metni bulur", () => {
    expect(matchesSearch("1234", "CR-001", "Yapı A.Ş.", "1234567890")).toBe(
      true,
    );
    expect(matchesSearch("yapi", "CR-001", "Yapı A.Ş.")).toBe(true);
  });

  it("eşleşme yoksa eler", () => {
    expect(matchesSearch("zzz", "CR-001", "Yapı A.Ş.")).toBe(false);
  });

  it("boş ve tanımsız alanlarda patlamaz", () => {
    // Cari kartların vergi no, kısa ad gibi alanları null gelebiliyor.
    expect(matchesSearch("yapi", null, undefined, "Yapı A.Ş.")).toBe(true);
    expect(matchesSearch("yapi", null, undefined)).toBe(false);
  });

  it("sorgunun başındaki ve sonundaki boşluğu yok sayar", () => {
    expect(matchesSearch("  yapi  ", "Yapı A.Ş.")).toBe(true);
  });
});
