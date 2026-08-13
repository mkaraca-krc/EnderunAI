import { describe, expect, it } from "vitest";

import {
  defaultSelection,
  isSelectable,
  toRequestLines,
} from "@/lib/purchasing/material-requirement-selection";
import type { ProjectMaterialRequirementLine } from "@/services/project-material-requirement.service";

/**
 * Malzeme ihtiyacı listesinde seçim kuralı.
 *
 * NEDEN VAR: aynı kural üç yerde işliyor — satır onay kutusu, "eksikleri
 * seç" düğmesi ve gönderilen miktar. Üçü ayrı yazılsaydı biri değişip
 * diğerleri unutulur, kullanıcı seçemediği bir satırı toplu seçimde
 * seçilmiş görürdü.
 */
function line(
  overrides: Partial<ProjectMaterialRequirementLine> = {},
): ProjectMaterialRequirementLine {
  return {
    inventoryItemId: "item-1",
    materialCode: "KBL-01",
    materialName: "NYA Kablo",
    unit: "m",
    requiredQuantity: 210,
    stockQuantity: 0,
    openRequestedQuantity: 0,
    shortageQuantity: 210,
    sourceLineCount: 1,
    canRequest: true,
    ...overrides,
  };
}

describe("malzeme ihtiyacı seçimi", () => {
  it("eksiği olan ve stok kartına bağlı satır seçilebilir", () => {
    expect(isSelectable(line())).toBe(true);
  });

  /**
   * Stok kartı olmayan malzeme talep EDİLEMEZ: depo mevcudu ve açık
   * talep onun üzerinden düşülüyor, bağsız satır ikinci kez talep
   * edilmeyi engelleyemez.
   */
  it("stok kartı olmayan satır seçilemez", () => {
    expect(isSelectable(line({ inventoryItemId: null, canRequest: false }))).toBe(
      false,
    );
  });

  it("eksiği kalmayan satır seçilemez", () => {
    expect(isSelectable(line({ shortageQuantity: 0 }))).toBe(false);
  });

  it("toplu seçim yalnız seçilebilir satırları alır", () => {
    const selection = defaultSelection([
      line({ inventoryItemId: "a" }),
      line({ inventoryItemId: "b", shortageQuantity: 0 }),
      line({ inventoryItemId: null, canRequest: false }),
      line({ inventoryItemId: "d", shortageQuantity: 12.5 }),
    ]);

    expect(Object.keys(selection).sort()).toEqual(["a", "d"]);
    expect(selection.d).toBe("12.5");
  });

  it("seçilen miktar kalan eksik kadar gelir", () => {
    const selection = defaultSelection([
      line({ inventoryItemId: "a", shortageQuantity: 150 }),
    ]);

    expect(selection.a).toBe("150");
  });

  /**
   * Okunamayan giriş SIFIR gider; sunucu bunu "kalan eksiğin tamamı"
   * diye yorumlar ve zaten kendi hesapladığı eksikle sınırlar. Ekranın
   * uydurma bir sayı göndermesi, sunucunun kırpma korumasını
   * anlamsızlaştırırdı.
   */
  it("geçersiz miktar sıfıra düşer", () => {
    const lines = toRequestLines({ a: "abc", b: "-5", c: "0" });

    expect(lines.every((x) => x.quantity === 0)).toBe(true);
  });

  it("virgüllü miktar okunur", () => {
    expect(toRequestLines({ a: "12,5" })).toEqual([
      { inventoryItemId: "a", quantity: 12.5 },
    ]);
  });
});
