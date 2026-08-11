import "@testing-library/jest-dom/vitest";

import { cleanup } from "@testing-library/react";
import { afterEach, expect, vi } from "vitest";

// Her testten sonra DOM temizleniyor: kalan bir modal, sonraki
// testin odak/görünürlük iddialarını sessizce bozar.
afterEach(() => {
  cleanup();
});

/**
 * jsdom'da olmayan tarayıcı yetenekleri.
 *
 * Modal gövde kaydırmayı kilitliyor ve odak yönetimi yapıyor;
 * bunlar jsdom'da kısmen var. Eksik olanları burada tamamlıyoruz ki
 * test, bileşenin gerçek davranışını sınasın — bileşeni jsdom'a
 * göre değiştirmek yerine.
 */
if (!window.matchMedia) {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }));
}

/**
 * jsdom DÜZEN HESAPLAMIYOR: `offsetParent` her zaman null döner.
 *
 * Modal'ın odak tuzağı görünür elemanları `offsetParent !== null`
 * ile süzüyor; bu yama olmadan tuzak hiçbir eleman bulamaz ve test,
 * bileşen doğru çalıştığı halde düşer.
 *
 * Bileşeni test ortamına göre değiştirmek yerine ORTAMIN eksiği
 * tamamlanıyor: gerçek tarayıcıda DOM'a bağlı ve gizlenmemiş bir
 * eleman zaten offsetParent taşır.
 */
Object.defineProperty(HTMLElement.prototype, "offsetParent", {
  configurable: true,
  get(this: HTMLElement) {
    return this.isConnected && this.style.display !== "none"
      ? this.parentElement
      : null;
  },
});

// expect zincirine dokunulmuyor; import'un kullanıldığından emin
// olmak için burada duruyor.
void expect;
