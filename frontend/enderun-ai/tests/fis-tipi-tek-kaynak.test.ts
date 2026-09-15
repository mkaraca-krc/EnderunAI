import { describe, expect, it } from "vitest";
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import {
  FIS_TIPI_ETIKETLERI,
  FIS_TIPI_SECENEKLERI,
  fisTipiEtiketi,
} from "@/lib/muhasebe/fis-tipi";

/**
 * MUHASEBE FİŞ TİPİ — ÜÇ İDDİALI SONDA (2026-09-16).
 *
 * Aynı eşleme ALTI yerde kopyalanmıştı, üç ayrı biçimde. İkisi YAZMA
 * yolundaydı (fiş oluşturma/düzenleme ekranındaki elle `<option>`
 * listesi) — yanlış etiketli bir fiş tipi deftere yazılır ve mali
 * müşavire öyle gider.
 */

// ═══ SONDA 1 — YÖN: hangi sayı hangi etiket ═══
describe("1) yön — enum değeri doğru etikete gidiyor", () => {
  it("arka uçtaki sıra birebir korunuyor", () => {
    expect(FIS_TIPI_ETIKETLERI[0]).toBe("Mahsup");
    expect(FIS_TIPI_ETIKETLERI[1]).toBe("Tahsil");
    expect(FIS_TIPI_ETIKETLERI[2]).toBe("Tediye");
    expect(FIS_TIPI_ETIKETLERI[3]).toBe("Açılış");
    expect(FIS_TIPI_ETIKETLERI[4]).toBe("Kapanış");
  });

  it("YON_KAYMASI_YAKALANIR — komşu etiketler birbirine geçmemiş", () => {
    // Tahsil ile Tediye tek harf farkla karışır; kayma tam burada olur.
    expect(FIS_TIPI_ETIKETLERI[1]).not.toBe("Tediye");
    expect(FIS_TIPI_ETIKETLERI[2]).not.toBe("Tahsil");
  });

  it("seçenek listesi de aynı yönde ve sıralı", () => {
    expect(FIS_TIPI_SECENEKLERI.map((s) => s.deger)).toEqual([0, 1, 2, 3, 4]);
    expect(FIS_TIPI_SECENEKLERI.map((s) => s.etiket)).toEqual([
      "Mahsup", "Tahsil", "Tediye", "Açılış", "Kapanış",
    ]);
  });

  it("bilinmeyen değer SESSİZ geçmez", () => {
    expect(fisTipiEtiketi(9)).toContain("bilinmeyen");
    expect(fisTipiEtiketi(9)).toContain("9");
  });
});

// ═══ SONDA 2 — HER ENUM ÜYESİNİN ETİKETİ VAR (arka uç kaynağından) ═══
describe("2) bütünlük — arka uçtaki her enum üyesinin etiketi var", () => {
  const kaynak = readFileSync(
    join(
      __dirname, "..", "..", "..",
      "backend", "EnderunAI.Api", "Models", "Accounting", "AccountingVoucher.cs"
    ),
    "utf8"
  );

  const govde = /enum\s+AccountingVoucherType\s*\{([^}]*)\}/.exec(kaynak)?.[1] ?? "";
  const uyeler = [...govde.matchAll(/(\w+)\s*=\s*(\d+)/g)].map((m) => ({
    ad: m[1],
    deger: Number(m[2]),
  }));

  it("tarama sağlığı: arka uç enum'u OKUNDU (boş küme kanıt değil)", () => {
    // Kural 48: dosya bulunamazsa aşağıdaki test boş kümede yeşil yanardı.
    expect(govde.length).toBeGreaterThan(10);
    expect(uyeler.length).toBeGreaterThanOrEqual(5);
  });

  it("HER_UYENIN_ETIKETI_VAR — eksik kalan yok", () => {
    const eksik = uyeler.filter(
      (u) => !(u.deger in FIS_TIPI_ETIKETLERI)
    );
    expect(
      eksik,
      `Arka uçta olup etiketi olmayan fiş tipi: ${eksik.map((e) => `${e.ad}=${e.deger}`).join(", ")}`
    ).toEqual([]);
  });

  it("fazlalık da yok — etiketi olup arka uçta olmayan değer", () => {
    const arkaDegerler = new Set(uyeler.map((u) => u.deger));
    const fazla = Object.keys(FIS_TIPI_ETIKETLERI)
      .map(Number)
      .filter((d) => !arkaDegerler.has(d));
    expect(fazla, `Arka uçta olmayan etiket: ${fazla.join(", ")}`).toEqual([]);
  });
});

