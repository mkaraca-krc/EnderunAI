import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";

import { Modal } from "@/components/ui/modal";
import { SearchableSelect } from "@/components/ui/searchable-select";

/**
 * ARANABİLİR SEÇİCİ.
 *
 * Canlıda 150 cari düz `<select>` ile seçiliyordu; tarayıcının kendi
 * tuş davranışı yalnız İLK HARFE atlar, yani "Yılmaz İnşaat"ı bulmak
 * için listeyi kaydırmak gerekiyordu.
 *
 * Buradaki sözler:
 *  - yazınca süzülür ve Türkçe katlama çalışır ("sube" → "Şube"),
 *  - ↑/↓ gezinir, Enter seçer,
 *  - eşleşmeyen metin yazıp çıkan kullanıcının SEÇİMİ KAYBOLMAZ,
 *  - diyalog içinde Esc önce LİSTEYİ kapatır, formu değil.
 */
const OPTIONS = [
  { id: "1", code: "CARI-001", title: "Yılmaz İnşaat" },
  { id: "2", code: "CARI-002", title: "Şube Ticaret" },
  { id: "3", code: "CARI-003", title: "Ada Yapı", extra: ["1234567890"] },
];

function Sahne({
  inModal = false,
  onClose = () => {},
}: {
  inModal?: boolean;
  onClose?: () => void;
}) {
  const [value, setValue] = useState("");

  const alan = (
    <SearchableSelect
      label="Cari"
      value={value}
      onChange={(next) => setValue(next)}
      options={OPTIONS}
    />
  );

  return (
    <div>
      {inModal ? (
        <Modal open title="Çeki düzenle" onClose={onClose}>
          {alan}
        </Modal>
      ) : (
        alan
      )}

      <output data-testid="secili">{value || "bos"}</output>
    </div>
  );
}

describe("aranabilir seçici", () => {
  it("yazınca süzülür", async () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Cari");
    fireEvent.focus(input);

    // Başlangıçta hepsi listede.
    expect(screen.getByRole("listbox").querySelectorAll("li").length)
      .toBeGreaterThanOrEqual(3);

    fireEvent.change(input, { target: { value: "yılmaz" } });

    const list = screen.getByRole("listbox");
    expect(within(list).getByText(/Yılmaz İnşaat/)).toBeInTheDocument();
    expect(within(list).queryByText(/Ada Yapı/)).toBeNull();
  });

  it("TÜRKÇE KATLAMA: \"sube\" yazan \"Şube\"yi bulur", () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Cari");
    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "sube" } });

    // Kullanıcı arama kutusuna Türkçe karakter yazmak için klavye
    // değiştirmek zorunda kalmamalı.
    expect(
      within(screen.getByRole("listbox")).getByText(/Şube Ticaret/)
    ).toBeInTheDocument();
  });

  it("KOD ve ek alanlarda da arar", () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Cari");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "CARI-003" } });
    expect(
      within(screen.getByRole("listbox")).getByText(/Ada Yapı/)
    ).toBeInTheDocument();

    // Vergi numarası `extra` alanından geliyor.
    fireEvent.change(input, { target: { value: "1234567890" } });
    expect(
      within(screen.getByRole("listbox")).getByText(/Ada Yapı/)
    ).toBeInTheDocument();
  });

  it("↑/↓ gezinir, Enter seçer", () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Cari");
    fireEvent.focus(input);

    // Boş seçenek ilk sırada değil: liste vurgusu KAYITLAR üzerinde.
    fireEvent.keyDown(input, { key: "ArrowDown" });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(screen.getByTestId("secili")).toHaveTextContent("2");
    expect((input as HTMLInputElement).value).toContain("Şube Ticaret");
  });

  it("uçlarda başa sarar", () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Cari");
    fireEvent.focus(input);

    // İlk satırdayken yukarı → sona sarmalı (uzun listede sondaki
    // kaydı aramak için baştan aşağı inmek gerekmesin).
    fireEvent.keyDown(input, { key: "ArrowUp" });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(screen.getByTestId("secili")).toHaveTextContent("3");
  });

  it("EŞLEŞMEYEN METİN YAZIP ÇIKINCA SEÇİM KAYBOLMAZ", () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Cari") as HTMLInputElement;

    fireEvent.focus(input);
    fireEvent.keyDown(input, { key: "ArrowDown" });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(screen.getByTestId("secili")).toHaveTextContent("2");

    // Kullanıcı yanlış yazıp alandan çıkıyor.
    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "zzz" } });
    fireEvent.blur(input);

    // Sessizce boşaltmak, formu kaydeden kişiye carisiz bir kayıt
    // yazdırırdı.
    expect(screen.getByTestId("secili")).toHaveTextContent("2");
    expect(input.value).toContain("Şube Ticaret");
  });

  it("eşleşme yoksa bunu söyler", () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Cari");
    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "zzz" } });

    expect(screen.getByText("Eşleşen kayıt yok.")).toBeInTheDocument();
  });

  it("DİYALOG İÇİNDE Esc önce listeyi kapatır, formu değil", async () => {
    const onClose = vi.fn();

    render(<Sahne inModal onClose={onClose} />);

    const input = screen.getByLabelText("Cari");

    await waitFor(() =>
      expect(document.activeElement).not.toBe(document.body)
    );

    fireEvent.focus(input);
    expect(screen.getByRole("listbox")).toBeInTheDocument();

    // Diyalog kancası document üzerinde YAKALAMA evresinde dinliyor,
    // yani iç kontrolün işleyicisinden önce çalışıyor. Sözleşme
    // olmasaydı Esc formu kapatır ve kullanıcı yazdığını kaybederdi.
    // Olay ALANDAN gönderiliyor: diyalog dinleyicisi document
    // üzerinde yakalama evresinde olduğu için olayı target=input ile
    // görüyor. document'e doğrudan göndermek gerçek kullanıcıyı temsil
    // etmezdi — kimse "document"e basmıyor.
    fireEvent.keyDown(input, { key: "Escape" });

    expect(onClose).not.toHaveBeenCalled();

    // Liste kapandıktan SONRA Esc diyaloğu kapatır.
    fireEvent.blur(input);
    fireEvent.keyDown(document, { key: "Escape" });

    expect(onClose).toHaveBeenCalled();
  });
});
