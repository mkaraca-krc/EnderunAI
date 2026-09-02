import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/*
 * İKİ EKRAN AYNI DEĞERİ FARKLI OKUYAMAZ.
 *
 * NEDEN VAR: `app/gorevler/page.tsx` ile `app/gorevler/[id]/page.tsx`
 * durum → etiket eşlemesini AYRI AYRI yazmıştı. İkisi ayrıştı ve
 * Genel Müdür listede "Açık", detayda "Devam Ediyor" gördü.
 *
 * Yukarıdaki `gorev-durum-etiketi` testi kaynağın DOĞRU olduğunu
 * ölçer. Bu test kaynağın TEK olduğunu ölçer. İkisi ayrı iddialardır:
 * doğru bir kaynağın yanına ikinci bir doğru kopya konabilir ve
 * yarın ayrışır. Bugün tam olarak bu oldu.
 *
 * NASIL ÖLÇER: ekran dosyalarında durum etiketi DİZGE SABİTİ olarak
 * geçmemeli. Geçiyorsa o ekran kendi eşlemesini kurmuş demektir.
 *
 * DÜRÜST SINIR: dizge sabiti arar. Bir ekran etiketi parça
 * birleştirerek üretirse ("Devam " + "Ediyor") bu test görmez.
 * Etiketin YANLIŞ olmasına karşı değil, İKİNCİ BİR KOPYA doğmasına
 * karşı korur.
 */

const KOK = join(__dirname, "..");

const EKRANLAR = [
  join(KOK, "app", "gorevler", "page.tsx"),
  join(KOK, "app", "gorevler", "[id]", "page.tsx"),
];

// Tek kaynakta yaşayan etiketler. Ekran dosyasında görünürlerse
// orada ikinci bir eşleme doğmuş demektir.
const ETIKETLER = [
  "Devam Ediyor",
  "İade Edildi",
  "Onaylandı",
  "Tamamlandı, onay bekliyor",
];

describe("görev durumu — tek kaynak", () => {
  it("ekran dosyaları okunabildi (pozitif kontrol)", () => {
    for (const yol of EKRANLAR) {
      expect(readFileSync(yol, "utf8").length).toBeGreaterThan(500);
    }
  });

  it("hiçbir ekran durum etiketini kendi içinde yazmıyor", () => {
    const ihlaller: string[] = [];
    for (const yol of EKRANLAR) {
      const metin = readFileSync(yol, "utf8");
      for (const satir of metin.split("\n")) {
        // Yorum satırı savunma değildir; bu dosyalarda kusurun kendisi
        // yorumlarda ANLATILIYOR ve o anlatı ihlal sayılmamalı.
        const temiz = satir.trim();
        if (temiz.startsWith("*") || temiz.startsWith("//") || temiz.startsWith("/*")) {
          continue;
        }
        for (const etiket of ETIKETLER) {
          if (satir.includes(`"${etiket}"`)) {
            ihlaller.push(`${yol.replace(KOK, "")}: ${temiz}`);
          }
        }
      }
    }
    expect(ihlaller).toEqual([]);
  });

  it("detay ekranı kendi durum sabitlerini tanımlamıyor", () => {
    const metin = readFileSync(EKRANLAR[1], "utf8");
    const kendiSabitleri = metin
      .split("\n")
      .map((s) => s.trim())
      .filter((s) => /^const\s+DURUM_[A-Z_]+\s*=\s*\d+\s*;/.test(s));
    expect(kendiSabitleri).toEqual([]);
  });
});
