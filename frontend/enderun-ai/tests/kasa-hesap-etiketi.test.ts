import { describe, expect, it } from "vitest";

import { kasaHesapEtiketi } from "@/lib/finans/kasa-hesap-etiketi";
import { CashAccountType } from "@/services/cash-account.service";

/**
 * KASA/BANKA SEÇİM ETİKETİ.
 *
 * NEDEN VAR: canlıda 805088 numaralı çek Garanti yaprağı olduğu hâlde
 * Fibabanka hesabından ödenmiş göründü. Sebep kullanıcı hatası değil
 * EKRAN: altı banka hesabının `name` alanı birebir aynı
 * ("Ankara Merkez TL Hesabı"), açılır listede ayırt edilemiyorlardı.
 *
 * İki iptal edilmiş çekte de Garanti yaprağı KASADAN ödenmiş
 * işaretlenmiş; karışıklık kasa-banka arasında da yaşandı. Etiket bu
 * yüzden türü de taşıyor.
 */
describe("kasa/banka hesabı etiketi", () => {
  const bankaHesabi = {
    type: CashAccountType.Bank,
    code: "BANKA-004",
    name: "Ankara Merkez TL Hesabı",
    bankName: "Garanti Bankası",
  };

  /**
   * BANKA HESABINDA AYIRT EDİCİ BİLGİ BANKA ADIDIR.
   *
   * `name` altı hesapta aynı olduğu için etikete girmez; girseydi
   * hata aynen sürerdi.
   */
  it("banka hesabında banka adını ve kodu gösterir", () => {
    expect(kasaHesapEtiketi(bankaHesabi)).toBe(
      "Banka · Garanti Bankası — BANKA-004"
    );
  });

  /**
   * AYNI ADLI İKİ BANKA HESABI AYIRT EDİLEBİLİR OLMALI.
   *
   * Bu testin kırmızıya dönmesi, 805088 hatasının ekranda geri
   * geldiği anlamına gelir.
   */
  it("aynı adlı iki banka hesabı farklı etiket üretir", () => {
    const fiba = { ...bankaHesabi, code: "BANKA-001", bankName: "Fibabanka" };

    expect(kasaHesapEtiketi(bankaHesabi)).not.toBe(kasaHesapEtiketi(fiba));
  });

  /**
   * KASA BANKADAN AYRILIR.
   *
   * VCK-2026-000020 ve 000022'de Garanti çeki KASADAN ödenmiş
   * işaretlenmişti; tür etiketin başında duruyor.
   */
  it("kasada tür Kasa ve hesap adı gösterilir", () => {
    expect(
      kasaHesapEtiketi({
        type: CashAccountType.Cash,
        code: "MERKEZ-TL",
        name: "Ankara Merkez TL Kasası",
        bankName: null,
      })
    ).toBe("Kasa · Ankara Merkez TL Kasası — MERKEZ-TL");
  });

  /**
   * BANKA ADI BOŞSA HESAP ADINA DÜŞER — BOŞ ETİKET ÜRETMEZ.
   *
   * Görünür hata sessiz hatadan iyidir: banka adı girilmemiş bir
   * hesapta etiket "Banka · — BANKA-007" gibi yarım kalmaz.
   */
  it("banka adı boşsa hesap adına düşer", () => {
    expect(
      kasaHesapEtiketi({
        type: CashAccountType.Bank,
        code: "BANKA-007",
        name: "Yeni Hesap",
        bankName: "   ",
      })
    ).toBe("Banka · Yeni Hesap — BANKA-007");
  });
});
