import { describe, expect, it } from "vitest";

import {
  EMPTY_VALUE,
  amount,
  coefficient,
  decimal,
  decimalRange,
  money,
  moneyWhole,
  percent,
  quantity,
  unitPrice,
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

describe("alt-üst sınırlı ondalık", () => {
  /**
   * Alt sınır basılı belge için: "2" ile "2,00" alt alta gelince
   * miktar sütunu kayıyor.
   */
  it("alt sınıra kadar sıfır yazar", () => {
    expect(decimalRange(2, 2, 4)).toBe("2,00");
    expect(decimalRange(1250.5, 2, 4)).toBe("1.250,50");
  });

  it("alt sınırın üstünde gereksiz sıfır yazmaz", () => {
    expect(decimalRange(0.3125, 2, 4)).toBe("0,3125");
    expect(decimalRange(0.5, 2, 4)).toBe("0,50");
  });
});

describe("birim fiyat", () => {
  /**
   * BİRİM FİYAT TUTAR DEĞİLDİR. Üretici fiyatı veritabanında
   * `numeric(18,6)`; iki haneye yuvarlansaydı kullanıcı o rakamla
   * miktarı çarptığında toplam tutmazdı.
   */
  it("kuruş altı haneleri korur", () => {
    expect(unitPrice(12.4567)).toBe("12,4567 ₺");
  });

  it("en az iki hane yazar", () => {
    expect(unitPrice(8.5)).toBe("8,50 ₺");
    expect(unitPrice(8)).toBe("8,00 ₺");
  });

  it("para birimi kodunu yazar", () => {
    expect(unitPrice(12.4567, "USD")).toBe("12,4567 USD");
  });
});

/**
 * GUARDRAIL: tutar biçimi SABİT iki hanedir.
 *
 * `quantity` ve `decimal` sondaki sıfırları atıyor; o davranış tutara
 * sızarsa 1.250,00 ₺ ekranda "1.250 ₺" görünür ve kuruşu olan bir
 * bakiye kuruşsuzmuş gibi okunur.
 */
describe("tutarda sıfır kırpılmaz", () => {
  it("tam sayı tutar da iki hane yazar", () => {
    expect(amount(1250)).toBe("1.250,00");
    expect(money(1250)).toBe("1.250,00 ₺");
  });

  it("tek ondalıklı tutar ikinci haneyi yazar", () => {
    expect(money(1250.5)).toBe("1.250,50 ₺");
  });

  /** Kuruşsuz biçim yalnızca başlık rakamı için ve AYRI bir işlev. */
  it("kuruşsuz biçim ayrı işlevdedir", () => {
    expect(moneyWhole(1250.5)).toBe("1.251 ₺");
    expect(moneyWhole(1250.5)).not.toBe(money(1250.5));
  });
});

/**
 * GUARDRAIL: endeks katsayısı sekiz haneye kadar, YALNIZCA gösterim.
 *
 * Hane sayısı çağrı yerinde değil burada duruyor; ekranlar
 * `coefficient` çağırıyor.
 */
describe("endeks katsayısı", () => {
  it("sekiz haneye kadar iner", () => {
    expect(coefficient(0.00012345)).toBe("0,00012345");
  });

  it("kısa katsayıyı sıfırla şişirmez", () => {
    expect(coefficient(1.5)).toBe("1,5");
  });

  it("serbest ondalıkla aynı kuralı uygular", () => {
    expect(coefficient(0.00012345)).toBe(decimal(0.00012345, 8));
  });

  /** Boş değer sıfır sayılmaz: "veri yok" ile "sıfır" farklı şeyler. */
  it("boş değeri sıfır saymaz", () => {
    expect(coefficient(null)).toBe(EMPTY_VALUE);
    expect(unitPrice(null)).toBe(EMPTY_VALUE);
    expect(decimalRange(undefined, 2, 4)).toBe(EMPTY_VALUE);
  });
});
