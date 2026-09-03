import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import {
  TUR_ETIKETLERI,
  SECILEBILIR_TURLER,
  WorkTaskKind,
} from "@/services/work-task.service";

/*
 * GÖREV TÜRÜ: ÖN YÜZ EŞLEMESİ ARKA UÇLA HİZALI MI.
 *
 * `gorev-durum-etiketi.test.ts` ile aynı gerekçe ve aynı yöntem:
 * eşleme, ön yüzdeki bir kopyayla değil ARKA UCUN KENDİSİYLE
 * karşılaştırılır. Durum enum'unda tam olarak bu ölçüm, "tek kaynak"
 * sanılan servis enum'unun KENDİSİNİN yanlış olduğunu ortaya çıkardı
 * (`Draft=0` ve `Waiting=3` arka uçta hiç yoktu).
 *
 * TÜRDE EK BİR İDDİA VAR: `Belirsiz` bir seçenek DEĞİL. Arka uçtaki
 * üç yazma yolu da onu reddediyor. Kullanıcıya sunulan listede
 * görünürse, seçilebilen ama her seferinde reddedilen bir seçenek
 * doğar — ekran kullanıcıya olmayan bir imkân gösterir.
 *
 * DÜRÜST SINIR: enum'u C# kaynağından METİN olarak okur. Arka uç
 * değeri hesaplayarak üretirse ayrıştırma çöker; o yüzden çökme
 * sessiz değil, açık bir hata.
 */

const KOK = join(__dirname, "..");
const ENUM_DOSYASI = join(
  KOK, "..", "..", "backend", "EnderunAI.Api", "Models", "WorkTask.cs",
);

function enumDegerleri(kaynak: string, ad: string): Map<string, number> {
  const govde = kaynak.match(
    new RegExp(`public enum ${ad}\\s*\\{([\\s\\S]*?)\\}`),
  );
  if (!govde) {
    throw new Error(`${ad} enum'u ${ENUM_DOSYASI} içinde bulunamadı.`);
  }
  const bulunan = new Map<string, number>();
  for (const satir of govde[1].split("\n")) {
    const temiz = satir.replace(/\/\/.*$/, "").trim();
    const m = temiz.match(/^([A-Za-z][A-Za-z0-9_]*)\s*=\s*(\d+)\s*,?$/);
    if (m) bulunan.set(m[1], Number(m[2]));
  }
  return bulunan;
}

const kaynak = readFileSync(ENUM_DOSYASI, "utf8");
const arkaUcTur = enumDegerleri(kaynak, "WorkTaskKind");

describe("görev türü — tek kaynak", () => {
  it("arka uç enum'u okunabildi (pozitif kontrol)", () => {
    /*
     * BOŞ SONUÇ YOKLUĞUN KANITI DEĞİLDİR. Ayrıştırma çökseydi
     * aşağıdaki bütün testler boş küme üzerinde dolaşır ve SESSİZCE
     * yeşil verirdi.
     */
    expect(arkaUcTur.size).toBe(3);
    expect(arkaUcTur.get("Belirsiz")).toBe(0);
    expect(arkaUcTur.get("IsEmri")).toBe(1);
    expect(arkaUcTur.get("Hatirlatma")).toBe(2);
  });

  it("arka uçtaki her tür değerinin tam olarak bir etiketi var", () => {
    const etiketsiz: string[] = [];

    for (const [ad, deger] of arkaUcTur) {
      const etiket = TUR_ETIKETLERI[deger as WorkTaskKind];
      if (typeof etiket !== "string" || etiket.length === 0) {
        etiketsiz.push(`${ad}=${deger}`);
      }
    }

    expect(etiketsiz).toEqual([]);
  });

  it("ön yüzde arka uçta olmayan bir tür yok", () => {
    /*
     * HAYALET DEĞER, EKSİK DEĞER KADAR ZARARLI: durum enum'unda
     * `Waiting = 3` yıllarca hiçbir zaman eşleşmeyen ÖLÜ BİR DAL
     * besledi ("Başlat" düğmesi koşulu).
     */
    const arkaUcDegerleri = new Set(arkaUcTur.values());
    const hayalet = Object.keys(TUR_ETIKETLERI)
      .map(Number)
      .filter((deger) => !arkaUcDegerleri.has(deger));

    expect(hayalet).toEqual([]);
  });

  it("seçilebilir türler `Belirsiz` içermez", () => {
    /*
     * ASIL İDDİA. Sunucu `Belirsiz`i reddediyor; listede görünseydi
     * kullanıcı seçer ve 400 alırdı.
     */
    expect(SECILEBILIR_TURLER).not.toContain(WorkTaskKind.Belirsiz);
  });

  it("seçilebilir türler, `Belirsiz` dışındaki HER türü içerir", () => {
    /*
     * ÖTEKİ YARISI — VE ASIL SESSİZ KUSUR BURADA DOĞARDI.
     *
     * Yukarıdaki test tek başına, `SECILEBILIR_TURLER` BOŞ olsa bile
     * yeşil kalırdı: boş liste `Belirsiz` içermez. O zaman form hiçbir
     * tür sunmaz ve hiçbir görev açılamazdı.
     *
     * Arka uca yeni bir tür eklenip buraya eklenmediğinde de kırmızı
     * verir — yeni tür ekranda görünmeden canlıya çıkamaz.
     */
    const beklenen = [...arkaUcTur.values()].filter((d) => d !== 0).sort();
    expect([...SECILEBILIR_TURLER].sort()).toEqual(beklenen);
  });
});
