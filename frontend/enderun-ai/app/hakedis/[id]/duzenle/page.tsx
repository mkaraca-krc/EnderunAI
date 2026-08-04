"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { FormEvent, useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import HakedisEditor, {
  newItem,
  type HakedisEditorValue,
} from "@/components/hakedis/hakedis-editor";

import {
  progressPaymentService,
  ProgressPaymentStatus,
  type ProgressPaymentDetail,
} from "@/services/progress-payment.service";

/**
 * Taslak hakedişin düzenlenmesi. Yeni hakediş ekranıyla aynı editörü
 * kullanır; fark, mevcut kaydın form haline çevrilmesi.
 *
 * Yalnızca taslak düzenlenebilir — onaya gönderilmiş veya kesinleşmiş
 * hakediş sunucu tarafından da reddedilir.
 */
export default function EditProgressPaymentPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();

  const [detail, setDetail] = useState<ProgressPaymentDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const [periodStartDate, setPeriodStartDate] = useState("");
  const [periodEndDate, setPeriodEndDate] = useState("");
  const [progressPaymentDate, setProgressPaymentDate] = useState("");
  const [priceDifferenceAmount, setPriceDifferenceAmount] = useState(0);
  const [vatRate, setVatRate] = useState(20);
  const [withholdingNumerator, setWithholdingNumerator] = useState(4);
  const [withholdingDenominator, setWithholdingDenominator] = useState(10);
  const [incomeTaxWithholdingRate, setIncomeTaxWithholdingRate] = useState(0);
  const [description, setDescription] = useState("");
  const [notes, setNotes] = useState("");

  const [value, setValue] = useState<HakedisEditorValue>({
    items: [newItem()],
    advanceMaterials: [],
    offsets: [],
    deductions: [],
    paymentPlans: [],
  });

  const [previousTotalAmount, setPreviousTotalAmount] = useState(0);
  const [summary, setSummary] = useState<{
    netPayableAmount: number;
    planError: string | null;
  }>({ netPayableAmount: 0, planError: null });

  const load = useCallback(async () => {
    if (!params.id) return;

    setLoading(true);
    setError("");

    try {
      const result = await progressPaymentService.getById(params.id);
      setDetail(result);

      setPeriodStartDate(result.periodStartDate?.slice(0, 10) ?? "");
      setPeriodEndDate(result.periodEndDate?.slice(0, 10) ?? "");
      setProgressPaymentDate(result.progressPaymentDate.slice(0, 10));
      setPriceDifferenceAmount(result.priceDifferenceAmount);
      setVatRate(result.vatRate);
      setWithholdingNumerator(result.withholdingNumerator);
      setWithholdingDenominator(result.withholdingDenominator);
      setIncomeTaxWithholdingRate(result.incomeTaxWithholdingRate);
      setDescription(result.description ?? "");
      setNotes(result.notes ?? "");

      // Kaydın bölüm satırları hakedişin kendi kopyası; editör proje
      // bölümleriyle çalıştığı için kaynak bölüm kimliğine dönülür.
      const sectionSourceById = new Map(
        result.sections.map((x) => [x.id, x.projectHakedisSectionId ?? null])
      );

      setValue({
        items: result.items.map((item) => ({
          key: crypto.randomUUID(),
          engineeringPositionId: item.engineeringPositionId ?? null,
          sectionId: item.progressPaymentSectionId
            ? sectionSourceById.get(item.progressPaymentSectionId) ?? null
            : null,
          positionCode: item.positionCode,
          description: item.description,
          unit: item.unit,
          contractQuantity: item.contractQuantity,
          previousQuantity: item.previousQuantity,
          currentQuantity: item.currentQuantity,
          materialUnitPrice: item.materialUnitPrice,
          laborUnitPrice: item.laborUnitPrice,
          overheadUnitPrice: item.overheadUnitPrice,
          measurementReference: item.measurementReference ?? "",
          notes: item.notes ?? "",
        })),
        advanceMaterials: result.advanceMaterials.map((advance) => ({
          key: crypto.randomUUID(),
          positionCode: advance.positionCode,
          description: advance.description,
          unit: advance.unit,
          quantity: advance.quantity,
          unitPrice: advance.unitPrice,
          valuationRate: advance.valuationRate,
          notes: advance.notes ?? "",
        })),
        offsets: result.advanceOffsets.map((offset) => ({
          key: crypto.randomUUID(),
          advanceMaterialId: offset.advanceMaterialId,
          amount: offset.amount,
        })),
        deductions: result.deductions.map((deduction) => ({
          key: crypto.randomUUID(),
          deductionType: deduction.deductionType,
          description: deduction.description,
          rate: deduction.rate,
          manualAmount: deduction.isManualAmount ? deduction.amount : null,
          previousAmount: deduction.previousAmount,
          notes: deduction.notes ?? "",
          lines: deduction.lines.map((line) => ({
            key: crypto.randomUUID(),
            name: line.name,
            unitPrice: line.unitPrice,
            quantity: line.quantity,
            vatRate: line.vatRate,
          })),
        })),
        paymentPlans: result.paymentPlans.map((plan) => ({
          key: crypto.randomUUID(),
          paymentType: plan.paymentType,
          rate: plan.rate,
          maturityDays: plan.maturityDays ?? null,
          description: plan.description ?? "",
        })),
      });

      const context = await progressPaymentService.getPreviousContext(
        result.projectId,
        result.periodNumber,
        result.id
      );

      setPreviousTotalAmount(context.previousTotalAmount);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Hakediş yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    void load();
  }, [load]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!detail) return;

    setError("");

    const invalidItem = value.items.find(
      (line) =>
        !line.positionCode.trim() ||
        !line.description.trim() ||
        !line.unit.trim() ||
        Number(line.currentQuantity) <= 0
    );

    if (invalidItem) {
      return setError(
        "Tüm poz satırlarında poz kodu, açıklama, birim ve bu dönem miktarı dolu olmalı."
      );
    }

    if (summary.planError) return setError(summary.planError);

    if (summary.netPayableAmount < 0) {
      return setError(
        "Kesintiler hakedişi aşıyor; tahsil edilecek tutar negatif. Kesintileri gözden geçirin."
      );
    }

    setSaving(true);

    try {
      await progressPaymentService.update(detail.id, {
        periodStartDate: periodStartDate || null,
        periodEndDate: periodEndDate || null,
        progressPaymentDate,
        priceDifferenceAmount: Number(priceDifferenceAmount || 0),
        vatRate: Number(vatRate || 0),
        withholdingNumerator: Number(withholdingNumerator || 0),
        withholdingDenominator: Number(withholdingDenominator || 10),
        incomeTaxWithholdingRate: Number(incomeTaxWithholdingRate || 0),
        description: description.trim() || null,
        notes: notes.trim() || null,
        items: value.items.map((line) => ({
          engineeringPositionId: line.engineeringPositionId || null,
          positionCode: line.positionCode.trim(),
          description: line.description.trim(),
          unit: line.unit.trim(),
          contractQuantity: Number(line.contractQuantity || 0),
          currentQuantity: Number(line.currentQuantity || 0),
          unitPrice: 0,
          materialUnitPrice: Number(line.materialUnitPrice || 0),
          laborUnitPrice: Number(line.laborUnitPrice || 0),
          overheadUnitPrice: Number(line.overheadUnitPrice || 0),
          sectionId: line.sectionId,
          measurementReference: line.measurementReference?.trim() || null,
          notes: line.notes?.trim() || null,
        })),
        advanceMaterials: value.advanceMaterials.map((advance) => ({
          positionCode: advance.positionCode.trim(),
          description: advance.description.trim(),
          unit: advance.unit.trim(),
          quantity: Number(advance.quantity || 0),
          unitPrice: Number(advance.unitPrice || 0),
          valuationRate: Number(advance.valuationRate || 0),
          notes: advance.notes?.trim() || null,
        })),
        advanceOffsets: value.offsets.map((offset) => ({
          advanceMaterialId: offset.advanceMaterialId,
          amount: Number(offset.amount || 0),
          notes: null,
        })),
        deductions: value.deductions.map((deduction) => ({
          deductionType: deduction.deductionType,
          description: deduction.description.trim(),
          rate: Number(deduction.rate || 0),
          baseAmount: 0,
          manualAmount: deduction.manualAmount,
          notes: deduction.notes?.trim() || null,
          cumulativeBaseAmount: null,
          lines: deduction.lines.length
            ? deduction.lines.map((line) => ({
                name: line.name.trim(),
                unitPrice: Number(line.unitPrice || 0),
                quantity: Number(line.quantity || 0),
                vatRate: Number(line.vatRate || 0),
                notes: null,
              }))
            : null,
        })),
        paymentPlans: value.paymentPlans.map((plan) => ({
          paymentType: plan.paymentType,
          rate: Number(plan.rate || 0),
          maturityDays: plan.maturityDays,
          description: plan.description?.trim() || null,
        })),
      });

      router.push(`/hakedis/${detail.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Hakediş güncellenemedi.");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <ErpShell title="Hakediş Düzenle" description="">
        <div className="erp-loading">Hakediş yükleniyor...</div>
      </ErpShell>
    );
  }

  if (!detail) {
    return (
      <ErpShell title="Hakediş Düzenle" description="">
        <div className="erp-alert error">{error || "Hakediş bulunamadı."}</div>
      </ErpShell>
    );
  }

  if (detail.status !== ProgressPaymentStatus.Draft) {
    return (
      <ErpShell
        title={`Hakediş ${detail.progressPaymentNumber}`}
        description=""
      >
        <div className="erp-alert error">
          Sadece taslak hakediş düzenlenebilir. Bu hakediş onay sürecine
          girmiş.
        </div>
        <Link href={`/hakedis/${detail.id}`}>Hakedişe Dön</Link>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      title={`Hakediş Düzenle — ${detail.progressPaymentNumber}`}
      description={`${detail.projectCode} · ${detail.projectName} · ${detail.periodNumber}. dönem`}
    >
      <div className="erp-toolbar">
        <div>
          <strong>Taslak düzenleme</strong>
          <small>
            Önceki dönem miktarları sunucudan gelir ve elle değiştirilemez.
          </small>
        </div>

        <Link href={`/hakedis/${detail.id}`}>Hakedişe Dön</Link>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      <form onSubmit={submit}>
        <div className="erp-form-card">
          <div className="erp-form-grid">
            <label>
              <span>Dönem Başlangıcı</span>
              <input
                type="date"
                value={periodStartDate}
                onChange={(event) => setPeriodStartDate(event.target.value)}
              />
            </label>

            <label>
              <span>Dönem Bitişi</span>
              <input
                type="date"
                value={periodEndDate}
                onChange={(event) => setPeriodEndDate(event.target.value)}
              />
            </label>

            <label>
              <span>Hakediş Tarihi *</span>
              <input
                required
                type="date"
                value={progressPaymentDate}
                onChange={(event) => setProgressPaymentDate(event.target.value)}
              />
            </label>

            <label>
              <span>Fiyat Farkı</span>
              <input
                type="number"
                step="0.01"
                value={priceDifferenceAmount}
                onChange={(event) =>
                  setPriceDifferenceAmount(Number(event.target.value))
                }
              />
            </label>

            <label>
              <span>KDV Oranı (%)</span>
              <input
                type="number"
                step="0.01"
                min={0}
                max={100}
                value={vatRate}
                onChange={(event) => setVatRate(Number(event.target.value))}
              />
            </label>

            <label>
              <span>KDV Tevkifatı</span>
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "1fr auto 1fr",
                  gap: "8px",
                  alignItems: "center",
                }}
              >
                <input
                  type="number"
                  min={0}
                  value={withholdingNumerator}
                  onChange={(event) =>
                    setWithholdingNumerator(Number(event.target.value))
                  }
                />
                <strong>/</strong>
                <input
                  type="number"
                  min={1}
                  value={withholdingDenominator}
                  onChange={(event) =>
                    setWithholdingDenominator(Number(event.target.value))
                  }
                />
              </div>
            </label>

            <label>
              <span>Stopaj (%)</span>
              <input
                type="number"
                step="0.01"
                min={0}
                max={100}
                value={incomeTaxWithholdingRate}
                onChange={(event) =>
                  setIncomeTaxWithholdingRate(Number(event.target.value))
                }
              />
            </label>

            <label className="span-2">
              <span>Açıklama</span>
              <input
                value={description}
                onChange={(event) => setDescription(event.target.value)}
              />
            </label>

            <label className="span-2">
              <span>Notlar</span>
              <textarea
                rows={3}
                value={notes}
                onChange={(event) => setNotes(event.target.value)}
              />
            </label>
          </div>
        </div>

        <HakedisEditor
          projectId={detail.projectId}
          progressPaymentId={detail.id}
          progressPaymentDate={progressPaymentDate}
          priceDifferenceAmount={priceDifferenceAmount}
          vatRate={vatRate}
          withholdingNumerator={withholdingNumerator}
          withholdingDenominator={withholdingDenominator}
          incomeTaxWithholdingRate={incomeTaxWithholdingRate}
          previousTotalAmount={previousTotalAmount}
          value={value}
          onChange={setValue}
          onSummaryChange={setSummary}
        />

        <div className="erp-actions" style={{ marginTop: 16 }}>
          <Link href={`/hakedis/${detail.id}`}>Vazgeç</Link>

          <button type="submit" disabled={saving}>
            {saving ? "Kaydediliyor..." : "Değişiklikleri Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
