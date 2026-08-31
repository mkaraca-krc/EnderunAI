/* eslint-disable */
import { renderToString } from "react-dom/server";
import { hydrateRoot } from "react-dom/client";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/*
 * HİDRASYON DENEYİ — TARAYICISIZ ÜRETİM.
 *
 * React'in geliştirme derlemesi uyuşmazlığı AÇIK METİN olarak yazar;
 * canlıdaki küçültülmüş derleme yalnız "#418" der. Burada sunucu
 * çıktısını üretip aynı ağacı istemcide hidrate ediyoruz ve
 * console.error'ı yakalıyoruz.
 */

const apiCagrilari: string[] = [];

vi.mock("@/lib/api/api-client", () => ({
  ApiError: class ApiError extends Error {},
  apiClient: vi.fn(async (path: string) => {
    apiCagrilari.push(path);
    if (path.startsWith("tasks/dashboard")) {
      return { totalOpen: 0, assignedToMe: 0, dueToday: 0, overdue: 0, critical: 0, completedToday: 0 };
    }
    if (path.startsWith("tasks")) return { items: [], hasMore: false, nextCursor: null };
    if (path.startsWith("companies")) return [];
    if (path.startsWith("projects")) return [];
    return { items: [] };
  }),
}));

const SAHTE_KULLANICI = {
  id: "11111111-1111-1111-1111-111111111111",
  username: "test",
  fullName: "Test Kullanıcı",
  roles: ["Admin"],
  permissions: [],
  hasAllPermissions: true,
};

vi.mock("@/lib/use-current-user", () => ({
  useCurrentUser: () => ({ user: SAHTE_KULLANICI, loading: false }),
  clearCurrentUserCache: () => {},
}));

vi.mock("next/navigation", () => ({
  usePathname: () => "/gorevler",
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
  useParams: () => ({}),
  useSearchParams: () => new URLSearchParams(),
}));

let kurtarilan: string[] = [];
let hatalar: string[] = [];
let uyarilar: string[] = [];

beforeEach(() => {
  kurtarilan = [];
  hatalar = [];
  uyarilar = [];
  vi.spyOn(console, "error").mockImplementation((...a: unknown[]) => {
    hatalar.push(a.map(String).join(" "));
  });
  vi.spyOn(console, "warn").mockImplementation((...a: unknown[]) => {
    uyarilar.push(a.map(String).join(" "));
  });
});

afterEach(() => vi.restoreAllMocks());

