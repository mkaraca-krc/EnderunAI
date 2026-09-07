import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * PANEL AÇILDIĞINDA DA KAYDEDİLİR — YALNIZ KAPANIŞTA DEĞİL.
 *
 * ═══ DOĞURAN ARIZA (2026-09-06) ═══
 *
 * İlk sürümde `MessagePanelOpen` YALNIZ kapanışta yazılıyordu. Koda
 * yazılan gerekçe şuydu: *"açılış, bir sonraki kapanışta zaten
 * kaydedilir."* **Hiç kapatılmayan panel için bu cümle yanlıştı.**
 *
 * Mehmet ölçtü: paneli açık bıraktı, hiç kapatmadı, sayfayı yeniledi
 * — panel kapalı geldi. Açık bir panel "açık" olarak hiç
 * kaydedilmemişti.
 *
 * ═══ NEDEN KAYNAK TESTİ ═══
 *
 * Gerçek davranış oturum + ağ + yeniden yükleme gerektiriyor; onu
 * tarayıcıdan Mehmet doğruluyor. Bu test daha dar bir şeyi tutuyor
 * ama tam da kaybedileni: **açma yolunun yazma çağrısı taşıdığını.**
 * Asimetri geri gelirse burası kırmızı yanar.
 *
 * Testin dürüst sınırı: yazmanın SUNUCUYA ULAŞTIĞINI kanıtlamaz,
 * yalnız İSTENDİĞİNİ kanıtlar.
 */

const KAYNAK = readFileSync(
  join(__dirname, "..", "components", "mesajlar", "mesaj-baloncugu.tsx"),
  "utf8"
);

/** Bir fonksiyon gövdesini kabaca ayıklar (ilk `}, [` kapanışına kadar). */
function govde(baslangicDeseni: RegExp): string {
  const eslesme = KAYNAK.match(baslangicDeseni);
  expect(eslesme, `Fonksiyon bulunamadı: ${baslangicDeseni}`).not.toBeNull();

  const bas = eslesme!.index!;
  const son = KAYNAK.indexOf("}, [", bas);
  expect(son, "Fonksiyon sonu bulunamadı").toBeGreaterThan(bas);

  return KAYNAK.slice(bas, son);
}

describe("mesaj paneli kalıcılığı", () => {
  it("kaynak okunabiliyor ve iki yol da var (POZİTİF KONTROL)", () => {
    expect(KAYNAK).toContain("const ac = useCallback");
    expect(KAYNAK).toContain("const gercektenKapat = useCallback");
  });

  it("AÇMA yolu tercihi yazar", () => {
    expect(
      govde(/const ac = useCallback/),
      "Panel açıldığında tercih yazılmıyor. Hiç kapatılmayan bir panel " +
        "'açık' olarak hiç kaydedilmez ve yeniden yüklemede kapalı gelir."
    ).toContain("messagePanelOpen: true");
  });

  it("KAPATMA yolu tercihi yazar", () => {
    expect(govde(/const gercektenKapat = useCallback/)).toContain(
      "messagePanelOpen: false"
    );
  });

  it("iki alan da AYNI gecikmeli yazıcıdan geçer", () => {
    // Ayrı zamanlayıcılar, asimetrinin ilk hâliydi. Tek yazıcı,
    // art arda gelen değişiklikleri tek istekte birleştiriyor.
    expect(KAYNAK).toContain("TERCIH_YAZMA_GECIKMESI_MS");
    expect(KAYNAK).not.toContain("KONUSMA_YAZMA_GECIKMESI_MS");
  });
});
