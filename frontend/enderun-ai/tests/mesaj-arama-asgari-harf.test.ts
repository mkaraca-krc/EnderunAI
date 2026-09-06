import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { MESAJ_ARAMA_EN_AZ_HARF } from "@/lib/mesajlasma/arama-kurali";

/**
 * ARAYÜZDEKİ ASGARİ HARF, SUNUCUDAKİYLE AYNI OLMALI.
 *
 * DOĞURAN ARIZA (2026-09-06): ekran "en az 2 harf" diyordu, sunucu 3
 * istiyordu. Aradaki bir harf, kullanıcıya hiçbir açıklaması olmayan
 * boş bir liste olarak görünüyordu.
 *
 * BU TEST NEDEN KAYNAK OKUYOR: sayı iki ayrı dilde, iki ayrı derleme
 * biriminde yaşıyor. Aralarında çalışma anında bir bağ yok; tek bağ
 * bu testtir. Sunucudaki sabit değişip arayüzdeki unutulursa (ya da
 * tersi) burası kırmızı yanar.
 */

const SUNUCU_DOSYASI = join(
  __dirname,
  "..",
  "..",
  "..",
  "backend",
  "EnderunAI.Api",
  "Services",
  "Messaging",
  "MesajAramaKurali.cs"
);

function sunucudakiAsgari(): number {
  const kaynak = readFileSync(SUNUCU_DOSYASI, "utf8");

  const eslesme = kaynak.match(
    /public\s+const\s+int\s+EnAzHarf\s*=\s*(\d+)\s*;/
  );

  // POZİTİF KONTROL: desen tutmazsa test sessizce geçmemeli.
  // Bulunamayan sabit, "aynılar" demek değildir (Kural 48).
  expect(
    eslesme,
    `Sunucudaki EnAzHarf sabiti okunamadı: ${SUNUCU_DOSYASI}. ` +
      "Ayıklama deseni bozulmuş olabilir; bu hâlde test hiçbir şey ölçmüyor."
  ).not.toBeNull();

  return Number(eslesme![1]);
}

describe("mesaj arama asgari harf sayısı", () => {
  it("sunucudaki sabit okunabiliyor ve makul (POZİTİF KONTROL)", () => {
    const sunucu = sunucudakiAsgari();
    expect(Number.isInteger(sunucu)).toBe(true);
    expect(sunucu).toBeGreaterThan(0);
  });

  it("arayüzdeki sayı sunucudakiyle AYNI", () => {
    expect(MESAJ_ARAMA_EN_AZ_HARF).toBe(sunucudakiAsgari());
  });
});
