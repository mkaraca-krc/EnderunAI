import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import CommandPalette from "@/components/erp/command-palette";
import type { MenuGroup } from "@/lib/navigation/menu";

const push = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

/**
 * KOMUT PALETİ (Ctrl+K).
 *
 * Palet KENDİ menüsünü kurmuyor; kabuğun izne göre süzdüğü listeyi
 * alıyor. Buradaki testler paletin o listenin dışına çıkmadığını ve
 * klavyeyle tam kullanılabildiğini sabitler.
 */
const GROUPS: MenuGroup[] = [
  {
    key: "finance",
    label: "FİNANS",
    items: [
      { label: "Kasa Hesapları", href: "/finans/kasa", icon: "◆" },
      { label: "Çek Senet", href: "/finans/cekler", icon: "◆" },
    ],
  },
  {
    key: "hr",
    label: "İNSAN KAYNAKLARI",
    items: [{ label: "Bordro", href: "/insan-kaynaklari/bordro", icon: "◆" }],
  },
];

function open(props: Partial<Parameters<typeof CommandPalette>[0]> = {}) {
  const onClose = vi.fn();
  const onToggleFavorite = vi.fn();

  render(
    <CommandPalette
      open
      onClose={onClose}
      groups={GROUPS}
      favoritePaths={[]}
      onToggleFavorite={onToggleFavorite}
      {...props}
    />,
  );

  return { onClose, onToggleFavorite };
}

describe("CommandPalette", () => {
  beforeEach(() => {
    push.mockClear();
  });

  it("kapalıyken hiç render edilmez", () => {
    render(
      <CommandPalette
        open={false}
        onClose={() => {}}
        groups={GROUPS}
        favoritePaths={[]}
        onToggleFavorite={() => {}}
      />,
    );

    expect(screen.queryByTestId("command-palette")).toBeNull();
  });

  it("açılışta tüm erişilebilir sayfaları listeler", () => {
    open();

    expect(screen.getByText("Kasa Hesapları")).toBeInTheDocument();
    expect(screen.getByText("Bordro")).toBeInTheDocument();
  });

  /** Türkçe yazıma takılmadan arar: "cek" → "Çek Senet". */
  it("Türkçe karakter yazılmadan da bulur", () => {
    open();

    fireEvent.change(screen.getByLabelText("Sayfa ara"), {
      target: { value: "cek" },
    });

    expect(screen.getByText("Çek Senet")).toBeInTheDocument();
    expect(screen.queryByText("Bordro")).toBeNull();
  });

  /**
   * ASIL GÜVENCE: palete verilen liste kullanıcının görebildiği menü.
   * Listede olmayan bir sayfa hiçbir aramayla ortaya çıkmaz.
   */
  it("verilen listede olmayan sayfayı hiçbir aramada göstermez", () => {
    render(
      <CommandPalette
        open
        onClose={() => {}}
        groups={[GROUPS[0]]}
        favoritePaths={[]}
        onToggleFavorite={() => {}}
      />,
    );

    fireEvent.change(screen.getByLabelText("Sayfa ara"), {
      target: { value: "bordro" },
    });

    expect(screen.queryByText("Bordro")).toBeNull();
    expect(screen.getByText(/Eşleşen sayfa yok/)).toBeInTheDocument();
  });

  it("Enter ile ilk sonuca gider ve palet kapanır", () => {
    const { onClose } = open();

    const input = screen.getByLabelText("Sayfa ara");

    fireEvent.change(input, { target: { value: "kasa" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(push).toHaveBeenCalledWith("/finans/kasa");
    expect(onClose).toHaveBeenCalled();
  });

  /** Ok tuşları: fareye uzanmadan sonuçlar arasında gezinilebilmeli. */
  it("ok tuşuyla seçim aşağı iner", () => {
    open();

    const input = screen.getByLabelText("Sayfa ara");

    fireEvent.keyDown(input, { key: "ArrowDown" });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(push).toHaveBeenCalledWith("/finans/cekler");
  });

  it("sonuç yokken Enter bir yere götürmez", () => {
    open();

    const input = screen.getByLabelText("Sayfa ara");

    fireEvent.change(input, { target: { value: "zzzz-yok" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(push).not.toHaveBeenCalled();
  });

  it("Esc paleti kapatır", () => {
    const { onClose } = open();

    fireEvent.keyDown(document, { key: "Escape" });

    expect(onClose).toHaveBeenCalled();
  });

  /**
   * Kullanıcı aradığı sayfayı bulmuşken, ikinci kez aramak zorunda
   * kalmadan kısayola çevirebilmeli.
   */
  it("sonuç satırından favoriye eklenir", () => {
    const { onToggleFavorite } = open();

    fireEvent.click(
      screen.getByLabelText("Kasa Hesapları favorilere ekle"),
    );

    expect(onToggleFavorite).toHaveBeenCalledWith("/finans/kasa");
  });

  it("favori sayfa yıldızı dolu gösterir", () => {
    open({ favoritePaths: ["/finans/kasa"] });

    expect(
      screen.getByLabelText("Kasa Hesapları favorilerden çıkar"),
    ).toBeInTheDocument();
  });
});
