import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

import {
  MALZEME_TIPI,
  MALZEME_TIPI_ETIKETLERI,
  MALZEME_TIPI_SECENEKLERI,
  malzemeTipiEtiketi,
} from "@/lib/inventory/malzeme-tipi";

/*
 * MALZEME TİPİ ETİKETİ: ARKA UCUN ENUM'UYLA KARŞILAŞTIRILIR.
 *
 * ═══ ÖLÇÜLEN KUSUR (2026-09-13) ═══
 *
 * Arka uç: `Material=0, Equipment=1, Consumable=2, SparePart=3`.
 * Ön yüzde BEŞ kopya vardı, dördü yanlıştı: düzenleme formu, liste
 * etiketi ve detay etiketi 1 ile 2'yi TERS yazıyordu; `SparePart=3`
 * hiçbirinde YOKTU; `services/inventory.service.ts`teki tip de
 * `0 | 1 | 2` diyerek 3'ü reddediyordu.
 *
 * ═══ BU KUSUR VERİ YAZDIRDI ═══
 *
 * 5 Ağustos 2026'da KART AÇMA formu da ters eşlemeyi kullanıyordu.
 * Canlıdaki `END0003 İZOLE BANT` o gün açıldı, `Type = 1` yazıldı ve
 * hiç düzenlenmedi (`UpdatedByUserId` boş). Kullanıcı "Sarf malzemesi"
 * seçti, veriye `Equipment` yazıldı — kaydın tipi kullanıcının değil
 * EKRANIN hatası.
 *
 * DÜRÜST SINIR: enum'u C# kaynağından METİN olarak okur. Arka uç değeri
 * hesaplayarak üretirse okuma çöker; o yüzden ayrıştırma başarısız
 * olursa test SUSMAZ, DÜŞER.
 */

