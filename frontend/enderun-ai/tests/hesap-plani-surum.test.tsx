import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

/**
 * HESAP PLANI DÜZENLEME — SÜRÜM TELE TAŞINIYOR (HP/1 · K8).
 *
 * ───────────────────────────────────────────────────────────────
 * BU DOSYANIN ÖLÇTÜĞÜ ŞEY, ARKA UÇTAKİNDEN FARKLI
 * ───────────────────────────────────────────────────────────────
 *
 * Arka uçta `K8_EskiSurumle_Guncelleme_Reddedilir` sunucunun eski
 * damgayı REDDETTİĞİNİ kanıtlıyor. Burada kanıtlanan şey EKRANIN
 * DOĞRU DAMGAYI GÖNDERDİĞİ: sunucudan gelen `item.surum` aynen geri
 * veriliyor mu.
 *
 * İKİSİ AYRI: sunucu doğru davransa bile ekran sürümü göndermezse
 * ya da formdan alırsa koruma çalışmaz. Ekran sürümü kendi
 * durumundan alsaydı damga hiç eskimez, kullanıcı sayfada 10 dakika
 * dursa bile "güncel" görünürdü ve kayıp güncelleme sessizce olurdu.
 */

const HESAP_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const SUNUCU_SURUMU = "2026-08-30T09:15:42.123Z";

let gonderilenGovde: Record<string, unknown> | null = null;
let detaySurumu = SUNUCU_SURUMU;

const HESAP = () => ({
  id: HESAP_ID,
  companyId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  parentAccountId: null,
  code: "600",
  name: "SATIŞLAR",
  description: null,
  nature: 0,
  level: 1,
  isPostingAllowed: true,
  requiresProject: false,
  requiresCostCenter: false,
  currencyCode: null,
  isActive: true,
  createdAtUtc: "2026-08-01T00:00:00Z",
  updatedAtUtc: detaySurumu,
  surum: detaySurumu,
});

vi.mock("@/lib/api/api-client", () => ({
  ApiError: class ApiError extends Error {},
  apiClient: vi.fn(async (path: string, options?: { method?: string; body?: unknown }) => {
    if (path === "auth/me") {
      return {
        id: "u1",
        username: "test",
        fullName: "Test Kullanıcı",
        roles: ["Admin"],
        permissions: [],
        hasAllPermissions: true,
      };
    }

    if (path.startsWith(`accounting-accounts/${HESAP_ID}`) && options?.method === "PUT") {
      gonderilenGovde = options.body as Record<string, unknown>;
      return HESAP();
    }

    if (path.startsWith(`accounting-accounts/${HESAP_ID}`)) return HESAP();
    if (path.startsWith("accounting-accounts")) return [];
    if (path.startsWith("companies")) return [];

    return [];
  }),
}));

vi.mock("@/lib/use-current-user", () => ({
  useCurrentUser: () => ({
    user: {
      id: "u1",
      username: "test",
      fullName: "Test Kullanıcı",
      roles: ["Admin"],
      permissions: [],
      hasAllPermissions: true,
    },
    loading: false,
  }),
  clearCurrentUserCache: () => {},
}));

vi.mock("next/navigation", () => ({
  useParams: () => ({ id: HESAP_ID }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
  usePathname: () => `/muhasebe/hesap-plani/${HESAP_ID}`,
  useSearchParams: () => new URLSearchParams(),
}));

async function ekraniAc() {
  const { default: Sayfa } = await import("@/app/muhasebe/hesap-plani/[id]/page");
  return render(<Sayfa />);
}

beforeEach(() => {
  gonderilenGovde = null;
  detaySurumu = SUNUCU_SURUMU;
});

describe("hesap planı düzenleme — sürüm", () => {
  /**
   * S-A HEDEFİ: SÜRÜM İSTEĞE KONULUYOR.
   *
   * BU KIRMIZIYA DÖNERSE: sunucu her güncellemeyi "Sayfanın eski bir
   * sürümü açık" diye reddeder — hesap planı hiç düzenlenemez.
   */
  it("kaydetme isteğinde sürüm gönderiliyor", async () => {
    await ekraniAc();

    await waitFor(() =>
      expect(screen.getByDisplayValue("SATIŞLAR")).toBeInTheDocument(),
    );

    await userEvent.click(screen.getByRole("button", { name: /Kaydet/i }));

    await waitFor(() => expect(gonderilenGovde).not.toBeNull());

    expect(gonderilenGovde).toHaveProperty("surum");
  });

  /**
   * S-B HEDEFİ: GÖNDERİLEN SÜRÜM SUNUCUDAN GELEN DEĞER.
   *
   * BU KIRMIZIYA DÖNERSE: ekran damgayı kendi durumundan üretiyor
   * demektir; damga hiç eskimez ve kullanıcının 10 dakika önce
   * açtığı formdaki kayıp güncelleme YAKALANMAZ. Eşzamanlılık
   * koruması görünüşte çalışır, gerçekte hiçbir şey yapmaz.
   */
  it("gönderilen sürüm sunucudan gelen değerin AYNISI", async () => {
    await ekraniAc();

    await waitFor(() =>
      expect(screen.getByDisplayValue("SATIŞLAR")).toBeInTheDocument(),
    );

    await userEvent.click(screen.getByRole("button", { name: /Kaydet/i }));

    await waitFor(() => expect(gonderilenGovde).not.toBeNull());

    expect(gonderilenGovde!.surum).toBe(SUNUCU_SURUMU);
  });

  /**
   * KOD VE AKTİFLİK GÖNDERİLMİYOR (K1, K3).
   *
   * Gönderilirlerse sunucu yok sayar ve kullanıcı değiştirdiğini
   * sanır — Kural 62'nin sessiz yalanı.
   */
  it("kod ve aktiflik isteğe konulmuyor", async () => {
    await ekraniAc();

    await waitFor(() =>
      expect(screen.getByDisplayValue("SATIŞLAR")).toBeInTheDocument(),
    );

    await userEvent.click(screen.getByRole("button", { name: /Kaydet/i }));

    await waitFor(() => expect(gonderilenGovde).not.toBeNull());

    expect(gonderilenGovde).not.toHaveProperty("code");
    expect(gonderilenGovde).not.toHaveProperty("isActive");
  });
});
