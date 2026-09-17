import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { DEDUCTION_TYPE_OPTIONS } from "@/lib/hakedis/calculation";

/**
 * KESİNTİ VARSAYILAN ORANLARI — AÇIKLANAMAYAN ORAN YASAK (2026-09-17).
 *
 * "Diğer kesinti" türünün varsayılanı `%0,3` idi. Gerekçesi dört yerde
 * arandı (satır yorumu · arka uç · belgeler · doğuran commit) ve
 * hiçbirinde bulunamadı; mali müşavir de tanımadı.
 *
 * Kimsenin açıklayamadığı bir varsayılan oran KUSURDUR: kullanıcı türü
 * seçer seçmez ekrana bir oran gelir ve o oran hakedişten PARA KESER.
 *
 * Bu test iki şeyi çiviliyor:
 *   1. "Diğer" oransız kalacak.
 *   2. Oranı olan her tür, oranını ENUM BELGESİNDE de taşıyacak —
 *      yani oranın bir kaynağı olacak.
 */
describe("kesinti varsayılan oranları", () => {
  const enumKaynak = readFileSync(
    join(
      __dirname, "..", "..", "..",
      "backend", "EnderunAI.Api", "Models", "HakedisDeductionTypes.cs"
    ),
    "utf8"
  );

  it("tarama sağlığı: seçenekler ve enum okundu (boş küme kanıt değil)", () => {
    expect(DEDUCTION_TYPE_OPTIONS.length).toBeGreaterThanOrEqual(8);
    expect(enumKaynak).toContain("enum HakedisDeductionType");
  });

  it("DIGER_ORANSIZ — 'Diğer kesinti' varsayılan oran taşımaz", () => {
    const diger = DEDUCTION_TYPE_OPTIONS.find((o) => o.label === "Diğer kesinti");
    expect(diger, "'Diğer kesinti' seçeneği bulunamadı").toBeTruthy();
    expect(
      diger!.defaultRate,
      "'Diğer' tanımı gereği oransızdır; oranı kullanıcı girer. "
        + "Bir varsayılan koyacaksanız önce GEREKÇESİNİ yazın."
    ).toBe(0);
  });

  /**
   * GEREKÇELİ İSTİSNA — BULGU OLARAK DURUYOR, BÜYÜYEMİYOR.
   *
   * Barter'ın %40 varsayılanı bu testin İLK koşusunda yakalandı ve
   * kaldırılmadı: Mehmet Bey'in 2026-09-17 kararı yalnız "Diğer"i
   * kapsıyordu. Ama bulgu gizlenmiyor — burada, adıyla duruyor.
   *
   * BU, %0,3'TEN DAHA AĞIR BİR DURUM. Orada belge SESSİZDİ; burada
   * belge ÇELİŞİYOR: `HakedisDeductionType.Barter`in XML özeti
   * "Şantiye bazında DEĞİŞKEN ORANLI" diyor, ekran ise %40'ı
   * sabitliyor. Kod, kendi belgesinin aksini yapıyor.
   *
   * Karar sabah. İstisna listesi BÜYÜRSE bu testin amacı ölür; yeni
   * satır eklemeden önce oranın kaynağını yazın.
   */
  const GEREKCELI_ISTISNALAR: Record<string, string> = {
    Barter:
      "Enum belgesi 'şantiye bazında değişken oranlı' diyor ama ekran %40'ı "
      + "sabitliyor — belge ile davranış ÇELİŞİYOR. 2026-09-17'de bulgu olarak "
      + "kaydedildi; karar Mehmet Bey'de. Kaldırılmadı çünkü o günkü karar "
      + "yalnız 'Diğer'i kapsıyordu.",
  };

  it("ölü istisna bırakılmaz", () => {
    const etiketler = new Set(DEDUCTION_TYPE_OPTIONS.map((o) => o.label));
    for (const ad of Object.keys(GEREKCELI_ISTISNALAR)) {
      expect(
        [...etiketler].some((e) => e.includes(ad)),
        `İstisna artık geçersiz (böyle bir tür yok): ${ad}`
      ).toBe(true);
    }
  });

  it("ORANIN KAYNAGI VAR — sıfırdan farklı her oran enum belgesinde yazılı", () => {
    const gerekcesiz: string[] = [];

    for (const secenek of DEDUCTION_TYPE_OPTIONS) {
      if (!secenek.defaultRate) continue;
      if (Object.keys(GEREKCELI_ISTISNALAR).some((ad) => secenek.label.includes(ad))) continue;
      // Enum XML belgesi oranı "(%5)" / "(%0,5)" biçiminde taşıyor.
      const oranMetni = String(secenek.defaultRate).replace(".", ",");
      if (!enumKaynak.includes(`%${oranMetni}`)) gerekcesiz.push(
        `${secenek.label} = %${oranMetni}`
      );
    }

    expect(
      gerekcesiz,
      "Varsayılan oranı olup enum belgesinde gerekçesi YAZILI OLMAYAN tür(ler): "
        + gerekcesiz.join(", ")
        + ". Oranı ekleyen kişi kaynağını da yazmalı — açıklanamayan oran "
        + "hakedişten sessizce para keser."
    ).toEqual([]);
  });
});