const KOK = join(__dirname, "..");
const ENUM_DOSYASI = join(
  KOK, "..", "..", "backend", "EnderunAI.Api", "Models", "InventoryItem.cs",
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

describe("malzeme tipi — tek etiket kaynağı", () => {
  const arkaUc = enumDegerleri("InventoryItemType");

  it("ÖLÇÜM SAĞLIĞI: enum arka uçtan gerçekten okundu", () => {
    /*
     * Kural 48. Ayrıştırma sessizce boş dönerse aşağıdaki her iddia
     * BOŞ KÜME üzerinde koşar ve yeşil yanar.
     */
    expect(
      arkaUc.size,
      `InventoryItemType okunamadı (${arkaUc.size} üye).`,
    ).toBeGreaterThanOrEqual(4);
    expect(arkaUc.get("Material")).toBe(0); // pozitif kontrol
  });

  it("(a) HER ETİKET ARKA UCUN DEĞERİYLE AYNI YÖNE BAKIYOR", () => {
    /*
     * KIRMIZIYA DÖNERSE: ekran "Sarf" derken veri `Equipment` olur —
     * tam olarak END0003'ü doğuran kusur.
     */
    const beklenen: Record<string, string> = {
      Material: "malzeme",
      Equipment: "ekipman",
      Consumable: "sarf",
      SparePart: "yedek",
    };

    for (const [ad, deger] of arkaUc) {
      const etiket = malzemeTipiEtiketi(deger).toLocaleLowerCase("tr-TR");
      expect(
        etiket,
        `${ad}=${deger} etiketi "${etiket}" — "${beklenen[ad]}" beklenirdi.`,
      ).toContain(beklenen[ad]);
    }

    // Ön yüz sabitleri de arka uçtaki SAYILARLA birebir
    expect(
      { ...MALZEME_TIPI },
      "Ön yüz sabitleri arka uçtaki enum'dan ayrışmış.",
    ).toEqual(Object.fromEntries(arkaUc));
  });

  it("(b) KAPSAM: enum'un HER üyesinin bir etiketi var", () => {
    /*
     * BU İDDİA OLSAYDI `SparePart = 3`İN EKSİKLİĞİ ZATEN YAKALANIRDI.
     * Beş kopyanın hiçbirinde 3 yoktu ve kimse fark etmedi: eksik bir
     * üye hiçbir yerde HATA vermez, sadece görünmez olur.
     */
    const etiketsiz = [...arkaUc]
      .filter(([, deger]) => !(deger in MALZEME_TIPI_ETIKETLERI))
      .map(([ad, deger]) => `${ad}=${deger}`);

    expect(etiketsiz, "Etiketi olmayan malzeme tipi.").toEqual([]);

    // Açılır liste de TAM olmalı — etiket var ama seçenek yoksa
    // kullanıcı o tipi hiç seçemez (SparePart'ın hâli buydu).
    expect(
      MALZEME_TIPI_SECENEKLERI.map((s) => s.deger).sort(),
      "Açılır listede eksik tip var — kullanıcı onu seçemez.",
    ).toEqual([...arkaUc.values()].sort());

    // Bilinmeyen tip sessizce boş DÖNMEZ
    expect(malzemeTipiEtiketi(99)).toBe("Tip 99");
  });
});

/*
 * ═══ (c) YAPISAL MUHAFAZA: ÜÇÜNCÜ KOPYA AÇILAMAZ ═══
 *
 * Beş kopyayı düzeltmek kusuru BUGÜN kapatır. Asıl sebep aynı bilginin
 * birden çok yerde durmasıydı.
 */
describe("(c) yapısal muhafaza — malzeme tipi etiketi ikinci kez yazılmasın", () => {
  const DIZINLER = ["app", "components", "services", "hooks"];
  const TEK_KAYNAK = join("lib", "inventory", "malzeme-tipi.ts");

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

  /** Yorumlar atılıyor — muhafız kendi gerekçesini ihlal saymasın. */
  function yorumsuz(metin: string): string {
    return metin
      .replace(/\/\*[\s\S]*?\*\//g, "")
      .replace(/\{\/\*[\s\S]*?\*\/\}/g, "")
      .split("\n")
      .map((satir) => satir.replace(/\/\/.*$/, ""))
      .join("\n");
  }

  it("başka hiçbir dosya kendi malzeme tipi etiketini yazmıyor", () => {
    const dosyalar = DIZINLER.flatMap((d) => kaynaklar(join(KOK, d)));

    expect(
      dosyalar.length,
      "Tarama hiç dosya görmedi — 'ihlal yok' sonucu anlamsız.",
    ).toBeGreaterThan(200);

    /*
     * DESEN, KOPYANIN İMZASINI ARIYOR — SÖZCÜĞÜ DEĞİL.
     *
     * İlk yazımda desen düpedüz "Demirbaş" sözcüğünü arıyordu ve
     * muhafız `tool-asset-alert-widget.tsx`teki `<h3>Demirbaş</h3>`
     * BAŞLIĞINI ihlal saydı — yanlış alarm. (Yanlış alarmın kendisi
     * muhafızın gerçekten dosya okuduğunun kanıtıydı.)
     *
     * Kopyanın imzası iki biçimden biri: bir açılır liste seçeneği,
     * ya da sayıdan etikete bir eşleme satırı.
     */
    const ETIKETLER = "Sarf malzemesi|Stok malzemesi|Yedek Parça|Demirbaş|Ekipman";
    const DESENLER = [
      new RegExp(`<option[^>]*>\\s*(${ETIKETLER})\\s*<`),
      new RegExp(`[0-9]\\s*:\\s*["'\`](${ETIKETLER})["'\`]`),
    ];

    const ihlal = dosyalar
      .filter((yol) => relative(KOK, yol) !== TEK_KAYNAK)
      .filter((yol) => {
        const govde = yorumsuz(readFileSync(yol, "utf8"));
        return DESENLER.some((desen) => desen.test(govde));
      })
      .map((yol) => relative(KOK, yol));

    expect(
      ihlal,
      "Bu dosya(lar) kendi malzeme tipi etiketini yazıyor. Etiket TEK " +
        `yerden gelmeli: ${TEK_KAYNAK} — 'malzemeTipiEtiketi' ya da ` +
        "'MALZEME_TIPI_SECENEKLERI' kullanın.",
    ).toEqual([]);
  });
});
