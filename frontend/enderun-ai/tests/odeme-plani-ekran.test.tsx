import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { canAccessRoute, routePermission } from "@/lib/auth/route-permissions";

/**
 * ÖDEME PLANI EKRANLARI (ÖP/1b).
 *
 * İKİ KAPI AYRI AYRI ÖLÇÜLÜYOR:
 *   1. EKRAN GÖRÜNÜRLÜĞÜ — yol izni (bu dosya).
 *   2. UÇ 403 — sunucu tarafında, backend testlerinde.
 * Biri diğerinin yerine geçmez: arayüzdeki gizleme yalnız kolaylık,
 * gerçek kapı uçta. İkisi tek testte sınansaydı, arayüz kapısı
 * kaldırıldığında uç kapısının hâlâ durduğu görülmezdi.
 */

// ═══════════════════════════════════════════════════════════════
// 1. EKRAN GÖRÜNÜRLÜĞÜ
// ═══════════════════════════════════════════════════════════════

describe("ödeme planı — ekran görünürlüğü", () => {
  it("hazırlama VEYA onaylama izni yeter", () => {
    expect(routePermission("/finans/odeme-planlari")).toEqual([
      "payment.plan.prepare",
      "payment.plan.approve",
    ]);
  });

  it("detay sayfası da aynı kurala düşer", () => {
    expect(routePermission("/finans/odeme-planlari/abc-123")).toEqual([
      "payment.plan.prepare",
      "payment.plan.approve",
    ]);
  });

  /**
   * ONAYLAYAN, HAZIRLAMA İZNİ OLMADAN DA EKRANI AÇABİLMELİ.
   *
   * Tek anahtara bağlanırsa planı onaylayacak kişi kendi onay
   * ekranını açamaz.
   */
  it("yalnız onay izni olan ekranı açabilir", () => {
    expect(
      canAccessRoute("/finans/odeme-planlari", ["payment.plan.approve"], false),
    ).toBe(true);
  });

  it("yalnız hazırlama izni olan ekranı açabilir", () => {
    expect(
      canAccessRoute("/finans/odeme-planlari", ["payment.plan.prepare"], false),
    ).toBe(true);
  });

  /**
   * FİNANS MODÜLÜNÜ GÖREN HERKESE AÇILMAZ.
   *
   * Genel "/finans" kuralı finance.view istiyor. Ödeme planı kuralı
   * ondan SONRA kalsaydı bu test kırmızıya dönerdi — haftanın kime ne
   * ödeneceği finans modülünü açabilen herkesin önüne düşerdi.
   */
  it("yalnız finance.view olan ekranı AÇAMAZ", () => {
    expect(canAccessRoute("/finans/odeme-planlari", ["finance.view"], false)).toBe(
      false,
    );
  });
});

// ═══════════════════════════════════════════════════════════════
// 2. EKRANIN DAVRANIŞI — K9 ve K6
// ═══════════════════════════════════════════════════════════════

const PLAN_ID = "11111111-1111-1111-1111-111111111111";
const HESAP_ID = "22222222-2222-2222-2222-222222222222";
const SATIR_ID = "33333333-3333-3333-3333-333333333333";

/** Onaydaki plan; bir satırı karar bekliyor. */
const PLAN = {
  id: PLAN_ID,
  haftaBaslangici: "2027-03-01T00:00:00Z",
  odemeGunu: "2027-03-05T00:00:00Z",
  durum: 1, // Onayda
  hazirlayanUserId: null,
  onaylayanUserId: null,
  satirlar: [
    {
      id: SATIR_ID,
      currentAccountId: "44444444-4444-4444-4444-444444444444",
      cariUnvan: "Yılmaz İnşaat",
      onerilenTutar: 40000,
      yontem: 0,
      cekVadesi: null,
      oncelik: 1,
      cashAccountId: HESAP_ID,
      aciklama: null,
      karar: 0,
      onaylananTutar: null,
      odemeDurumu: 0,
      odenenTutar: 0,
      devirHaftaSayisi: 0,
      onaydanSonraDegisti: false,
      degisenAlanlar: [],
      kapanisSebebi: null,
      kapanisAciklamasi: null,
    },
  ],
  gecenHaftaninPlanDisi: [],
  butce: bosButce(),
};

/**
 * K6 — İKİ SAYI BİLEREK FARKLI VE TOPLAMLARI AYIRT EDİLEBİLİR.
 *
 * Nakit 40.000, çek 25.000. Toplamları 65.000 — ekranın HİÇBİR
 * yerinde görünmemeli. Değerler birbirine eşit seçilseydi, toplayan
 * bir hata "iki sayıdan biri" gibi görünüp testten kaçardı.
 */
function bosButce() {
  return { hesapBazindaNakit: [], gelecekYukumlulukler: [] };
}

function butce(bakiye: number) {
  return {
    hesapBazindaNakit: [
      {
        cashAccountId: HESAP_ID,
        nakitCikis: 40000,
        gosterilenBakiye: bakiye,
        fark: bakiye - 40000,
        bakiyeKaynagi: 0,
      },
    ],
    gelecekYukumlulukler: [{ yil: 2027, ay: 6, tutar: 25000 }],
  };
}

let izinler: string[] = [];
let bakiyeDegeri = 100000;

