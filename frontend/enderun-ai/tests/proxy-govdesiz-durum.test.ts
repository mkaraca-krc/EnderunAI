import { describe, expect, it } from "vitest";

/**
 * GÖVDESİZ DURUM KODLARI PROXY'DEN GEÇEBİLMELİ.
 *
 * ───────────────────────────────────────────────────────────────
 * BU KIRMIZIYA DÖNERSE
 * ───────────────────────────────────────────────────────────────
 *
 * 204 dönen HER uç tarayıcıya 502 olarak ulaşır. Arka uç doğru
 * çalışır, kullanıcı "Backend servisine bağlantı kurulamadı" görür.
 * Arka uçta 11 kontrolcüde 21 uç 204 dönüyor — ödeme planı satır
 * işlemleri, çek durumu, İK kayıtları dahil.
 *
 * ───────────────────────────────────────────────────────────────
 * NEDEN BU TEST VAR
 * ───────────────────────────────────────────────────────────────
 *
 * 2026-08-30'da canlıda yaşandı ve AYLARCA görülmeyebilirdi: proxy
 * her yanıtı `arrayBuffer()` ile okuyup gövde olarak geçiriyordu.
 * Web standardına göre `new Response(gövde, { status: 204 })`
 * FIRLATIR — boş tampon bile geçersiz. Fırlatan yapıcı `catch`e
 * düşüyor, proxy 502 döndürüyordu.
 *
 * EN KÖTÜ TARAFI: ilk kurban `istemci-hatalari` ucuydu, yani HATA
 * BİLDİRİM KANALININ KENDİSİ. Ekranlar çöküyor, bildirmeye
 * çalışıyor, bildirim de 502 alıyordu. Sessizliğin sebebi buydu.
 *
 * BU TEST WEB STANDARDINI SINIYOR, PROXY KODUNU DEĞİL: davranış
 * `Response` yapıcısından geliyor ve kütüphane sürümüyle
 * değişebilir. Standart değişirse test bunu söyler.
 */
describe("proxy — gövdesiz durum kodları", () => {
  const GOVDESIZ = [204, 205, 304];

  /**
   * ÖNCE KUSURU ÜRET: gövdeyle 204 kurmak FIRLATMALI.
   *
   * Bu iddia olmasaydı düzeltmenin neyi çözdüğü belirsiz kalırdı —
   * "gövde vermeyince çalışıyor" ancak "gövde verince çalışmıyor"
   * ile birlikte anlam taşır.
   */
  it.each(GOVDESIZ)("%i durumunda GÖVDE VERİLİRSE fırlatır", (durum) => {
    expect(() => new Response(new ArrayBuffer(0), { status: durum })).toThrow();
  });

  /** Düzeltmenin yaptığı: gövde yerine null. */
  it.each(GOVDESIZ)("%i durumunda null gövdeyle kurulabilir", (durum) => {
    const yanit = new Response(null, { status: durum });

    expect(yanit.status).toBe(durum);
  });

  /**
   * GÖVDELİ DURUMLAR ETKİLENMEMELİ — düzeltme fazla geniş olmasın.
   *
   * Bu iddia olmasaydı, HER yanıtın gövdesini atan bir "düzeltme" de
   * üstteki testleri geçerdi ve uygulama hiçbir veri alamazdı.
   */
  it.each([200, 201, 400, 401, 409, 500])(
    "%i durumunda gövde korunur",
    async (durum) => {
      const govde = JSON.stringify({ mesaj: "veri" });
      const yanit = new Response(govde, { status: durum });

      expect(await yanit.text()).toBe(govde);
    },
  );

  /**
   * PROXY KAYNAĞINDA GÖVDESİZ DURUM AYIRIMI VAR.
   *
   * Davranış testi jsdom'da proxy'yi uçtan uca koşamıyor (Next
   * çalışma anı gerekiyor); bu sözleşme testi ayrımın KODDA
   * durduğunu tutuyor. Kaldırılırsa 21 uç sessizce 502'ye döner.
   */
  it("proxy kaynağı gövdesiz durumları ayırıyor", async () => {
    const { readFileSync } = await import("node:fs");
    const { join } = await import("node:path");

    const kaynak = readFileSync(
      join(__dirname, "..", "app", "api", "backend", "[...path]", "route.ts"),
      "utf8",
    );

    expect(kaynak).toContain("204");
    expect(kaynak).toMatch(/govdesizDurumlar|gövdesiz/i);
  });
});
