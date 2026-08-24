import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import {
  KOK,
  cozuluyorMu,
  hedefler,
  hesaplanmisHedefler,
  rotaDeseni,
  rotalar,
  sablonuNormalize,
  tarananDosyaSayisi,
} from "./bekci/rota-envanteri";

/**
 * ROTA BEKÇİSİ.
 *
 * NEDEN VAR: M1/5'te Yapılacaklar satırları `/gorevler/{id}`'ye
 * bağlandı ama o dinamik rota hiç oluşturulmamıştı. Kullanıcı bir
 * göreve tıklayınca BOŞ SAYFA gördü ve bu SEKİZ GÜN canlıda durdu.
 * Hata, `/gorevler` ekranının varlığını görüp `{id}` biçiminin de
 * olduğunu VARSAYMAKTAN çıktı.
 *
 * KURAL: bir bağlantı hedefinin varlığı, önekinin varlığıyla
 * kanıtlanmaz.
 *
 * KAPSAM YALNIZ SAYFA ROTALARI. `app/api/**` altındaki uç noktalar
 * bu turda DIŞARIDA — ayrı bir bekçi işi, notu DURUM.md'de.
 */

function cizgiSatirlari(dosya: string): string[] {
  return readFileSync(join(KOK, "tests", "bekci", dosya), "utf8")
    .split("\n")
    .map((s) => s.trim())
    .filter((s) => s.length > 0 && !s.startsWith("#"));
}

const desenler = rotalar().map(rotaDeseni);

/** `dosya|hedef` — gerekçe karşılaştırmaya girmez. */
function cizgiAnahtari(satir: string): string {
  const p = satir.split("|");
  return `${p[0]}|${p[1]}`;
}

describe("rota bekçisi — tarama gerçekten çalışıyor", () => {
  /**
   * BEKÇİNİN BOŞA DÜŞMEDİĞİNİ KANITLAR.
   *
   * Bir kaynak tarayıcısının en sinsi arızası hiçbir şey taramamak:
   * sıfır bulgu "her şey yolunda" gibi görünür. Bu proje bunu bir
   * kez yaşadı — `CommentEntityTypeGuardTests`'in yolu yanlış
   * olsaydı sessizce yeşil kalacaktı.
   */
  it("dosya, rota ve hedef sayıları makul", () => {
    expect(tarananDosyaSayisi()).toBeGreaterThan(300);
    expect(rotalar().length).toBeGreaterThan(150);
    expect(rotalar().filter((r) => r.includes("[")).length).toBeGreaterThan(30);
    expect(hedefler().length).toBeGreaterThan(400);
  });
});

describe("rota bekçisi — değişmez hedefler", () => {
  it("her değişmez hedef bir rotaya çözülüyor", () => {
    const cizgi = new Set(cizgiSatirlari("rota-cizgi.txt").map(cizgiAnahtari));

    const kirik = hedefler()
      .filter((h) => h.tur === "degismez")
      .filter((h) => !cozuluyorMu(h.ham, desenler))
      .map((h) => `${h.dosya}|${h.ham}`)
      .filter((a) => !cizgi.has(a));

    expect(
      [...new Set(kirik)],
      "Bu bağlantılar var olmayan bir sayfaya gidiyor — kullanıcı " +
        "tıklayınca boş sayfa görür:\n" + [...new Set(kirik)].join("\n")
    ).toEqual([]);
  });
});

describe("rota bekçisi — şablon hedefler", () => {
  it("her şablon hedef bir dinamik rotayla eşleşiyor", () => {
    const cizgi = new Set(cizgiSatirlari("rota-cizgi.txt").map(cizgiAnahtari));

    const kirik = hedefler()
      .filter((h) => h.tur === "sablon")
      .filter((h) => !cozuluyorMu(sablonuNormalize(h.ham), desenler))
      .map((h) => `${h.dosya}|${h.ham}`)
      .filter((a) => !cizgi.has(a));

    expect(
      [...new Set(kirik)],
      "Bu şablon hedeflerin öneki ya da segment sayısı hiçbir dinamik " +
        "rotayla eşleşmiyor:\n" + [...new Set(kirik)].join("\n")
    ).toEqual([]);
  });
});

describe("rota bekçisi — çizgi çift yönlü", () => {
  /**
   * ÇİZGİDE ARTIK VAR OLMAYAN SATIR KALAMAZ.
   *
   * Tek yönlü bir çizgi zamanla yalan söyler: düzeltilmiş bir hedef
   * çizgide durmaya devam eder ve borç olduğundan büyük görünür.
   * Daha kötüsü, o satır bir gün BAŞKA bir kırık hedefi kazara
   * örtebilir.
   */
  it("çizgideki her satır hâlâ gerçekten çözülmüyor", () => {
    const suanki = new Set(
      hedefler()
        .filter(
          (h) =>
            !cozuluyorMu(
              h.tur === "sablon" ? sablonuNormalize(h.ham) : h.ham,
              desenler
            )
        )
        .map((h) => `${h.dosya}|${h.ham}`)
    );

    const olmayan = cizgiSatirlari("rota-cizgi.txt")
      .map(cizgiAnahtari)
      .filter((a) => !suanki.has(a));

    expect(
      olmayan,
      "Bu satırlar rota-cizgi.txt içinde ama artık çözülmeyen bir " +
        "hedef değiller. Düzeltilmişlerse SİLİN — çizgi küçülmeli:\n" +
        olmayan.join("\n")
    ).toEqual([]);
  });
});

describe("rota bekçisi — hesaplanmış hedef cırcırı", () => {
  function cizgiHaritasi(): Map<string, number> {
    const m = new Map<string, number>();

    for (const satir of cizgiSatirlari("rota-hesaplanmis-cizgi.txt")) {
      const ayrac = satir.lastIndexOf(":");
      m.set(satir.slice(0, ayrac), Number(satir.slice(ayrac + 1)));
    }

    return m;
  }

  function toplam(m: Map<string, number>): number {
    return [...m.values()].reduce((a, b) => a + b, 0);
  }

  /**
   * YENİ HESAPLANMIŞ HEDEF EKLENEMEZ.
   *
   * Değişkenden gelen hedef statik olarak doğrulanamaz, yani bekçinin
   * kör noktası. Kör nokta büyümemeli: yeni bağlantı yazan kişi
   * değişmez ya da şablon hedef kullanır.
   */
  it("hesaplanmış hedef sayısı çizgiyi aşmıyor", () => {
    const suanki = toplam(hesaplanmisHedefler());
    const temel = toplam(cizgiHaritasi());

    expect(
      suanki,
      `Doğrulanamayan bağlantı hedefi ${suanki}, çizgi ${temel}. ` +
        "Bekçinin kör noktası büyüyemez — hedefi değişmez ya da şablon " +
        "biçimde yazın."
    ).toBeLessThanOrEqual(temel);
  });

  it("çizgide artık var olmayan dosya kalmıyor", () => {
    const suanki = hesaplanmisHedefler();

    const olmayan = [...cizgiHaritasi().keys()].filter(
      (d) => !suanki.has(d)
    );

    expect(
      olmayan,
      "Bu dosyalar çizgide ama artık hesaplanmış hedef taşımıyorlar. " +
        "SİLİN — çizgi küçülmeli:\n" + olmayan.join("\n")
    ).toEqual([]);
  });
});
