import { describe, expect, it } from "vitest";

import { guessMapping } from "@/components/engineering/boq-import-mapping";
import { BoqSectionRule } from "@/services/project-boq.service";

/**
 * İcmal aktarımında sütun tahmini.
 *
 * NEDEN GERÇEK BAŞLIKLARLA: tahmin yanlış olduğunda hata sessiz
 * değil ama masraflı — kullanıcı 350 satırlık dosyayı yükleyip
 * "birim fiyat okunamadı" hatalarını görüyor ve nedenini bulması
 * gerekiyor. Başlıklar aşağıda NATURA icmalinden birebir alındı
 * (Elektrik sayfası, 2. satır); uydurma değil.
 */

/** NATURA icmali, "Elektrik" sayfası, başlık satırı. Birebir kopya. */
const NATURA_HEADERS = [
  "Poz",
  "AÇIKLAMA",
  "Ana Malzeme Kapsamı",
  "İşçilik Kapsamı",
  "Birim",
  "Keşif Miktarı",
  "Malzeme Birim Fiyatı",
  "İşçilik \nBirim Fiyatı",
  "G.G KAR BİRİM FİYATI",
  "Toplam      Birim Fiyat",
  "Tutar",
  "Pursantaj\n(%)",
  "1 Dönem İmalat Miktarı (Hakediş)",
  "(Hakediş)",
  "2 Dönem İmalat Miktarı (Hakediş)",
  "Kümülatif İmalat Miktarı (Hakediş)",
];

function inspection(headers: string[]) {
  return {
    sheetNames: ["İcmal", "Elektrik"],
    sheetName: "Elektrik",
    headerRowIndex: 2,
    headers,
    sampleRows: [],
    totalRowCount: 389,
  };
}

describe("icmal sütun tahmini", () => {
  it("NATURA başlıklarında dokuz sütunu da doğru bulur", () => {
    const mapping = guessMapping(inspection(NATURA_HEADERS));

    expect(mapping.codeColumn).toBe(1);
    expect(mapping.descriptionColumn).toBe(2);
    expect(mapping.unitColumn).toBe(5);
    expect(mapping.quantityColumn).toBe(6);
    expect(mapping.materialColumn).toBe(7);
    expect(mapping.laborColumn).toBe(8);
    expect(mapping.overheadColumn).toBe(9);
    expect(mapping.totalColumn).toBe(11);
  });

  /**
   * Asıl tuzak: "Ana Malzeme Kapsamı" ve "İşçilik Kapsamı" METİN
   * sütunları ("Yüklenici" yazar) fiyat sütunlarından ÖNCE geliyor ve
   * ikisi de aranan kelimeyi içeriyor. Eleme olmadan tahmin metni
   * seçiyor, aktarımda her satır düşüyordu.
   */
  it("kapsam sütunlarını fiyat sütunu sanmaz", () => {
    const mapping = guessMapping(inspection(NATURA_HEADERS));

    expect(NATURA_HEADERS[mapping.materialColumn - 1]).toBe(
      "Malzeme Birim Fiyatı"
    );
    expect(NATURA_HEADERS[mapping.laborColumn - 1]).toBe(
      "İşçilik \nBirim Fiyatı"
    );
  });

  it("birim sütununu 'Malzeme Birim Fiyatı' ile karıştırmaz", () => {
    const mapping = guessMapping(inspection(NATURA_HEADERS));
    expect(NATURA_HEADERS[mapping.unitColumn - 1]).toBe("Birim");
  });

  it("ayrı kısım sütunu yoksa null döner ve kural EmptyUnit kalır", () => {
    const mapping = guessMapping(inspection(NATURA_HEADERS));

    // NATURA'da ayrı "Kısım" sütunu yok; kısımlar birimi boş
    // satırlardan tanınıyor. Ölçüldü: bu kuralla 12 kısım + 350 kalem.
    expect(mapping.sectionColumn).toBeNull();
    expect(mapping.sectionRule).toBe(BoqSectionRule.EmptyUnit);
  });

  it("ENDERUN şablonunda kendi sütunlarını bulur", () => {
    const mapping = guessMapping(
      inspection([
        "Kısım",
        "Poz No",
        "Tanım",
        "Birim",
        "Sözleşme Miktarı",
        "Malzeme B.F.",
        "Montaj B.F.",
        "GG&K B.F.",
      ])
    );

    expect(mapping.sectionColumn).toBe(1);
    expect(mapping.codeColumn).toBe(2);
    expect(mapping.descriptionColumn).toBe(3);
    expect(mapping.unitColumn).toBe(4);
    expect(mapping.quantityColumn).toBe(5);
    expect(mapping.materialColumn).toBe(6);
    expect(mapping.laborColumn).toBe(7);
    expect(mapping.overheadColumn).toBe(8);
  });
});
