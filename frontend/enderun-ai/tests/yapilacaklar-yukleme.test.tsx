import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/**
 * "SESSİZ YÜKLENİYOR" — YENİDEN ÜRETME.
 *
 * BELİRTİ: /yapilacaklar ekranı "Yükleniyor…" durumunda takılı
 * kalıyor; liste gelmiyor, HATA DA GÖSTERİLMİYOR.
 *
 * Bu dosya önce hatayı ÜRETİR, sonra düzeltme onu yeşile çevirir.
 * Üç şüpheli var ve testler onları AYIRIYOR — tahmin edilmiyor:
 *   1. `useModuleActions` her render'da yeni NESNE döndürüyor
 *   2. `usePermissions` de yeni nesne döndürüyor
 *   3. Erken çıkışta (`user?.id` yok) yükleme durumu sıfırlanmıyor
 * Ayrıca dördüncü bir kusur var: saha raporu servis yolu yanlış
 * (404). Onun tek başına kilitleyip kilitlemediği de sınanıyor.
 *
 * `apiClient` ve `useCurrentUser` taklit ediliyor; `usePermissions`
 * ve `useModuleActions` GERÇEK — kusur onlarda, taklit edilirse
 * test hiçbir şey ölçmez.
 */

const apiCagrilari: string[] = [];

/** Saha raporu ucu canlıda 404 dönüyor — taklit de öyle davranır. */
const YOK_UCLARI = ["project-sites/daily-reports/pending-approval"];

