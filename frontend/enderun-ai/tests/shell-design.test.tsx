import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

/*
 * Kabuk gerçek uçlara gidiyor; testin konusu ağ değil, tasarım
 * bayrağının DOM'a doğru basılması. Yan bileşenler (Hızır balonu,
 * bildirim zili, mesai gözcüsü) kendi isteklerini attıkları için
 * susturuluyor.
 */
vi.mock("next/navigation", () => ({
  usePathname: () => "/cariler",
  // Komut paleti kabuğun içinde duruyor ve yönlendiriciyi istiyor.
  useRouter: () => ({ push: vi.fn() }),
}));

/*
 * vi.fn() DEĞİL düz işlev: yapılandırmada restoreMocks açık ve
 * vi.fn()'in gövdesi her testten önce sıfırlanıyor; çağrı undefined
 * döndürüp kabuk `.then` çağırdığında patlıyordu.
 */
vi.mock("@/lib/api/api-client", () => ({
  apiClient: () => Promise.reject(new Error("test ortamı: istek yok")),
}));

vi.mock("@/components/hizir/hizir-bubble", () => ({
  default: () => null,
}));

vi.mock("@/components/notifications/notification-bell", () => ({
  default: () => null,
}));

vi.mock("@/components/work-hour-session-watcher", () => ({
  default: () => null,
}));

vi.mock("@/components/logout-button", () => ({
  LogoutButton: () => null,
}));

const { default: ErpShell } = await import("@/components/erp/erp-shell");

/**
 * TASARIM DİLİ OPT-IN.
 *
 * Bayrak olmadan `.rw` basılsaydı A1 tokenları tek hamlede 175
 * sayfaya inerdi; hiçbiri tek tek gözden geçirilmemiş olurdu. Bu test
 * kapsamın yanlışlıkla genişlemesini yakalar.
 */
describe("ErpShell tasarım bayrağı", () => {
  it("varsayılan klasik: .rw basılmaz", async () => {
    render(
      <ErpShell title="Cari Kartlar">
        <p>içerik</p>
      </ErpShell>,
    );

    await waitFor(() => expect(screen.getByText("içerik")).toBeInTheDocument());

    const main = document.querySelector("main.erp-main");

    expect(main).not.toBeNull();
    expect(main!.classList.contains("rw")).toBe(false);
  });

  it("design=\"redwood\" verildiğinde .rw basılır", async () => {
    render(
      <ErpShell title="Cari Kartlar" design="redwood">
        <p>içerik</p>
      </ErpShell>,
    );

    await waitFor(() => expect(screen.getByText("içerik")).toBeInTheDocument());

    const main = document.querySelector("main.erp-main");

    expect(main!.classList.contains("rw")).toBe(true);
  });
});
