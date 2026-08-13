import { describe, expect, it } from "vitest";

import {
  EMPTY_VALUE,
  amount,
  money,
  percent,
  quantity,
  whole,
} from "@/lib/format/turkish";

/**
 * TÜRKÇE SAYI BİÇİMİ — arayüzün tek kaynağı.
 *
 * NEDEN VAR: binlik ile ondalık ayıracının yer değiştirmesi tutarı bin
 * kat yanlış okutur ("60,000.00" → altmış). Backend'de aynı kural
 * TurkishFormat ile korunuyor; bu testler arayüz tarafının ondan
 * ayrışmadığını sabitler.
 */
describe("tutar biçimi", () => {
  it("binlik nokta, ondalık virgül", () => {
    expect(amount(60000)).toBe("60.000,00");
    expect(amount(1234.5)).toBe("1.234,50");
  });

  it("negatif tutar işaretini korur", () => {
    expect(amount(-1500)).toBe("-1.500,00");
  });

  it("kuruş yuvarlaması iki haneye iner", () => {
    expect(amount(10.005)).toBe("10,01");
  });

  /**
   * VERİ YOK ile SIFIR farklıdır: null'ı sıfır göstermek, olmayan bir
   * bakiyeyi sıfır bakiye gibi göstermek olurdu.
   */
  it("boş değer sıfır değil, çizgi", () => {
    expect(amount(null)).toBe(EMPTY_VALUE);
    expect(amount(undefined)).toBe(EMPTY_VALUE);
    expect(amount(0)).toBe("0,00");
  });

  it("sayı olmayan değer çizgi", () => {
    expect(amount(Number.NaN)).toBe(EMPTY_VALUE);
  });
});

describe("para birimi", () => {
  /** Simge SONDA: sağa hizalı sütunda rakamlar hizada kalır. */
  it("simge tutarın sonunda", () => {
    expect(money(2500)).toBe("2.500,00 ₺");
  });

  it("farklı para birimi verilebilir", () => {
    expect(money(2500, "$")).toBe("2.500,00 $");
  });
});

describe("yüzde", () => {
  /** İşaret BAŞTA: Türkçe yazım kuralı. */
  it("işaret başta ve tek ondalık", () => {
    expect(percent(5.5)).toBe("%5,5");
  });

  it("ondalık sayısı verilebilir", () => {
    expect(percent(5.55, 2)).toBe("%5,55");
  });
});

describe("miktar", () => {
  /**
   * Sondaki sıfırlar yazılmaz: backend dört hane tutuyor ama stokta
   * "3,0000" görmek gürültüdür.
   */
  it("gereksiz sıfır göstermez", () => {
    expect(quantity(3)).toBe("3");
    expect(quantity(1250.75)).toBe("1.250,75");
    expect(quantity(0.5)).toBe("0,5");
  });
});

describe("tam sayı", () => {
  it("ondalık göstermez", () => {
    expect(whole(320)).toBe("320");
    expect(whole(1500)).toBe("1.500");
  });
});
