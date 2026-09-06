import { render, screen, fireEvent } from "@testing-library/react";
import { useState, type ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

/**
 * PANEL KABUKTA YAŞAR — ROTA DEĞİŞİMİ ONU SÖKMEZ.
 *
 * ═══ NE SINANIYOR ═══
 *
 * Mehmet'in koyduğu kabul ölçütü: *"panel açıkken ve kutuya yazı
 * yazılmışken başka bir ekrana geç → panel açık kalmalı, taslak
 * yerinde durmalı."*
 *
 * Sahte bir kabuk kuruluyor: `{children}` + panel. `children`
 * değiştiriliyor (rota değişiminin React ağacındaki karşılığı) ve
 * panelin AYAKTA, taslağın YERİNDE kaldığı sınanıyor.
 *
 * ═══ SABOTAJ BUNU KIRAR ═══
 *
 * Panel `children` İÇİNE taşınırsa, `children` değiştiğinde sökülür,
 * durum sıfırlanır ve taslak kaybolur — test kırmızı yanar. Aynı
 * arıza canlıda "yazdığım mesaj kayboldu" olarak görünürdü.
 *
 * ═══ NEDEN GERÇEK BİLEŞEN DEĞİL ═══
 *
 * Gerçek `MesajBaloncugu` ağ çağrısı yapıyor ve `next/navigation`
 * istiyor. Sınanan şey ONUN İÇİ DEĞİL, KABUĞUN YAPISI: panel
 * `children`'ın dışında mı. Yapının kendisini `erp-shell` üzerinde
 * `mesaj-paneli-yapi.test.ts` ayrıca tutuyor; ikisi birlikte hem
 * davranışı hem yerleşimi kapsıyor.
 */

afterEach(() => vi.restoreAllMocks());

/** Panelin yerine geçen, durumu olan en küçük bileşen. */
function SahtePanel() {
  const [taslak, setTaslak] = useState("");

  return (
    <div>
      <span data-testid="panel">panel açık</span>
      <input
        aria-label="taslak"
        value={taslak}
        onChange={(e) => setTaslak(e.target.value)}
      />
    </div>
  );
}

/*
 * ROTA DEĞİŞİMİ = FARKLI BİLEŞEN TİPİ.
 *
 * İlk yazımda iki "sayfa" da aynı `<p>` idi; React aynı konumdaki
 * aynı tipteki bileşeni KORUYOR ve sabotaj ayağı kırmıyordu — yani
 * test bir şey ölçmüyordu. Next'te rota değişimi farklı bir sayfa
 * BİLEŞENİ render eder; alt ağaç gerçekten sökülür. Model bunu
 * yansıtmazsa sonuç da yansıtmaz.
 */
function SayfaA() {
  return <p>sayfa A</p>;
}

function SayfaB() {
  return <section>sayfa B</section>;
}

/** DOĞRU YERLEŞİM: panel `children`'ın DIŞINDA. */
function KabukDogru({ children }: { children: ReactNode }) {
  return (
    <div>
      <main>{children}</main>
      <SahtePanel />
    </div>
  );
}

/*
 * SABOTAJ YERLEŞİMİ — GERÇEĞE SADIK MODEL.
 *
 * İkinci denemede de yanlış modellemiştim: paneli `<main>{children}
 * <SahtePanel/></main>` diye yazınca React onu KONUMUNA göre koruyor
 * (0. çocuk tip değiştirip sökülüyor, 1. çocuk aynı kalıyor) ve
 * sabotaj yine kırmıyordu.
 *
 * Gerçekte panel SAYFA BİLEŞENİNİN KENDİ AĞACINDA olurdu. Rota
 * değişince o bileşenin tamamı — panel dahil — sökülür. Model bunu
 * yansıtıyor: her sahte sayfa kendi panelini render ediyor.
 */
function SayfaA_PanelIcerde() {
  return (
    <>
      <p>sayfa A</p>
      <SahtePanel />
    </>
  );
}

function SayfaB_PanelIcerde() {
  return (
    <>
      <section>sayfa B</section>
      <SahtePanel />
    </>
  );
}

function KabukSabotajli({ children }: { children: ReactNode }) {
  return (
    <div>
      <main>{children}</main>
    </div>
  );
}

describe("mesaj paneli kabukta yaşar", () => {
  it("rota değişince panel açık kalır ve taslak yerinde durur", () => {
    const { rerender } = render(
      <KabukDogru>
        <SayfaA />
      </KabukDogru>
    );

    fireEvent.change(screen.getByLabelText("taslak"), {
      target: { value: "yarım kalmış mesaj" },
    });

    rerender(
      <KabukDogru>
        <SayfaB />
      </KabukDogru>
    );

    expect(screen.getByText("sayfa B")).toBeTruthy();
    expect(screen.getByTestId("panel")).toBeTruthy();
    expect(
      (screen.getByLabelText("taslak") as HTMLInputElement).value,
      "Panel kabukta yaşıyorsa taslak rota değişiminden etkilenmez."
    ).toBe("yarım kalmış mesaj");
  });

  it("SABOTAJ AYAĞI: panel children içindeyse taslak KAYBOLUR", () => {
    /*
     * Bu ayak, yukarıdaki testin gerçekten bir şey ölçtüğünü kanıtlar.
     * Yerleşim bozulduğunda davranış GERÇEKTEN değişmiyorsa, yeşil
     * hiçbir şey söylemez (Kural 25).
     */
    const { rerender } = render(
      <KabukSabotajli>
        <SayfaA_PanelIcerde />
      </KabukSabotajli>
    );

    fireEvent.change(screen.getByLabelText("taslak"), {
      target: { value: "yarım kalmış mesaj" },
    });

    rerender(
      <KabukSabotajli>
        <SayfaB_PanelIcerde />
      </KabukSabotajli>
    );

    expect(screen.getByText("sayfa B")).toBeTruthy();
    expect(
      (screen.getByLabelText("taslak") as HTMLInputElement).value,
      "Panel children içindeyse rota değişimi onu SÖKER ve taslak " +
        "kaybolur. Kaybolmuyorsa yukarıdaki test bir şey ölçmüyor."
    ).toBe("");
  });
});
