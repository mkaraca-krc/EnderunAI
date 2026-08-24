import { renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

/**
 * KANCA REFERANS KARARLILIĞI — ÜÇ KUSURU AYIRIR.
 *
 * `/yapilacaklar` ekranı "Yükleniyor…" durumunda kilitleniyordu.
 * Üç şüpheli vardı; bu dosya hangisinin sorumlu olduğunu TAHMİN
 * ETMEDEN ayırıyor:
 *
 *   1. `useModuleActions` her render'da yeni NESNE döndürüyor
 *   2. `usePermissions` de yeni nesne döndürüyor
 *   3. Erken çıkışta yükleme sıfırlanmıyor (ayrı dosyada sınanıyor)
 *
 * Ekranın `useCallback` bağımlılık dizisinde NESNENİN KENDİSİ var
 * (`taskActions`), alanları değil. Nesne her render'da yeniyse
 * `useCallback` her render'da yeni bir fonksiyon üretir, efekt
 * yeniden tetiklenir ve döngü başlar.
 */

const SAHTE_KULLANICI = {
  id: "11111111-1111-1111-1111-111111111111",
  username: "test",
  fullName: "Test",
  roles: ["Admin"],
  permissions: ["tasks.view"],
  hasAllPermissions: false,
};

vi.mock("@/lib/use-current-user", () => ({
  useCurrentUser: () => ({ user: SAHTE_KULLANICI, loading: false }),
  clearCurrentUserCache: () => {},
}));

describe("useModuleActions referans kararlılığı", () => {
  it("aynı girdide aynı nesneyi döndürüyor", async () => {
    const { useModuleActions } = await import("@/lib/auth/module-actions");

    const { result, rerender } = renderHook(() => useModuleActions("tasks"));

    const ilk = result.current;
    rerender();
    const ikinci = result.current;

    expect(
      ikinci,
      "useModuleActions her render'da YENİ nesne döndürüyor. Bu nesne " +
        "bir ekranın useCallback bağımlılık dizisine girdiğinde, o " +
        "callback her render'da yenilenir, ona bağlı efekt her " +
        "render'da tetiklenir ve sonsuz istek döngüsü doğar " +
        "(/yapilacaklar, 2026-08-24)."
    ).toBe(ilk);
  });

  it("can işlevi de kararlı", async () => {
    const { useModuleActions } = await import("@/lib/auth/module-actions");

    const { result, rerender } = renderHook(() => useModuleActions("tasks"));

    const ilk = result.current.can;
    rerender();

    expect(result.current.can).toBe(ilk);
  });
});

describe("usePermissions referans kararlılığı", () => {
  /**
   * İKİNCİ ŞÜPHELİYİ AYIRIR.
   *
   * `usePermissions` de yeni nesne döndürüyor. Ama `useModuleActions`
   * onun ALANLARINI (`has`, `loading`) tüketiyor, nesnesini değil —
   * yani kararsızlık YUKARI TAŞINMIYOR. Bu test o iddiayı kanıtlar:
   * `has` kararlıysa ikinci kusur ekranı kilitleyemez.
   */
  it("has işlevi render'lar arasında kararlı", async () => {
    const { usePermissions } = await import("@/lib/use-permissions");

    const { result, rerender } = renderHook(() => usePermissions());

    const ilk = result.current.has;
    rerender();

    expect(
      result.current.has,
      "`has` kararsız olsaydı kusur useModuleActions'a da taşınırdı."
    ).toBe(ilk);
  });

  it("nesnenin kendisi kararlı", async () => {
    const { usePermissions } = await import("@/lib/use-permissions");

    const { result, rerender } = renderHook(() => usePermissions());

    const ilk = result.current;
    rerender();

    expect(result.current).toBe(ilk);
  });
});
