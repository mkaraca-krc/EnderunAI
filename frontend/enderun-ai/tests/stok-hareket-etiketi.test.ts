import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

import {
  STOK_HAREKET_ETIKETLERI,
  STOK_HAREKET_TIPI,
  stokArtiranHareket,
  stokAzaltanHareket,
  stokHareketEtiketi,
} from "@/lib/inventory/hareket-tipi";

/*
 * STOK HAREKET ETİKETİ: ARKA UCUN ENUM'UYLA KARŞILAŞTIRILIR.
 *
 * ═══ ÖLÇÜLEN KUSUR (2026-09-13) ═══
 *
 * Arka uç: `TransferIn = 2`, `TransferOut = 3`; transferde KAYNAK
 * depoya `TransferOut` yazılıyor. Ön yüzde ÜÇ ayrı dosya bu eşlemeyi
 * elle yazmıştı ve ÜÇÜ DE tersti — transfer girişleri ekranda çıkış
 * görünüyordu. Dahası kusur yalnız etikette değildi:
 *
 *   · renk: `type === 2` sarı (çıkış rengi) boyanıyordu
 *   · SAYI: `/depo-stok` "bugünkü çıkış" toplamı `type === 2`yi de
 *     ekliyordu — transfer GİRİŞLERİ günlük çıkışa yazılıyordu
 *
 * Etiket yanlışsa kullanıcı görür; SAYI yanlışsa görmez.
 *
 * ═══ BU TEST NEYİ ÖLÇER ═══
 *
 * Ön yüzdeki başka bir kopyayla değil, ARKA UCUN KENDİ KAYNAĞIYLA
 * karşılaştırır — kopya da yanlış olabilir, nitekim üçü de yanlıştı.
 *
 * DÜRÜST SINIR: enum'u C# kaynağından METİN olarak okur. Arka uç
 * değeri hesaplanarak üretirse (ör. `Receipt = 1 << 0`) bu okuma
 * çöker; o yüzden ayrıştırma başarısız olursa test SUSMAZ, DÜŞER.
 */

const KOK = join(__dirname, "..");
const ENUM_DOSYASI = join(
  KOK, "..", "..", "backend", "EnderunAI.Api", "Models", "StockMovement.cs",
);

function enumDegerleri(ad: string): Map<string, number> {
  const kaynak = readFileSync(ENUM_DOSYASI, "utf8");
  const govde = kaynak.match(new RegExp(`public enum ${ad}\\s*\\{([\\s\\S]*?)\\}`));
  if (!govde) throw new Error(`${ad} enum'u ${ENUM_DOSYASI} içinde bulunamadı.`);

  const bulunan = new Map<string, number>();
  for (const satir of govde[1].split("\n")) {
    const temiz = satir.replace(/\/\/.*$/, "").trim();
    const m = temiz.match(/^([A-Za-z][A-Za-z0-9_]*)\s*=\s*(\d+)\s*,?$/);
    if (m) bulunan.set(m[1], Number(m[2]));
  }
  return bulunan;
}

