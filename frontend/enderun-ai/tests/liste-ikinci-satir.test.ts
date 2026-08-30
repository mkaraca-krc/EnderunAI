import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * LİSTE HÜCRESİNDE İKİNCİ SATIR AYRACI — TEK KAYNAK.
 *
 * NEDEN VAR: sütunlar ikinci bir bilgiyi `<small>` ile yazıyor —
 * çek no altında belge no, banka adı altında keşideci. `.erp-table`
 * bunu blok yapıyordu; DataTable'ın kullandığı
 * `.erp-data-table-grid` için karşılığı YOKTU. Sonuç canlıda:
 *
 *     "HALKBANKFIRAT LIFE"          (banka + keşideci)
 *     "C1 1796766ACK-2026-000005"   (çek no + belge no)
 *
 * İki ayrı bilgi tek kelimeymiş gibi okunuyordu. 20 ekran bu
 * desende.
 *
 * NEDEN CSS SÖZLEŞMESİ, NEDEN RENDER TESTİ DEĞİL: kural harici bir
 * stil dosyasında ve jsdom onu uygulamıyor — render testi kuralın
 * varlığını ölçemez, yalnız işaretlemeyi ölçer. İşaretleme zaten
 * doğruydu; eksik olan stildi.
 *
 * LİSTEDEN SÜRÜLÜYOR: tablo sınıfları tek tek elle yazılmıyor, iki
 * sınıf da aynı kuraldan geçiyor. Yeni bir tablo sınıfı eklenirse
 * buraya eklenmesi gerekir — elle kurulmuş kümenin donması riski
 * (Kural 58) burada kabul ediliyor çünkü tablo sınıfı sayısı ikide
 * sabit ve üçüncüsü bilinçli bir karar olur.
 */

const KOK = join(__dirname, "..");

/** Liste hücresi basan tablo sınıfları. */
const TABLO_SINIFLARI = [
  { sinif: ".erp-table", secici: ".erp-table td small" },
  {
    sinif: ".erp-data-table-grid",
    secici: ".erp-data-table-grid tbody td small",
  },
];

function stil(): string {
  return readFileSync(join(KOK, "app", "globals.css"), "utf8");
}

/** Seçicinin gövdesinde `display: block` var mı. */
function blokMu(css: string, secici: string): boolean {
  const kacir = secici.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

  // Seçici tek başına ya da virgüllü bir grubun parçası olabilir.
  const kalip = new RegExp(
    `(^|,|\\})\\s*[^{}]*${kacir}\\s*[^{}]*\\{([^}]*)\\}`,
    "m",
  );

  const eslesme = kalip.exec(css);

  if (!eslesme) return false;

  return /display\s*:\s*block/.test(eslesme[2]);
}

describe("liste hücresinde ikinci satır", () => {
  /**
   * TARAMA BOŞA DÜŞMÜYOR. Stil dosyası okunamazsa ya da boşalırsa
   * "kural var" testi sessizce geçmemeli.
   */
  it("stil dosyası okunuyor", () => {
    const css = stil();

    expect(css.length).toBeGreaterThan(50_000);
    expect(css).toContain(".erp-data-table-grid");
  });

  it.each(TABLO_SINIFLARI)(
    "$sinif ikinci satırı blok yapıyor",
    ({ sinif, secici }) => {
      expect(
        blokMu(stil(), secici),
        `"${secici}" için display:block kuralı yok. Bu sınıfla basılan ` +
          "listelerde <small> satır içi kalır ve iki ayrı bilgi tek " +
          "kelimeymiş gibi okunur — canlıda \"HALKBANKFIRAT LIFE\" ve " +
          '"C1 1796766ACK-2026-000005" böyle çıktı.\n\n' +
          `Düzeltme ${sinif} kuralının yanına yazılır, ekranlara ayrı ` +
          "ayrı DEĞİL: 20 ekran bu desende ve kopyalar zamanla ayrışır.",
      ).toBe(true);
    },
  );
});
