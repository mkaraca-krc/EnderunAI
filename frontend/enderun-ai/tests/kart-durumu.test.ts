import { describe, expect, it } from "vitest";
import {
  kartDurumEtiketi,
  kartDurumIsareti,
} from "@/lib/inventory/kart-durumu";

/**
 * S1 — KART DURUMU İLE STOK DURUMU AYRI KAVRAMLAR.
 *
 * Ölçüm anında canlıdaki 9 malzeme kartının 9'u da pasifti ve liste
 * hepsine "Normal" diyordu. "Normal" stok seviyesiydi; kartın pasif
 * olduğunu listede hiçbir şey söylemiyordu.
 */
describe("kart durumu etiketi", () => {
  it("aktif kart 'Aktif', pasif kart 'Pasif' der", () => {
    expect(kartDurumEtiketi(true)).toBe("Aktif");
    expect(kartDurumEtiketi(false)).toBe("Pasif");
  });

  it("listede YALNIZ pasif kart işaretlenir — aktif kart gürültü üretmez", () => {
    expect(kartDurumIsareti(false)).toBe("Pasif");
    expect(kartDurumIsareti(true)).toBeNull();
  });

  it("işaret, stok seviyesi sözcükleriyle karışmaz", () => {
    // Stok seviyesi sütunu "Normal"/"Kritik" der; kart durumu ASLA.
    const isaret = kartDurumIsareti(false);
    expect(isaret).not.toBe("Normal");
    expect(isaret).not.toBe("Kritik");
  });
});
