import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * TARAYICI DİYALOGLARI YASAK.
 *
 * window.confirm / window.prompt / window.alert uygulamada
 * kullanılmaz. Tarayıcı penceresi biçimlendirilemiyor, gerekçeyi
 * zorunlu tutamıyor, hata mesajını aynı yerde gösteremiyor ve
 * erişilebilir değil. Yerine Modal, ConfirmDialog ve Drawer var.
 *
 * BU TEST ÖNCE SAYIYORDU. Yayma başladığında 50 dosyada 98 kullanım
 * vardı (72 confirm, 26 prompt); hepsini tek pakette değiştirmek
 * uygulamanın yarısına aynı anda dokunmak olurdu. O yüzden test bir
 * TAVAN tutuyor ve sayının artmamasını garanti ediyordu; her modül
 * geçtikçe tavan düşürüldü:
 * 98 -> 97 -> 95 -> 74 -> 63 -> 60 -> 55 -> 52 -> 46 -> 33 -> 21 ->
 * 11 -> 0.
 *
 * Sayı sıfıra indi; tavan kalktı, yerine yasak geldi. GERİ DÖNÜŞ YOK:
 * bundan sonra eklenen her kullanım testi kırar.
 *
 * YAYMA SIRASINDA BULUNANLAR — bu diyalogların neden kaldırıldığını
 * anlatan gerçek örnekler:
 *
 * - VAZGEÇİLEMEYEN DİYALOG: `prompt` sonucu `?? ""` ile karşılanınca
 *   "Vazgeç" boş metne dönüşüyor ve işlem YİNE yapılıyordu (görev
 *   tamamlama, tatil takvimi doğrulama).
 * - BOŞ GEREKÇE: `reason === null` yalnızca "Vazgeç"i yakalıyor; boş
 *   kutuya OK denince metin "" olarak geçiyordu. Hakediş gerekçesiz
 *   iptal edilebiliyordu.
 * - İPTAL BAŞARI GİBİ GÖRÜNÜYORDU: ay onayından vazgeçen kullanıcıya
 *   yeşil "Onay iptal edildi." bildirimi çıkıyordu.
 * - SESSİZ SAYI HATASI: `prompt` ile alınan "1.250,50" metni 1.25
 *   olarak okunuyordu.
 */

const ROOTS = ["app", "components", "lib", "services"];

const PATTERN = /(?<![.\w])(?:window\.)?(confirm|prompt|alert)\s*\(/g;

function collectFiles(dir: string): string[] {
  const entries = readdirSync(dir);
  const files: string[] = [];

  for (const entry of entries) {
    const full = join(dir, entry);

    if (statSync(full).isDirectory()) {
      files.push(...collectFiles(full));
      continue;
    }

    if (/\.(tsx?|jsx?)$/.test(entry)) files.push(full);
  }

  return files;
}

function scan() {
  const hits: { file: string; count: number }[] = [];

  for (const root of ROOTS) {
    for (const file of collectFiles(root)) {
      // Bileşenin KENDİSİ tarayıcı diyaloğunun yerine geçiyor; içindeki
      // "confirm" sözcükleri (confirmLabel, onConfirm) sayılmamalı.
      if (file.includes(join("components", "ui"))) continue;

      const source = readFileSync(file, "utf8");
      const matches = source.match(PATTERN);

      if (matches?.length) hits.push({ file, count: matches.length });
    }
  }

  return hits;
}

describe("tarayıcı diyalogları", () => {
  it("hiç kullanılmıyor", () => {
    const hits = scan();

    expect(
      hits.map((hit) => `${hit.file} (${hit.count})`),
      "Tarayıcı diyaloğu yasak. Onay için ConfirmDialog, form için " +
        "Modal, yan panel için Drawer kullanın. ConfirmDialog gerekçeyi " +
        "zorunlu tutabiliyor (requireReason), hatayı diyaloğun içinde " +
        "gösteriyor ve vazgeçme yolu bırakıyor.",
    ).toEqual([]);
  });
});
