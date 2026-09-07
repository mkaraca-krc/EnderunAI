import { defineConfig } from "@playwright/test";

/**
 * DÜZEN TESTLERİ — GERÇEK TARAYICI.
 *
 * Vitest/jsdom bu soruları CEVAPLAYAMAZ: jsdom'da düzen motoru yok,
 * `getBoundingClientRect` her zaman sıfır döner. "Composer görünen
 * alanın içinde mi" ancak gerçek bir tarayıcıda ölçülür.
 *
 * YIĞINI BU DOSYA AYAĞA KALDIRMIYOR: `deploy/scripts/duzen-testi.sh`
 * kaldırıyor (ayrı arka uç + ayrı ön yüz + tohumlanmış konuşma).
 * `webServer` kullanılmadı çünkü gereken şey tek bir sunucu değil,
 * enderun_ai_test'e bağlı İKİ süreç.
 */
export default defineConfig({
  testDir: "./tests/duzen",
  // TEK İŞÇİ: iki koşu aynı tohumlanmış konuşmayı paylaşıyor.
  workers: 1,
  // YENİDEN DENEME YOK: düzen ölçümü kararlıdır; "ikinci denemede
  // geçti" burada bir bilgi değil, gürültüdür.
  retries: 0,
  reporter: [["list"]],
  use: {
    baseURL: process.env.DUZEN_URL ?? "http://127.0.0.1:3001",
    // Ekran görüntüsü/iz yalnız düşünce: düzen hatası GÖRÜLEREK
    // anlaşılır, metinden değil.
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
});
