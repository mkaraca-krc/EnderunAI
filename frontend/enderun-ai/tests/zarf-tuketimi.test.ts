import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * ZARF DÖNEN UCU DÜZ DİZİ GİBİ TÜKETEN EKRAN — MUHAFIZ.
 *
 * ───────────────────────────────────────────────────────────────
 * BU KIRMIZIYA DÖNERSE
 * ───────────────────────────────────────────────────────────────
 *
 * O ekran AÇILMAZ. Sunucu `{ items, hasMore, nextCursor }` ya da
 * `PagedResult` döndürür, istemci düz dizi bekler ve ilk `.map` /
 * `.slice` çağrısında `TypeError` alır. Ekran boş bile kalmaz —
 * çöker.
 *
 * KAYNAK: `/gorevler` (2026-08-30). Uç F4 turunda zarfa çevrildi,
 * ekran güncellenmedi: `M.slice is not a function`. Ekran hiç
 * açılmıyordu ve "WorkTasks 1 kayıt" bunun sonucuydu — kullanıcının
 * ilgisizliği değil.
 *
 * NEDEN AYLARCA GÖRÜLMEDİ: istemci hata bildirim kanalı da ayrı bir
 * arızayla (204/502) çöküktü. Ekran çöküyor, bildirmeye çalışıyor,
 * bildirim de düşüyordu (Kural 66).
 *
 * ───────────────────────────────────────────────────────────────
 * NASIL ÖLÇÜYOR
 * ───────────────────────────────────────────────────────────────
 *
 * İki taraf da GERÇEK KAYNAKTAN sürülüyor (Kural 58):
 *
 *   ARKA UÇ  → hangi controller'ın KÖK liste ucu zarf dönüyor
 *              (`[HttpGet]` argümansız + gövdesinde PagedResult ya
 *               da hasMore)
 *   ÖN YÜZ   → hangi `apiClient<X[]>` çağrısı o kök rotaya gidiyor
 *
 * KÖK UÇ AYRIMI ŞART: aynı controller'ın alt uçları (`/arama`,
 * `/{id}`) düz dizi ya da tek nesne dönebiliyor. Controller
 * seviyesinde eşleştirmek yanlış alarm üretirdi (Kural 47).
 */

const ONYUZ = join(__dirname, "..");
const DEPO = join(ONYUZ, "..", "..");
const CONTROLLERS = join(DEPO, "backend", "EnderunAI.Api", "Controllers");

/** Kök liste ucu zarf dönen controller'ların rotaları. */
function zarfRotalari(): Map<string, string> {
  const rotalar = new Map<string, string>();

  let dosyalar: string[];
  try {
    dosyalar = readdirSync(CONTROLLERS).filter((x) => x.endsWith("Controller.cs"));
  } catch {
    return rotalar;
  }

  for (const ad of dosyalar) {
    const metin = readFileSync(join(CONTROLLERS, ad), "utf8");

    const rotaEslesme = /\[Route\("api\/([^"]+)"\)\]/.exec(metin);
    if (!rotaEslesme) continue;

    const rota = rotaEslesme[1];

    /*
     * KÖK UÇ: argümansız `[HttpGet]`. Gövdesi bir sonraki `[Http`
     * niteliğine kadar sürüyor.
     */
    const kokBas = metin.search(/\[HttpGet\]\s*\n/);
    if (kokBas < 0) continue;

    const sonraki = metin.slice(kokBas + 10).search(/\[Http(Get|Post|Put|Delete)/);
    const govde =
      sonraki < 0 ? metin.slice(kokBas) : metin.slice(kokBas, kokBas + 10 + sonraki);

    if (/PagedResult|hasMore|nextCursor/.test(govde)) rotalar.set(rota, ad);
  }

  return rotalar;
}

/** `apiClient<X[]>( "yol" )` çağrılarının hedef yolları. */
function diziBekleyenCagrilar(): { dosya: string; tip: string; yol: string }[] {
  const bulunan: { dosya: string; tip: string; yol: string }[] = [];

  const gez = (dizin: string): string[] => {
    const cikti: string[] = [];
    let girdiler: string[];
    try {
      girdiler = readdirSync(dizin);
    } catch {
      return cikti;
    }
    for (const g of girdiler) {
      if (g === "node_modules" || g === ".next") continue;
      const yol = join(dizin, g);
      if (statSync(yol).isDirectory()) cikti.push(...gez(yol));
      else if (yol.endsWith(".ts") || yol.endsWith(".tsx")) cikti.push(yol);
    }
    return cikti;
  };

  const kalip =
    /apiClient<\s*([A-Za-z_][\w]*)\s*\[\]\s*>\s*\(\s*[`"']([A-Za-z][\w\-/]*)/g;

  for (const dosya of [
    ...gez(join(ONYUZ, "services")),
    ...gez(join(ONYUZ, "app")),
    ...gez(join(ONYUZ, "components")),
  ]) {
    const metin = readFileSync(dosya, "utf8");

    for (const m of metin.matchAll(kalip)) {
      bulunan.push({
        dosya: relative(ONYUZ, dosya),
        tip: m[1],
        yol: m[2],
      });
    }
  }

  return bulunan;
}

describe("zarf tüketimi", () => {
  const rotalar = zarfRotalari();
  const cagrilar = diziBekleyenCagrilar();

  /**
   * TARAMA BOŞA DÜŞMÜYOR.
   *
   * İki taraftan biri boşalırsa "uyumsuzluk yok" testi sessizce
   * yeşil kalırdı — boş küme her iddiayı doğrular (Kural 48).
   */
  it("tarama boşa düşmüyor", () => {
    expect(rotalar.size, "zarf dönen kök uç bulunamadı").toBeGreaterThan(3);
    expect(cagrilar.length, "dizi bekleyen çağrı bulunamadı").toBeGreaterThan(15);
  });

  /**
   * ZARF DÖNEN KÖK UCU DÜZ DİZİ GİBİ TÜKETEN YOK.
   *
   * BU KIRMIZIYA DÖNERSE: listelenen ekran(lar) AÇILMIYOR demektir.
   */
  it("zarf dönen kök uç düz dizi gibi tüketilmiyor", () => {
    const uyumsuz = cagrilar
      .filter((c) => rotalar.has(c.yol))
      .map((c) => `${c.dosya}: apiClient<${c.tip}[]>("${c.yol}") — ${rotalar.get(c.yol)} zarf dönüyor`);

    expect(
      uyumsuz,
      "ZARF DÖNEN UCU DÜZ DİZİ GİBİ TÜKETEN ÇAĞRI(LAR):\n" +
        uyumsuz.join("\n") +
        "\n\nSunucu { items, hasMore, nextCursor } ya da PagedResult " +
        "döndürüyor; istemci dizi bekliyor. Ekran ilk .map/.slice " +
        "çağrısında TypeError ile ÇÖKER — boş kalmaz, açılmaz.\n" +
        "Düzeltme: dönüş tipini zarfa çevirin ve `.items` okuyun.",
    ).toEqual([]);
  });
});
