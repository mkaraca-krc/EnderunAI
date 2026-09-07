import { render, screen, fireEvent } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import {
  TaslakDeposuSaglayici,
  useTaslakDeposu,
} from "@/lib/mesajlasma/taslak-deposu";

/**
 * İKİ YÜZEY AYNI TASLAĞI GÖRÜR — VE KONUŞMA AYRIMI BOZULMAZ.
 *
 * ═══ DOĞURAN KUSUR ═══
 *
 * Taslak panelde tutuluyordu; `/mesajlar` tam sayfası onu görmüyor,
 * kendi yerel durumuna düşüyordu. Panelde yazıp `/mesajlar`'a giden
 * yazdığını KAYBEDİYORDU.
 *
 * **Ve bu azınlığın yolu değil:** dar pencerede ✉ düğmesi paneli
 * açmıyor, doğrudan `/mesajlar`'a götürüyor — telefondan giren herkes
 * bu yoldan geçiyor.
 *
 * ═══ ÜÇ ŞART, ÜÇÜ AYRI ═══
 *
 * Paylaşım tek başına yetmez: tek bir GENEL taslak alanı da paylaşımı
 * sağlar ama konuşma ayrımını bozar. Üçü ayrı ayrı sınanıyor.
 */

/** Bir yüzeyi (panel ya da tam sayfa) taklit eden en küçük bileşen. */
function Yuzey({ ad }: { ad: string }) {
  const depo = useTaslakDeposu();
  const [secili, setSecili] = useState("A");

  const taslak = depo ? (depo.taslaklar[secili] ?? "") : "";

  return (
    <div>
      <button type="button" onClick={() => setSecili("A")}>
        {ad}-A
      </button>
      <button type="button" onClick={() => setSecili("B")}>
        {ad}-B
      </button>
      <input
        aria-label={`${ad}-kutu`}
        value={taslak}
        onChange={(e) => depo?.taslakYaz(secili, e.target.value)}
      />
    </div>
  );
}

const kutu = (ad: string) =>
  screen.getByLabelText(`${ad}-kutu`) as HTMLInputElement;

describe("taslak iki yüzey arasında paylaşılır", () => {
  it("panelde yazılan TAM SAYFADA görünür", () => {
    render(
      <TaslakDeposuSaglayici>
        <Yuzey ad="panel" />
        <Yuzey ad="sayfa" />
      </TaslakDeposuSaglayici>
    );

    fireEvent.change(kutu("panel"), { target: { value: "yarım mesaj" } });

    expect(
      kutu("sayfa").value,
      "Panelde yazılan metin tam sayfada görünmüyor — iki yüzey ayrı " +
        "kaynak kullanıyor demektir. Dar pencerede giden kullanıcı " +
        "yazdığını kaybeder."
    ).toBe("yarım mesaj");
  });

  it("tam sayfada yazılan PANELDE görünür (ters yön)", () => {
    render(
      <TaslakDeposuSaglayici>
        <Yuzey ad="panel" />
        <Yuzey ad="sayfa" />
      </TaslakDeposuSaglayici>
    );

    fireEvent.change(kutu("sayfa"), { target: { value: "sayfadan yazıldı" } });
    expect(kutu("panel").value).toBe("sayfadan yazıldı");
  });

  it("KONUŞMA AYRIMI BOZULMUYOR: A'nın metni B'de görünmüyor", () => {
    // Tek genel alan yukarıdaki iki testi GEÇER, bunu geçemez.
    render(
      <TaslakDeposuSaglayici>
        <Yuzey ad="panel" />
      </TaslakDeposuSaglayici>
    );

    fireEvent.change(kutu("panel"), { target: { value: "A metni" } });
    fireEvent.click(screen.getByText("panel-B"));

    expect(
      kutu("panel").value,
      "B konuşmasında A'nın metni görünüyor — depo konuşma başına " +
        "tutmuyor demektir."
    ).toBe("");

    fireEvent.click(screen.getByText("panel-A"));
    expect(kutu("panel").value).toBe("A metni");
  });
});
