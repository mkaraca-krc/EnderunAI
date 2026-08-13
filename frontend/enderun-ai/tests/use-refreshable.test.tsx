import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { useRefreshable } from "@/lib/data/use-refreshable";

/**
 * TAZELEME KANCASI — arayüzün tek mekanizması.
 *
 * Bugün her ekran kendi load() fonksiyonunu yazıyor; kimi mutasyondan
 * sonra tazeliyor, kimi tazelemiyor. Bu testler kancanın üç sözünü
 * sabitler: mutasyondan sonra tazeler, form doldururken araya girmez,
 * hata anında ekrandaki veriyi silmez.
 */
describe("useRefreshable", () => {
  it("ilk yüklemede veriyi çeker ve zaman damgası bırakır", async () => {
    const fetcher = vi.fn().mockResolvedValue({ total: 5 });

    const { result } = renderHook(() => useRefreshable(fetcher));

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.data).toEqual({ total: 5 });
    expect(result.current.lastUpdatedAt).toBeInstanceOf(Date);
    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  /**
   * ASIL SÖZ: kaydet/sil/onayla bitince liste kendiliğinden tazelenir.
   * Ekranların tek tek "sonra load() çağırmayı unutma" demesi gerekmez.
   */
  it("mutasyondan sonra veriyi tazeler", async () => {
    const fetcher = vi.fn().mockResolvedValue({ total: 1 });

    const { result } = renderHook(() => useRefreshable(fetcher));

    await waitFor(() => expect(result.current.loading).toBe(false));

    await act(async () => {
      await result.current.mutate(async () => "kaydedildi");
    });

    expect(fetcher).toHaveBeenCalledTimes(2);
  });

  /**
   * BAŞARISIZ MUTASYON TAZELEMEZ: hata veren bir kayıttan sonra
   * listeyi yenilemek kullanıcıya "oldu" izlenimi verirdi.
   */
  it("mutasyon hata verirse tazelemez", async () => {
    const fetcher = vi.fn().mockResolvedValue({ total: 1 });

    const { result } = renderHook(() => useRefreshable(fetcher));

    await waitFor(() => expect(result.current.loading).toBe(false));

    await expect(
      act(async () => {
        await result.current.mutate(async () => {
          throw new Error("kaydedilemedi");
        });
      }),
    ).rejects.toThrow("kaydedilemedi");

    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  /**
   * FORM KORUMASI: kullanıcı yazarken sessiz tazeleme araya girmez ve
   * girdiyi sıfırlamaz.
   */
  it("meşgulken sessiz tazeleme atlanır, elle yenileme geçer", async () => {
    vi.useFakeTimers();

    const fetcher = vi.fn().mockResolvedValue({ total: 1 });

    const { result } = renderHook(() =>
      useRefreshable(fetcher, { intervalMs: 1000 }),
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(fetcher).toHaveBeenCalledTimes(1);

    act(() => result.current.setBusy(true));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });

    // Üç tur geçti ama form açık: veri tazelenmedi.
    expect(fetcher).toHaveBeenCalledTimes(1);

    // Elle yenileme NİYET BEYANIDIR, meşgulken de çalışır.
    await act(async () => {
      await result.current.refresh();
    });

    expect(fetcher).toHaveBeenCalledTimes(2);

    vi.useRealTimers();
  });

  it("aralık verilmezse periyodik tazeleme yapmaz", async () => {
    vi.useFakeTimers();

    const fetcher = vi.fn().mockResolvedValue({ total: 1 });

    renderHook(() => useRefreshable(fetcher));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(60_000);
    });

    expect(fetcher).toHaveBeenCalledTimes(1);

    vi.useRealTimers();
  });

  /**
   * HATA ANINDA ESKİ VERİ EKRANDA KALIR: kullanıcının baktığı rakamı
   * sebepsiz yok etmek, tabloyu boşaltmak olurdu.
   */
  it("hata sonrası önceki veri korunur", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce({ total: 7 })
      .mockRejectedValueOnce(new Error("ağ hatası"));

    const { result } = renderHook(() => useRefreshable(fetcher));

    await waitFor(() => expect(result.current.loading).toBe(false));

    await act(async () => {
      await result.current.refresh();
    });

    expect(result.current.error).toBe("ağ hatası");
    expect(result.current.data).toEqual({ total: 7 });
  });

  it("enabled false ise hiç yüklemez", async () => {
    const fetcher = vi.fn().mockResolvedValue({ total: 1 });

    const { result } = renderHook(() =>
      useRefreshable(fetcher, { enabled: false }),
    );

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(fetcher).not.toHaveBeenCalled();
  });
});
