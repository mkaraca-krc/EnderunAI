import { afterEach, describe, expect, it, vi } from "vitest";

import { messagingService } from "@/services/messaging.service";

/**
 * SERVİSİN TELDE GÖNDERDİĞİ GÖVDE — NESNE Mİ, METİN Mİ?
 *
 * ═══ NEDEN GEREKLİ (2026-09-06) ═══
 *
 * Birebir konuşma açma ucunun ARKA UÇ TESTİ VARDI ve YEŞİLDİ
 * (`MesajlasmaUclariTests.BirebirKonusma_IkinciKezAcilmaz`,
 * `KendineKonusma_Acilmaz`). Buna rağmen canlıda 400 alındı.
 *
 * Sebep: o testler ucu C#'tan DOĞRU BİÇİMLİ bir gövdeyle çağırıyor.
 * Kırılan şey uç değildi, ÖN YÜZ ↔ UÇ SÖZLEŞMESİYDİ — ve onu sınayan
 * hiçbir test yoktu. "Üç adımın ikisi yeşilken üçüncüsü canlıda
 * kırılıyor" tam olarak buydu.
 *
 * `api-client-govde-sozlesmesi.test.ts` bu hatanın YAPISAL sebebini
 * tutuyor (çağıran stringify etmesin). Bu test ise SONUCU tutuyor:
 * telden çıkan gövde gerçekten bir nesne mi. Biri kaynağı, diğeri
 * çıktıyı sınıyor; yapısal kural bir gün başka bir yoldan delinirse
 * bu test yine yakalar.
 */

afterEach(() => {
  vi.unstubAllGlobals();
});

/** İsteği yakalayan sahte fetch. */
function fetchYakala(cevap: unknown = {}) {
  const cagrilar: { url: string; init: RequestInit }[] = [];

  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init: RequestInit) => {
      cagrilar.push({ url: String(url), init });

      return {
        ok: true,
        status: 200,
        headers: new Headers({ "content-type": "application/json" }),
        json: async () => cevap,
        text: async () => JSON.stringify(cevap),
      } as unknown as Response;
    })
  );

  return cagrilar;
}

describe("mesajlaşma servisi istek biçimi", () => {
  it("birebirAc gövdeyi NESNE olarak gönderir, metin olarak değil", async () => {
    const cagrilar = fetchYakala({ id: "k1" });

    await messagingService.birebirAc("11111111-2222-3333-4444-555555555555");

    expect(cagrilar.length, "fetch hiç çağrılmadı (POZİTİF KONTROL)").toBe(1);

    const govde = JSON.parse(String(cagrilar[0].init.body));

    // ÇİFT ÇEVRİMDE burası bir METİN olurdu, nesne değil.
    expect(
      typeof govde,
      "Gövde çözüldüğünde nesne olmalı. Metin çıkıyorsa çift " +
        "JSON.stringify yapılmış demektir — sunucu 400 döner."
    ).toBe("object");

    expect(govde.karsiUserId).toBe("11111111-2222-3333-4444-555555555555");
  });

  it("gonder gövdeyi NESNE olarak gönderir", async () => {
    const cagrilar = fetchYakala({ id: "m1" });

    await messagingService.gonder("k1", "merhaba");

    expect(cagrilar.length).toBe(1);

    const govde = JSON.parse(String(cagrilar[0].init.body));
    expect(typeof govde).toBe("object");
    expect(govde.govde).toBe("merhaba");
  });
});
