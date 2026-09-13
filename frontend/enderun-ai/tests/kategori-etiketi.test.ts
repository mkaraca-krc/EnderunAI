import { describe, expect, it } from "vitest";
import { kategoriEtiketi } from "@/lib/inventory/kategori-etiketi";

describe("kategori etiketi", () => {
  it("yapılandırılmış kategoriyi tercih eder", () => {
    expect(kategoriEtiketi({ categoryLabel: "Özel İmalat", category: null }))
      .toBe("Özel İmalat");
  });

  it("eski serbest metne düşer", () => {
    expect(kategoriEtiketi({ categoryLabel: null, category: "AYDINLATMA" }))
      .toBe("AYDINLATMA");
  });

  it("ekrandan açılan kart artık '—' göstermez", () => {
    // Ölçülen hâl: category=null, categoryLabel dolu.
    expect(kategoriEtiketi({ categoryLabel: "Kablo", category: null })).not.toBe("—");
  });

  it("ikisi de yoksa '—'", () => {
    expect(kategoriEtiketi({})).toBe("—");
    expect(kategoriEtiketi({ categoryLabel: "  ", category: "" })).toBe("—");
  });
});
