import path from "node:path";

import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

/**
 * Frontend test harness'ı.
 *
 * NEDEN VAR: bugüne kadar tek güvence `tsc --noEmit`, eslint ve
 * `next build` idi — hiçbiri DAVRANIŞ sınamıyor. Modal standardı
 * onlarca ekrana yayılmadan önce Esc, odak tuzağı ve gerekçe
 * zorunluluğu gibi kuralların otomatik doğrulanması gerekiyordu;
 * bileşen dağıldıktan sonra bir regresyon sessizce her ekrana
 * birden yayılırdı.
 *
 * Next.js'in kendi derleyicisi kullanılmıyor; test edilen şey
 * bileşenlerin tarayıcıdaki davranışı, sunucu tarafı render değil.
 * Bu yüzden @vitejs/plugin-react + jsdom yeterli ve çok daha hızlı.
 */
export default defineConfig({
  plugins: [react()],
  resolve: {
    // tsconfig'deki "@/*" takma adının test tarafındaki karşılığı.
    // İkisi ayrışırsa test, uygulamanın gerçekten kullandığı
    // dosyadan başka bir dosyayı sınar.
    alias: {
      "@": path.resolve(__dirname, "./"),
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./tests/setup.ts"],
    include: ["tests/**/*.test.{ts,tsx}"],
    // Test sırasında ekrana düşen React uyarıları gürültü değil,
    // sinyaldir; sessize alınmıyor.
    silent: false,
    restoreMocks: true,
  },
});
