import { describe, expect, it } from "vitest";

import { parseScannedItem } from "@/lib/inventory/qr";

/**
 * QR / BARKOD OKUTMA ÇÖZÜMLEMESİ.
 *
 * Kasada üç farklı şey aynı kutuya okutuluyor ve üçü de doğru
 * ayrılmalı. Kimlik ile arama terimi karıştırılırsa bir GUID metin
 * olarak aratılır, hiçbir sonuç dönmez ve etiket okutmak SESSİZCE
 * çalışmaz — kullanıcı neden olmadığını da anlayamaz.
 */
describe("parseScannedItem", () => {
  it("bizim etiketimizdeki URL'den kimliği söker", () => {
    const id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    expect(
      parseScannedItem(`https://erp.example.com/depo-stok/malzeme/${id}`)
    ).toEqual({ kind: "id", id });
  });

  it("çıplak GUID'i de kimlik sayar", () => {
    const id = "3F2504E0-4F89-11D3-9A0C-0305E82C3301";

    expect(parseScannedItem(id)).toEqual({
      kind: "id",
      id: id.toLowerCase(),
    });
  });

  it("üretici barkodunu arama terimi sayar", () => {
    expect(parseScannedItem("8691234567890")).toEqual({
      kind: "term",
      term: "8691234567890",
    });
  });

  it("stok kodunu arama terimi sayar", () => {
    expect(parseScannedItem("MLZ-0042")).toEqual({
      kind: "term",
      term: "MLZ-0042",
    });
  });

  it("okuyucunun eklediği boşlukları temizler", () => {
    expect(parseScannedItem("  MLZ-0042  ")).toEqual({
      kind: "term",
      term: "MLZ-0042",
    });
  });

  it("boş okumada null döner — boş terim tüm kartları getirirdi", () => {
    expect(parseScannedItem("   ")).toBeNull();
  });
});
