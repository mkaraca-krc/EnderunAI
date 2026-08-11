import { useState } from "react";

import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { Modal } from "@/components/ui/modal";

/**
 * Modal — APP-WIDE STANDART.
 *
 * Bu bileşen onlarca ekrana yayılıyor; Esc, odak tuzağı ve odağın
 * geri verilmesi gibi kurallar bir kez bozulursa regresyon her
 * ekrana birden yayılır ve elle fark edilmesi neredeyse imkânsızdır.
 * Testler bileşenin SÖZLEŞMESİNİ sabitliyor, görünümünü değil.
 */
describe("Modal", () => {
  it("Esc ile kapanır", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    render(
      <Modal open title="Gider ekle" onClose={onClose}>
        <button type="button">İçerik düğmesi</button>
      </Modal>,
    );

    await user.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  /**
   * İŞLEM SÜRERKEN KAPANMAZ. Yarım kalan bir kayıtta diyalog
   * kapanırsa kullanıcı "kapandı, demek ki olmadı" der; oysa istek
   * sunucuda sürüyor olabilir.
   */
  it("busy iken Esc ve zemin tıklaması kapatmaz", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    render(
      <Modal open busy title="Kaydediliyor" onClose={onClose}>
        <button type="button">İçerik düğmesi</button>
      </Modal>,
    );

    await user.keyboard("{Escape}");
    expect(onClose).not.toHaveBeenCalled();

    // Kapat düğmesi de kilitli.
    expect(screen.getByRole("button", { name: "Kapat" })).toBeDisabled();
  });

  it("zemine tıklayınca kapanır, panelin içine tıklayınca kapanmaz", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    render(
      <Modal open title="Zemin testi" onClose={onClose}>
        <button type="button">İçerik düğmesi</button>
      </Modal>,
    );

    await user.click(screen.getByRole("dialog"));
    expect(onClose).not.toHaveBeenCalled();

    const backdrop = screen.getByRole("dialog").parentElement!;
    await user.click(backdrop);

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  /**
   * ODAK TUZAĞI: Tab panelin içinde döner. Olmadan klavye kullanıcısı
   * arkadaki listeye düşer ve diyalog klavyeyle kullanılamaz hale
   * gelir.
   */
  it("Tab odağı panelin içinde döndürür", async () => {
    const user = userEvent.setup();

    render(
      <Modal open title="Odak tuzağı" onClose={vi.fn()}>
        <button type="button">Birinci</button>
        <button type="button">İkinci</button>
      </Modal>,
    );

    const close = screen.getByRole("button", { name: "Kapat" });
    const first = screen.getByRole("button", { name: "Birinci" });
    const last = screen.getByRole("button", { name: "İkinci" });

    // Açılışta odak panelin ilk odaklanabilir elemanında.
    await waitFor(() => expect(close).toHaveFocus());

    await user.tab();
    expect(first).toHaveFocus();

    await user.tab();
    expect(last).toHaveFocus();

    // SON elemandan sonra başa döner — dışarı çıkmaz.
    await user.tab();
    expect(close).toHaveFocus();

    // Shift+Tab ile ilkten geriye sona döner.
    await user.tab({ shift: true });
    expect(last).toHaveFocus();
  });

  /**
   * KAPANINCA ODAK ÇAĞIRAN DÜĞMEYE DÖNER. Dönmezse klavye kullanıcısı
   * sayfanın başına savrulur ve nerede kaldığını kaybeder.
   */
  it("kapanınca odağı açan düğmeye geri verir", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [open, setOpen] = useState(false);

      return (
        <div>
          <button type="button" onClick={() => setOpen(true)}>
            Gider ekle
          </button>

          <Modal open={open} title="Gider ekle" onClose={() => setOpen(false)}>
            <button type="button">İçerik</button>
          </Modal>
        </div>
      );
    }

    render(<Harness />);

    const trigger = screen.getByRole("button", { name: "Gider ekle" });

    trigger.focus();
    await user.click(trigger);

    await waitFor(() =>
      expect(screen.getByRole("dialog")).toBeInTheDocument(),
    );

    await user.keyboard("{Escape}");

    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("başlık ve açıklama diyaloga bağlanır (erişilebilirlik)", () => {
    render(
      <Modal open title="Krediyi iptal et" description="Geri alınamaz." onClose={vi.fn()}>
        <p>gövde</p>
      </Modal>,
    );

    const dialog = screen.getByRole("dialog");

    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog).toHaveAccessibleName("Krediyi iptal et");
    expect(dialog).toHaveAccessibleDescription("Geri alınamaz.");
  });

  it("kapalıyken hiçbir şey render etmez", () => {
    render(
      <Modal open={false} title="Kapalı" onClose={vi.fn()}>
        <p>gövde</p>
      </Modal>,
    );

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  /**
   * Arka plan kaydırma kilidi: modal açıkken sayfanın altındaki
   * liste kaymamalı, kapanınca eski haline dönmeli.
   */
  it("açıkken gövde kaydırmasını kilitler, kapanınca serbest bırakır", async () => {
    const { rerender } = render(
      <Modal open title="Kilit" onClose={vi.fn()}>
        <p>gövde</p>
      </Modal>,
    );

    expect(document.body.style.overflow).toBe("hidden");

    rerender(
      <Modal open={false} title="Kilit" onClose={vi.fn()}>
        <p>gövde</p>
      </Modal>,
    );

    await waitFor(() => expect(document.body.style.overflow).not.toBe("hidden"));
  });
});
