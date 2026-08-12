import { describe, expect, it } from "vitest";

import { summarizeCheques } from "@/lib/cheques/totals";
import { ChequeStatus, type ChequeListItem } from "@/services/cheque.service";

/**
 * Çek defteri toplamları.
 *
 * NEDEN VAR: üst satırdaki "Listelenen toplam" ile ay alt toplamları
 * AYRI hesaplanıyordu ve kuralları farklıydı — üst toplam iptal
 * edilmiş çekleri sayıyor, ay toplamları saymıyordu. Aynı ekranda
 * birbirini tutmayan iki rakam vardı ve hiçbir test bunu yakalamıyordu.
 *
 * Kilitlenen kural: iptal edilen çek HİÇBİR toplama girmez, satırı
 * ise denetim izi olarak listede kalır.
 */

let counter = 0;

function cheque(
  overrides: Partial<ChequeListItem> & { dueDate: string; amountTry: number }
): ChequeListItem {
  counter += 1;

  return {
    id: `cheque-${counter}`,
    chequeNumber: `CEK-${counter}`,
    direction: 1,
    status: ChequeStatus.Issued,
    amount: overrides.amountTry,
    amountTry: overrides.amountTry,
    currencyCode: "TRY",
    dueDate: overrides.dueDate,
    ...overrides,
  } as ChequeListItem;
}

describe("çek toplamları", () => {
  /**
   * Kullanıcının istediği asıl güvence: iptalli bir listede üst
   * toplam ile ay toplamlarının toplamı BİRBİRİNE EŞİT ve ikisi de
   * iptal tutarını dışarıda bırakıyor.
   */
  it("iptalli senaryoda üst toplam = ay toplamlarının toplamı", () => {
    const items = [
      cheque({ dueDate: "2026-01-15", amountTry: 40_000 }),
      cheque({ dueDate: "2026-01-20", amountTry: 25_000, status: ChequeStatus.Voided }),
      cheque({ dueDate: "2026-02-10", amountTry: 60_000 }),
      cheque({ dueDate: "2026-02-18", amountTry: 15_000, status: ChequeStatus.Voided }),
    ];

    const { listTotal, groups } = summarizeCheques(items);

    const sumOfGroups = groups.reduce((sum, group) => sum + group.total, 0);

    expect(listTotal).toBe(sumOfGroups);

    // İptaller (25.000 + 15.000) hariç: 40.000 + 60.000.
    expect(listTotal).toBe(100_000);
  });

  it("iptal edilen çek ay toplamına ve adedine girmez", () => {
    const items = [
      cheque({ dueDate: "2026-03-05", amountTry: 10_000 }),
      cheque({ dueDate: "2026-03-09", amountTry: 7_500, status: ChequeStatus.Voided }),
    ];

    const [march] = summarizeCheques(items).groups;

    expect(march.total).toBe(10_000);
    expect(march.count).toBe(1);
  });

  /**
   * İptal satırı LİSTEDE KALIR: mali etkisi yok ama kaydın kendisi
   * denetim izi. Satırı gizlemek, "bu çek hiç var olmadı" demek olurdu.
   */
  it("iptal edilen çekin satırı listede kalır", () => {
    const items = [
      cheque({ dueDate: "2026-04-01", amountTry: 5_000 }),
      cheque({ dueDate: "2026-04-02", amountTry: 3_000, status: ChequeStatus.Voided }),
    ];

    const [april] = summarizeCheques(items).groups;

    expect(april.rows).toHaveLength(2);
    expect(april.count).toBe(1);
  });

  it("tamamı iptal olan ayda toplam sıfırdır", () => {
    const items = [
      cheque({ dueDate: "2026-05-01", amountTry: 9_000, status: ChequeStatus.Voided }),
    ];

    const { listTotal, groups } = summarizeCheques(items);

    expect(listTotal).toBe(0);
    expect(groups[0].total).toBe(0);
    expect(groups[0].count).toBe(0);
    // Ay yine görünür: satır denetim izi olarak duruyor.
    expect(groups[0].rows).toHaveLength(1);
  });

  /**
   * Defter değeri toplanır, ham tutar değil. Üst toplam eskiden
   * `amountTry || amount` kullanıyordu; kur karşılığı sıfır olan bir
   * dövizli çekte ham tutarı ekleyip ay toplamından ayrışırdı.
   */
  it("dövizli çekte TL karşılığı toplanır, ham tutar değil", () => {
    const items = [
      cheque({
        dueDate: "2026-06-01",
        amountTry: 35_000,
        amount: 1_000,
        currencyCode: "USD",
      }),
    ];

    expect(summarizeCheques(items).listTotal).toBe(35_000);
  });

  it("boş listede toplam sıfır, grup yok", () => {
    const { listTotal, groups } = summarizeCheques([]);

    expect(listTotal).toBe(0);
    expect(groups).toHaveLength(0);
  });
});
