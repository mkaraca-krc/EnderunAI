"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  engineeringPositionService,
  type EngineeringPositionListItem,
} from "@/services/engineering-position.service";

import {
  progressPaymentService,
  type ProgressPaymentItemRequest,
} from "@/services/progress-payment.service";

import {
  projectBoqService,
} from "@/services/project-boq.service";

import {
  projectMeasurementService,
} from "@/services/project-measurement.service";

type LineForm = ProgressPaymentItemRequest & {
  key: string;
};

const today = new Date().toISOString().slice(0, 10);

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

function newLine(): LineForm {
  return {
    key: crypto.randomUUID(),
    engineeringPositionId: null,
    positionCode: "",
    description: "",
    unit: "",
    contractQuantity: 0,
    currentQuantity: 0,
    unitPrice: 0,
    measurementReference: "",
    notes: "",
  };
}

export default function NewProgressPaymentPage() {
  const router = useRouter();

  const [companies, setCompanies] =
    useState<CompanyListItem[]>([]);

  const [projects, setProjects] =
    useState<ProjectListItem[]>([]);

  const [positions, setPositions] =
    useState<EngineeringPositionListItem[]>([]);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  const [error, setError] = useState("");
  const [sourceBoqName, setSourceBoqName] =
    useState("");

  const [
    sourceMeasurementId,
    setSourceMeasurementId,
  ] = useState("");

  const [
    sourceMeasurementName,
    setSourceMeasurementName,
  ] = useState("");

  const [companyId, setCompanyId] = useState("");
  const [projectId, setProjectId] = useState("");

  const [progressPaymentNumber, setProgressPaymentNumber] =
    useState("");

  const [periodNumber, setPeriodNumber] = useState(1);

  const [periodStartDate, setPeriodStartDate] =
    useState(today);

  const [periodEndDate, setPeriodEndDate] =
    useState(today);

  const [progressPaymentDate, setProgressPaymentDate] =
    useState(today);

  const [priceDifferenceAmount, setPriceDifferenceAmount] =
    useState(0);

  const [vatRate, setVatRate] = useState(20);

  const [withholdingNumerator, setWithholdingNumerator] =
    useState(4);

  const [withholdingDenominator, setWithholdingDenominator] =
    useState(10);

  const [description, setDescription] = useState("");
  const [notes, setNotes] = useState("");

  const [lines, setLines] = useState<LineForm[]>([
    newLine(),
  ]);

  useEffect(() => {
    async function load() {
      setLoading(true);
      setError("");

      try {
        const searchParams =
          new URLSearchParams(
            window.location.search
          );

        const requestedProjectId =
          searchParams.get("projectId");

        const requestedBoqId =
          searchParams.get("boqId");

        const requestedMeasurementId =
          searchParams.get("measurementId");

        const [
          companyRows,
          projectRows,
          positionRows,
        ] = await Promise.all([
          companyService.getAll(),
          projectService.getAll(),
          engineeringPositionService.getAll(),
        ]);

        setCompanies(companyRows);
        setProjects(projectRows);
        setPositions(positionRows);

        if (requestedMeasurementId) {
          const measurement =
            await projectMeasurementService.getById(
              requestedMeasurementId
            );

          setCompanyId(
            measurement.companyId
          );

          setProjectId(
            measurement.projectId
          );

          setSourceMeasurementId(
            measurement.id
          );

          setSourceMeasurementName(
            `${measurement.measurementNumber} — ${measurement.boqNumber}`
          );

          setSourceBoqName("");

          setDescription(
            `${measurement.measurementNumber} numaralı onaylı metrajdan oluşturuldu.`
          );

          setLines(
            measurement.items
              .filter(
                (measurementItem) =>
                  Number(
                    measurementItem.currentQuantity
                  ) > 0
              )
              .map(
                (measurementItem) => ({
                  key: crypto.randomUUID(),

                  engineeringPositionId:
                    measurementItem
                      .engineeringPositionId ??
                    null,

                  positionCode:
                    measurementItem.positionCode,

                  description:
                    measurementItem.description,

                  unit:
                    measurementItem.unit,

                  contractQuantity:
                    measurementItem
                      .contractQuantity,

                  currentQuantity:
                    measurementItem
                      .currentQuantity,

                  unitPrice:
                    measurementItem.unitPrice,

                  measurementReference:
                    measurementItem
                      .measurementReference ??
                    "",

                  notes:
                    measurementItem.notes ?? "",
                })
              )
          );

          return;
        }

        if (requestedBoqId) {
          const boq =
            await projectBoqService.getById(
              requestedBoqId
            );

          setCompanyId(boq.companyId);
          setProjectId(boq.projectId);

          setSourceBoqName(
            `${boq.boqNumber} ${boq.revisionCode} — ${boq.name}`
          );

          setDescription(
            `${boq.boqNumber} ${boq.revisionCode} keşfinden oluşturuldu.`
          );

          setLines(
            boq.items.map((boqItem) => ({
              key: crypto.randomUUID(),

              engineeringPositionId:
                boqItem.engineeringPositionId ??
                null,

              positionCode:
                boqItem.positionCode,

              description:
                boqItem.description,

              unit:
                boqItem.unit,

              contractQuantity:
                boqItem.contractQuantity,

              currentQuantity: 0,

              unitPrice:
                boqItem.unitPrice,

              measurementReference: "",

              notes:
                boqItem.notes ?? "",
            }))
          );

          return;
        }

        if (requestedProjectId) {
          const requestedProject =
            projectRows.find(
              (project) =>
                project.id === requestedProjectId
            );

          if (requestedProject) {
            setCompanyId(
              requestedProject.companyId
            );

            setProjectId(
              requestedProject.id
            );

            return;
          }
        }

        if (companyRows.length === 1) {
          setCompanyId(companyRows[0].id);
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Hakediş ekranı yüklenemedi."
        );
      } finally {
        setLoading(false);
      }
    }

    void load();
  }, []);

  const filteredProjects = useMemo(
    () =>
      projects.filter(
        (project) =>
          !companyId ||
          project.companyId === companyId
      ),
    [companyId, projects]
  );

  const filteredPositions = useMemo(
    () =>
      positions.filter(
        (position) =>
          !companyId ||
          position.companyId === companyId
      ),
    [companyId, positions]
  );

  const selectedProject = useMemo(
    () =>
      projects.find(
        (project) => project.id === projectId
      ),
    [projectId, projects]
  );

  useEffect(() => {
    if (!selectedProject) {
      return;
    }

    if (selectedProject.contractAmount !== undefined) {
      setVatRate(20);
    }
  }, [selectedProject]);

  const preview = useMemo(() => {
    const currentAmount = lines.reduce(
      (total, line) =>
        total +
        Number(line.currentQuantity || 0) *
          Number(line.unitPrice || 0),
      0
    );

    const taxableAmount =
      currentAmount +
      Number(priceDifferenceAmount || 0);

    const vatAmount =
      taxableAmount *
      (Number(vatRate || 0) / 100);

    const withholdingAmount =
      withholdingDenominator > 0
        ? vatAmount *
          (withholdingNumerator /
            withholdingDenominator)
        : 0;

    const grossPayableAmount =
      taxableAmount + vatAmount;

    const netPayableAmount =
      grossPayableAmount - withholdingAmount;

    return {
      currentAmount,
      taxableAmount,
      vatAmount,
      withholdingAmount,
      grossPayableAmount,
      netPayableAmount,
    };
  }, [
    lines,
    priceDifferenceAmount,
    vatRate,
    withholdingNumerator,
    withholdingDenominator,
  ]);

  function updateLine(
    key: string,
    changes: Partial<LineForm>
  ) {
    setLines((current) =>
      current.map((line) =>
        line.key === key
          ? { ...line, ...changes }
          : line
      )
    );
  }

  function selectPosition(
    key: string,
    positionId: string
  ) {
    const position = filteredPositions.find(
      (item) => item.id === positionId
    );

    if (!position) {
      updateLine(key, {
        engineeringPositionId: null,
        positionCode: "",
        description: "",
        unit: "",
      });

      return;
    }

    updateLine(key, {
      engineeringPositionId: position.id,
      positionCode: position.code,
      description: position.name,
      unit: position.unit,
    });
  }

  function addLine() {
    setLines((current) => [
      ...current,
      newLine(),
    ]);
  }

  function removeLine(key: string) {
    setLines((current) =>
      current.length === 1
        ? current
        : current.filter(
            (line) => line.key !== key
          )
    );
  }

  async function submit(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    setError("");

    if (!companyId) {
      setError("Şirket seçimi zorunludur.");
      return;
    }

    if (!projectId) {
      setError("Proje seçimi zorunludur.");
      return;
    }

    if (!progressPaymentNumber.trim()) {
      setError("Hakediş numarası zorunludur.");
      return;
    }

    const invalidLine = lines.find(
      (line) =>
        !line.positionCode.trim() ||
        !line.description.trim() ||
        !line.unit.trim() ||
        Number(line.currentQuantity) <= 0 ||
        Number(line.unitPrice) < 0
    );

    if (invalidLine) {
      setError(
        "Tüm poz satırlarında poz, miktar ve birim fiyat bilgilerini kontrol edin."
      );
      return;
    }

    setSaving(true);

    try {
      const result =
        await progressPaymentService.create({
          companyId,
          projectId,
          projectMeasurementId:
            sourceMeasurementId || null,
          progressPaymentNumber:
            progressPaymentNumber.trim(),
          periodNumber,
          periodStartDate:
            periodStartDate || null,
          periodEndDate:
            periodEndDate || null,
          progressPaymentDate,
          priceDifferenceAmount:
            Number(priceDifferenceAmount || 0),
          vatRate: Number(vatRate || 0),
          withholdingNumerator:
            Number(withholdingNumerator || 0),
          withholdingDenominator:
            Number(withholdingDenominator || 10),
          description:
            description.trim() || null,
          notes: notes.trim() || null,
          items: lines.map((line) => ({
            engineeringPositionId:
              line.engineeringPositionId || null,
            positionCode:
              line.positionCode.trim(),
            description:
              line.description.trim(),
            unit: line.unit.trim(),
            contractQuantity:
              Number(line.contractQuantity || 0),
            currentQuantity:
              Number(line.currentQuantity || 0),
            unitPrice:
              Number(line.unitPrice || 0),
            measurementReference:
              line.measurementReference?.trim() ||
              null,
            notes:
              line.notes?.trim() || null,
          })),
          deductions: [],
        });

      router.push(`/hakedis/${result.id}`);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Hakediş kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      title="Yeni Hakediş"
      description="Proje, dönem, metraj ve poz bilgilerini girerek yeni hakediş oluşturun."
    >
      <div className="erp-toolbar">
        <div>
          <strong>Hakediş oluşturma</strong>
          <small>
            Kesin hesaplar backend hesap motoru
            tarafından yeniden hesaplanacaktır.
          </small>
        </div>

        <Link href="/hakedis">
          Hakediş Listesine Dön
        </Link>
      </div>

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      {sourceMeasurementName && (
        <div className="erp-alert success">
          <strong>Metrajdan aktarıldı:</strong>{" "}
          {sourceMeasurementName}
          <br />
          Pozlar, bu dönem miktarları ve birim
          fiyatlar onaylı metrajdan otomatik
          getirildi.
        </div>
      )}

      {sourceBoqName && (
        <div className="erp-alert success">
          <strong>Keşiften aktarıldı:</strong>{" "}
          {sourceBoqName}
          <br />
          Poz, sözleşme miktarı ve birim fiyatlar
          otomatik getirildi. Yalnızca bu dönem
          miktarlarını girin.
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
                  loading ||
                  Boolean(sourceBoqName || sourceMeasurementName)
                }
                onChange={(event) => {
                  setCompanyId(event.target.value);
                  setProjectId("");
                  setLines([newLine()]);
                }}
              >
                <option value="">
                  Şirket seçin
                </option>

                {companies.map((company) => (
                  <option
                    key={company.id}
                    value={company.id}
                  >
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
                  !companyId ||
                  Boolean(sourceBoqName || sourceMeasurementName)
                }
                onChange={(event) =>
                  setProjectId(event.target.value)
                }
              >
                <option value="">
                  Proje seçin
                </option>

                {filteredProjects.map(
                  (project) => (
                    <option
                      key={project.id}
                      value={project.id}
                    >
                      {project.code} — {project.name}
                    </option>
                  )
                )}
              </select>
            </label>

            <label>
              <span>Hakediş No *</span>
              <input
                required
                value={progressPaymentNumber}
                onChange={(event) =>
                  setProgressPaymentNumber(
                    event.target.value.toUpperCase()
                  )
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
                onChange={(event) =>
                  setPeriodNumber(
                    Number(event.target.value)
                  )
                }
              />
            </label>

            <label>
              <span>Dönem Başlangıcı</span>
              <input
                type="date"
                value={periodStartDate}
                onChange={(event) =>
                  setPeriodStartDate(
                    event.target.value
                  )
                }
              />
            </label>

            <label>
              <span>Dönem Bitişi</span>
              <input
                type="date"
                value={periodEndDate}
                onChange={(event) =>
                  setPeriodEndDate(
                    event.target.value
                  )
                }
              />
            </label>

            <label>
              <span>Hakediş Tarihi *</span>
              <input
                required
                type="date"
                value={progressPaymentDate}
                onChange={(event) =>
                  setProgressPaymentDate(
                    event.target.value
                  )
                }
              />
            </label>

            <label>
              <span>Fiyat Farkı</span>
              <input
                type="number"
                step="0.01"
                min={0}
                value={priceDifferenceAmount}
                onChange={(event) =>
                  setPriceDifferenceAmount(
                    Number(event.target.value)
                  )
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
                onChange={(event) =>
                  setVatRate(
                    Number(event.target.value)
                  )
                }
              />
            </label>

            <label>
              <span>Tevkifat</span>
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns:
                    "1fr auto 1fr",
                  gap: "8px",
                  alignItems: "center",
                }}
              >
                <input
                  type="number"
                  min={0}
                  value={withholdingNumerator}
                  onChange={(event) =>
                    setWithholdingNumerator(
                      Number(event.target.value)
                    )
                  }
                />

                <strong>/</strong>

                <input
                  type="number"
                  min={1}
                  value={withholdingDenominator}
                  onChange={(event) =>
                    setWithholdingDenominator(
                      Number(event.target.value)
                    )
                  }
                />
              </div>
            </label>

            <label className="span-2">
              <span>Açıklama</span>
              <input
                value={description}
                onChange={(event) =>
                  setDescription(event.target.value)
                }
              />
            </label>

            <label className="span-2">
              <span>Notlar</span>
              <textarea
                rows={3}
                value={notes}
                onChange={(event) =>
                  setNotes(event.target.value)
                }
              />
            </label>
          </div>
        </div>

        <div className="erp-table-card">
          <div className="erp-toolbar">
            <div>
              <strong>Poz ve Metraj Satırları</strong>
              <small>
                {lines.length} satır
              </small>
            </div>

            <button
              type="button"
              onClick={addLine}
            >
              + Poz Satırı Ekle
            </button>
          </div>

          <div style={{ overflowX: "auto" }}>
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Poz</th>
                  <th>Sözleşme Miktarı</th>
                  <th>Bu Dönem</th>
                  <th>Birim Fiyat</th>
                  <th>Tutar</th>
                  <th>Metraj Ref.</th>
                  <th></th>
                </tr>
              </thead>

              <tbody>
                {lines.map((line) => (
                  <tr key={line.key}>
                    <td style={{ minWidth: 300 }}>
                      <select
                        value={
                          line.engineeringPositionId ??
                          ""
                        }
                        onChange={(event) =>
                          selectPosition(
                            line.key,
                            event.target.value
                          )
                        }
                      >
                        <option value="">
                          Poz seçin
                        </option>

                        {filteredPositions.map(
                          (position) => (
                            <option
                              key={position.id}
                              value={position.id}
                            >
                              {position.code} —{" "}
                              {position.name}
                            </option>
                          )
                        )}
                      </select>

                      <small>
                        {line.positionCode ||
                          "Poz seçilmedi"}
                        {line.unit
                          ? ` · ${line.unit}`
                          : ""}
                      </small>
                    </td>

                    <td>
                      <input
                        type="number"
                        min={0}
                        step="0.0001"
                        value={
                          line.contractQuantity || ""
                        }
                        onChange={(event) =>
                          updateLine(line.key, {
                            contractQuantity:
                              Number(
                                event.target.value
                              ),
                          })
                        }
                      />
                    </td>

                    <td>
                      <input
                        type="number"
                        min={0}
                        step="0.0001"
                        value={
                          line.currentQuantity || ""
                        }
                        onChange={(event) =>
                          updateLine(line.key, {
                            currentQuantity:
                              Number(
                                event.target.value
                              ),
                          })
                        }
                      />
                    </td>

                    <td>
                      <input
                        type="number"
                        min={0}
                        step="0.0001"
                        value={line.unitPrice || ""}
                        onChange={(event) =>
                          updateLine(line.key, {
                            unitPrice:
                              Number(
                                event.target.value
                              ),
                          })
                        }
                      />
                    </td>

                    <td>
                      <strong>
                        {money.format(
                          Number(
                            line.currentQuantity || 0
                          ) *
                            Number(
                              line.unitPrice || 0
                            )
                        )}
                      </strong>
                    </td>

                    <td>
                      <input
                        value={
                          line.measurementReference ??
                          ""
                        }
                        onChange={(event) =>
                          updateLine(line.key, {
                            measurementReference:
                              event.target.value,
                          })
                        }
                        placeholder="METRAJ-001"
                      />
                    </td>

                    <td>
                      <button
                        type="button"
                        onClick={() =>
                          removeLine(line.key)
                        }
                        disabled={lines.length === 1}
                      >
                        Sil
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        <div
          className="erp-form-card"
          style={{ marginTop: 16 }}
        >
          <h3>Hesap Ön İzlemesi</h3>

          <div className="erp-form-grid">
            <Summary
              label="Bu Dönem İmalat"
              value={preview.currentAmount}
            />

            <Summary
              label="Fiyat Farkı Dahil Matrah"
              value={preview.taxableAmount}
            />

            <Summary
              label="KDV"
              value={preview.vatAmount}
            />

            <Summary
              label={`Tevkifat ${withholdingNumerator}/${withholdingDenominator}`}
              value={preview.withholdingAmount}
            />

            <Summary
              label="Brüt Ödeme"
              value={preview.grossPayableAmount}
            />

            <Summary
              label="Tahmini Net Ödeme"
              value={preview.netPayableAmount}
              strong
            />
          </div>
        </div>

        <div
          className="erp-actions"
          style={{ marginTop: 16 }}
        >
          <Link href="/hakedis">
            Vazgeç
          </Link>

          <button
            type="submit"
            disabled={
              saving ||
              loading ||
              !companyId ||
              !projectId
            }
          >
            {saving
              ? "Hakediş Kaydediliyor..."
              : "Taslak Hakedişi Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}

function Summary({
  label,
  value,
  strong = false,
}: {
  label: string;
  value: number;
  strong?: boolean;
}) {
  return (
    <div>
      <span>{label}</span>
      <div
        style={{
          marginTop: 6,
          fontSize: strong ? 22 : 18,
          fontWeight: strong ? 800 : 600,
        }}
      >
        {money.format(value)}
      </div>
    </div>
  );
}
