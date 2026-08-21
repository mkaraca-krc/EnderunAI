import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { ChequeVoidDialog } from "@/components/finans/cheque-void-dialog";

/**
 * ÇEK İPTAL DİYALOĞU.
 *
 * İptalin nedeni SAYILABİLİR olmak zorunda: serbest metinle "kaç çek
 * karşılıksız çıktı" sorusu hiç cevaplanamıyor. Buradaki üç söz:
 *  - neden seçilmeden onay düğmesi açılmaz,
 *  - kapanmış çekte "Yanlış giriş" listede GÖRÜNMEZ,
 *  - "Diğer" seçilirse açıklama zorunlu.
 *
 * Aynı kurallar uçta da var; burada denenen, kullanıcının sunucu
 * hatasıyla karşılaşmadan doğru yolu görmesi.
 */
describe("çek iptal diyaloğu", () => {
  function setup(overrides: Partial<Parameters<typeof ChequeVoidDialog>[0]> = {}) {
    const onConfirm = vi.fn();

    render(
      <ChequeVoidDialog
        open
        fromClosedState={false}
        statusName="Portföy"
        onCancel={() => {}}
        onConfirm={onConfirm}
        {...overrides}
      />
    );

    return { onConfirm, button: screen.getByRole("button", { name: "İptal Et" }) };
  }

  it("neden seçilmeden onay düğmesi kapalı", () => {
    const { button } = setup();

    expect(button).toBeDisabled();
  });

  it("neden seçilince onay açılır ve sayılabilir neden gönderilir", () => {
    const { onConfirm, button } = setup();

    fireEvent.change(screen.getByRole("combobox"), { target: { value: "1" } });
    expect(button).not.toBeDisabled();

    fireEvent.click(button);

    expect(onConfirm).toHaveBeenCalledWith({ reasonKind: 1, reason: "" });
  });

  it("açık çekte \"Yanlış giriş\" seçilebilir", () => {
    setup();

    expect(
      screen.getByRole("option", { name: "Yanlış giriş" })
    ).toBeInTheDocument();
  });

  it("KAPANMIŞ çekte \"Yanlış giriş\" listede yok", () => {
    setup({ fromClosedState: true, statusName: "Tahsil Edildi" });

    expect(screen.queryByRole("option", { name: "Yanlış giriş" })).toBeNull();

    // Diğer nedenler duruyor: kapanmış çek de karşılıksız çıkabilir.
    expect(screen.getByRole("option", { name: "Karşılıksız" })).toBeInTheDocument();
  });

  it("kapanmış çekte storno uyarısı ve durum adı gösterilir", () => {
    setup({ fromClosedState: true, statusName: "Tahsil Edildi" });

    expect(screen.getByText(/Tahsil Edildi/)).toBeInTheDocument();
    expect(screen.getByText(/storno/i)).toBeInTheDocument();
  });

  it("\"Diğer\" seçilince açıklama zorunlu", () => {
    const { onConfirm, button } = setup();

    fireEvent.change(screen.getByRole("combobox"), { target: { value: "90" } });

    // Açıklamasız "Diğer", "iptal edildi, sebebi yazılmadı" demek.
    expect(button).toBeDisabled();

    fireEvent.change(screen.getByRole("textbox"), {
      target: { value: "  Mükerrer kayıt  " },
    });

    expect(button).not.toBeDisabled();
    fireEvent.click(button);

    // Metin kırpılarak gidiyor: boşlukla dolu açıklama zorunluluğu
    // atlatmamalı ve kayıtta baş/son boşluk kalmamalı.
    expect(onConfirm).toHaveBeenCalledWith({
      reasonKind: 90,
      reason: "Mükerrer kayıt",
    });
  });
});