describe("hidrasyon", () => {

  it("POZITIF KONTROL — kasitli uyusmazlik YAKALANIYOR mu", async () => {
    /*
     * İLK DENEMEM BOZUKTU: `typeof window === "undefined"` ile ayırmaya
     * çalıştım ama jsdom ortamında `window` sunucu geçişinde de var —
     * iki taraf aynı dalı çizdi, uyuşmazlık hiç doğmadı. Düzeneğin kör
     * olduğunu sandım; kör olan kontrolün kendisiydi.
     *
     * Bu sürüm iki FARKLI ağaç kullanıyor: uyuşmazlık kesin.
     */
    const sunucuHtml = renderToString(
      <div><p>ortak</p><span>SUNUCU-METNI</span></div>,
    );

    const kap = document.createElement("div");
    kap.innerHTML = sunucuHtml;
    document.body.appendChild(kap);

    await act(async () => {
      hydrateRoot(
        kap,
        <div><p>ortak</p><span>ISTEMCI-METNI</span></div>,
        {
          onRecoverableError: (e: unknown, i: unknown) =>
            kurtarilan.push(
              String((e as Error)?.message ?? e) +
                " | " +
                String((i as { componentStack?: string })?.componentStack ?? ""),
            ),
        },
      );
    });

    const hepsi = [...kurtarilan, ...hatalar, ...uyarilar];
    const hidrasyon = hepsi.filter((x) =>
      /hydrat|did not match|server rendered|Minified React error #(418|423|425)/i.test(x),
    );

    console.log("=== POZITIF KONTROL: yakalanan =", hidrasyon.length, "| toplam kayit =", hepsi.length);
    for (const h of hepsi.slice(0, 3)) console.log("  >>> " + h.slice(0, 900));

    expect(hidrasyon.length, "DENEY KOR — kasitli uyusmazligi bile yakalamiyor").toBeGreaterThan(0);
  });


  it("saat ilerlese bile /gorevler uyusmazlik uretmiyor", async () => {
    /*
     * İLK KOŞUMDA BU TEST YEŞİLDİ VE BENİ YANILTTI: `renderToString` ile
     * `hydrateRoot` aynı saniyede koştuğu için iki taraf AYNI saati
     * yazdı. Canlıda sunucu geçişi DERLEME ANINDA (21:42:21), istemci
     * geçişi kullanıcının açtığı anda oluyor — arada saatler var.
     *
     * Burada saati ileri alarak canlıdaki farkı üretiyoruz.
     */
    const { default: Sayfa } = await import("@/app/gorevler/page");

    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-08-30T21:42:21Z"));
    const sunucuHtml = renderToString(<Sayfa />);

    // Kullanıcı ekranı 20 dakika sonra açıyor.
    vi.setSystemTime(new Date("2026-08-30T22:02:21Z"));

    const kap = document.createElement("div");
    kap.innerHTML = sunucuHtml;
    document.body.appendChild(kap);

    await act(async () => {
      hydrateRoot(kap, <Sayfa />, {
        onRecoverableError: (e: unknown, i: unknown) =>
          kurtarilan.push(
            String((e as Error)?.message ?? e) +
              " | " +
              String((i as { componentStack?: string })?.componentStack ?? ""),
          ),
      });
    });
    vi.useRealTimers();

    const hepsi = [...kurtarilan, ...hatalar, ...uyarilar];
    const hidrasyon = hepsi.filter((x) =>
      /hydrat|did not match|server rendered/i.test(x),
    );

    /*
     * BU İDDİA TERS ÇEVRİLDİ — VE BU, KUSURUN KANITI.
     *
     * Düzeltmeden ÖNCE burada `toBeGreaterThan(0)` yazıyordu ve test
     * YEŞİLDİ: 20 dakikalık saat farkı gerçekten uyuşmazlık üretiyordu.
     * `data-table.tsx` damgayı çizimden çıkarınca aynı test KIRMIZI
     * verdi — çünkü artık uyuşmazlık doğmuyordu.
     *
     * Şimdiki hâli kalıcı muhafız: saat ne kadar ilerlerse ilerlesin
     * ekran hidrasyon uyuşmazlığı üretmemeli.
     */
    expect(
      hidrasyon,
      "SAAT FARKI HİDRASYON UYUŞMAZLIĞI ÜRETTİ:\n" + hidrasyon.join("\n"),
    ).toEqual([]);
  });

  it("/gorevler sunucu ve istemci ciktisi uyusuyor mu", async () => {
    const { default: Sayfa } = await import("@/app/gorevler/page");

    // 1) SUNUCU GEÇİŞİ
    const sunucuHtml = renderToString(<Sayfa />);
    expect(sunucuHtml.length, "sunucu ciktisi bos — deney gecersiz").toBeGreaterThan(200);

    // 2) İSTEMCİ HİDRASYONU — aynı ağaç
    const kap = document.createElement("div");
    kap.innerHTML = sunucuHtml;
    document.body.appendChild(kap);

    await act(async () => {
      hydrateRoot(kap, <Sayfa />, {
        onRecoverableError: (e: unknown, i: unknown) =>
          kurtarilan.push(String((e as Error)?.message ?? e) + " | " + String((i as any)?.componentStack ?? "")),
      });
    });

    const hepsi = [...kurtarilan, ...hatalar, ...uyarilar];
    const hidrasyon = hepsi.filter((x) =>
      /hydrat|did not match|server rendered|Minified React error #(418|423|425)/i.test(x),
    );

    console.log("=== SUNUCU HTML UZUNLUK ===", sunucuHtml.length);
    console.log("=== TOPLAM KONSOL KAYDI ===", hepsi.length);
    for (const h of hidrasyon) console.log("=== HIDRASYON ===\n" + h.slice(0, 3000));
    if (hidrasyon.length === 0) {
      for (const h of hepsi.slice(0, 6)) console.log("--- diger ---\n" + h.slice(0, 600));
    }

    expect(hidrasyon, hidrasyon.join("\n\n")).toEqual([]);
  });
});
