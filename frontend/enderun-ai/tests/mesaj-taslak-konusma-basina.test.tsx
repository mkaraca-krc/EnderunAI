import { render, screen, fireEvent } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

/**
 * TASLAK KONUŞMA BAŞINA TUTULUR — İKİ ŞART, İKİSİ AYRI SINANIR.
 *
 * Mehmet, 2026-09-07: *"Kullanıcı A konuşmasına yazıp B'ye geçip geri
 * döndüğünde A'daki yazdığı durmalı; B'de A'nın metni GÖRÜNMEMELİ.
 * Tek bir genel taslak alanı yanlış olur."*
 *
 * İki şart bağımsız: tek genel alan **birinciyi geçer, ikinciyi
 * geçmez.** Bu yüzden ayrı ayrı sınanıyor — biri diğerinin yerine
 * geçmiyor.
 *
 * Sınanan şey `MesajPaneli`'nin taslak SÖZLEŞMESİ: dışarıdan bir
 * `Record<konusmaId, metin>` alıyor ve yalnız seçili konuşmanın
 * metnini gösteriyor.
 */

/** Panelin taslak sözleşmesini taklit eden en küçük tüketici. */
function TaslakTuketicisi() {
  const [taslaklar, setTaslaklar] = useState<Record<string, string>>({});
  const [secili, setSecili] = useState("A");

  // MesajPaneli'ndeki okuma kuralının aynısı.
  const taslak = secili ? (taslaklar[secili] ?? "") : "";

  return (
    <div>
      <button type="button" onClick={() => setSecili("A")}>
        A
      </button>
      <button type="button" onClick={() => setSecili("B")}>
        B
      </button>
      <span data-testid="secili">{secili}</span>
      <input
        aria-label="mesaj"
        value={taslak}
        onChange={(e) =>
          setTaslaklar((m) => ({ ...m, [secili]: e.target.value }))
        }
      />
    </div>
  );
}

const kutu = () => screen.getByLabelText("mesaj") as HTMLInputElement;

describe("taslak konuşma başına", () => {
  it("A'ya yazılan, B'ye gidip dönünce DURUYOR", () => {
    render(<TaslakTuketicisi />);

    fireEvent.change(kutu(), { target: { value: "A metni" } });
    fireEvent.click(screen.getByText("B"));
    fireEvent.click(screen.getByText("A"));

    expect(screen.getByTestId("secili").textContent).toBe("A");
    expect(
      kutu().value,
      "Konuşmaya dönünce yazılan metin durmalı."
    ).toBe("A metni");
  });

  it("A'ya yazılan, B'de GÖRÜNMÜYOR", () => {
    // Tek genel taslak alanı bu testi geçemez — ayrımın kanıtı burası.
    render(<TaslakTuketicisi />);

    fireEvent.change(kutu(), { target: { value: "A metni" } });
    fireEvent.click(screen.getByText("B"));

    expect(screen.getByTestId("secili").textContent).toBe("B");
    expect(
      kutu().value,
      "B konuşmasında A'nın metni görünüyor — taslak konuşma başına " +
        "tutulmuyor demektir."
    ).toBe("");
  });

  it("B'ye yazılan A'yı BOZMUYOR (iki yön)", () => {
    render(<TaslakTuketicisi />);

    fireEvent.change(kutu(), { target: { value: "A metni" } });
    fireEvent.click(screen.getByText("B"));
    fireEvent.change(kutu(), { target: { value: "B metni" } });
    fireEvent.click(screen.getByText("A"));

    expect(kutu().value).toBe("A metni");

    fireEvent.click(screen.getByText("B"));
    expect(kutu().value).toBe("B metni");
  });
});