// ═══ SONDA 3 — YAPISAL MUHAFAZA: kopya geri gelmesin ═══
describe("3) yapısal — eşleme tek kaynakta, kopya geri gelmemiş", () => {
  const muhasebeKok = join(__dirname, "..", "app", "muhasebe");

  function dosyalar(kok: string): string[] {
    const bulunan: string[] = [];
    for (const g of readdirSync(kok, { withFileTypes: true })) {
      const yol = join(kok, g.name);
      if (g.isDirectory()) bulunan.push(...dosyalar(yol));
      else if (g.name.endsWith(".tsx") || g.name.endsWith(".ts")) bulunan.push(yol);
    }
    return bulunan;
  }

  const hepsi = dosyalar(muhasebeKok);

  it("tarama sağlığı: muhasebe ekranları bulundu", () => {
    expect(hepsi.length).toBeGreaterThan(5);
  });

  it("KOPYA_ESLEME_YOK — 'Mahsup' yalnız tek kaynakta yazılı", () => {
    const kopyalar = hepsi.filter((yol) =>
      /["'>]Mahsup["'<]/.test(readFileSync(yol, "utf8"))
    );
    expect(
      kopyalar.map((y) => y.slice(muhasebeKok.length + 1)),
      "Fiş tipi etiketi elle yazılmış. lib/muhasebe/fis-tipi.ts kullanın."
    ).toEqual([]);
  });

  /*
   * SONDA DARALTILDI (2026-09-16) — KENDİ YANLIŞ KIRMIZIM.
   *
   * İlk yazımda desen `<option value={0..4}>` idi ve HER enum'un seçim
   * listesini yakalıyordu: `hesap-plani` ekranlarındaki Borç/Alacak
   * listesi de kırmızı yandı. O liste BAŞKA bir enum'dur ve bu testin
   * konusu değildir. Bir sonda, ölçtüğü ayrımı korumak zorundadır
   * (Kural 84): "bir enum listesi" ile "FİŞ TİPİ listesi" aynı şey değil.
   *
   * Desen artık ETİKETE bakıyor — fiş tipi etiketleri elle yazılmışsa
   * yanar, başka bir enum'un listesi yanmaz.
   */
  it("elle yazılmış FİŞ TİPİ <option> listesi geri gelmemiş", () => {
    const suclu = hepsi.filter((yol) =>
      /<option[^>]*>(Mahsup|Tahsil|Tediye)</.test(readFileSync(yol, "utf8"))
    );
    expect(suclu.map((y) => y.slice(muhasebeKok.length + 1))).toEqual([]);
  });

  it("SONDA_ISIRIYOR — elle yazılmış fiş tipi listesi yakalanır", () => {
    // Kural 93: yeşil, sondanın ısırabildiği gösterilmeden rapora girmez.
    const ornek = '<option value={1}>Tahsil</option>';
    expect(/<option[^>]*>(Mahsup|Tahsil|Tediye)</.test(ornek)).toBe(true);
    // Ve başka bir enum'un listesini YAKALAMAZ.
    const baska = '<option value={1}>Alacak</option>';
    expect(/<option[^>]*>(Mahsup|Tahsil|Tediye)</.test(baska)).toBe(false);
  });
});
