import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import PersonnelOvertimePanel from "@/components/hr/personnel-overtime-panel";
import { personnelOvertimeService } from "@/services/personnel-overtime.service";
import type { PersonnelOvertimeSummary } from "@/services/personnel-overtime.service";

/**
 * TUTAR MASKELEMESİNİN UI TARAFI.
 *
 * Elden ödeme sızıntısının iki katmanı var: uç tutarı hiç
 * döndürmemeli (backend testleriyle sabit) ve ekran, tutar
 * gelmediğinde o alanı HİÇ ÇİZMEMELİ.
 *
 * İkincisi bugüne kadar sınanmıyordu. Ekran maskeyi görmezden gelip
 * `amount` alanını yine de basmaya kalksaydı, yetkisiz kullanıcıya
 * "₺0,00" ya da "undefined" gösterir; ilki yanlış bilgi, ikincisi
 * alanın var olduğunu ele verir.
 *
 * Bileşen SERVİS ÜZERİNDEN besleniyor; test uca gitmiyor, servisi
 * taklit ediyor — sınanan şey ekranın maskeye uyup uymadığı.
 */
function summary(
  overrides: Partial<PersonnelOvertimeSummary> = {},
): PersonnelOvertimeSummary {
  return {
    year: 2026,
    annualLimit: 270,
    overtimeHours: 15,
    sundayHours: 0,
    publicHolidayHours: 0,
    limitStatus: "ok",
    limitStatusName: "Sınır içinde",
    limitCountsOvertimeOnly: true,
    consent: { year: 2026, date: "2026-01-05", isValid: true },
    amountsHidden: false,
    totalAmount: 3_600,
    currentMonth: {
      year: 2026,
      month: 8,
      hours: 15,
      overtimeHours: 15,
      sundayHours: 0,
      publicHolidayHours: 0,
      amount: 3_600,
    },
    takeHome: {
      officialNet: 45_000,
      manualExtraMonthly: 9_000,
      overtimeExtra: 3_600,
      totalExtra: 12_600,
      totalTakeHome: 57_600,
      hourlyRate: 240,
      dailyWorkHours: 8,
      baseExcludesOvertime: true,
    },
    notLandedCount: 0,
    lines: [],
    ...overrides,
  } as PersonnelOvertimeSummary;
}

describe("Tutar maskelemesi — personel mesai paneli", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("yetki varken saatler ve tutarlar birlikte görünür", async () => {
    vi.spyOn(personnelOvertimeService, "get").mockResolvedValue(summary());

    render(<PersonnelOvertimePanel personnelId="p-1" />);

    await waitFor(() =>
      expect(screen.getByText("Mesai saati")).toBeInTheDocument(),
    );

    expect(screen.getByText("Mesai tutarı (elden)")).toBeInTheDocument();
    expect(screen.getByText("Toplam ele geçen")).toBeInTheDocument();
    expect(screen.getByText("Resmî net")).toBeInTheDocument();
  });

  /**
   * ANA TEST: yetki yokken SAATLER görünür, TUTARLAR hiç çizilmez.
   * Panelin işi saatleri göstermek; tutar dar yetkiye tabi.
   */
  it("yetki yokken saat görünür ama hiçbir tutar çizilmez", async () => {
    vi.spyOn(personnelOvertimeService, "get").mockResolvedValue(
      summary({
        amountsHidden: true,
        totalAmount: null,
        currentMonth: {
          year: 2026,
          month: 8,
          hours: 15,
          overtimeHours: 15,
          sundayHours: 0,
          publicHolidayHours: 0,
          amount: null,
        },
        takeHome: {
          officialNet: null,
          manualExtraMonthly: null,
          overtimeExtra: null,
          totalExtra: null,
          totalTakeHome: null,
          hourlyRate: null,
          dailyWorkHours: 8,
          baseExcludesOvertime: true,
        },
      }),
    );

    render(<PersonnelOvertimePanel personnelId="p-1" />);

    // Saatler yerinde: panelin işi bu.
    await waitFor(() =>
      expect(screen.getByText("Mesai saati")).toBeInTheDocument(),
    );

    // Metin iki düğüme bölünüyor ("15" + " saat"); eleman
    // metninin tamamına bakılıyor.
    expect(
      screen.getByText(
        (_, element) => element?.textContent?.trim() === "15 saat",
        { selector: "strong" },
      ),
    ).toBeInTheDocument();

    // Tutar satırlarının hiçbiri DOM'da YOK — gizlenmiş değil, hiç
    // çizilmemiş.
    expect(screen.queryByText("Mesai tutarı (elden)")).not.toBeInTheDocument();
    expect(screen.queryByText("Toplam ele geçen")).not.toBeInTheDocument();
    expect(screen.queryByText("Resmî net")).not.toBeInTheDocument();
    expect(screen.queryByText("Manuel elden")).not.toBeInTheDocument();
  });

  /**
   * Maskeliyken ekranda hiçbir para biçimi kalmamalı: "₺0,00" bile
   * yanlış bilgi olurdu ve alanın varlığını ele verirdi.
   */
  it("maskeliyken ekranda para biçimli bir metin kalmaz", async () => {
    vi.spyOn(personnelOvertimeService, "get").mockResolvedValue(
      summary({
        amountsHidden: true,
        totalAmount: null,
        currentMonth: {
          year: 2026,
          month: 8,
          hours: 15,
          overtimeHours: 15,
          sundayHours: 0,
          publicHolidayHours: 0,
          amount: null,
        },
        takeHome: {
          officialNet: null,
          manualExtraMonthly: null,
          overtimeExtra: null,
          totalExtra: null,
          totalTakeHome: null,
          hourlyRate: null,
          dailyWorkHours: 8,
          baseExcludesOvertime: true,
        },
      }),
    );

    const { container } = render(<PersonnelOvertimePanel personnelId="p-1" />);

    await waitFor(() =>
      expect(screen.getByText("Mesai saati")).toBeInTheDocument(),
    );

    // ₺ işareti ya da "TL" geçmiyor.
    expect(container.textContent ?? "").not.toMatch(/₺|\bTL\b/);
  });
});
