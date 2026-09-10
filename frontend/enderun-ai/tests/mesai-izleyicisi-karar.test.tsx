import { render, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import WorkHourSessionWatcher from "@/components/work-hour-session-watcher";

/**
 * MESAİ/1 — KALICI ÇIKIŞI YALNIZ GERÇEK BİR "MESAİ DIŞI" KARARI ÜRETİR.
 *
 * İzleyici eskiden `!status.isAllowed` gördüğünde çıkış yapıyordu.
 * Bu, çıkışı bir kararın VARLIĞINA değil, bir iznin YOKLUĞUNA
 * bağlıyordu: `isAllowed` taşımayan HER 200 cevabı (HTML sayfası,
 * eski biçimli gövde, beklenmedik bir gövde) çerezi kalıcı olarak
 * siliyordu. Okunamayan bir cevap "mesai dışı" DEĞİLDİR.
 *
 * YALNIZ `fetch` TAKLİT EDİLİYOR: bileşen, `apiClient` ve
 * `cikisIstegi` gerçek. Çıkış, `/api/auth/logout`a giden istek
 * sayılarak ölçülüyor — kullanıcının çerezini silen istek o.
 *
 * AYAKLARIN HEPSİ AYNI BEKLEME ADIMINI KULLANIYOR ve pozitif ayak
 * o adımın sonunda çıkışı GÖRÜYOR. Yani "çıkış yok" iddiası, bekleme
 * çıkışı görmeye yetmediği için geçemez (Kural 48).
 */

type Cevap = () => Promise<Response>;

let durumCagrisi = 0;
let cikislar: string[] = [];

function kur(cevap: Cevap) {
  durumCagrisi = 0;
  cikislar = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (girdi: RequestInfo | URL, secenek?: RequestInit) => {
      const url = String(girdi);
      if (url.includes("auth/work-hours-status")) {
        durumCagrisi += 1;
        return cevap();
      }
      if (url === "/api/auth/logout") {
        cikislar.push(String(secenek?.body ?? ""));
        return new Response(JSON.stringify({ success: true }), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      }
      throw new Error(`Beklenmeyen istek: ${url}`);
    }),
  );
}

const json =
  (govde: unknown, durum = 200): Cevap =>
  () =>
    Promise.resolve(
      new Response(JSON.stringify(govde), {
        status: durum,
        headers: { "content-type": "application/json" },
      }),
    );

/** İzleyicinin ilk yoklaması cevaplandı VE işlendi. */
async function yoklamaIslendi() {
  await waitFor(() => expect(durumCagrisi).toBe(1));
  // Cevabın gövdesi okunup karar verilene kadar birkaç tur.
  for (let i = 0; i < 5; i += 1) {
    await new Promise((r) => setTimeout(r, 10));
  }
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("MESAİ/1 — pozitif kontrol: gerçek mesai dışı çıkış üretir", () => {
  it("karar 'mesai-disi' → çıkış VAR, sebep mesai-izleyicisi", async () => {
    kur(
      json({
        karar: "mesai-disi",
        isAllowed: false,
        isExempt: false,
        windowEndsAtUtc: null,
        minutesRemaining: null,
      }),
    );
    render(<WorkHourSessionWatcher />);
    await yoklamaIslendi();

    expect(cikislar).toHaveLength(1);
    expect(JSON.parse(cikislar[0])).toEqual({ reason: "mesai-izleyicisi" });
  });
});

describe("MESAİ/1 — okuma hatası çıkış üretmez", () => {
  const ayaklar: Array<[string, Cevap]> = [
    [
      "503 belirlenemedi (arka uç kararı okuyamadı)",
      json({ karar: "belirlenemedi", message: "okunamadı" }, 503),
    ],
    ["500 sunucu hatası", json({ message: "boom" }, 500)],
    ["502 vekil: arka uç ulaşılamaz", json({ message: "Arka uç yanıt vermedi." }, 502)],
    [
      "200 ama HTML (JSON olmayan gövde)",
      () =>
        Promise.resolve(
          new Response("<!doctype html><title>Giriş</title>", {
            status: 200,
            headers: { "content-type": "text/html" },
          }),
        ),
    ],
    ["200 ama karar alanı yok (eski/eksik gövde)", json({ isAllowed: false })],
    ["200 ama gövde null", json(null)],
    ["ağ hatası (fetch reddedildi)", () => Promise.reject(new TypeError("Failed to fetch"))],
  ];

  it.each(ayaklar)("%s → çıkış YOK", async (_ad, cevap) => {
    kur(cevap);
    render(<WorkHourSessionWatcher />);
    await yoklamaIslendi();

    expect(cikislar).toHaveLength(0);
  });
});

describe("MESAİ/1 — izinli oturum", () => {
  it("karar 'izinli', 3 dk kaldı → uyarı görünür, çıkış YOK", async () => {
    kur(
      json({
        karar: "izinli",
        isAllowed: true,
        isExempt: false,
        windowEndsAtUtc: "2026-09-10T15:00:00Z",
        minutesRemaining: 3,
      }),
    );
    const { findByRole } = render(<WorkHourSessionWatcher />);
    await yoklamaIslendi();

    expect(await findByRole("alert")).toHaveTextContent("3 dakika");
    expect(cikislar).toHaveLength(0);
  });

  /*
   * İSTEMCİ ÇIKARIMI KARAR DEĞİLDİR: `minutesRemaining <= 0` sunucunun
   * "izinli" dediği bir cevapta istemcinin kendi hesabıydı. Pencere
   * gerçekten kapandıysa sunucu bir sonraki yoklamada "mesai-disi"
   * der; arka uç ara katmanı da o arada her isteği zaten kesiyor.
   */
  it("karar 'izinli' ama 0 dk → çıkış YOK (kararı sunucu verir)", async () => {
    kur(
      json({
        karar: "izinli",
        isAllowed: true,
        isExempt: false,
        windowEndsAtUtc: "2026-09-10T15:00:00Z",
        minutesRemaining: 0,
      }),
    );
    render(<WorkHourSessionWatcher />);
    await yoklamaIslendi();

    expect(cikislar).toHaveLength(0);
  });
});