vi.mock("@/lib/api/api-client", () => ({
  ApiError: class ApiError extends Error {},
  apiClient: vi.fn(async (path: string) => {
    apiCagrilari.push(path);

    /*
     * GERÇEK AĞ GECİKMESİ TAKLİT EDİLİYOR.
     *
     * Anında dönen taklitle test YANILTIYORDU: her turun
     * `setYukleniyor(false)` çağrısı bir sonraki turun
     * `setYukleniyor(true)` çağrısından önce yetişiyor ve ekran
     * "yükleme bitti" anları gösteriyordu. Canlıda uçlar 30-250 ms
     * sürüyor (ölçüldü) — o gecikmede her yeni tur, öncekinin
     * kapanışından ÖNCE yükleme durumunu yeniden açıyor ve yazı
     * hiç kaybolmuyor. Kusurun görünürlüğü zamanlamaya bağlı;
     * gecikmesiz test onu gizliyordu.
     */
    await new Promise((r) => setTimeout(r, 40));

    if (YOK_UCLARI.some((u) => path.startsWith(u))) {
      throw new Error("İşlem başarısız: 404");
    }

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

const kullaniciDurumu = { user: SAHTE_KULLANICI as unknown, loading: false };

vi.mock("@/lib/use-current-user", () => ({
  useCurrentUser: () => kullaniciDurumu,
  clearCurrentUserCache: () => {},
}));

async function ekraniAc() {
  const { default: Sayfa } = await import("@/app/yapilacaklar/page");
  return render(<Sayfa />);
}

beforeEach(() => {
  apiCagrilari.length = 0;
  kullaniciDurumu.user = SAHTE_KULLANICI;
  kullaniciDurumu.loading = false;
  vi.resetModules();
});

describe("yapılacaklar — yükleme durumundan çıkış", () => {
  /**
   * ASIL BELİRTİ — VE ÖLÇÜMÜ DÜZELTİLDİ.
   *
   * İlk sürüm yalnız `waitFor` ile "bir an için kayboluyor mu" diye
   * sordu ve GEÇTİ — ekran bozukken bile. Sebep: kararsız referans
   * her render'da yeni bir tur başlatıyor, tur `setYukleniyor(true)`
   * ile açılıp `false` ile kapanıyor ve DOM saniyede yüzlerce kez
   * gidip geliyor. `waitFor` DOM'u doğrudan yokluyor, o mikro
   * pencereleri yakalıyor; TARAYICI ise 60 fps'te boyuyor ve
   * 16 ms'den kısa pencereyi İNSANA HİÇ GÖSTERMİYOR.
   *
   * Kullanıcının gördüğü "sürekli Yükleniyor…" ile testin gördüğü
   * "bir an kayboldu" aynı DOM'un iki farklı okuması. İnsana uyan
   * iddia şu: yazı kaybolmalı VE KAYBOLMUŞ KALMALI.
   */
  it("yükleme durumundan çıkıyor ve çıkmış kalıyor", async () => {
    await ekraniAc();

    await waitFor(
      () => {
        expect(screen.queryByText("Yükleniyor…")).toBeNull();
      },
      { timeout: 3000 }
    );

    // Sürekli yokluk: 400 ms boyunca 20 kez örnekleniyor.
    for (let i = 0; i < 20; i++) {
      await new Promise((r) => setTimeout(r, 20));

      expect(
        screen.queryByText("Yükleniyor…"),
        `Yükleme yazısı ${i * 20} ms sonra GERİ GELDİ. Ekran yükleme ` +
          "durumuna yeniden giriyor — kullanıcı bunu kesintisiz " +
          '"Yükleniyor…" olarak görür.'
      ).toBeNull();
    }
  });

  /**
   * KÖK NEDENİ AYIRAN TEST.
   *
   * Kararsız referans efekti her render'da yeniden tetikliyorsa
   * istek sayısı sınırsız büyür. Beş kaynak + iki görev sorgusu =
   * bir turda en fazla 7 çağrı; 30'u aşması döngü demektir.
   */
  it("istekler bir kez atılıyor, döngüye girmiyor", async () => {
    await ekraniAc();

    await new Promise((r) => setTimeout(r, 1500));

    expect(
      apiCagrilari.length,
      `İstek sayısı ${apiCagrilari.length}. Bir tur en fazla 7 çağrı ` +
        "yapmalı; bunu aşması efektin her render'da yeniden " +
        "tetiklendiği (kararsız referans) anlamına gelir.\n" +
        `Çağrılar: ${apiCagrilari.slice(0, 15).join(", ")}…`
    ).toBeLessThan(30);
  });

  /**
   * DÖRDÜNCÜ KUSURU TEK BAŞINA SINAR.
   *
   * Saha raporu ucu 404 dönüyor. Bu TEK BAŞINA ekranı kilitliyor mu,
   * yoksa kaynak-başına hata yalıtımı onu tutuyor mu? Kilitlemiyorsa
   * yanlış yol ayrı bir kusurdur ve kök neden DEĞİLDİR.
   */
  it("404 dönen tek kaynak ekranı kilitlemiyor", async () => {
    await ekraniAc();

    await waitFor(
      () => {
        expect(screen.queryByText("Yükleniyor…")).toBeNull();
      },
      { timeout: 3000 }
    );

    // Hata yalıtımı çalışıyorsa 404 veren kaynak çağrılmış olmalı.
    expect(
      apiCagrilari.some((p) => p.startsWith("project-sites/daily-reports")),
      "Saha raporu ucu hiç çağrılmadı — testin kurgusu yanlış."
    ).toBe(true);
  });

  /**
   * ÜÇÜNCÜ KUSUR: KİMLİK YOKSA YÜKLEME AÇIK KALIYOR MU.
   *
   * `user.id` gelmezse `yukle()` erken dönüyor ve `setYukleniyor(false)`
   * hiç çalışmıyor; başlangıç değeri `true` olduğu için ekran
   * sonsuza kadar "Yükleniyor…" der. Bugünkü asıl sebep bu olmasa da
   * `auth/me` bir gün düşerse aynı belirtiyi verir — ve o hata
   * `use-current-user.ts` içinde YUTULUYOR.
   */
  it("kullanıcı kimliği yoksa da yükleme durumundan çıkıyor", async () => {
    kullaniciDurumu.user = null;

    await ekraniAc();

    await waitFor(
      () => {
        expect(screen.queryByText("Yükleniyor…")).toBeNull();
      },
      { timeout: 3000 }
    );
  });
});
