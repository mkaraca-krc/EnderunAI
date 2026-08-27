import { describe, expect, it } from "vitest";

import {
  odemeHesabiSecilebilirMi,
  secilebilirOdemeHesaplari,
} from "@/lib/cheques/odeme-hesabi";
import { CashAccountType } from "@/services/cash-account.service";
import { ChequeDirection, ChequeStatus } from "@/services/cheque.service";

const kasa = { id: "1", type: CashAccountType.Cash };
const banka = { id: "2", type: CashAccountType.Bank };

describe("ödeme hesabı süzgeci", () => {
  it("verilen çeğin ödenmesinde kasayı elemeli", () => {
    expect(
      odemeHesabiSecilebilirMi(
        ChequeDirection.Issued,
        ChequeStatus.Issued,
        ChequeStatus.Paid,
        kasa
      )
    ).toBe(false);
  });

  it("verilen çeğin ödenmesinde bankayı bırakmalı", () => {
    expect(
      odemeHesabiSecilebilirMi(
        ChequeDirection.Issued,
        ChequeStatus.Issued,
        ChequeStatus.Paid,
        banka
      )
    ).toBe(true);
  });

  // Kural DAR olmalı: elden tahsil gerçek bir akış.
  it("alınan çeğin tahsilinde kasayı elememeli", () => {
    expect(
      odemeHesabiSecilebilirMi(
        ChequeDirection.Received,
        ChequeStatus.Portfolio,
        ChequeStatus.Collected,
        kasa
      )
    ).toBe(true);
  });

  it("verilen çeğin karşılıksız çıkmasında kasayı elememeli", () => {
    expect(
      odemeHesabiSecilebilirMi(
        ChequeDirection.Issued,
        ChequeStatus.Issued,
        ChequeStatus.Bounced,
        kasa
      )
    ).toBe(true);
  });

  it("listeden yalnız kasayı çıkarmalı, sırayı bozmamalı", () => {
    const hepsi = [kasa, banka];

    expect(
      secilebilirOdemeHesaplari(
        ChequeDirection.Issued,
        ChequeStatus.Issued,
        ChequeStatus.Paid,
        hepsi
      )
    ).toEqual([banka]);

    expect(
      secilebilirOdemeHesaplari(
        ChequeDirection.Received,
        ChequeStatus.Portfolio,
        ChequeStatus.Collected,
        hepsi
      )
    ).toEqual(hepsi);
  });
});
