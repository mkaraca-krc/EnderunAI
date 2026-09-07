import { render, screen, fireEvent } from "@testing-library/react";
import { useEffect, useState, type ReactNode } from "react";
import { describe, expect, it } from "vitest";

/**
 * ASIL KABUL ÖLÇÜTÜ: PANEL SÖKÜLMÜYOR.
 *
 * ═══ NEDEN TASLAK YETMEZ (Mehmet, 2026-09-07) ═══
 *
 * *"Taslağın durması bir BELİRTİ. Asıl hedef panelin SÖKÜLMEMESİ.
 * Taslak yanlışlıkla da düzelebilir — ör. `localStorage`'a yazılırsa
 * panel yine her geçişte sökülür ama taslak durur. O zaman 'düzeldi'
 * der, M3/2c-2'nin canlı bağlantısı yine her ekran değişiminde kopar."*
 *
 * Bu yüzden ölçülen şey metin değil, **monte sayısı**.
 *
 * ═══ İKİ AYAK — BİRİ OLMADAN DİĞERİ HİÇBİR ŞEY SÖYLEMEZ ═══
 *
 * · KÖK YERLEŞİMİ  : üç ekran değişiminden sonra sayaç **1**.
 * · SAYFA YERLEŞİMİ: aynı ölçüm **4** vermeli.
 *
 * İkincisi sondanın kusuru GÖRDÜĞÜNÜN kanıtı. 4 çıkmazsa düzenek
 * üretimi taklit etmiyordur ve 1 sonucu da bir şey söylemez (Kural 81).
 */

/** Monte sayısını sayan, panel yerine geçen en küçük bileşen. */
function SahtePanel({ say }: { say: () => void }) {
  const [taslak, setTaslak] = useState("");

  useEffect(() => {
    say();
  }, [say]);

  return (
    <div>
      <span data-testid="panel">panel</span>
      <input
        aria-label="taslak"
        value={taslak}
        onChange={(e) => setTaslak(e.target.value)}
      />
    </div>
  );
}

/* ROTA DEĞİŞİMİ = FARKLI BİLEŞEN TİPİ. Üretimde `{children}` konumunda
   `GorevlerPage` → `YapilacaklarPage` geçişi olur ve React alt ağacı
   söker; model bunu yansıtmazsa sonuç da yansıtmaz. */
function SayfaA() {
  return <p>A</p>;
}
function SayfaB() {
  return <section>B</section>;
}
function SayfaC() {
  return <article>C</article>;
}
function SayfaD() {
  return <aside>D</aside>;
}

/** KÖK YERLEŞİMİ: panel `{children}`'ın DIŞINDA. */
function KokYerlesimi({ children, say }: { children: ReactNode; say: () => void }) {
  return (
    <div>
      <main>{children}</main>
      <SahtePanel say={say} />
    </div>
  );
}

/*
 * SAYFA YERLEŞİMİ: HER SAYFA KENDİ PANELİNİ RENDER EDER.
 *
 * İLK MODELİM YANLIŞTI VE SONDA 1 VERDİ (4 beklenirken): tek bir
 * `SayfaPanelli` bileşenine `Sayfa` prop'u geçiriyordum. React aynı
 * TİPTEKİ bileşeni koruyor, dolayısıyla içindeki panel de sökülmüyordu.
 *
 * Üretimde durum farklı: `GorevlerPage` ve `YapilacaklarPage` AYRI
 * bileşenler ve her biri kendi `<ErpShell>`'ini (dolayısıyla kendi
 * panelini) kuruyor. Model bunu yansıtmalı — Kural 81, üçüncü kez.
 */
function SayfaAPanelli({ say }: { say: () => void }) {
  return (
    <>
      <SayfaA />
      <SahtePanel say={say} />
    </>
  );
}
function SayfaBPanelli({ say }: { say: () => void }) {
  return (
    <>
      <SayfaB />
      <SahtePanel say={say} />
    </>
  );
}
function SayfaCPanelli({ say }: { say: () => void }) {
  return (
    <>
      <SayfaC />
      <SahtePanel say={say} />
    </>
  );
}
function SayfaDPanelli({ say }: { say: () => void }) {
  return (
    <>
      <SayfaD />
      <SahtePanel say={say} />
    </>
  );
}

function KabukYok({ children }: { children: ReactNode }) {
  return (
    <div>
      <main>{children}</main>
    </div>
  );
}

describe("mesaj paneli monte sayısı", () => {
  it("KÖK YERLEŞİMİ: üç ekran değişiminden sonra sayaç 1", () => {
    let sayac = 0;
    const say = () => {
      sayac += 1;
    };

    const { rerender } = render(
      <KokYerlesimi say={say}>
        <SayfaA />
      </KokYerlesimi>
    );

    fireEvent.change(screen.getByLabelText("taslak"), {
      target: { value: "yarım kalmış mesaj" },
    });

    for (const S of [SayfaB, SayfaC, SayfaD]) {
      rerender(
        <KokYerlesimi say={say}>
          <S />
        </KokYerlesimi>
      );
    }

    expect(
      sayac,
      "Panel kökte yaşıyorsa ekran değişimi onu SÖKMEZ; monte sayısı 1 kalır. " +
        "Büyükse panel her geçişte yeniden doğuyor demektir ve canlı bağlantı " +
        "da her geçişte kopar."
    ).toBe(1);

    // Belirti de doğrulanıyor — ama kabul ölçütü yukarıdaki sayı.
    expect((screen.getByLabelText("taslak") as HTMLInputElement).value).toBe(
      "yarım kalmış mesaj"
    );
  });

  it("SAYFA YERLEŞİMİ (sonda ayağı): aynı ölçüm 4 verir", () => {
    let sayac = 0;
    const say = () => {
      sayac += 1;
    };

    const { rerender } = render(
      <KabukYok>
        <SayfaAPanelli say={say} />
      </KabukYok>
    );

    fireEvent.change(screen.getByLabelText("taslak"), {
      target: { value: "yarım kalmış mesaj" },
    });

    for (const S of [SayfaBPanelli, SayfaCPanelli, SayfaDPanelli]) {
      rerender(
        <KabukYok>
          <S say={say} />
        </KabukYok>
      );
    }

    expect(
      sayac,
      "Bu ayak, yukarıdaki testin gerçekten bir şey ölçtüğünü kanıtlar. " +
        "4 çıkmazsa düzenek üretimi taklit etmiyordur ve 1 sonucu da bir " +
        "şey söylemez (Kural 81)."
    ).toBe(4);

    // Kusurun BELİRTİSİ: taslak da gitmiş olmalı.
    expect((screen.getByLabelText("taslak") as HTMLInputElement).value).toBe("");
  });
});
