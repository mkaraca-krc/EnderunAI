import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

/*
 * ARKA UÇTAN GELEN ÖN YÜZ ROTALARI.
 *
 * NEDEN VAR: Hızır'ın pano brifingi ön yüz rotalarını ARKA UÇTA sabit
 * kodluyor (`HizirBriefingSources.cs`). İkisi mevcut değildi:
 *   /muhasebe/tedarikci-faturalari  → gerçeği /muhasebe/faturalar
 *   /santiye/gunluk-raporlar        → app/santiye/ dizini hiç yok
 *
 * Genel Müdür panoda "N tedarikçi faturası onay bekliyor" uyarısına
 * tıklayınca boş sayfa görüyordu. Bulgu tarayıcı konsolundan çıktı:
 * Next.js bağlantıları önyüklüyor ve nginx günlüğünde iki 404 kaldı
 * (referans: /dashboard).
 *
 * NEDEN MEVCUT BEKÇİ GÖRMEDİ: `tests/route-guard.test.ts` tam bu sınıf
 * için yazılmıştı — "bir bağlantı hedefinin varlığı, önekinin
 * varlığıyla kanıtlanmaz" — ama YALNIZ ÖN YÜZÜ tarıyor. Arka uçtan
 * gelen bağlantılar kapsamının dışındaydı: bekçi yeşil, bağlantı kırık.
 *
 * DÜRÜST SINIR: yalnız DİZGE SABİTİ olarak yazılmış rotaları görür.
 * Arka uç bir rotayı parça birleştirerek üretirse ($"/projeler/{id}")
 * bu tarama onu değerlendirmez — öneki (`/projeler`) kontrol edilir,
 * dinamik kuyruğu edilmez.
 */

const KOK = join(__dirname, "..");
const ARKA_UC = join(KOK, "..", "..", "backend", "EnderunAI.Api");
const ON_YUZ_APP = join(KOK, "app");

/** Arka uçta ön yüz rotası üreten dosyalar. */
const KAYNAK_DIZINLER = [
  join(ARKA_UC, "Services", "Hizir"),
  join(ARKA_UC, "Services", "Notifications"),
];

/** Ön yüz rotası olmayan, ama aynı biçimde yazılan yollar. */
const ROTA_DISI = /^\/(api|health|swagger|portal)\b/;

function csDosyalari(dizin: string, biriktir: string[] = []): string[] {
  for (const ad of readdirSync(dizin)) {
    const tam = join(dizin, ad);
    if (statSync(tam).isDirectory()) csDosyalari(tam, biriktir);
    else if (ad.endsWith(".cs")) biriktir.push(tam);
  }
  return biriktir;
}

/** `app/` altında bu rotanın bir `page.tsx`'i var mı? */
function sayfaVarMi(rota: string): boolean {
  const parcalar = rota.replace(/^\//, "").split("/").filter(Boolean);
  let dizin = ON_YUZ_APP;

  for (const parca of parcalar) {
    const dogrudan = join(dizin, parca);
    try {
      if (statSync(dogrudan).isDirectory()) {
        dizin = dogrudan;
        continue;
      }
    } catch {
      // dinamik segment olabilir: [id], [slug]
    }

    const dinamik = readdirSync(dizin).find((x) => /^\[.+\]$/.test(x));
    if (!dinamik) return false;
    dizin = join(dizin, dinamik);
  }

  try {
    return statSync(join(dizin, "page.tsx")).isFile();
  } catch {
    return false;
  }
}

type Bulgu = { dosya: string; satir: number; rota: string };

function tara(): { rotalar: Set<string>; kirik: Bulgu[]; dosyaSayisi: number } {
  const rotalar = new Set<string>();
  const kirik: Bulgu[] = [];
  let dosyaSayisi = 0;

  for (const dizin of KAYNAK_DIZINLER) {
    for (const yol of csDosyalari(dizin)) {
      dosyaSayisi += 1;
      const satirlar = readFileSync(yol, "utf8").split("\n");

      for (let i = 0; i < satirlar.length; i++) {
        /*
         * YORUM SATIRLARI ATLANIYOR — ÖLÇÜM DÜZELTMESİ.
         *
         * İlk koşumda düzeltmeyi yaptım ve muhafız YİNE kırmızı verdi:
         * eski rotayı açıklayan YORUMUM `"/muhasebe/tedarikci-faturalari"`
         * dizgesini taşıyordu ve tarama onu bir bağlantı sandı.
         *
         * Yorum bağlantı üretmez. Muhafız çalışan kodu ölçmeli, onu
         * anlatan metni değil. (Aynı hatayı KABUK paketinde de yaptım:
         * açıklama yorumum redwood sözleşmesini tetiklemişti.)
         */
        const kirpik = satirlar[i].trimStart();
        if (kirpik.startsWith("//") || kirpik.startsWith("*") || kirpik.startsWith("/*")) {
          continue;
        }

        for (const eslesme of satirlar[i].matchAll(/"(\/[a-z][a-z0-9/-]{2,60})"/g)) {
          const rota = eslesme[1];
          if (ROTA_DISI.test(rota)) continue;

          rotalar.add(rota);
          if (!sayfaVarMi(rota)) {
            kirik.push({ dosya: relative(KOK, yol), satir: i + 1, rota });
          }
        }
      }
    }
  }

  return { rotalar, kirik, dosyaSayisi };
}

const sonuc = tara();

describe("arka uçtan gelen ön yüz rotaları", () => {
  it("tarama boşa düşmüyor", () => {
    /*
     * POZİTİF KONTROL: arka uç taşınırsa ya da desen bozulursa aşağıdaki
     * iddia boş kümede sessizce yeşil kalırdı. Sayı ayrıca basılıyor —
     * kapsam daralırsa gözle görülsün (Kural 70 ailesi).
     */
    console.log(
      `[arka uç rotaları] taranan .cs: ${sonuc.dosyaSayisi} · ` +
        `bulunan rota: ${sonuc.rotalar.size} · kırık: ${sonuc.kirik.length}`,
    );

    expect(sonuc.dosyaSayisi).toBeGreaterThan(3);
    expect(sonuc.rotalar.size).toBeGreaterThan(10);
  });

  it("hepsinin ön yüzde bir sayfası var", () => {
    const liste = sonuc.kirik.map(
      (b) => `${b.dosya}:${b.satir}  ${b.rota}`,
    );

    expect(
      liste,
      liste.length === 0
        ? ""
        : "ARKA UÇ, ÖN YÜZDE OLMAYAN BİR ROTAYA BAĞLANIYOR:\n" +
          liste.join("\n") +
          "\n\nKullanıcı bu bağlantıya tıklarsa boş sayfa görür.",
    ).toEqual([]);
  });
});
