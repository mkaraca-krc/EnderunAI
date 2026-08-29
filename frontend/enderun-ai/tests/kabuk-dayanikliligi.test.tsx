import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/**
 * KABUK ÇÖKERSE BEYAZ EKRAN GELMEZ.
 *
 * BU DOSYA GERÇEK `ErpShell`i RENDER EDİYOR — hata sınırını yalıtılmış
 * olarak sınamak yetmez. Sınır doğru çalışıp kabuğa YANLIŞ bağlanmış
 * olabilirdi: yalnız `{children}` sarılsaydı kabuğun KENDİ çöküşü
 * yakalanmaz, kullanıcı yine boş ekran görürdü. Ölçülen şey bağlantı.
 *
 * ÇÖKÜŞ KABUĞUN İÇİNDEN GELİYOR: `NotificationBell` taklidi
 * fırlatıyor. Bu bileşen kabuğun kendi JSX'inde, `{children}`ın
 * DIŞINDA — yani yalnız dış katman yakalayabilir.
 */

const konsol = vi.spyOn(console, "error");

let bildirilenler: unknown[] = [];

vi.mock("@/components/notifications/notification-bell", () => ({
  default: () => {
    throw new TypeError("Cannot read properties of undefined (reading '0')");
  },
}));

vi.mock("@/services/istemci-hatasi.service", () => ({
  istemciHatasiBildir: (bilgi: unknown) => {
    bildirilenler.push(bilgi);
  },
}));

vi.mock("@/lib/api/api-client", () => ({
  ApiError: class ApiError extends Error {},
  apiClient: vi.fn(async (path: string) => {
    if (path === "auth/me") {
      return {
        id: "u1",
        username: "test",
        fullName: "Test Kullanıcı",
        roles: ["Ön Muhasebe"],
        permissions: ["finance.view"],
        hasAllPermissions: false,
      };
    }
    return [];
  }),
}));

vi.mock("next/navigation", () => ({
  usePathname: () => "/finans",
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
  useParams: () => ({}),
  useSearchParams: () => new URLSearchParams(),
}));

beforeEach(() => {
  konsol.mockImplementation(() => {});
  bildirilenler = [];
});

describe("kabuk dayanıklılığı", () => {
  it("kabuğun kendi çöküşü hata ekranına düşer, ekran BOŞ KALMAZ", async () => {
    const { default: ErpShell } = await import("@/components/erp/erp-shell");

    const { container } = render(
      <ErpShell design="redwood" title="Finans" description="">
        <p>Sayfa içeriği</p>
      </ErpShell>,
    );

    await waitFor(() =>
      expect(screen.getByText(/Bir şeyler ters gitti/)).toBeInTheDocument(),
    );

    /*
     * ASIL İDDİA. Sınır kabuğa bağlanmasaydı React ağacı kökünden
     * söker, `container` boş kalırdı — canlıda görülen beyaz ekran.
     */
    expect(container.textContent?.trim().length ?? 0).toBeGreaterThan(0);
    expect(screen.getByRole("alert")).toBeInTheDocument();
  });

  it("çöküş kayda bildirilir", async () => {
    const { default: ErpShell } = await import("@/components/erp/erp-shell");

    render(
      <ErpShell design="redwood" title="Finans" description="">
        <p>Sayfa içeriği</p>
      </ErpShell>,
    );

    await waitFor(() => expect(bildirilenler.length).toBeGreaterThan(0));

    const bilgi = bildirilenler[0] as Record<string, unknown>;

    expect(bilgi.nerede).toBe("kabuk");
    expect(bilgi.hataAdi).toBe("TypeError");

    /*
     * KİŞİSEL VERİ GİTMİYOR: taklit oturum "Test Kullanıcı" adını
     * taşıyor ve bildirimin hiçbir alanında görünmemeli.
     */
    expect(JSON.stringify(bilgi)).not.toContain("Test Kullanıcı");
    expect(JSON.stringify(bilgi)).not.toContain("test");
  });
});
