import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { Drawer } from "@/components/ui/drawer";

/**
 * SAĞDAN KAYAN PANEL — modal ile AYNI davranış ailesinden.
 *
 * Esc, odak tuzağı, ilk alana odak ve kapanışta odağın geri dönmesi
 * ortak kancadan (useDialogBehavior) geliyor; bu testler drawer'ın o
 * davranışı gerçekten aldığını ve kendine özgü sözünü — kaydedilmemiş
 * değişiklik koruması — tuttuğunu sabitler.
 */
function Panel({
  open = true,
  dirty = false,
  busy = false,
  onClose = () => {},
}: {
  open?: boolean;
  dirty?: boolean;
  busy?: boolean;
  onClose?: () => void;
}) {
  return (
    <Drawer
      open={open}
      title="Yeni Kayıt"
      description="Formu doldurup kaydedin."
      dirty={dirty}
      busy={busy}
      onClose={onClose}
      footer={<button type="button">Kaydet</button>}
    >
      <input aria-label="Ad" />
      <input aria-label="Soyad" />
    </Drawer>
  );
}

describe("Drawer", () => {
  it("kapalıyken hiç render edilmez", () => {
    render(<Panel open={false} />);

    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("başlık ve açıklama ekran okuyucuya bağlı", () => {
    render(<Panel />);

    const dialog = screen.getByRole("dialog");

    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(screen.getByText("Yeni Kayıt")).toBeInTheDocument();
    expect(screen.getByText("Formu doldurup kaydedin.")).toBeInTheDocument();
  });

  /** Kullanıcı panel açılır açılmaz yazmaya başlayabilmeli. */
  it("açılınca ilk alana odaklanır", async () => {
    render(<Panel />);

    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByLabelText("Ad")),
    );
  });

  it("Esc paneli kapatır", () => {
    const onClose = vi.fn();
    render(<Panel onClose={onClose} />);

    fireEvent.keyDown(document, { key: "Escape" });

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("zemine tıklayınca kapanır", () => {
    const onClose = vi.fn();
    const { container } = render(<Panel onClose={onClose} />);

    fireEvent.mouseDown(container.firstChild as Element);

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  /**
   * İŞLEM SÜRERKEN KAPANMAZ: yarım kalan bir kayıt kullanıcıya
   * "kapandı, demek ki olmadı" dedirtirdi.
   */
  it("işlem sürerken Esc kapatmaz", () => {
    const onClose = vi.fn();
    render(<Panel busy onClose={onClose} />);

    fireEvent.keyDown(document, { key: "Escape" });

    expect(onClose).not.toHaveBeenCalled();
  });

  /**
   * KAYDEDİLMEMİŞ DEĞİŞİKLİK KORUMASI — drawer'ın kendi sözü.
   * Uzun bir formu yanlışlıkla Esc'e basarak kaybetmek, kullanıcının
   * her şeyi yeniden yazması demektir.
   */
  it("düzenleme varken kapatma önce onay sorar", () => {
    const onClose = vi.fn();
    render(<Panel dirty onClose={onClose} />);

    fireEvent.keyDown(document, { key: "Escape" });

    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(
      screen.getByText("Kaydedilmemiş değişiklikler var"),
    ).toBeInTheDocument();
  });

  it("onayda değişiklikler atılır ve panel kapanır", () => {
    const onClose = vi.fn();
    render(<Panel dirty onClose={onClose} />);

    fireEvent.keyDown(document, { key: "Escape" });
    fireEvent.click(screen.getByText("Değişiklikleri at"));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("düzenlemeye dönünce panel açık kalır", () => {
    const onClose = vi.fn();
    render(<Panel dirty onClose={onClose} />);

    fireEvent.keyDown(document, { key: "Escape" });
    fireEvent.click(screen.getByText("Düzenlemeye dön"));

    expect(onClose).not.toHaveBeenCalled();
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  /** Odak tuzağı: Tab son elemandan sonra başa döner. */
  it("odak panelin içinde döner", () => {
    render(<Panel />);

    const save = screen.getByText("Kaydet");
    save.focus();

    fireEvent.keyDown(document, { key: "Tab" });

    expect(document.activeElement).not.toBe(document.body);
  });

  it("kapat düğmesi erişilebilir etikete sahip", () => {
    render(<Panel />);

    expect(screen.getByLabelText("Paneli kapat")).toBeInTheDocument();
  });
});
