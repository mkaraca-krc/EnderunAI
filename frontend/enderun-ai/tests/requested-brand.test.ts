import { describe, expect, it } from "vitest";

import {
  brandMismatch,
  requestedBrandError,
  requestedBrandLabel,
  requestedBrandState,
} from "@/lib/purchasing/requested-brand";

/**
 * İSTENEN MARKANIN ÜÇ DURUMU tek modülde yorumlanıyor; bu test o
 * yorumun ekranlar arasında kaymadığının güvencesi.
 *
 * Backend kuralıyla birebir aynı olmalı: marka boş + muadil işaretsiz
 * GEÇERSİZ, diğer üç bileşim geçerli.
 */
describe("istenen marka durumları", () => {
  it("marka dolu, muadil kabul yok → zorunlu", () => {
    const item = { requestedBrand: "Schneider", brandIrrelevant: false };

    expect(requestedBrandState(item)).toBe("required");
    expect(requestedBrandLabel(item)).toBe("Schneider (zorunlu)");
    expect(requestedBrandError(item)).toBeNull();
  });

  it("marka dolu, muadil kabul var → tercih (bilgi atılmaz)", () => {
    const item = { requestedBrand: "Siemens", brandIrrelevant: true };

    expect(requestedBrandState(item)).toBe("preferred");
    expect(requestedBrandLabel(item)).toContain("Siemens");
    expect(requestedBrandLabel(item)).toContain("muadil");
  });

  it("marka boş, muadil kabul var → farketmez", () => {
    const item = { requestedBrand: null, brandIrrelevant: true };

    expect(requestedBrandState(item)).toBe("irrelevant");
    expect(requestedBrandLabel(item)).toBe("Marka farketmez");
    expect(requestedBrandError(item)).toBeNull();
  });

  it("marka boş, muadil kabul yok → geçersiz", () => {
    expect(
      requestedBrandError({ requestedBrand: "", brandIrrelevant: false }),
    ).not.toBeNull();
  });

  it("yalnızca boşluk marka sayılmaz", () => {
    const item = { requestedBrand: "   ", brandIrrelevant: false };

    expect(requestedBrandState(item)).toBe("irrelevant");
    expect(requestedBrandError(item)).not.toBeNull();
  });
});

describe("marka sapması", () => {
  it("zorunlu markada farklı marka geldiyse sapma", () => {
    expect(
      brandMismatch({
        requestedBrand: "Schneider",
        brandIrrelevant: false,
        brand: "ABB",
      }),
    ).toBe(true);
  });

  it("aynı marka büyük/küçük harf farkıyla sapma değildir", () => {
    expect(
      brandMismatch({
        requestedBrand: "Schneider",
        brandIrrelevant: false,
        brand: "SCHNEIDER",
      }),
    ).toBe(false);
  });

  /**
   * Muadil kabul edilen kalemde farklı marka gelmesi zaten BEKLENEN
   * sonuçtur; orada uyarı çıkarmak ekranı gürültüye boğar ve gerçek
   * sapmaların fark edilmesini zorlaştırır.
   */
  it("muadil kabulde farklı marka sapma sayılmaz", () => {
    expect(
      brandMismatch({
        requestedBrand: "Siemens",
        brandIrrelevant: true,
        brand: "ABB",
      }),
    ).toBe(false);
  });

  it("tedarikçi marka yazmadıysa sapma iddia edilmez", () => {
    expect(
      brandMismatch({
        requestedBrand: "Schneider",
        brandIrrelevant: false,
        brand: null,
      }),
    ).toBe(false);
  });
});
