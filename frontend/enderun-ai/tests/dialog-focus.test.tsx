import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import { Drawer } from "@/components/ui/drawer";
import { Modal } from "@/components/ui/modal";

/**
 * DİYALOG İÇİNDE YAZARKEN ODAK KAYBI.
 *
 * Canlıda bildirilen belirti: çek düzenlemede tutar alanına bir rakam
 * yazınca odak kaçıyor, ikinciyi yazmak için tekrar tıklamak gerekiyor.
 *
 * SEBEP MASKELEME DEĞİL — maskeleme hiç yok. `useDialogBehavior`
 * kancasının etkisi `onRequestClose`e bağımlı; çağıran taraf
 * `onClose={() => setShowEditModal(false)}` diye SATIR İÇİ ok
 * fonksiyonu verdiği için bu bağımlılık HER RENDERDA değişiyor.
 * Sonuç: her tuşta effect sökülüp yeniden kuruluyor — temizlik
 * `restore?.focus?.()` ile odağı geri veriyor, yeni kurulum da
 * paneldeki İLK odaklanabilir elemana odaklanıyor. İkisi de kullanıcıyı
 * yazdığı alandan atıyor.
 *
 * Bu kanca modal ve drawer'ın ORTAK davranışı; yani hata çek ekranına
 * özgü değil, satır içi `onClose` veren her diyalogda var.
 */
function Sahne() {
  const [open, setOpen] = useState(true);
  const [amount, setAmount] = useState("");

  return (
    <Modal
      open={open}
      title="Çeki düzenle"
      // SATIR İÇİ OK FONKSİYONU — uygulamadaki her çağrı yeri böyle.
      onClose={() => setOpen(false)}
    >
      <input
        aria-label="Tutar"
        value={amount}
        onChange={(event) => setAmount(event.target.value)}
      />
    </Modal>
  );
}

/** Drawer aynı kancayı kullanıyor; hata orada da vardı. */
function DrawerSahne() {
  const [open, setOpen] = useState(true);
  const [amount, setAmount] = useState("");

  return (
    <Drawer open={open} title="Çek detayı" onClose={() => setOpen(false)}>
      <input
        aria-label="Tutar"
        value={amount}
        onChange={(event) => setAmount(event.target.value)}
      />
    </Drawer>
  );
}

describe("diyalog içinde odak", () => {
  it("art arda yazarken odak alanda kalır", async () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Tutar") as HTMLInputElement;

    // Açılıştaki odaklama setTimeout(0) ile geliyor; önce onun
    // yerleşmesini bekliyoruz ki ölçtüğümüz şey açılış odağı olmasın.
    await waitFor(() => expect(document.activeElement).not.toBe(document.body));

    input.focus();
    expect(document.activeElement).toBe(input);

    // İlk rakam.
    fireEvent.change(input, { target: { value: "2" } });
    await waitFor(() => expect(input.value).toBe("2"));

    // ASIL SÖZ: yazdıktan sonra odak HÂLÂ alanda. Kaçarsa kullanıcı
    // ikinci rakam için tekrar tıklamak zorunda kalıyor.
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(document.activeElement).toBe(input);

    // İkinci rakam tıklamadan yazılabilmeli.
    fireEvent.change(input, { target: { value: "28" } });
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(input.value).toBe("28");
    expect(document.activeElement).toBe(input);
  });

  it("drawer içinde de odak alanda kalır", async () => {
    render(<DrawerSahne />);

    const input = screen.getByLabelText("Tutar") as HTMLInputElement;

    await waitFor(() => expect(document.activeElement).not.toBe(document.body));

    input.focus();
    fireEvent.change(input, { target: { value: "5" } });

    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(input.value).toBe("5");
    expect(document.activeElement).toBe(input);
  });
});
