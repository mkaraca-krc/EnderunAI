import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useCallback, useState } from "react";
import { describe, expect, it, vi } from "vitest";

import { SearchableSelect, type SearchableOption } from "@/components/ui";

/**
 * ARANABİLİR SEÇİCİ — SUNUCU KİPİ.
 *
 * Hesap planı canlıda 1.114 satır (~168 KB) ve her ekran açılışında
 * tamamı iniyordu. Sunucu kipinde liste yazdıkça geliyor.
 *
 * BURADAKİ EN KRİTİK SÖZ YARIŞ KORUMASI: hızlı yazan kullanıcıda
 * istekler sırayla dönmez. "150" için açılan istek "1500" için
 * açılandan SONRA dönerse eski sonuç yenisini ezer — kullanıcı ekranda
 * gördüğü listeden seçer, yani YANLIŞ hesabı seçer ve bunu fark etmez.
 */
function Sahne({
  loadOptions,
}: {
  loadOptions: (
    query: string,
    signal: AbortSignal
  ) => Promise<{ options: SearchableOption[]; total: number }>;
}) {
  const [value, setValue] = useState("");

  return (
    <div>
      <SearchableSelect
        label="Hesap"
        value={value}
        onChange={setValue}
        options={[]}
        loadOptions={loadOptions}
        debounceMs={10}
      />
      <output data-testid="secili">{value || "bos"}</output>
    </div>
  );
}

describe("aranabilir seçici — sunucu kipi", () => {
  it("EN AZ 2 KARAKTER: tek harfte sunucuya sorulmuyor", async () => {
    const load = vi.fn();

    render(<Sahne loadOptions={load} />);
    const input = screen.getByLabelText("Hesap");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "1" } });

    await new Promise((resolve) => setTimeout(resolve, 40));

    // Tek harf 1.114 satırın neredeyse tamamını döndürürdü; hem
    // gereksiz yük hem işe yaramaz liste.
    expect(load).not.toHaveBeenCalled();
    expect(screen.getByText(/en az 2 karakter/i)).toBeInTheDocument();
  });

  it("BEKLEME: her tuşta değil, yazma durunca bir kez soruyor", async () => {
    const load = vi.fn().mockResolvedValue({ options: [], total: 0 });

    render(<Sahne loadOptions={load} />);
    const input = screen.getByLabelText("Hesap");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "15" } });
    fireEvent.change(input, { target: { value: "150" } });
    fireEvent.change(input, { target: { value: "1500" } });

    await waitFor(() => expect(load).toHaveBeenCalled());
    await new Promise((resolve) => setTimeout(resolve, 40));

    // Üç tuş, tek istek.
    expect(load).toHaveBeenCalledTimes(1);
    expect(load.mock.calls[0][0]).toBe("1500");
  });

  it("YARIŞ KORUMASI: geç dönen eski istek yeni sonucu EZMİYOR", async () => {
    const cozumleyiciler: ((value: {
      options: SearchableOption[];
      total: number;
    }) => void)[] = [];

    const load = vi.fn(
      (query: string, signal: AbortSignal) =>
        new Promise<{ options: SearchableOption[]; total: number }>(
          (resolve) => {
            cozumleyiciler.push(resolve);

            // İptal edilen istek hiç çözülmemiş sayılır — gerçek
            // fetch() de böyle davranır.
            signal.addEventListener("abort", () => {
              /* bilerek boş: iptal edilen yanıt işlenmez */
            });
          }
        )
    );

    function Yavas() {
      const [value, setValue] = useState("");
      const kararli = useCallback(load, []);

      return (
        <div>
          <SearchableSelect
            label="Hesap"
            value={value}
            onChange={setValue}
            options={[]}
            loadOptions={kararli}
            debounceMs={0}
          />
        </div>
      );
    }

    render(<Yavas />);
    const input = screen.getByLabelText("Hesap");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "eski" } });
    await waitFor(() => expect(cozumleyiciler.length).toBe(1));

    fireEvent.change(input, { target: { value: "yeni" } });
    await waitFor(() => expect(cozumleyiciler.length).toBe(2));

    // Önce YENİ istek dönüyor.
    cozumleyiciler[1]({
      options: [{ id: "y", code: "770", title: "Yeni Sonuç" }],
      total: 1,
    });

    await waitFor(() =>
      expect(screen.getByText(/Yeni Sonuç/)).toBeInTheDocument()
    );

    // ŞİMDİ eski istek geç dönüyor. Koruma olmasaydı listeyi ezerdi.
    cozumleyiciler[0]({
      options: [{ id: "e", code: "100", title: "Eski Sonuç" }],
      total: 1,
    });

    await new Promise((resolve) => setTimeout(resolve, 30));

    expect(screen.getByText(/Yeni Sonuç/)).toBeInTheDocument();
    expect(screen.queryByText(/Eski Sonuç/)).toBeNull();
  });

  it("kaç kayıt daha var — SUNUCUNUN saydığı toplamdan", async () => {
    const load = vi.fn().mockResolvedValue({
      options: [
        { id: "1", code: "770.01", title: "Genel Yönetim" },
        { id: "2", code: "770.02", title: "Kira" },
      ],
      // Sunucu 2 satır döndü ama toplam eşleşme 137.
      total: 137,
    });

    render(<Sahne loadOptions={load} />);
    const input = screen.getByLabelText("Hesap");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "770" } });

    // Çizilen satırdan türetilseydi "0 kayıt daha" derdi ve kullanıcı
    // aramayı daraltmayı hiç düşünmezdi.
    await waitFor(() =>
      expect(screen.getByText(/135 kayıt daha var/)).toBeInTheDocument()
    );
  });
});
