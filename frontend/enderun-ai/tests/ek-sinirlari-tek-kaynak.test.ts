import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import {
  DOSYA_BASINA_EN_FAZLA_BAYT,
  MESAJ_BASINA_EN_FAZLA_DOSYA,
  IZINLI_UZANTILAR,
} from "@/lib/mesajlasma/ek-kurallari";

/**
 * EK SINIRLARI İKİ TARAFTA AYNI OLMAK ZORUNDA (MESAJ/3 C1).
 *
 * ═══ NEDEN BU TEST VAR ═══
 *
 * Sınır iki yerde yazılı: sunucu (`MesajEkiKurallari.cs`) ve istemci
 * (`ek-kurallari.ts`). Koruma sunucudaki; istemcideki kullanıcıya
 * yardım ediyor. İkisi ayrışırsa iki yönde de zarar var:
 *
 *   istemci daha GENİŞSE -> kullanıcı 20 MB yükler, sunucu reddeder
 *   istemci daha DARSA   -> meşru dosya hiç denenmez, sebebi de
 *                           görünmez
 *
 * İkinci hâl daha sinsi: hata yok, yalnız özellik sessizce eksik.
 *
 * ═══ NEDEN TEK KAYNAK YAPILMADI ═══
 *
 * Sunucu C#, istemci TypeScript. Ortak bir kaynaktan üretmek bir
 * kod üretim adımı gerektirirdi; bu proje için o adımın maliyeti
 * bu testten yüksek. Karar burada yazılı ki "neden iki yerde" diye
 * soran ikinci kez düşünmesin.
 */
describe("ek sınırları tek kaynak", () => {
  const sunucu = readFileSync(
    join(
      __dirname,
      "..", "..", "..",
      "backend", "EnderunAI.Api", "Services", "Messaging",
      "MesajEkiKurallari.cs"
    ),
    "utf8"
  );

  it("dosya boyutu sınırı aynı", () => {
    const eslesme = /DosyaBasinaEnFazlaBayt\s*=\s*([0-9]+)L\s*\*\s*1024\s*\*\s*1024/
      .exec(sunucu);

    expect(eslesme, "Sunucudaki boyut sınırı okunamadı").not.toBeNull();

    const sunucuMb = Number(eslesme![1]);
    expect(DOSYA_BASINA_EN_FAZLA_BAYT).toBe(sunucuMb * 1024 * 1024);
  });

  it("mesaj başına dosya sayısı aynı", () => {
    const eslesme = /MesajBasinaEnFazlaDosya\s*=\s*([0-9]+)/.exec(sunucu);

    expect(eslesme, "Sunucudaki dosya sayısı okunamadı").not.toBeNull();
    expect(MESAJ_BASINA_EN_FAZLA_DOSYA).toBe(Number(eslesme![1]));
  });

  it("izinli uzantı listeleri aynı", () => {
    const blok = /IzinliUzantilar\s*=[\s\S]*?\{([\s\S]*?)\};/.exec(sunucu);
    expect(blok, "Sunucudaki uzantı listesi okunamadı").not.toBeNull();

    const sunucuUzantilar = [...blok![1].matchAll(/"(\.[a-z0-9]+)"/g)]
      .map((m) => m[1])
      .sort();

    // POZİTİF KONTROL: liste gerçekten okundu ve beklenen büyüklükte.
    // Boş bir liste iki tarafta da "aynı" çıkardı (Kural 48).
    expect(sunucuUzantilar.length).toBeGreaterThan(10);

    expect([...IZINLI_UZANTILAR].sort()).toEqual(sunucuUzantilar);
  });
});