describe("stok hareket tipi — tek etiket kaynağı", () => {
  const arkaUc = enumDegerleri("StockMovementType");

  it("ÖLÇÜM SAĞLIĞI: enum arka uçtan gerçekten okundu", () => {
    /*
     * Kural 48. Ayrıştırma sessizce boş dönerse aşağıdaki her
     * karşılaştırma BOŞ KÜME üzerinde koşar ve yeşil yanar — ölçüm
     * ölmüş, dünya değişmemiş olur.
     */
    expect(
      arkaUc.size,
      `StockMovementType ${ENUM_DOSYASI} içinden okunamadı (${arkaUc.size} üye). ` +
        "Aşağıdaki iddiaların hiçbiri bu hâlde bir şey söylemez.",
    ).toBeGreaterThanOrEqual(7);
    // Pozitif kontrol: bilinen bir üye bilinen değerinde mi
    expect(arkaUc.get("Receipt")).toBe(0);
  });

  it("ön yüz sabitleri arka uçtaki SAYILARLA birebir", () => {
    const onYuz = new Map<string, number>(Object.entries(STOK_HAREKET_TIPI));
    expect(
      Object.fromEntries([...onYuz].sort()),
      "Ön yüz sabitleri arka uçtaki enum'dan ayrışmış.",
    ).toEqual(Object.fromEntries([...arkaUc].sort()));
  });

  it("her enum üyesinin etiketi var (bilinmeyen tip gizlenmez)", () => {
    const etiketsiz = [...arkaUc].filter(([, deger]) => !(deger in STOK_HAREKET_ETIKETLERI));
    expect(
      etiketsiz.map(([ad, deger]) => `${ad}=${deger}`),
      "Etiketi olmayan hareket tipi: ekranda 'Hareket N' görünür.",
    ).toEqual([]);
    // Tanımsız tip sessizce boş DÖNMEZ
    expect(stokHareketEtiketi(99)).toBe("Hareket 99");
  });

  it("KUSURUN KENDİSİ: yön etiketleri enum'la aynı yöne bakıyor", () => {
    /*
     * KIRMIZIYA DÖNERSE: transfer girişi ekranda çıkış (ya da tersi)
     * görünür. 2026-09-13'te üç dosyada birden böyleydi.
     */
    expect(
      stokHareketEtiketi(arkaUc.get("TransferIn")!).toLocaleLowerCase("tr-TR"),
      "TransferIn (depoya GİRİŞ) etiketi 'giriş' demiyor.",
    ).toContain("giriş");
    expect(
      stokHareketEtiketi(arkaUc.get("TransferOut")!).toLocaleLowerCase("tr-TR"),
      "TransferOut (depodan ÇIKIŞ) etiketi 'çıkış' demiyor.",
    ).toContain("çıkış");
    expect(stokHareketEtiketi(arkaUc.get("Receipt")!)).toBe("Giriş");
    expect(stokHareketEtiketi(arkaUc.get("Issue")!)).toBe("Çıkış");
  });

  it("KUSURUN SAYI HÂLİ: azaltan/artıran ayrımı doğru yönde", () => {
    /*
     * KIRMIZIYA DÖNERSE: "bugünkü çıkış" toplamı transfer girişlerini
     * de sayar ve kullanıcı bunu ekranda GÖREMEZ.
     */
    expect(stokAzaltanHareket(arkaUc.get("Issue")!)).toBe(true);
    expect(stokAzaltanHareket(arkaUc.get("TransferOut")!)).toBe(true);
    expect(stokAzaltanHareket(arkaUc.get("TransferIn")!)).toBe(false);
    expect(stokAzaltanHareket(arkaUc.get("Receipt")!)).toBe(false);

    expect(stokArtiranHareket(arkaUc.get("Receipt")!)).toBe(true);
    expect(stokArtiranHareket(arkaUc.get("TransferIn")!)).toBe(true);
    expect(stokArtiranHareket(arkaUc.get("TransferOut")!)).toBe(false);
  });
});

/*
 * ═══ YAPISAL MUHAFAZA: DÖRDÜNCÜ KOPYA AÇILMASIN ═══
 *
 * Üç kopyayı düzeltmek kusuru BUGÜN kapatır. Asıl sebep aynı bilginin
 * birden çok yerde durmasıydı; bu muhafaza olmadan yarın dördüncüsü
 * açılır ve aynı gün tekrar gelir.
 */
describe("yapısal muhafaza — hareket etiketi ikinci kez yazılmasın", () => {
  const DIZINLER = ["app", "components", "services", "hooks"];
  const TEK_KAYNAK = join("lib", "inventory", "hareket-tipi.ts");

  function kaynaklar(dizin: string): string[] {
    const bulunan: string[] = [];
    let girdiler: string[];
    try {
      girdiler = readdirSync(dizin);
    } catch {
      return bulunan;
    }
    for (const girdi of girdiler) {
      if (girdi === "node_modules" || girdi === ".next") continue;
      const yol = join(dizin, girdi);
      if (statSync(yol).isDirectory()) {
        bulunan.push(...kaynaklar(yol));
        continue;
      }
      if (/\.tsx?$/.test(girdi)) bulunan.push(yol);
    }
    return bulunan;
  }

  it("başka hiçbir dosya kendi transfer etiketini yazmıyor", () => {
    const dosyalar = DIZINLER.flatMap((d) => kaynaklar(join(KOK, d)));

    // ÖLÇÜM SAĞLIĞI (Kural 48): dosya listesi boşsa "ihlal yok"
    // sonucu taramanın ölü olduğunun da kanıtı olabilir.
    expect(
      dosyalar.length,
      "Tarama hiç dosya görmedi — 'ihlal yok' sonucu anlamsız.",
    ).toBeGreaterThan(200);

    const DESEN = /["'`]\s*Transfer\s+(giriş|çıkış|Giriş|Çıkış|Girişi|Çıkışı)/i;
    const ihlal = dosyalar
      .filter((yol) => relative(KOK, yol) !== TEK_KAYNAK)
      .filter((yol) => DESEN.test(readFileSync(yol, "utf8")))
      .map((yol) => relative(KOK, yol));

    expect(
      ihlal,
      "Bu dosya(lar) kendi transfer etiketini yazıyor. Etiket TEK yerden " +
        `gelmeli: ${TEK_KAYNAK} — 'stokHareketEtiketi' kullanın.`,
    ).toEqual([]);
  });
});
