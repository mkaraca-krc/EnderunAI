import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * YORUM BİLEŞENİ TAKILAN HER YER, OKUMA KARARINI AÇIKÇA VERİR.
 *
 * NEDEN: yorum kapısı üç tipte ekran kapısından DAR
 * (teklif → `offer_tracking.view`, mal kabul →
 * `purchasing-receipts.view`, satın alma talebi →
 * `purchasing-requests.view`). Ekranı açabilen ama yorum izni
 * olmayan kullanıcı 403 ya da boş bir hata kutusu GÖRMEMELİ —
 * olmayan bir bölümün hata vermesi, kullanıcıya bozulmuş bir ekran
 * gösterir ve "sistem çalışmıyor" izlenimi bırakır.
 *
 * `canRead` TypeScript'te zorunlu, yani atlanamaz. Bu test bir adım
 * ötesini tutuyor: kararın SABİT `true` ile geçilmediğini. Sabit
 * `true`, zorunlu prop'u sağlar ama kararı vermez — kapıyı açık
 * bırakmanın en kolay yolu tam olarak budur.
 */

const ROOT = join(__dirname, "..");

const BILESENLER = ["<CommentThread", "<AttachmentPanel"];

function kaynaklar(dizin: string): string[] {
  const bulunan: string[] = [];

  for (const girdi of readdirSync(dizin)) {
    if (girdi === "node_modules" || girdi === ".next") continue;

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...kaynaklar(yol));
      continue;
    }

    if (/\.tsx$/.test(girdi)) bulunan.push(yol);
  }

  return bulunan;
}

/** Bileşeni RENDER EDEN dosyalar — tanımın kendisi hariç. */
function takmaYerleri() {
  return kaynaklar(join(ROOT, "app"))
    .map((yol) => ({ yol, kod: readFileSync(yol, "utf8") }))
    .filter(({ kod }) => BILESENLER.some((b) => kod.includes(b)));
}

describe("yorum bileşeni takma sözleşmesi", () => {
  it("en az bir takma yeri var (tarama boşa düşmüyor)", () => {
    expect(takmaYerleri().length).toBeGreaterThan(0);
  });

  it("her takma yeri canRead geçiyor", () => {
    const eksik = takmaYerleri()
      .filter(({ kod }) => !kod.includes("canRead="))
      .map(({ yol }) => yol.replace(ROOT, ""));

    expect(
      eksik,
      "Bu ekranlar yorum bileşenini takıyor ama `canRead` geçmiyor:\n" +
        eksik.join("\n")
    ).toEqual([]);
  });

  it("okuma kararı sabit true ile geçilmiyor", () => {
    const sabit = takmaYerleri()
      .filter(({ kod }) => /canRead=\{\s*true\s*\}/.test(kod))
      .map(({ yol }) => yol.replace(ROOT, ""));

    expect(
      sabit,
      "Bu ekranlar `canRead` kararını SABİT `true` ile geçiyor. " +
        "Zorunlu prop sağlanmış olur ama karar verilmemiş olur — " +
        "yorum izni olmayan kullanıcı bölümü görür ve uçtan hata yer. " +
        "İzin kancasından gelen bir değer geçin:\n" + sabit.join("\n")
    ).toEqual([]);
  });
});
