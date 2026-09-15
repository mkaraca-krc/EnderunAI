import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import {
  stokSeviyesi,
  stokSeviyesiEtiketi,
  stokSeviyesiAciklamasi,
} from "@/lib/inventory/stok-seviyesi";

/**
 * S1 — "NORMAL" ÖLÇÜLMEDEN YAZILMAZ.
 *
 * Sütun iki hâl tanıyordu ve kritik olmayan HER satıra `Normal`
 * yazıyordu. Canlıda ölçüldü (2026-09-16): stok seviyesi 0 satır,
 * stok 0, hareket 0 — yani dokuz kartın dokuzunda `Normal` yazıyordu
 * ve hiçbiri ölçülmemişti.
 *
 * EN ÖNEMLİ TEST: `SEVIYE_TANIMSIZSA_NORMAL_YAZMAZ`.
 */
describe("stok seviyesi — üç hâl", () => {
  it("SEVIYE_TANIMSIZSA_NORMAL_YAZMAZ", () => {
    const s = stokSeviyesi(false, false);
    expect(s).toBe("tanimsiz");
    expect(stokSeviyesiEtiketi(s)).toBe("—");
    expect(stokSeviyesiEtiketi(s)).not.toBe("Normal");
  });

  it("seviye tanımsızken 'kritik' bayrağı da NORMAL'e çevirmez", () => {
    // Sunucu bir sebeple kritik derse ama seviye satırı yoksa,
    // sessizce 'Normal' demek en kötü cevaptır.
    expect(stokSeviyesi(false, true)).toBe("tanimsiz");
  });

  it("seviye tanımlı + kritik -> kritik", () => {
    expect(stokSeviyesi(true, true)).toBe("kritik");
    expect(stokSeviyesiEtiketi(stokSeviyesi(true, true))).toBe("Kritik");
  });

  it("seviye tanımlı + kritik değil -> normal", () => {
    expect(stokSeviyesi(true, false)).toBe("normal");
    expect(stokSeviyesiEtiketi(stokSeviyesi(true, false))).toBe("Normal");
  });

  it("her hâlin bir açıklaması var (sessiz '—' bırakılmaz)", () => {
    for (const s of ["kritik", "normal", "tanimsiz"] as const) {
      expect(stokSeviyesiAciklamasi(s).length).toBeGreaterThan(8);
    }
    expect(stokSeviyesiAciklamasi("tanimsiz")).toMatch(/ölçülmedi/);
  });
});

/**
 * KAYNAK MUHAFIZI — başlık ve kavram ayrımı.
 *
 * "Durum" sözcüğü kart durumu (Aktif/Pasif) ile stok seviyesini
 * karıştırıyordu. Başlık geri dönerse bu test kırmızı yanar.
 */
describe("S1 kaynak muhafızı", () => {
  const sayfa = readFileSync(
    join(__dirname, "..", "app", "depo-stok", "page.tsx"),
    "utf8"
  );

  it("tarama sağlığı: sayfa okundu ve sütun tanımı bulundu", () => {
    expect(sayfa.length).toBeGreaterThan(1000);
    expect(sayfa).toContain('key: "durum"');
  });

  it('başlık "Stok Seviyesi" — "Stok Durumu"na geri dönmemiş', () => {
    expect(sayfa).toContain('header: "Stok Seviyesi"');
    expect(sayfa).not.toContain('header: "Stok Durumu"');
    expect(sayfa).not.toContain('header: "Durum"');
  });

  it("kart aktifliği satırda işaretleniyor (Malzeme sütununda rozet)", () => {
    expect(sayfa).toContain("kartDurumEtiketi(row.isActive)");
    // Dışa aktarımda da: ekranda rozet, CSV'de sözcük.
    expect(sayfa).toContain("kartDurumIsareti(row.isActive)");
  });

  it("arşiv dahil çekiliyor (pasif kart listede görünebilmeli)", () => {
    expect(sayfa).toContain("includeInactive: true");
  });
});