vi.mock("@/lib/api/api-client", () => ({
  ApiError: class ApiError extends Error {},
  apiClient: vi.fn(async (path: string) => {
    if (path.startsWith(`odeme-planlari/${PLAN_ID}/butce`)) return butce(bakiyeDegeri);
    if (path.startsWith(`odeme-planlari/${PLAN_ID}`)) {
      return { ...PLAN, butce: butce(bakiyeDegeri) };
    }

    /*
     * KABUK DA `auth/me` ÇAĞIRIYOR — TAKLİT GERÇEK ŞEKLİ DÖNMELİ.
     *
     * Önce her bilinmeyen yol `[]` dönüyordu ve kabuk oturumu bir DİZİ
     * sanıp çöküyordu. Test o zaman ekranı değil, taklidin ürettiği
     * bir kazayı ölçüyordu.
     */
    if (path === "auth/me") {
      return {
        id: "u1",
        username: "test",
        fullName: "Test Kullanıcı",
        roles: ["Ön Muhasebe"],
        permissions: izinler,
        hasAllPermissions: false,
      };
    }

    return [];
  }),
}));

vi.mock("@/lib/use-current-user", () => ({
  useCurrentUser: () => ({
    user: {
      id: "u1",
      username: "test",
      fullName: "Test Kullanıcı",
      roles: ["Ön Muhasebe"],
      permissions: izinler,
      hasAllPermissions: false,
    },
    loading: false,
  }),
  clearCurrentUserCache: () => {},
}));

vi.mock("next/navigation", () => ({
  useParams: () => ({ id: PLAN_ID }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
  usePathname: () => `/finans/odeme-planlari/${PLAN_ID}`,
  useSearchParams: () => new URLSearchParams(),
}));

async function ekraniAc() {
  const { default: Sayfa } = await import(
    "@/app/finans/odeme-planlari/[id]/page"
  );
  return render(<Sayfa />);
}

beforeEach(() => {
  izinler = ["payment.plan.prepare", "payment.plan.approve"];
  bakiyeDegeri = 100000;
});

describe("ödeme planı detayı — K9 yetmezlik uyarısı", () => {
  /**
   * K9 — BAKİYE YETMİYORSA EKRAN AÇIKÇA SÖYLER.
   *
   * Uyarı ENGELLEMİYOR: onay düğmesi yerinde kalıyor. GM yine
   * onaylayabilir ama görmeden onaylamış olmaz.
   */
  it("fark eksiyse uyarı görünür ve onay düğmesi kalır", async () => {
    bakiyeDegeri = 30000; // 40.000 çıkacak → 10.000 açık

    await ekraniAc();

    await waitFor(() =>
      expect(screen.getByText(/Bakiye yetmiyor/i)).toBeInTheDocument(),
    );

    expect(screen.getAllByText(/10\.000,00/).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Onayla" })).toBeInTheDocument();
  });

  it("bakiye yetiyorsa uyarı YOK", async () => {
    bakiyeDegeri = 100000;

    await ekraniAc();

    await waitFor(() =>
      expect(screen.getByText(/Ödeme Satırları/)).toBeInTheDocument(),
    );

    expect(screen.queryByText(/Bakiye yetmiyor/i)).not.toBeInTheDocument();
  });
});

describe("ödeme planı detayı — K6 iki ayrı sayı", () => {
  /**
   * NAKİT ÇIKIŞI VE GELECEK YÜKÜMLÜLÜK AYRI GÖSTERİLİR, TOPLANMAZ.
   *
   * Çek bu hafta para çıkarmıyor ama hafta bittiğinde borç duruyor.
   * Toplanırsa hafta olduğundan pahalı görünür ve gerçek nakit
   * ihtiyacı bu şişkinliğin içinde kaybolur.
   */
  it("iki sayı da ayrı ayrı görünür", async () => {
    await ekraniAc();

    await waitFor(() =>
      expect(screen.getByText(/Bu Cuma Çıkacak Nakit/)).toBeInTheDocument(),
    );

    expect(screen.getByText(/Bu Hafta Yaratılan Gelecek Yükümlülük/))
      .toBeInTheDocument();
    expect(screen.getAllByText(/40\.000,00/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/25\.000,00/).length).toBeGreaterThan(0);
    expect(screen.getByText(/Haziran 2027/)).toBeInTheDocument();
  });

  it("iki sayının TOPLAMI hiçbir yerde görünmez", async () => {
    await ekraniAc();

    await waitFor(() =>
      expect(screen.getByText(/Bu Cuma Çıkacak Nakit/)).toBeInTheDocument(),
    );

    // 40.000 + 25.000 = 65.000 — böyle bir sayı ekranda OLMAMALI.
    expect(screen.queryByText(/65\.000,00/)).not.toBeInTheDocument();
  });
});

describe("ödeme planı detayı — onay düğmeleri izne bağlı", () => {
  /**
   * ÖN MUHASEBE EKRANI AÇAR AMA ONAYLAYAMAZ.
   *
   * Hazırlama izni ekranı açmaya yeter; karar düğmeleri
   * payment.plan.approve olmadan görünmez. Bu ARAYÜZ kapısı —
   * uçtaki 403 backend testinde ayrıca sınanıyor.
   */
  it("yalnız hazırlama izniyle karar düğmeleri görünmez", async () => {
    izinler = ["payment.plan.prepare"];

    await ekraniAc();

    await waitFor(() =>
      expect(screen.getByText(/Ödeme Satırları/)).toBeInTheDocument(),
    );

    expect(screen.queryByRole("button", { name: "Onayla" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reddet" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Kısmi Onayla" }))
      .not.toBeInTheDocument();
  });

  it("onay izniyle karar düğmeleri görünür", async () => {
    izinler = ["payment.plan.prepare", "payment.plan.approve"];

    await ekraniAc();

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Onayla" })).toBeInTheDocument(),
    );

    expect(screen.getByRole("button", { name: "Reddet" })).toBeInTheDocument();
  });
});
