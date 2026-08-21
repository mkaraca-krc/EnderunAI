import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import { Modal } from "@/components/ui/modal";
import { TutarInput } from "@/components/ui/tutar-input";

/**
 * TUTAR GİRİŞİ.
 *
 * Üç söz canlıda yaşanmış şikâyetlerden geliyor:
 *  - tek seferde yazılabilmeli (odak kaçmamalı),
 *  - imleç ortada rakam eklenince sona atlamamalı,
 *  - NOKTA da ondalık sayılmalı — sayısal tuş takımında virgül yok;
 *    kabul edilmezse "1234.5" yazan kullanıcı 12.345 kaydeder ve
 *    farkı görmez.
 *
 * Dördüncü test, yeni düzeltilen diyalog odak hatasının geri
 * gelmediğini sabitliyor: alan bir modalın içindeyken de aynı üç söz
 * geçerli.
 */

/**
 * Yazmayı taklit eder.
 *
 * `input.value` ELLE YAZILMAZ: React'in değer izleyicisi o atamayla
 * güncellenir ve ardından gelen change olayını "değer zaten aynı"
 * diye yutar — onChange hiç çalışmaz. Değer ve imleç `target` ile
 * birlikte veriliyor; sıra önemli, `value` ataması imleci sona
 * kaydırdığı için `selectionStart` ondan SONRA yazılıyor.
 */
function type(input: HTMLInputElement, keys: string) {
  for (const key of keys) {
    const caret = input.selectionStart ?? input.value.length;

    const next =
      input.value.slice(0, caret) + key + input.value.slice(caret);

    fireEvent.change(input, {
      target: { value: next, selectionStart: caret + 1, selectionEnd: caret + 1 },
    });
  }
}

/** Belirli bir konuma tek karakter ekler — imleç testleri için. */
function insertAt(input: HTMLInputElement, at: number, key: string) {
  const next = input.value.slice(0, at) + key + input.value.slice(at);

  fireEvent.change(input, {
    target: { value: next, selectionStart: at + 1, selectionEnd: at + 1 },
  });
}

function Sahne({ inModal = false }: { inModal?: boolean }) {
  const [value, setValue] = useState<number | null>(null);

  const alan = (
    <TutarInput
      label="Tutar"
      id="tutar"
      value={value}
      // SATIR İÇİ OK FONKSİYONU — uygulamadaki her çağrı yeri böyle.
      onChange={(next) => setValue(next)}
    />
  );

  return (
    <div>
      {inModal ? (
        <Modal open title="Çeki düzenle" onClose={() => {}}>
          {alan}
        </Modal>
      ) : (
        alan
      )}

      <output data-testid="ham">{value === null ? "bos" : String(value)}</output>
    </div>
  );
}

