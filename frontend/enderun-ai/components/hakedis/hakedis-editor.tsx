"use client";

import { Fragment, useCallback, useEffect, useMemo, useState } from "react";

import {
  calculateAdvanceMaterial,
  calculateDeduction,
  calculateDeductionLine,
  calculateHeader,
  calculateItem,
  calculatePaymentPlan,
  DEDUCTION_TYPE_OPTIONS,
  round2,
  validatePaymentPlan,
} from "@/lib/hakedis/calculation";

import {
  progressPaymentService,
  type OpenAdvanceMaterial,
  type ProjectHakedisSection,
} from "@/services/progress-payment.service";

const money = new Intl.NumberFormat("tr-TR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

function num(value: string) {
  const parsed = Number(value.replace(",", "."));
  return Number.isFinite(parsed) ? parsed : 0;
}

// ---------- Form tipleri ----------

export type ItemForm = {
  key: string;
  engineeringPositionId: string | null;
  sectionId: string | null;
  positionCode: string;
  description: string;
  unit: string;
  contractQuantity: number;
  previousQuantity: number;
  currentQuantity: number;
  materialUnitPrice: number;
  laborUnitPrice: number;
  overheadUnitPrice: number;
  measurementReference: string;
  notes: string;
};

export type AdvanceForm = {
  key: string;
  positionCode: string;
  description: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  valuationRate: number;
  notes: string;
};

export type OffsetForm = {
  key: string;
  advanceMaterialId: string;
  amount: number;
};

export type DeductionLineForm = {
  key: string;
  name: string;
  unitPrice: number;
  quantity: number;
  vatRate: number;
};

export type DeductionForm = {
  key: string;
  deductionType: number;
  description: string;
  rate: number;
  manualAmount: number | null;
  previousAmount: number;
  notes: string;
  lines: DeductionLineForm[];
};

export type PaymentPlanForm = {
  key: string;
  paymentType: number;
  rate: number;
  maturityDays: number | null;
  description: string;
};

export type HakedisEditorValue = {
  items: ItemForm[];
  advanceMaterials: AdvanceForm[];
  offsets: OffsetForm[];
  deductions: DeductionForm[];
  paymentPlans: PaymentPlanForm[];
};

export function newItem(): ItemForm {
  return {
    key: crypto.randomUUID(),
    engineeringPositionId: null,
    sectionId: null,
    positionCode: "",
    description: "",
    unit: "",
    contractQuantity: 0,
    previousQuantity: 0,
    currentQuantity: 0,
    materialUnitPrice: 0,
    laborUnitPrice: 0,
    overheadUnitPrice: 0,
    measurementReference: "",
    notes: "",
  };
}

export function newAdvance(): AdvanceForm {
  return {
    key: crypto.randomUUID(),
    positionCode: "",
    description: "",
    unit: "",
    quantity: 0,
    unitPrice: 0,
    // Bedellendirme sözleşmede genelde %100'ün altındadır.
    valuationRate: 80,
    notes: "",
  };
}

export function newDeduction(deductionType: number): DeductionForm {
  const option = DEDUCTION_TYPE_OPTIONS.find((x) => x.value === deductionType);

  return {
    key: crypto.randomUUID(),
    deductionType,
    description: option?.label ?? "Kesinti",
    rate: option?.defaultRate ?? 0,
    manualAmount: null,
    previousAmount: 0,
    notes: "",
    lines:
      option?.defaultLines?.map((name) => ({
        key: crypto.randomUUID(),
        name,
        unitPrice: 0,
        quantity: 0,
        vatRate: 20,
      })) ?? [],
  };
}

export function defaultPaymentPlans(): PaymentPlanForm[] {
  // NATURA dağılımı: nakit + 90 gün + 120 gün.
  return [
    { key: crypto.randomUUID(), paymentType: 0, rate: 40, maturityDays: null, description: "Nakit" },
    { key: crypto.randomUUID(), paymentType: 1, rate: 30, maturityDays: 90, description: "90 gün vadeli çek" },
    { key: crypto.randomUUID(), paymentType: 1, rate: 30, maturityDays: 120, description: "120 gün vadeli çek" },
  ];
}

type Props = {
  projectId: string;
  /** Düzenlemede kendi ihzaratını mahsup listesinden çıkarmak için. */
  progressPaymentId?: string;
  progressPaymentDate: string;
  priceDifferenceAmount: number;
  vatRate: number;
  withholdingNumerator: number;
  withholdingDenominator: number;
  incomeTaxWithholdingRate: number;
  /** Önceki hakedişlerin toplamı (minha). Sunucudan gelir. */
  previousTotalAmount: number;
  value: HakedisEditorValue;
  onChange: (value: HakedisEditorValue) => void;
  /** Üst hesabın canlı sonucu — kaydetme öncesi doğrulama için. */
  onSummaryChange?: (summary: { netPayableAmount: number; planError: string | null }) => void;
};

/**
 * Hakediş giriş editörü: bölümlü poz satırları, üç bileşenli birim
 * fiyat, pursantaj, ihzarat, alt kalemli kesintiler, ödeme dağılımı ve
 * canlı üst hesap.
 *
 * Hesaplar sunucudaki motorun aynısını uygular ama tek doğruluk kaynağı
 * sunucudur; buradaki rakamlar kullanıcı girdikçe görsün diye.
 */
export default function HakedisEditor({
  projectId,
  progressPaymentId,
  progressPaymentDate,
  priceDifferenceAmount,
  vatRate,
  withholdingNumerator,
  withholdingDenominator,
  incomeTaxWithholdingRate,
  previousTotalAmount,
  value,
  onChange,
  onSummaryChange,
}: Props) {
  const [sections, setSections] = useState<ProjectHakedisSection[]>([]);
  const [openAdvances, setOpenAdvances] = useState<OpenAdvanceMaterial[]>([]);
  const [sectionNotice, setSectionNotice] = useState("");

  const loadSections = useCallback(async () => {
    if (!projectId) {
      setSections([]);
      return;
    }

    try {
      const rows = await progressPaymentService.getProjectSections(projectId);
      setSections(rows.filter((x) => x.isActive));
      setSectionNotice(
        rows.length === 0
          ? "Bu projede imalat bölümü tanımlı değil. NATURA şablonunu tek tıkla kurabilirsiniz."
          : ""
      );
    } catch {
      setSections([]);
    }
  }, [projectId]);

  useEffect(() => {
    void loadSections();
  }, [loadSections]);

  useEffect(() => {
    if (!projectId) {
      setOpenAdvances([]);
      return;
    }

    progressPaymentService
      .getOpenAdvanceMaterials(projectId, progressPaymentId)
      .then(setOpenAdvances)
      .catch(() => setOpenAdvances([]));
  }, [projectId, progressPaymentId]);

  async function applySectionTemplate() {
    try {
      const template = await progressPaymentService.getSectionTemplate();

      await progressPaymentService.replaceProjectSections(
        projectId,
        template.map((x) => ({
          id: null,
          order: x.order,
          name: x.name,
          code: String(x.order),
          isActive: true,
        }))
      );

      await loadSections();
    } catch {
      setSectionNotice("Bölüm şablonu kurulamadı.");
    }
  }

  // ---------- Hesaplar ----------

  const itemResults = useMemo(
    () =>
      value.items.map((item) => ({
        item,
        result: calculateItem({
          sectionId: item.sectionId,
          contractQuantity: item.contractQuantity,
          previousQuantity: item.previousQuantity,
          currentQuantity: item.currentQuantity,
          materialUnitPrice: item.materialUnitPrice,
          laborUnitPrice: item.laborUnitPrice,
          overheadUnitPrice: item.overheadUnitPrice,
        }),
      })),
    [value.items]
  );

  const cumulativeWorkAmount = useMemo(
    () => round2(itemResults.reduce((sum, x) => sum + x.result.cumulativeAmount, 0)),
    [itemResults]
  );

  /** Bölüm icmali — NATURA çıktısındaki tablo. */
  const sectionSummary = useMemo(() => {
    const map = new Map<
      string,
      { name: string; material: number; labor: number; overhead: number; current: number }
    >();

    for (const { item, result } of itemResults) {
      const key = item.sectionId ?? "";
      const name =
        sections.find((x) => x.id === item.sectionId)?.name ?? "Bölümsüz";

      const current = map.get(key) ?? {
        name,
        material: 0,
        labor: 0,
        overhead: 0,
        current: 0,
      };

      current.material = round2(current.material + result.materialAmount);
      current.labor = round2(current.labor + result.laborAmount);
      current.overhead = round2(current.overhead + result.overheadAmount);
      current.current = round2(current.current + result.currentAmount);

      map.set(key, current);
    }

    return [...map.values()];
  }, [itemResults, sections]);

  /**
   * Açık ihzarat: önceki dönemlerden kalan + bu hakedişte açılan −
   * bu hakedişte mahsup edilen.
   */
  const openAdvanceTotal = useMemo(() => {
    const fromPrevious = openAdvances.reduce((sum, x) => sum + x.openAmount, 0);

    const openedNow = value.advanceMaterials.reduce(
      (sum, x) =>
        sum + calculateAdvanceMaterial(x.quantity, x.unitPrice, x.valuationRate),
      0
    );

    const offsetNow = value.offsets.reduce((sum, x) => sum + (x.amount || 0), 0);

    return Math.max(0, round2(fromPrevious + openedNow - offsetNow));
  }, [openAdvances, value.advanceMaterials, value.offsets]);

  const cumulativeBase = round2(cumulativeWorkAmount + openAdvanceTotal);

  const deductionResults = useMemo(
    () =>
      value.deductions.map((deduction) => ({
        deduction,
        result: calculateDeduction({
          rate: deduction.rate,
          cumulativeBaseAmount: cumulativeBase,
          previousAmount: deduction.previousAmount,
          manualAmount: deduction.manualAmount,
          lines: deduction.lines.map((line) => ({
            unitPrice: line.unitPrice,
            quantity: line.quantity,
            vatRate: line.vatRate,
          })),
        }),
      })),
    [cumulativeBase, value.deductions]
  );

  const totalDeduction = useMemo(
    () => round2(deductionResults.reduce((sum, x) => sum + x.result.amount, 0)),
    [deductionResults]
  );

  const header = useMemo(
    () =>
      calculateHeader({
        cumulativeWorkAmount,
        cumulativeAdvanceMaterialAmount: openAdvanceTotal,
        previousTotalAmount,
        priceDifferenceAmount,
        vatRate,
        withholdingNumerator,
        withholdingDenominator,
        incomeTaxWithholdingRate,
        totalDeductionAmount: totalDeduction,
      }),
    [
      cumulativeWorkAmount,
      openAdvanceTotal,
      previousTotalAmount,
      priceDifferenceAmount,
      vatRate,
      withholdingNumerator,
      withholdingDenominator,
      incomeTaxWithholdingRate,
      totalDeduction,
    ]
  );

  const planParts = useMemo(
    () =>
      value.paymentPlans.map((x) => ({
        paymentType: x.paymentType,
        rate: x.rate,
        maturityDays: x.maturityDays,
      })),
    [value.paymentPlans]
  );

  const planError = useMemo(() => validatePaymentPlan(planParts), [planParts]);

  const planResults = useMemo(
    () =>
      calculatePaymentPlan(header.netPayableAmount, progressPaymentDate, planParts),
    [header.netPayableAmount, planParts, progressPaymentDate]
  );

  useEffect(() => {
    onSummaryChange?.({
      netPayableAmount: header.netPayableAmount,
      planError,
    });
  }, [header.netPayableAmount, onSummaryChange, planError]);

  // ---------- Değişiklik yardımcıları ----------

  function patch(changes: Partial<HakedisEditorValue>) {
    onChange({ ...value, ...changes });
  }

  function updateItem(key: string, changes: Partial<ItemForm>) {
    patch({
      items: value.items.map((x) => (x.key === key ? { ...x, ...changes } : x)),
    });
  }

  function updateAdvance(key: string, changes: Partial<AdvanceForm>) {
    patch({
      advanceMaterials: value.advanceMaterials.map((x) =>
        x.key === key ? { ...x, ...changes } : x
      ),
    });
  }

  function updateDeduction(key: string, changes: Partial<DeductionForm>) {
    patch({
      deductions: value.deductions.map((x) =>
        x.key === key ? { ...x, ...changes } : x
      ),
    });
  }

  function updateDeductionLine(
    deductionKey: string,
    lineKey: string,
    changes: Partial<DeductionLineForm>
  ) {
    patch({
      deductions: value.deductions.map((deduction) =>
        deduction.key === deductionKey
          ? {
              ...deduction,
              lines: deduction.lines.map((line) =>
                line.key === lineKey ? { ...line, ...changes } : line
              ),
            }
          : deduction
      ),
    });
  }

  function updatePlan(key: string, changes: Partial<PaymentPlanForm>) {
    patch({
      paymentPlans: value.paymentPlans.map((x) =>
        x.key === key ? { ...x, ...changes } : x
      ),
    });
  }

  /** Mahsup tutarı açık bakiyeyi aşamaz — sunucu da reddeder. */
  function setOffset(advanceId: string, amount: number, openAmount: number) {
    const safe = Math.min(Math.max(0, amount), openAmount);
    const existing = value.offsets.find((x) => x.advanceMaterialId === advanceId);

    if (safe === 0) {
      patch({
        offsets: value.offsets.filter((x) => x.advanceMaterialId !== advanceId),
      });
      return;
    }

    patch({
      offsets: existing
        ? value.offsets.map((x) =>
            x.advanceMaterialId === advanceId ? { ...x, amount: safe } : x
          )
        : [
            ...value.offsets,
            { key: crypto.randomUUID(), advanceMaterialId: advanceId, amount: safe },
          ],
    });
  }

  return (
    <>
      {/* ---------- POZ SATIRLARI ---------- */}
      <div className="erp-table-card" style={{ marginTop: 18 }}>
        <div className="erp-table-header">
          <h2>İmalat Pozları</h2>
          <p>
            Her poz bir imalat bölümüne bağlanır. Birim fiyat üç bileşenden
            oluşur: malzeme + montaj + genel gider &amp; kâr.
          </p>

          {sectionNotice && (
            <div className="erp-alert" style={{ marginTop: 10 }}>
              {sectionNotice}{" "}
              {projectId && (
                <button
                  type="button"
                  onClick={() => void applySectionTemplate()}
                  style={{ marginLeft: 8 }}
                >
                  NATURA şablonunu kur (12 bölüm)
                </button>
              )}
            </div>
          )}
        </div>

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th style={{ minWidth: 150 }}>Bölüm</th>
                <th style={{ minWidth: 110 }}>Poz</th>
                <th style={{ minWidth: 180 }}>Açıklama</th>
                <th>Birim</th>
                <th>Sözleşme</th>
                <th>Önceki</th>
                <th>Bu Dönem</th>
                <th>Toplam</th>
                <th>Malzeme BF</th>
                <th>Montaj BF</th>
                <th>GG&amp;K BF</th>
                <th>Tutar</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {itemResults.map(({ item, result }) => (
                <tr key={item.key}>
                  <td>
                    <select
                      value={item.sectionId ?? ""}
                      onChange={(event) =>
                        updateItem(item.key, {
                          sectionId: event.target.value || null,
                        })
                      }
                    >
                      <option value="">Bölümsüz</option>
                      {sections.map((section) => (
                        <option key={section.id} value={section.id}>
                          {section.name}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <input
                      value={item.positionCode}
                      onChange={(event) =>
                        updateItem(item.key, { positionCode: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      value={item.description}
                      onChange={(event) =>
                        updateItem(item.key, { description: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      style={{ width: 60 }}
                      value={item.unit}
                      onChange={(event) =>
                        updateItem(item.key, { unit: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      step="0.01"
                      style={{ width: 90 }}
                      value={item.contractQuantity}
                      onChange={(event) =>
                        updateItem(item.key, {
                          contractQuantity: num(event.target.value),
                        })
                      }
                    />
                  </td>
                  {/* Önceki miktar sunucudan gelir; kullanıcı değiştiremez. */}
                  <td>
                    <span className="tabular">
                      {money.format(item.previousQuantity)}
                    </span>
                  </td>
                  <td>
                    <input
                      type="number"
                      step="0.01"
                      style={{ width: 90 }}
                      value={item.currentQuantity}
                      onChange={(event) =>
                        updateItem(item.key, {
                          currentQuantity: num(event.target.value),
                        })
                      }
                    />
                  </td>
                  <td>
                    <strong className="tabular">
                      {money.format(result.cumulativeQuantity)}
                    </strong>
                    <small>
                      {result.exceedsContractQuantity
                        ? "Sözleşme miktarı aşıldı"
                        : `%${money.format(result.completionRate)}`}
                    </small>
                  </td>
                  <td>
                    <input
                      type="number"
                      step="0.01"
                      style={{ width: 100 }}
                      value={item.materialUnitPrice}
                      onChange={(event) =>
                        updateItem(item.key, {
                          materialUnitPrice: num(event.target.value),
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      step="0.01"
                      style={{ width: 100 }}
                      value={item.laborUnitPrice}
                      onChange={(event) =>
                        updateItem(item.key, {
                          laborUnitPrice: num(event.target.value),
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      step="0.01"
                      style={{ width: 100 }}
                      value={item.overheadUnitPrice}
                      onChange={(event) =>
                        updateItem(item.key, {
                          overheadUnitPrice: num(event.target.value),
                        })
                      }
                    />
                  </td>
                  <td>
                    <strong className="tabular">
                      {money.format(result.currentAmount)}
                    </strong>
                    <small>BF {money.format(result.unitPrice)}</small>
                  </td>
                  <td>
                    <button
                      type="button"
                      disabled={value.items.length === 1}
                      onClick={() =>
                        patch({
                          items: value.items.filter((x) => x.key !== item.key),
                        })
                      }
                    >
                      Sil
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div style={{ padding: "14px 22px" }}>
          <button
            type="button"
            onClick={() => patch({ items: [...value.items, newItem()] })}
          >
            Poz Satırı Ekle
          </button>
        </div>
      </div>

      {/* ---------- BÖLÜM İCMALİ ---------- */}
      {sectionSummary.length > 0 && (
        <div className="erp-table-card" style={{ marginTop: 18 }}>
          <div className="erp-table-header">
            <h2>Bölüm İcmali</h2>
          </div>
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Bölüm</th>
                  <th>Malzeme</th>
                  <th>Montaj</th>
                  <th>GG&amp;K</th>
                  <th>Bu Hakediş</th>
                </tr>
              </thead>
              <tbody>
                {sectionSummary.map((section) => (
                  <tr key={section.name}>
                    <td>{section.name}</td>
                    <td className="tabular">{money.format(section.material)}</td>
                    <td className="tabular">{money.format(section.labor)}</td>
                    <td className="tabular">{money.format(section.overhead)}</td>
                    <td className="tabular">
                      <strong>{money.format(section.current)}</strong>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ---------- İHZARAT ---------- */}
      <div className="erp-table-card" style={{ marginTop: 18 }}>
        <div className="erp-table-header">
          <h2>İhzarat</h2>
          <p>
            Sahaya gelmiş ama monte edilmemiş malzeme. Oranla bedellendirilir;
            imalata dönüştüğünde sonraki hakedişte mahsup edilir.
          </p>
        </div>

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Poz</th>
                <th>Açıklama</th>
                <th>Birim</th>
                <th>Miktar</th>
                <th>Birim Fiyat</th>
                <th>Bedellendirme %</th>
                <th>Tutar</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {value.advanceMaterials.length === 0 && (
                <tr>
                  <td colSpan={8}>Bu hakedişte ihzarat yok.</td>
                </tr>
              )}

              {value.advanceMaterials.map((advance) => (
                <tr key={advance.key}>
                  <td>
                    <input
                      value={advance.positionCode}
                      onChange={(event) =>
                        updateAdvance(advance.key, {
                          positionCode: event.target.value,
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      value={advance.description}
                      onChange={(event) =>
                        updateAdvance(advance.key, {
                          description: event.target.value,
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      style={{ width: 60 }}
                      value={advance.unit}
                      onChange={(event) =>
                        updateAdvance(advance.key, { unit: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      step="0.01"
                      style={{ width: 90 }}
                      value={advance.quantity}
                      onChange={(event) =>
                        updateAdvance(advance.key, {
                          quantity: num(event.target.value),
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      step="0.01"
                      style={{ width: 100 }}
                      value={advance.unitPrice}
                      onChange={(event) =>
                        updateAdvance(advance.key, {
                          unitPrice: num(event.target.value),
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      step="0.01"
                      style={{ width: 80 }}
                      value={advance.valuationRate}
                      onChange={(event) =>
                        updateAdvance(advance.key, {
                          valuationRate: num(event.target.value),
                        })
                      }
                    />
                  </td>
                  <td className="tabular">
                    <strong>
                      {money.format(
                        calculateAdvanceMaterial(
                          advance.quantity,
                          advance.unitPrice,
                          advance.valuationRate
                        )
                      )}
                    </strong>
                  </td>
                  <td>
                    <button
                      type="button"
                      onClick={() =>
                        patch({
                          advanceMaterials: value.advanceMaterials.filter(
                            (x) => x.key !== advance.key
                          ),
                        })
                      }
                    >
                      Sil
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div style={{ padding: "14px 22px" }}>
          <button
            type="button"
            onClick={() =>
              patch({ advanceMaterials: [...value.advanceMaterials, newAdvance()] })
            }
          >
            İhzarat Kalemi Ekle
          </button>
        </div>

        {/* Önceki dönemlerden kalan açık ihzarat — mahsup girişi */}
        {openAdvances.length > 0 && (
          <>
            <div className="erp-table-header" style={{ paddingTop: 18 }}>
              <h2>Önceki Dönemlerden Açık İhzarat</h2>
              <p>
                İmalata dönen kısmı mahsup edin. Mahsup açık bakiyeyi aşamaz —
                aynı iş iki kez tahsil edilemez.
              </p>
            </div>

            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Kaynak</th>
                    <th>Poz</th>
                    <th>Açıklama</th>
                    <th>Açık Bakiye</th>
                    <th>Bu Hakedişte Mahsup</th>
                    <th>Kalan</th>
                  </tr>
                </thead>
                <tbody>
                  {openAdvances.map((advance) => {
                    const offset =
                      value.offsets.find(
                        (x) => x.advanceMaterialId === advance.id
                      )?.amount ?? 0;

                    return (
                      <tr key={advance.id}>
                        <td>
                          {advance.sourcePeriodNumber}. dönem
                          <small>{advance.sourceProgressPaymentNumber}</small>
                        </td>
                        <td>{advance.positionCode}</td>
                        <td>{advance.description}</td>
                        <td className="tabular">
                          {money.format(advance.openAmount)}
                        </td>
                        <td>
                          <input
                            type="number"
                            step="0.01"
                            max={advance.openAmount}
                            style={{ width: 120 }}
                            value={offset}
                            onChange={(event) =>
                              setOffset(
                                advance.id,
                                num(event.target.value),
                                advance.openAmount
                              )
                            }
                          />
                        </td>
                        <td className="tabular">
                          {money.format(round2(advance.openAmount - offset))}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>

      {/* ---------- KESİNTİLER ---------- */}
      <div className="erp-table-card" style={{ marginTop: 18 }}>
        <div className="erp-table-header">
          <h2>Kesintiler</h2>
          <p>
            Oransal kesintiler kümülatif tabandan hesaplanır: kümülatif ×
            oran − önceden kesilen. Yemek, konaklama ve İSG kalemleri alt
            satırlardan (birim × adet × KDV) gelir.
          </p>
        </div>

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th style={{ minWidth: 160 }}>Tür</th>
                <th style={{ minWidth: 160 }}>Açıklama</th>
                <th>Oran %</th>
                <th>Kümülatif Taban</th>
                <th>Önceden Kesilen</th>
                <th>Bu Hakediş</th>
                <th>Kümülatif</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {deductionResults.length === 0 && (
                <tr>
                  <td colSpan={8}>Kesinti yok.</td>
                </tr>
              )}

              {deductionResults.map(({ deduction, result }) => {
                const option = DEDUCTION_TYPE_OPTIONS.find(
                  (x) => x.value === deduction.deductionType
                );

                return (
                  <Fragment key={deduction.key}>
                    <tr>
                      <td>{option?.label ?? "Kesinti"}</td>
                      <td>
                        <input
                          value={deduction.description}
                          onChange={(event) =>
                            updateDeduction(deduction.key, {
                              description: event.target.value,
                            })
                          }
                        />
                      </td>
                      <td>
                        {option?.hasLines ? (
                          <small>alt kalemden</small>
                        ) : (
                          <input
                            type="number"
                            step="0.01"
                            style={{ width: 80 }}
                            value={deduction.rate}
                            onChange={(event) =>
                              updateDeduction(deduction.key, {
                                rate: num(event.target.value),
                              })
                            }
                          />
                        )}
                      </td>
                      <td className="tabular">
                        {option?.hasLines ? "-" : money.format(cumulativeBase)}
                      </td>
                      <td>
                        <input
                          type="number"
                          step="0.01"
                          style={{ width: 110 }}
                          value={deduction.previousAmount}
                          onChange={(event) =>
                            updateDeduction(deduction.key, {
                              previousAmount: num(event.target.value),
                            })
                          }
                        />
                      </td>
                      <td className="tabular">
                        <strong>{money.format(result.amount)}</strong>
                      </td>
                      <td className="tabular">
                        {money.format(result.cumulativeAmount)}
                      </td>
                      <td>
                        <button
                          type="button"
                          onClick={() =>
                            patch({
                              deductions: value.deductions.filter(
                                (x) => x.key !== deduction.key
                              ),
                            })
                          }
                        >
                          Sil
                        </button>
                      </td>
                    </tr>

                    {deduction.lines.map((line) => {
                      const lineResult = calculateDeductionLine(line);

                      return (
                        <tr key={line.key} style={{ background: "#fbfdfd" }}>
                          <td style={{ paddingLeft: 40 }}>
                            <input
                              style={{ width: 130 }}
                              value={line.name}
                              onChange={(event) =>
                                updateDeductionLine(deduction.key, line.key, {
                                  name: event.target.value,
                                })
                              }
                            />
                          </td>
                          <td>
                            <input
                              type="number"
                              step="0.01"
                              style={{ width: 100 }}
                              placeholder="Birim fiyat"
                              value={line.unitPrice}
                              onChange={(event) =>
                                updateDeductionLine(deduction.key, line.key, {
                                  unitPrice: num(event.target.value),
                                })
                              }
                            />
                          </td>
                          <td>
                            <input
                              type="number"
                              step="1"
                              style={{ width: 80 }}
                              placeholder="Adet"
                              value={line.quantity}
                              onChange={(event) =>
                                updateDeductionLine(deduction.key, line.key, {
                                  quantity: num(event.target.value),
                                })
                              }
                            />
                          </td>
                          <td>
                            <input
                              type="number"
                              step="0.01"
                              style={{ width: 70 }}
                              placeholder="KDV %"
                              value={line.vatRate}
                              onChange={(event) =>
                                updateDeductionLine(deduction.key, line.key, {
                                  vatRate: num(event.target.value),
                                })
                              }
                            />
                          </td>
                          <td className="tabular">
                            <small>KDV {money.format(lineResult.vatAmount)}</small>
                          </td>
                          <td className="tabular">
                            {money.format(lineResult.grossAmount)}
                          </td>
                          <td colSpan={2}>
                            <button
                              type="button"
                              onClick={() =>
                                updateDeduction(deduction.key, {
                                  lines: deduction.lines.filter(
                                    (x) => x.key !== line.key
                                  ),
                                })
                              }
                            >
                              Kalemi Sil
                            </button>
                          </td>
                        </tr>
                      );
                    })}

                    {option?.hasLines && (
                      <tr style={{ background: "#fbfdfd" }}>
                        <td colSpan={8} style={{ paddingLeft: 40 }}>
                          <button
                            type="button"
                            onClick={() =>
                              updateDeduction(deduction.key, {
                                lines: [
                                  ...deduction.lines,
                                  {
                                    key: crypto.randomUUID(),
                                    name: "",
                                    unitPrice: 0,
                                    quantity: 0,
                                    vatRate: 20,
                                  },
                                ],
                              })
                            }
                          >
                            Alt Kalem Ekle
                          </button>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={5}>
                  <strong>TOPLAM KESİNTİ</strong>
                </td>
                <td className="tabular">
                  <strong>{money.format(totalDeduction)}</strong>
                </td>
                <td colSpan={2}></td>
              </tr>
            </tfoot>
          </table>
        </div>

        <div style={{ padding: "14px 22px", display: "flex", gap: 8, flexWrap: "wrap" }}>
          {DEDUCTION_TYPE_OPTIONS.map((option) => (
            <button
              key={option.value}
              type="button"
              onClick={() =>
                patch({
                  deductions: [...value.deductions, newDeduction(option.value)],
                })
              }
            >
              + {option.label}
            </button>
          ))}
        </div>
      </div>

      {/* ---------- ÖDEME DAĞILIMI ---------- */}
      <div className="erp-table-card" style={{ marginTop: 18 }}>
        <div className="erp-table-header">
          <h2>Ödeme Dağılımı</h2>
          <p>
            Oranların toplamı %100 olmalı. Vadeli çekler hakediş
            kesinleştirilince çek defterine otomatik girer ve vadeleri nakit
            akışına düşer.
          </p>
        </div>

        {planError && <div className="erp-alert error">{planError}</div>}

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Ödeme Şekli</th>
                <th>Açıklama</th>
                <th>Oran %</th>
                <th>Vade (gün)</th>
                <th>Vade Tarihi</th>
                <th>Tutar</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {value.paymentPlans.map((plan, index) => (
                <tr key={plan.key}>
                  <td>
                    <select
                      value={plan.paymentType}
                      onChange={(event) =>
                        updatePlan(plan.key, {
                          paymentType: Number(event.target.value),
                          maturityDays:
                            Number(event.target.value) === 0 ? null : plan.maturityDays ?? 90,
                        })
                      }
                    >
                      <option value={0}>Nakit</option>
                      <option value={1}>Vadeli Çek</option>
                    </select>
                  </td>
                  <td>
                    <input
                      value={plan.description}
                      onChange={(event) =>
                        updatePlan(plan.key, { description: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      step="0.01"
                      style={{ width: 80 }}
                      value={plan.rate}
                      onChange={(event) =>
                        updatePlan(plan.key, { rate: num(event.target.value) })
                      }
                    />
                  </td>
                  <td>
                    {plan.paymentType === 1 ? (
                      <input
                        type="number"
                        step="1"
                        style={{ width: 80 }}
                        value={plan.maturityDays ?? 0}
                        onChange={(event) =>
                          updatePlan(plan.key, {
                            maturityDays: Math.round(num(event.target.value)),
                          })
                        }
                      />
                    ) : (
                      <small>-</small>
                    )}
                  </td>
                  <td>
                    {planResults[index]?.dueDate
                      ? new Date(planResults[index].dueDate!).toLocaleDateString("tr-TR")
                      : "-"}
                  </td>
                  <td className="tabular">
                    <strong>{money.format(planResults[index]?.amount ?? 0)}</strong>
                  </td>
                  <td>
                    <button
                      type="button"
                      onClick={() =>
                        patch({
                          paymentPlans: value.paymentPlans.filter(
                            (x) => x.key !== plan.key
                          ),
                        })
                      }
                    >
                      Sil
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div style={{ padding: "14px 22px", display: "flex", gap: 8 }}>
          <button
            type="button"
            onClick={() =>
              patch({
                paymentPlans: [
                  ...value.paymentPlans,
                  {
                    key: crypto.randomUUID(),
                    paymentType: 0,
                    rate: 0,
                    maturityDays: null,
                    description: "",
                  },
                ],
              })
            }
          >
            Parça Ekle
          </button>

          {value.paymentPlans.length === 0 && (
            <button
              type="button"
              onClick={() => patch({ paymentPlans: defaultPaymentPlans() })}
            >
              NATURA dağılımını kur (%40 nakit / %30 / %30)
            </button>
          )}
        </div>
      </div>

      {/* ---------- ÜST HESAP (canlı) ---------- */}
      <div className="erp-form-card" style={{ marginTop: 18, padding: 22 }}>
        <h2 style={{ marginBottom: 14 }}>Üst Hesap</h2>

        <div style={{ maxWidth: 520 }}>
          <SummaryLine label="Kümülatif imalat" value={cumulativeWorkAmount} />
          <SummaryLine label="Açık ihzarat" value={openAdvanceTotal} />
          <SummaryLine label="Kümülatif toplam" value={header.cumulativeTotalAmount} bold />
          <SummaryLine label="Önceki hakedişler (minha)" value={-previousTotalAmount} />
          <SummaryLine label="Bu hakediş" value={header.currentAmount} bold />
          {priceDifferenceAmount !== 0 && (
            <SummaryLine label="Fiyat farkı" value={priceDifferenceAmount} />
          )}
          <SummaryLine label={`KDV (%${vatRate})`} value={header.vatAmount} />
          <SummaryLine label="Brüt tutar" value={header.grossPayableAmount} bold />
          {header.withholdingAmount > 0 && (
            <SummaryLine
              label={`KDV tevkifatı (${withholdingNumerator}/${withholdingDenominator})`}
              value={-header.withholdingAmount}
            />
          )}
          {header.incomeTaxWithholdingAmount > 0 && (
            <SummaryLine
              label={`Stopaj (%${incomeTaxWithholdingRate})`}
              value={-header.incomeTaxWithholdingAmount}
            />
          )}
          <SummaryLine label="Kesintiler" value={-totalDeduction} />

          <div
            style={{
              borderTop: "2px solid #0f2f38",
              marginTop: 8,
              paddingTop: 8,
            }}
          >
            <SummaryLine
              label="TAHSİL EDİLECEK"
              value={header.netPayableAmount}
              bold
            />
          </div>

          {header.netPayableAmount < 0 && (
            <div className="erp-alert error" style={{ marginTop: 12 }}>
              Kesintiler hakedişi aşıyor; tahsil edilecek tutar negatif. Bu
              hakediş kesinleştirilemez.
            </div>
          )}
        </div>
      </div>
    </>
  );
}

function SummaryLine({
  label,
  value,
  bold,
}: {
  label: string;
  value: number;
  bold?: boolean;
}) {
  return (
    <div
      style={{
        display: "flex",
        justifyContent: "space-between",
        padding: "4px 0",
        fontWeight: bold ? 700 : 400,
      }}
    >
      <span>{label}</span>
      <span className="tabular">{money.format(value)}</span>
    </div>
  );
}
