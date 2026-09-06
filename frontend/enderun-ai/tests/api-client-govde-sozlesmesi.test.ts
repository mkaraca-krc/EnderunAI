import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * `apiClient` GÖVDEYİ KENDİ STRINGIFY EDER — ÇAĞIRAN ETMEZ.
 *
 * ═══ DOĞURAN ARIZA (2026-09-06) ═══
 *
 * Mehmet tarayıcıdan konuşma açmayı denedi: `POST
 * /api/backend/mesajlar/konusmalar/birebir` → **400**, ekranda
 * "İşlem başarısız: 400".
 *
 * Sebep: `apiClient` gövdeyi `JSON.stringify(options.body)` ile
 * kendisi çeviriyor. Servis içinde bir kez daha `JSON.stringify`
 * çağrılınca gövde bir NESNE değil, bir JSON METNİ olarak gidiyor —
 * `"{\"karsiUserId\":\"...\"}"`. Sunucu onu kayda bağlayamıyor.
 *
 * ═══ NEDEN BU TEST, TEK TEK DÜZELTME DEĞİL ═══
 *
 * Arıza mesajlaşmada göründü ama ölçüm ÜÇ serviste TOPLAM YEDİ çağrı
 * buldu: stok sayımı (4), envanter kategorisi (1), mesajlaşma (2).
 * Yalnız görüneni düzeltmek, kalan beşini canlıda bırakırdı — ve
 * sekizincisi yarın yazılırdı (Kural 79: kusur birden çok yerde
 * yaşıyorsa düzeltme kaynakta yapılır).
 *
 * ═══ DAR TUTULUYOR ═══
 *
 * `body: JSON.stringify(...)` kendi başına hata DEĞİL: `fetch`i
 * doğrudan çağıran dosyalarda doğrusu budur (`app/login/page.tsx`,
 * `inventory-movement.service.ts` gibi). Bu test yalnız `apiClient`
 * KULLANAN dosyalara bakar.
 */

const ROOT = join(__dirname, "..");
const ATLANAN = new Set(["node_modules", ".next", "tests"]);

function kaynaklar(dizin: string): string[] {
  const bulunan: string[] = [];

  for (const girdi of readdirSync(dizin)) {
    if (ATLANAN.has(girdi)) continue;

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...kaynaklar(yol));
      continue;
    }

    if (/\.(ts|tsx)$/.test(girdi)) bulunan.push(yol);
  }

  return bulunan;
}

/** `apiClient` çağıran dosyalar — tanımın kendisi hariç. */
function apiClientKullananlar() {
  return kaynaklar(ROOT)
    .filter((yol) => !yol.endsWith(join("lib", "api", "api-client.ts")))
    .map((yol) => ({ yol, kod: readFileSync(yol, "utf8") }))
    .filter(({ kod }) => kod.includes("apiClient"));
}

describe("apiClient gövde sözleşmesi", () => {
  it("apiClient kullanan dosya var (POZİTİF KONTROL)", () => {
    // Tarama bozulursa liste boşalır ve aşağıdaki test boş kümede
    // YEŞİL döner — hiçbir şey ölçmediği hâlde (Kural 48).
    expect(apiClientKullananlar().length).toBeGreaterThan(5);
  });

  it("apiClient çağıran hiçbir dosya gövdeyi kendisi stringify etmez", () => {
    const suclular = apiClientKullananlar()
      .filter(({ kod }) => /body:\s*JSON\.stringify/.test(kod))
      .map(({ yol }) => yol.replace(ROOT + "/", ""));

    expect(
      suclular,
      "Bu dosyalar `apiClient` kullanıyor ve gövdeyi KENDİLERİ " +
        "stringify ediyor. apiClient bunu zaten yapıyor; çift çevrim " +
        "sunucuya nesne yerine METİN gönderir ve 400 üretir.\n  - " +
        suclular.join("\n  - ")
    ).toEqual([]);
  });

  it("apiClient gövdeyi gerçekten kendisi çeviriyor (SÖZLEŞMENİN DAYANAĞI)", () => {
    // Bu iddia doğru olmasaydı yukarıdaki test YANLIŞ olurdu.
    const kod = readFileSync(join(ROOT, "lib", "api", "api-client.ts"), "utf8");
    expect(kod).toMatch(/JSON\.stringify\(options\.body\)/);
  });
});
