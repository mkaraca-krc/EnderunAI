import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { HataSiniri } from "@/components/erp/hata-siniri";

/**
 * HATA SINIRI — BEYAZ EKRAN YERİNE HATA EKRANI.
 *
 * BU TESTİN ÖLÇTÜĞÜ ŞEY EKRANIN VARLIĞI, mesajın metni değil.
 * Metne bağlanan bir test, kelime değişince kırılır ve kimse
 * sınırın hâlâ çalışıp çalışmadığını öğrenemez. Ölçüt: çöken ağacın
 * yerine GÖRÜNÜR bir şey geliyor mu.
 */

const konsol = vi.spyOn(console, "error");

beforeEach(() => {
  /*
   * React, sınır yakalasa BİLE hatayı konsola yazıyor. Susturulmazsa
   * test çıktısı okunmaz olur; susturma testin ölçtüğü şeyi
   * değiştirmiyor.
   */
  konsol.mockImplementation(() => {});
});

afterEach(() => {
  konsol.mockReset();
});

function Patlayan(): React.ReactElement {
  throw new TypeError("Cannot read properties of undefined (reading '0')");
}

describe("hata sınırı", () => {
  it("çöken bileşenin yerine hata ekranı gelir — boş ekran DEĞİL", () => {
    const { container } = render(
      <HataSiniri nerede="kabuk" bicim="tam">
        <Patlayan />
      </HataSiniri>,
    );

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.getByText(/Bir şeyler ters gitti/)).toBeInTheDocument();

    /*
     * ASIL İDDİA: ekran BOŞ DEĞİL. Sınır olmasaydı React ağacı
     * kökünden söker ve container boş kalırdı — canlıda görülen
     * beyaz ekran tam olarak budur.
     */
    expect(container.textContent?.trim().length ?? 0).toBeGreaterThan(0);
  });

  it("sağlam ağaç olduğu gibi geçer", () => {
    render(
      <HataSiniri nerede="içerik" bicim="govde">
        <p>Ödeme Satırları</p>
      </HataSiniri>,
    );

    expect(screen.getByText("Ödeme Satırları")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  /**
   * KAYIT YAPISAL BİLGİ TAŞIR, KİŞİSEL VERİ TAŞIMAZ.
   *
   * Bildirimin gönderdiği alanlar tek tek sınanıyor: fazladan bir
   * alan eklenirse bu test bunu yakalar.
   */
  it("bildirim yalnız yapısal alanları taşır", async () => {
    const bildirilen: unknown[] = [];

    render(
      <HataSiniri
        nerede="kabuk"
        bicim="tam"
        onHata={(bilgi) => bildirilen.push(bilgi)}
      >
        <Patlayan />
      </HataSiniri>,
    );

    await waitFor(() => expect(bildirilen).toHaveLength(1));

    const bilgi = bildirilen[0] as Record<string, unknown>;

    expect(Object.keys(bilgi).sort()).toEqual([
      "hataAdi",
      "mesaj",
      "nerede",
      "yol",
    ]);
    expect(bilgi.nerede).toBe("kabuk");
    expect(bilgi.hataAdi).toBe("TypeError");
    expect(String(bilgi.mesaj).length).toBeLessThanOrEqual(200);
  });

  /**
   * MESAJ KISALTILIYOR.
   *
   * Bir servis hatası iş metnini mesajın içinde taşıyabilir; uzun
   * mesaj günlüğe iş verisi sızdırmanın yoludur.
   */
  it("uzun mesaj 200 karaktere kısaltılır", async () => {
    const bildirilen: { mesaj: string }[] = [];

    function UzunHata(): React.ReactElement {
      throw new Error("x".repeat(500));
    }

    render(
      <HataSiniri
        nerede="içerik"
        bicim="govde"
        onHata={(bilgi) => bildirilen.push(bilgi)}
      >
        <UzunHata />
      </HataSiniri>,
    );

    await waitFor(() => expect(bildirilen).toHaveLength(1));
    expect(bildirilen[0].mesaj).toHaveLength(200);
  });

  /**
   * "YENİDEN DENE" SAYFAYI YENİLEMEZ, DURUMU SIFIRLAR.
   *
   * `window.location.reload()` açık paneli, filtreyi ve kaydırmayı
   * siler; kullanıcıyı işinin başına döndürür.
   */
  it("yeniden dene sağlam ağacı geri getirir", async () => {
    let patlasin = true;

    function BazenPatlayan(): React.ReactElement {
      if (patlasin) throw new Error("geçici");
      return <p>Geri geldi</p>;
    }

    render(
      <HataSiniri nerede="içerik" bicim="govde">
        <BazenPatlayan />
      </HataSiniri>,
    );

    expect(screen.getByRole("alert")).toBeInTheDocument();

    patlasin = false;
    await userEvent.click(screen.getByRole("button", { name: "Yeniden Dene" }));

    expect(screen.getByText("Geri geldi")).toBeInTheDocument();
  });
});