describe("tutar girişi", () => {
  it("tek seferde 2814000 yazılabilir, odak alanda kalır", async () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Tutar") as HTMLInputElement;
    input.focus();

    type(input, "2814000");

    // Yazdıkça biçimlenmiş olmalı.
    expect(input.value).toBe("2.814.000");

    // ASIL SÖZ: tek seferde yazıldı, odak hiç kaçmadı.
    expect(document.activeElement).toBe(input);
    expect(screen.getByTestId("ham")).toHaveTextContent("2814000");

    // Odaktan çıkınca kuruş tamamlanır.
    fireEvent.blur(input);
    await waitFor(() => expect(input.value).toBe("2.814.000,00"));
  });

  it("ortaya rakam eklenince imleç eklenen rakamın sağında kalır", () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Tutar") as HTMLInputElement;
    input.focus();

    type(input, "1234");
    expect(input.value).toBe("1.234");

    // İmleci "1.2|34" konumuna götürüp araya 9 ekle:
    // rakamlar 1-2-9-3-4 olur, biçimli hâli "12.934".
    insertAt(input, 3, "9");

    expect(input.value).toBe("12.934");

    // İmleç EKLENEN RAKAMIN SAĞINDA: "12.9|34" — solunda üç rakam
    // (1, 2, 9) var. Sona atsaydı kullanıcı her düzeltmede yerini
    // kaybeder, düzeltmeyi baştan yazmak zorunda kalırdı.
    expect(input.selectionStart).toBe(4);
    expect(input.value.slice(0, input.selectionStart ?? 0)).toBe("12.9");
  });

  it("hem virgül hem NOKTA ondalık kabul edilir", async () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Tutar") as HTMLInputElement;

    input.focus();
    type(input, "1234,5");

    expect(screen.getByTestId("ham")).toHaveTextContent("1234.5");

    fireEvent.blur(input);
    await waitFor(() => expect(input.value).toBe("1.234,50"));

    // Aynı sayı NOKTAYLA yazıldığında da aynı sonucu vermeli.
    input.focus();
    fireEvent.change(input, { target: { value: "" } });
    type(input, "1234.5");

    expect(screen.getByTestId("ham")).toHaveTextContent("1234.5");

    fireEvent.blur(input);
    await waitFor(() => expect(input.value).toBe("1.234,50"));
  });

  /*
   * "ARDINDAKİ RAKAM SAYISI" KURALININ TUZAĞI.
   *
   * Nokta ayrımı "ardında 3 rakam varsa binlik" diyor. Kullanıcı
   * 1 → . → 5 → 0 → 0 sırasıyla yazarsa ara metin "1.500" olur ve kural
   * körü körüne uygulanırsa ondalık BİNLİĞE döner: 1,50 yazan kullanıcı
   * fazladan bir sıfır basınca 1.500 kaydeder. Sessiz ve bin kat pahalı.
   *
   * KURAL: ondalık kısım bir kez başladıysa binlik yorumuna DÖNÜLMEZ;
   * üçüncü hane yorumu değiştirmez, engellenir (kuruş iki hanedir).
   */
  it("ondalık başladıktan sonra üçüncü hane tutarı bin katına çıkarmaz", () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Tutar") as HTMLInputElement;
    input.focus();

    // NOKTAYLA: 1 . 5 0 0
    type(input, "1.50");
    expect(input.value).toBe("1,50");
    expect(screen.getByTestId("ham")).toHaveTextContent("1.5");

    type(input, "0");
    expect(input.value).toBe("1,50");
    expect(screen.getByTestId("ham")).toHaveTextContent("1.5");
    expect(screen.queryByText("1500")).toBeNull();
  });

  it("virgülde de üçüncü hane tutarı bin katına çıkarmaz", () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Tutar") as HTMLInputElement;
    input.focus();

    type(input, "1,50");
    expect(input.value).toBe("1,50");

    type(input, "0");
    expect(input.value).toBe("1,50");
    expect(screen.getByTestId("ham")).toHaveTextContent("1.5");
  });

  it("tek seferde yapıştırılan \"1.500\" binlik sayılır", () => {
    render(<Sahne />);

    const input = screen.getByLabelText("Tutar") as HTMLInputElement;

    // Yapıştırma: Türkçe biçimli metin binlik taşır. Elle yazılan
    // "1.5" ile ayrımı, ayıracın ARDINDAKİ rakam sayısı yapıyor —
    // binlik öbeği her zaman üç rakamdır.
    fireEvent.change(input, { target: { value: "1.500" } });

    expect(input.value).toBe("1.500");
    expect(screen.getByTestId("ham")).toHaveTextContent("1500");
  });

  it("diyalog içinde de aynı üç söz geçerli", async () => {
    render(<Sahne inModal />);

    const input = screen.getByLabelText("Tutar") as HTMLInputElement;

    // Açılıştaki odaklama setTimeout(0) ile geliyor; ölçtüğümüz şey
    // açılış odağı olmasın.
    await waitFor(() => expect(document.activeElement).not.toBe(document.body));

    input.focus();
    type(input, "2814000");

    // Diyalog kancası her tuşta yeniden kurulsaydı odak başlıktaki
    // ✕ düğmesine kaçar ve metin tek karakterde kalırdı.
    expect(input.value).toBe("2.814.000");
    expect(document.activeElement).toBe(input);

    insertAt(input, 1, "5");

    expect(input.value).toBe("25.814.000");
    expect(input.selectionStart).toBe(2);

    fireEvent.change(input, { target: { value: "1234.5" } });
    expect(screen.getByTestId("ham")).toHaveTextContent("1234.5");
  });
});
