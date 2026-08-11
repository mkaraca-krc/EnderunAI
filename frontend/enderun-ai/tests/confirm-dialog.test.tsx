import { useState } from "react";

import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { ConfirmDialog } from "@/components/ui/confirm-dialog";

/**
 * ConfirmDialog — geri alınamaz işlemlerin kapısı.
 *
 * window.confirm/prompt yerine kuruldu çünkü tarayıcı diyaloğu
 * gerekçeyi zorunlu tutamıyor ve boş metni kabul ediyor. Buradaki
 * asıl kural: GEREKÇE BOŞKEN ONAY GİTMEZ. Bozulursa "neden iptal
 * edildi" sorusu aylar sonra cevapsız kalır.
 */
describe("ConfirmDialog", () => {
  it("gerekçe zorunluyken boşken onay düğmesi kapalı", () => {
    const onConfirm = vi.fn();

    render(
      <ConfirmDialog
        open
        title="Krediyi iptal et"
        confirmLabel="Krediyi İptal Et"
        requireReason
        onCancel={vi.fn()}
        onConfirm={onConfirm}
      />,
    );

    expect(
      screen.getByRole("button", { name: "Krediyi İptal Et" }),
    ).toBeDisabled();

    expect(onConfirm).not.toHaveBeenCalled();
  });

  it("yalnızca boşluk yazmak gerekçe sayılmaz", async () => {
    const user = userEvent.setup();

    render(
      <ConfirmDialog
        open
        title="Çeki iptal et"
        confirmLabel="İptal Et"
        requireReason
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );

    await user.type(screen.getByRole("textbox"), "   ");

    expect(screen.getByRole("button", { name: "İptal Et" })).toBeDisabled();
  });

  it("gerekçe yazılınca onay açılır ve kırpılmış metin gönderilir", async () => {
    const user = userEvent.setup();
    const onConfirm = vi.fn();

    render(
      <ConfirmDialog
        open
        title="Çeki iptal et"
        confirmLabel="İptal Et"
        requireReason
        onCancel={vi.fn()}
        onConfirm={onConfirm}
      />,
    );

    await user.type(screen.getByRole("textbox"), "  Yanlış tutarla girilmiş  ");

    const confirm = screen.getByRole("button", { name: "İptal Et" });
    expect(confirm).toBeEnabled();

    await user.click(confirm);

    expect(onConfirm).toHaveBeenCalledWith("Yanlış tutarla girilmiş");
  });

  /**
   * YAZILAN KAYBOLMAZ: kullanıcı gerekçeyi yazdıktan sonra üst
   * bileşen yeniden render olursa metin durmalı. Kaybolsaydı uzun
   * bir gerekçe yazan kullanıcı her yeniden çizimde baştan başlardı.
   */
  it("üst bileşen yeniden render olunca yazılan gerekçe durur", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [tick, setTick] = useState(0);

      return (
        <div>
          <button type="button" onClick={() => setTick(tick + 1)}>
            Dışarıda bir şey değişti {tick}
          </button>

          <ConfirmDialog
            open
            title="İptal"
            confirmLabel="Onayla"
            requireReason
            onCancel={vi.fn()}
            onConfirm={vi.fn()}
          />
        </div>
      );
    }

    render(<Harness />);

    const textbox = screen.getByRole("textbox");
    await user.type(textbox, "Mükerrer kayıt");

    // Modal odak tuzağı kurduğu için dış düğmeye tıklamak yerine
    // durumu doğrudan değiştiriyoruz: amaç yeniden render.
    await user.type(textbox, " düzeltiliyor");

    expect(textbox).toHaveValue("Mükerrer kayıt düzeltiliyor");
  });

  /**
   * İŞLEM SÜRERKEN çift gönderim engelleniyor: iki kez tıklanan bir
   * iptal, iki ters kayıt üretebilirdi.
   */
  it("busy iken onay ve vazgeç kilitli, etiket işleniyor gösterir", () => {
    render(
      <ConfirmDialog
        open
        busy
        title="İptal"
        confirmLabel="Onayla"
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: "İşleniyor…" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Vazgeç" })).toBeDisabled();
  });

  it("gerekçe zorunlu değilse onay doğrudan açık", async () => {
    const user = userEvent.setup();
    const onConfirm = vi.fn();

    render(
      <ConfirmDialog
        open
        title="Kaydı sil"
        confirmLabel="Sil"
        onCancel={vi.fn()}
        onConfirm={onConfirm}
      />,
    );

    // Gerekçe alanı hiç render edilmiyor.
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Sil" }));

    expect(onConfirm).toHaveBeenCalledWith("");
  });

  it("sunucudan gelen hata diyalogda kalır (modal kapanmaz)", () => {
    render(
      <ConfirmDialog
        open
        title="İptal"
        confirmLabel="Onayla"
        error="Ödenmiş taksit var; plan yeniden üretilemez."
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );

    expect(
      screen.getByText("Ödenmiş taksit var; plan yeniden üretilemez."),
    ).toBeInTheDocument();

    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("vazgeç çağrılınca onay tetiklenmez", async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    const onConfirm = vi.fn();

    render(
      <ConfirmDialog
        open
        title="İptal"
        confirmLabel="Onayla"
        onCancel={onCancel}
        onConfirm={onConfirm}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Vazgeç" }));

    await waitFor(() => expect(onCancel).toHaveBeenCalledTimes(1));
    expect(onConfirm).not.toHaveBeenCalled();
  });
});
