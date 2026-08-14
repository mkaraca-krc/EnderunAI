"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import HakedisEditor, {
  defaultPaymentPlans,
  newItem,
  type HakedisEditorValue,
} from "@/components/hakedis/hakedis-editor";

import { companyService, type CompanyListItem } from "@/services/company.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import { progressPaymentService } from "@/services/progress-payment.service";
import { projectBoqService } from "@/services/project-boq.service";
import { projectMeasurementService } from "@/services/project-measurement.service";

const today = new Date().toISOString().slice(0, 10);

function emptyValue(): HakedisEditorValue {
  return {
    items: [newItem()],
    advanceMaterials: [],
    offsets: [],
    deductions: [],
    paymentPlans: defaultPaymentPlans(),
  };
}

/**
 * Yeni hakediş. Başlık bilgileri burada, imalat/ihzarat/kesinti/ödeme
 * girişi HakedisEditor içinde.
 *
 * Önceki dönem miktarları ve önceden kesilen tutarlar sunucudan tek
 * çağrıda gelir (previous-context); kullanıcı bunları elle girmez.
 */
export default function NewProgressPaymentPage() {
  const router = useRouter();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const [sourceBoqName, setSourceBoqName] = useState("");
  const [sourceMeasurementId, setSourceMeasurementId] = useState("");
  const [sourceMeasurementName, setSourceMeasurementName] = useState("");

  const [companyId, setCompanyId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [progressPaymentNumber, setProgressPaymentNumber] = useState("");
  const [periodNumber, setPeriodNumber] = useState(1);
  const [periodStartDate, setPeriodStartDate] = useState(today);
  const [periodEndDate, setPeriodEndDate] = useState(today);
  const [progressPaymentDate, setProgressPaymentDate] = useState(today);
  const [priceDifferenceAmount, setPriceDifferenceAmount] = useState(0);
  const [vatRate, setVatRate] = useState(20);
  const [withholdingNumerator, setWithholdingNumerator] = useState(4);
  const [withholdingDenominator, setWithholdingDenominator] = useState(10);
  const [incomeTaxWithholdingRate, setIncomeTaxWithholdingRate] = useState(0);
  const [description, setDescription] = useState("");
  const [notes, setNotes] = useState("");

  const [value, setValue] = useState<HakedisEditorValue>(emptyValue);
  const [previousTotalAmount, setPreviousTotalAmount] = useState(0);
  const [summary, setSummary] = useState<{
    netPayableAmount: number;
    planError: string | null;
  }>({ netPayableAmount: 0, planError: null });

  useEffect(() => {
    async function load() {
      setLoading(true);
      setError("");

      try {
        const searchParams = new URLSearchParams(window.location.search);
        const requestedProjectId = searchParams.get("projectId");
        const requestedBoqId = searchParams.get("boqId");
        const requestedMeasurementId = searchParams.get("measurementId");

        const [companyRows, projectRows] = await Promise.all([
          companyService.getAll(),
          projectService.getAll(),
        ]);

        setCompanies(companyRows);
        setProjects(projectRows);

        if (requestedMeasurementId) {
          const measurement = await projectMeasurementService.getById(
            requestedMeasurementId
          );

          setCompanyId(measurement.companyId);
          setProjectId(measurement.projectId);
          setSourceMeasurementId(measurement.id);
          setSourceMeasurementName(
            `${measurement.measurementNumber} — ${measurement.boqNumber}`
          );
          setDescription(
            `${measurement.measurementNumber} numaralı onaylı metrajdan oluşturuldu.`
          );

          setValue({
            ...emptyValue(),
            items: measurement.items
              .filter((x) => Number(x.currentQuantity) > 0)
              .map((x) => ({
                ...newItem(),
                engineeringPositionId: x.engineeringPositionId ?? null,
                positionCode: x.positionCode,
                description: x.description,
                unit: x.unit,
                contractQuantity: x.contractQuantity,
                currentQuantity: x.currentQuantity,
                // Metrajdan gelen tek birim fiyat malzemeye yazılır;
                // kullanıcı montaj/GG&K ayrımını sonra girer.
                materialUnitPrice: x.unitPrice,
                measurementReference: x.measurementReference ?? "",
                notes: x.notes ?? "",
              })),
          });

          return;
        }

        if (requestedBoqId) {
          const boq = await projectBoqService.getById(requestedBoqId);

          setCompanyId(boq.companyId);
          setProjectId(boq.projectId);
          setSourceBoqName(`${boq.boqNumber} ${boq.revisionCode} — ${boq.name}`);
          setDescription(
            `${boq.boqNumber} ${boq.revisionCode} keşfinden oluşturuldu.`
          );

          setValue({
            ...emptyValue(),
            items: boq.items.map((x) => ({
              ...newItem(),
              engineeringPositionId: x.engineeringPositionId ?? null,
              // İcmal bağı taşınıyor: editördeki "sahaya göre" sütunu ve
              // sunucudaki saha dondurması bu bağa dayanıyor.
              projectBoqItemId: x.id,
              sectionId: x.projectHakedisSectionId ?? null,
              positionCode: x.positionCode,
              description: x.description,
              unit: x.unit,
              contractQuantity: x.contractQuantity,
              currentQuantity: 0,
              materialUnitPrice: x.materialUnitPrice || x.unitPrice,
              laborUnitPrice: x.laborUnitPrice,
              overheadUnitPrice: x.overheadUnitPrice,
              notes: x.notes ?? "",
            })),
          });

          return;
        }

        if (requestedProjectId) {
          const requested = projectRows.find((x) => x.id === requestedProjectId);

          if (requested) {
            setCompanyId(requested.companyId);
            setProjectId(requested.id);
            return;
          }
        }

        if (companyRows.length === 1) {
          setCompanyId(companyRows[0].id);
        }
      } catch (err) {
        setError(
          err instanceof Error ? err.message : "Hakediş ekranı yüklenemedi."
        );
      } finally {
        setLoading(false);
      }
    }

    void load();
  }, []);

  /**
   * Proje veya dönem değişince önceki dönem bağlamını tazele: poz
   * miktarları, önceden kesilenler ve minha toplamı.
   */
  const loadPreviousContext = useCallback(async () => {
    if (!projectId || periodNumber <= 0) {
      setPreviousTotalAmount(0);
      return;
    }

    try {
      const context = await progressPaymentService.getPreviousContext(
        projectId,
        periodNumber
      );

      setPreviousTotalAmount(context.previousTotalAmount);

      setValue((current) => ({
        ...current,
        items: current.items.map((item) => ({
          ...item,
          previousQuantity:
            context.previousQuantities.find(
              (x) =>
                x.positionCode.toLowerCase() ===
                item.positionCode.trim().toLowerCase()
            )?.quantity ?? 0,
        })),
        deductions: current.deductions.map((deduction) => ({
          ...deduction,
          previousAmount:
            context.previousDeductions.find(
              (x) => x.deductionType === deduction.deductionType
            )?.amount ?? 0,
        })),
      }));
    } catch {
      setPreviousTotalAmount(0);
    }
  }, [periodNumber, projectId]);

  useEffect(() => {
    void loadPreviousContext();
  }, [loadPreviousContext]);

  const filteredProjects = useMemo(
    () => projects.filter((x) => !companyId || x.companyId === companyId),
    [companyId, projects]
  );

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");

    if (!companyId) return setError("Şirket seçimi zorunludur.");
    if (!projectId) return setError("Proje seçimi zorunludur.");
    if (!progressPaymentNumber.trim())
      return setError("Hakediş numarası zorunludur.");

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
      const result = await progressPaymentService.create({
        companyId,
        projectId,
        projectMeasurementId: sourceMeasurementId || null,
        progressPaymentNumber: progressPaymentNumber.trim(),
        periodNumber,
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
          // Bileşenler gönderildiği için toplam sunucuda kurulur.
          unitPrice: 0,
          materialUnitPrice: Number(line.materialUnitPrice || 0),
          laborUnitPrice: Number(line.laborUnitPrice || 0),
          overheadUnitPrice: Number(line.overheadUnitPrice || 0),
          sectionId: line.sectionId,
          // İcmalden gelen satırda bağ taşınır; sunucu saha miktarını
          // bu bağ üzerinden bulup kayda dondurur.
          projectBoqItemId: line.projectBoqItemId,
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
          // Taban gönderilmiyor: sunucu hakedişin kümülatifini kullanır.
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

      router.push(`/hakedis/${result.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Hakediş kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Yeni Hakediş"
      description="İmalat, ihzarat, kesintiler ve ödeme dağılımı — üst hesap girdikçe hesaplanır."
    >
      <div className="erp-toolbar">
        <div>
          <strong>Hakediş oluşturma</strong>
          <small>
            Ekrandaki tutarlar önizlemedir; kesin hesap kaydederken sunucuda
            yeniden yapılır.
          </small>
        </div>

        <Link href="/hakedis">Hakediş Listesine Dön</Link>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      {sourceMeasurementName && (
        <div className="erp-alert success">
          <strong>Metrajdan aktarıldı:</strong> {sourceMeasurementName}
          <br />
          Birim fiyatlar malzeme bileşenine yazıldı; montaj ve genel gider
          ayrımını poz satırlarından girebilirsiniz.
        </div>
      )}

      {sourceBoqName && (
        <div className="erp-alert success">
          <strong>Keşiften aktarıldı:</strong> {sourceBoqName}
          <br />
          Sözleşme miktarları ve birim fiyatlar geldi; bu dönem miktarlarını
          girin.
        </div>
      )}

      <form onSubmit={submit}>
        <div className="erp-form-card">
          <div className="erp-form-grid">
            <label>
              <span>Şirket *</span>
              <select
                required
                value={companyId}
                disabled={
                  loading || Boolean(sourceBoqName || sourceMeasurementName)
                }
                onChange={(event) => {
                  setCompanyId(event.target.value);
                  setProjectId("");
                  setValue(emptyValue());
                }}
              >
                <option value="">Şirket seçin</option>
                {companies.map((company) => (
                  <option key={company.id} value={company.id}>
                    {company.code} — {company.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Proje *</span>
              <select
                required
                value={projectId}
                disabled={
                  !companyId || Boolean(sourceBoqName || sourceMeasurementName)
                }
                onChange={(event) => setProjectId(event.target.value)}
              >
                <option value="">Proje seçin</option>
                {filteredProjects.map((project) => (
                  <option key={project.id} value={project.id}>
                    {project.code} — {project.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Hakediş No *</span>
              <input
                required
                value={progressPaymentNumber}
                onChange={(event) =>
                  setProgressPaymentNumber(event.target.value.toUpperCase())
                }
                placeholder="Örn. HD-001"
              />
            </label>

            <label>
              <span>Dönem No *</span>
              <input
                required
                type="number"
                min={1}
                value={periodNumber}
                onChange={(event) => setPeriodNumber(Number(event.target.value))}
              />
            </label>

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
          projectId={projectId}
          companyId={companyId}
          periodNumber={periodNumber}
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
          <Link href="/hakedis">Vazgeç</Link>

          <button
            type="submit"
            disabled={saving || loading || !companyId || !projectId}
          >
            {saving ? "Hakediş Kaydediliyor..." : "Taslak Hakedişi Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
