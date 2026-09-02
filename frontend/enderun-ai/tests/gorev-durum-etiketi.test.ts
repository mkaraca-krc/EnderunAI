import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import {
  DURUM_ETIKETLERI,
  DURUM_RENKLERI,
  DURUM_ROZET_TURU,
  ONCELIK_ETIKETLERI,
} from "@/services/work-task.service";

/*
 * GÖREV DURUMU: TEK ETİKET KAYNAĞI.
 *
 * NEDEN VAR: Genel Müdür listede "Açık" gören bir görevin detayında
 * "Devam Ediyor" gördü. Kayıt DEĞİŞMEMİŞTİ — ölçüldü: Status = 1,
 * UpdatedAtUtc boş, StartedAtUtc boş. İki ekran AYNI SAYIYI farklı
 * okuyordu.
 *
 * SEBEP: detay ekranı kendi sabitlerini yazmış ve YOĞUN 0-TABANLI
 * numaralandırma varsaymıştı (DURUM_OPEN = 0), oysa arka uçtaki
 * `WorkTaskStatus` seyrek: Open=1, InProgress=2, Completed=4,
 * Cancelled=5, Approved=6, Returned=7. Üçü atlanmış. Koddaki yorum
 * "Sunucudaki WorkTaskStatus ile aynı sıra" diyordu — yanlış bir
 * iddiaydı ve kimse ölçmemişti.
 *
 * KUSUR TEK KAYITTA DEĞİLDİ: altı gerçek değerden üçü ayrışıyordu
 * (1, 2, 4), ikisi hiç çevrilmiyordu (6, 7). Yalnız 1 fark edildi.
 *
 * BU TEST NEYİ ÖLÇER: etiket kaynağı ARKA UCUN KENDİSİYLE karşılaştırılır.
 * Ön yüzdeki bir kopyayla değil — kopya da yanlış olabilir, nitekim
 * öyleydi (`Draft=0` ve `Waiting=3` arka uçta HİÇ YOKTU, `Approved=6`
 * ve `Returned=7` ise ön yüzde yoktu).
 *
 * DÜRÜST SINIR: enum'u C# kaynağından metin olarak okur. Arka uç
 * değeri hesaplanarak üretirse (ör. `Open = 1 << 0`) bu okuma çöker;
 * o yüzden ayrıştırma başarısız olursa test SUSMAZ, düşer.
 */

const KOK = join(__dirname, "..");
const ENUM_DOSYASI = join(
  KOK, "..", "..", "backend", "EnderunAI.Api", "Models", "WorkTask.cs",
);

function enumDegerleri(kaynak: string, ad: string): Map<string, number> {
  const govde = kaynak.match(
    new RegExp(`public enum ${ad}\\s*\\{([\\s\\S]*?)\\}`),
  );
  if (!govde) {
    throw new Error(`${ad} enum'u ${ENUM_DOSYASI} içinde bulunamadı.`);
  }
  const bulunan = new Map<string, number>();
  for (const satir of govde[1].split("\n")) {
    const temiz = satir.replace(/\/\/.*$/, "").trim();
    const m = temiz.match(/^([A-Za-z][A-Za-z0-9_]*)\s*=\s*(\d+)\s*,?$/);
    if (m) bulunan.set(m[1], Number(m[2]));
  }
  return bulunan;
}

const kaynak = readFileSync(ENUM_DOSYASI, "utf8");
const arkaUcDurum = enumDegerleri(kaynak, "WorkTaskStatus");
const arkaUcOncelik = enumDegerleri(kaynak, "WorkTaskPriority");

describe("görev durumu — tek etiket kaynağı", () => {
  it("arka uç enum'u okunabildi (pozitif kontrol)", () => {
    // Boş sonuç yokluğun kanıtı değildir: ayrıştırma çökerse aşağıdaki
    // bütün testler sessizce yeşil verirdi.
    expect(arkaUcDurum.size).toBeGreaterThanOrEqual(6);
    expect(arkaUcDurum.get("Open")).toBe(1);
    expect(arkaUcOncelik.size).toBeGreaterThanOrEqual(4);
  });

  it("her arka uç durumunun TAM OLARAK BİR etiketi var", () => {
    const eksik = [...arkaUcDurum.entries()]
      .filter(([, deger]) => !(deger in DURUM_ETIKETLERI))
      .map(([ad, deger]) => `${ad}=${deger}`);
    expect(eksik).toEqual([]);
  });

  it("her arka uç durumunun TAM OLARAK BİR rengi var", () => {
    const eksik = [...arkaUcDurum.entries()]
      .filter(([, deger]) => !(deger in DURUM_RENKLERI))
      .map(([ad, deger]) => `${ad}=${deger}`);
    expect(eksik).toEqual([]);
  });

  it("her arka uç durumunun bir rozet türü var", () => {
    const eksik = [...arkaUcDurum.entries()]
      .filter(([, deger]) => !(deger in DURUM_ROZET_TURU))
      .map(([ad, deger]) => `${ad}=${deger}`);
    expect(eksik).toEqual([]);
  });

  it("etiket kaynağında arka uçta OLMAYAN durum yok (hayalet değer)", () => {
    const gecerli = new Set([...arkaUcDurum.values()]);
    const hayalet = Object.keys(DURUM_ETIKETLERI)
      .map(Number)
      .filter((deger) => !gecerli.has(deger));
    expect(hayalet).toEqual([]);
  });

  it("her arka uç önceliğinin bir etiketi var, hayalet öncelik yok", () => {
    const eksik = [...arkaUcOncelik.entries()]
      .filter(([, deger]) => !(deger in ONCELIK_ETIKETLERI))
      .map(([ad, deger]) => `${ad}=${deger}`);
    const gecerli = new Set([...arkaUcOncelik.values()]);
    const hayalet = Object.keys(ONCELIK_ETIKETLERI)
      .map(Number)
      .filter((deger) => !gecerli.has(deger));
    expect({ eksik, hayalet }).toEqual({ eksik: [], hayalet: [] });
  });
});
