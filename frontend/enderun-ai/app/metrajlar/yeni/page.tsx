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
import { currencyMoney, quantity } from "@/lib/format/turkish";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  projectBoqService,
  ProjectBoqStatus,
  type ProjectBoqDetail,
  type ProjectBoqListItem,
} from "@/services/project-boq.service";

import {
  projectMeasurementService,
  type ProjectMeasurementItemRequest,
} from "@/services/project-measurement.service";

type MeasurementLine =
  ProjectMeasurementItemRequest & {
    key: string;
    positionCode: string;
    description: string;
    unit: string;
    contractQuantity: number;
    unitPrice: number;
  };

function todayValue() {
  const date = new Date();

  const year = date.getFullYear();
  const month = String(
    date.getMonth() + 1
  ).padStart(2, "0");

  const day = String(
    date.getDate()
  ).padStart(2, "0");

  return `${year}-${month}-${day}`;
}

function formatMoney(
  amount: number,
  currencyCode: string
) {
  return currencyMoney(amount, currencyCode);
}

function formatQuantity(value: number) {
  return quantity(value);
}

export default function NewProjectMeasurementPage() {
  const router = useRouter();

  const [companies, setCompanies] =
    useState<CompanyListItem[]>([]);

  const [projects, setProjects] =
    useState<ProjectListItem[]>([]);

  const [boqs, setBoqs] =
    useState<ProjectBoqListItem[]>([]);

  const [selectedBoq, setSelectedBoq] =
    useState<ProjectBoqDetail | null>(null);

  const [lines, setLines] =
    useState<MeasurementLine[]>([]);

  const [companyId, setCompanyId] =
    useState("");

  const [projectId, setProjectId] =
    useState("");

  const [projectBoqId, setProjectBoqId] =
    useState("");

  const [
    measurementNumber,
    setMeasurementNumber,
  ] = useState("");

  const [
    measurementDate,
    setMeasurementDate,
  ] = useState(todayValue());

  const [description, setDescription] =
    useState("");

  const [notes, setNotes] =
    useState("");

  const [loading, setLoading] =
    useState(true);

  const [boqLoading, setBoqLoading] =
    useState(false);

  const [saving, setSaving] =
    useState(false);

  const [error, setError] =
    useState("");

  useEffect(() => {
    async function loadInitialData() {
      setLoading(true);
      setError("");

      try {
        const [
          companyRows,
          projectRows,
        ] = await Promise.all([
          companyService.getAll(),
          projectService.getAll(),
        ]);

        setCompanies(companyRows);
        setProjects(projectRows);

        if (companyRows.length === 1) {
          setCompanyId(companyRows[0].id);
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Metraj ekranı yüklenemedi."
        );
      } finally {
        setLoading(false);
      }
    }

    void loadInitialData();
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

  const totalAmount = useMemo(
    () =>
      lines.reduce(
        (sum, line) =>
          sum +
          Number(
            line.currentQuantity || 0
          ) *
            Number(
              line.unitPrice || 0
            ),
        0
      ),
    [lines]
  );

  const activeLineCount = useMemo(
    () =>
      lines.filter(
        (line) =>
          Number(
            line.currentQuantity || 0
          ) > 0
      ).length,
    [lines]
  );

  async function loadBoqs(
    selectedProjectId: string
  ) {
    setProjectBoqId("");
    setSelectedBoq(null);
    setLines([]);
    setBoqs([]);

    if (!selectedProjectId) {
      return;
    }

    setBoqLoading(true);
    setError("");

    try {
      const rows =
        await projectBoqService.getAll({
          companyId,
          projectId: selectedProjectId,
          status: ProjectBoqStatus.Approved,
        });

      setBoqs(
        rows.filter(
          (row) =>
            row.isCurrentRevision &&
            row.status ===
              ProjectBoqStatus.Approved
        )
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Onaylı keşifler yüklenemedi."
      );
    } finally {
      setBoqLoading(false);
    }
  }

  async function selectBoq(
    boqId: string
  ) {
    setProjectBoqId(boqId);
    setSelectedBoq(null);
    setLines([]);
    setError("");

    if (!boqId) {
      return;
    }

    setBoqLoading(true);

    try {
      const detail =
        await projectBoqService.getById(
          boqId
        );

      setSelectedBoq(detail);

      setLines(
        detail.items.map((item) => ({
          key: item.id,
          projectBoqItemId: item.id,
          positionCode:
            item.positionCode,
          description:
            item.description,
          unit:
            item.unit,
          contractQuantity:
            item.contractQuantity,
          unitPrice:
            item.unitPrice,
          currentQuantity: 0,
          measurementReference: "",
          location: "",
          block: "",
          floor: "",
          room: "",
          notes: "",
        }))
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Keşif detayları yüklenemedi."
      );
    } finally {
      setBoqLoading(false);
    }
  }

  function updateLine(
    key: string,
    changes: Partial<MeasurementLine>
  ) {
    setLines((current) =>
      current.map((line) =>
        line.key === key
          ? {
              ...line,
              ...changes,
            }
          : line
      )
    );
  }

  async function submit(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();
    setError("");

    if (!companyId) {
      setError(
        "Şirket seçimi zorunludur."
      );
      return;
    }

    if (!projectId) {
      setError(
        "Proje seçimi zorunludur."
      );
      return;
    }

    if (!projectBoqId) {
      setError(
        "Onaylı keşif seçimi zorunludur."
      );
      return;
    }

    if (!measurementNumber.trim()) {
      setError(
        "Metraj numarası zorunludur."
      );
      return;
    }

    const selectedLines =
      lines.filter(
        (line) =>
          Number(
            line.currentQuantity || 0
          ) > 0
      );

    if (selectedLines.length === 0) {
      setError(
        "En az bir keşif kalemine bu dönem miktarı girilmelidir."
      );
      return;
    }

    const exceededLine =
      selectedLines.find(
        (line) =>
          Number(
            line.currentQuantity || 0
          ) >
          Number(
            line.contractQuantity || 0
          )
      );

    if (exceededLine) {
      setError(
        `${exceededLine.positionCode} pozunda bu dönem miktarı keşif miktarını aşamaz.`
      );
      return;
    }

    setSaving(true);

    try {
      const result =
        await projectMeasurementService.create({
          companyId,
          projectId,
          projectBoqId,
          measurementNumber:
            measurementNumber
              .trim()
              .toUpperCase(),
          measurementDate:
            new Date(
              `${measurementDate}T12:00:00`
            ).toISOString(),
          description:
            description.trim() || null,
          notes:
            notes.trim() || null,
          items: selectedLines.map(
            (line) => ({
              projectBoqItemId:
                line.projectBoqItemId,
              currentQuantity:
                Number(
                  line.currentQuantity || 0
                ),
              measurementReference:
                line.measurementReference
                  ?.trim() || null,
              location:
                line.location?.trim() ||
                null,
              block:
                line.block?.trim() ||
                null,
              floor:
                line.floor?.trim() ||
                null,
              room:
                line.room?.trim() ||
                null,
              notes:
                line.notes?.trim() ||
                null,
            })
          ),
        });

      router.push(
        `/metrajlar/${result.id}`
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Metraj kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Yeni Metraj"
      description="Onaylı keşif üzerinden dönemsel saha metrajı oluşturun."
    >
      <div className="erp-toolbar">
        <div>
          <strong>
            Yeni Metraj Oluştur
          </strong>

          <small>
            Keşif kalemleri otomatik
            yüklenir.
          </small>
        </div>

        <Link href="/metrajlar">
          Metraj Listesine Dön
        </Link>
      </div>

      {error && (
        <div className="erp-alert error">
          {error}
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
                disabled={loading}
                onChange={(event) => {
                  const value =
                    event.target.value;

                  setCompanyId(value);
                  setProjectId("");
                  setProjectBoqId("");
                  setBoqs([]);
                  setSelectedBoq(null);
                  setLines([]);
                }}
              >
                <option value="">
                  Şirket seçin
                </option>

                {companies.map(
                  (company) => (
                    <option
                      key={company.id}
                      value={company.id}
                    >
                      {company.code} —{" "}
                      {company.name}
                    </option>
                  )
                )}
              </select>
            </label>

            <label>
              <span>Proje *</span>

              <select
                required
                value={projectId}
                disabled={
                  !companyId ||
                  loading
                }
                onChange={(event) => {
                  const value =
                    event.target.value;

                  setProjectId(value);
                  void loadBoqs(value);
                }}
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
                      {project.code} —{" "}
                      {project.name}
                    </option>
                  )
                )}
              </select>
            </label>

            <label>
              <span>
                Onaylı Keşif *
              </span>

              <select
                required
                value={projectBoqId}
                disabled={
                  !projectId ||
                  boqLoading
                }
                onChange={(event) =>
                  void selectBoq(
                    event.target.value
                  )
                }
              >
                <option value="">
                  {boqLoading
                    ? "Keşifler yükleniyor..."
                    : "Keşif seçin"}
                </option>

                {boqs.map((boq) => (
                  <option
                    key={boq.id}
                    value={boq.id}
                  >
                    {boq.boqNumber} —{" "}
                    {boq.name} —{" "}
                    {boq.revisionCode}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Metraj No *</span>

              <input
                required
                value={
                  measurementNumber
                }
                onChange={(event) =>
                  setMeasurementNumber(
                    event.target.value
                      .toUpperCase()
                  )
                }
                placeholder="Örn. MTR-001"
              />
            </label>

            <label>
              <span>
                Metraj Tarihi *
              </span>

              <input
                required
                type="date"
                value={measurementDate}
                onChange={(event) =>
                  setMeasurementDate(
                    event.target.value
                  )
                }
              />
            </label>

            <label>
              <span>Para Birimi</span>

              <input
                readOnly
                value={
                  selectedBoq
                    ?.currencyCode ||
                  "TRY"
                }
              />
            </label>

            <label className="span-2">
              <span>Açıklama</span>

              <input
                value={description}
                onChange={(event) =>
                  setDescription(
                    event.target.value
                  )
                }
                placeholder="Metraj dönemi veya saha açıklaması"
              />
            </label>

            <label className="span-2">
              <span>Notlar</span>

              <textarea
                rows={3}
                value={notes}
                onChange={(event) =>
                  setNotes(
                    event.target.value
                  )
                }
              />
            </label>
          </div>
        </div>

        <div
          className="erp-table-card"
          style={{ marginTop: 16 }}
        >
          <div className="erp-toolbar">
            <div>
              <strong>
                Metraj Kalemleri
              </strong>

              <small>
                {lines.length} keşif
                kalemi ·{" "}
                {activeLineCount} kullanılan
                kalem ·{" "}
                {formatMoney(
                  totalAmount,
                  selectedBoq
                    ?.currencyCode ||
                    "TRY"
                )}
              </small>
            </div>
          </div>

          <div
            style={{
              overflowX: "auto",
            }}
          >
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Poz</th>
                  <th>Birim</th>
                  <th>Keşif Miktarı</th>
                  <th>Bu Dönem *</th>
                  <th>Mahall</th>
                  <th>Blok</th>
                  <th>Kat</th>
                  <th>Oda</th>
                  <th>Referans</th>
                  <th>Tutar</th>
                </tr>
              </thead>

              <tbody>
                {!projectBoqId && (
                  <tr>
                    <td colSpan={10}>
                      Önce proje ve onaylı
                      keşif seçin.
                    </td>
                  </tr>
                )}

                {projectBoqId &&
                  boqLoading && (
                    <tr>
                      <td colSpan={10}>
                        Keşif kalemleri
                        yükleniyor...
                      </td>
                    </tr>
                  )}

                {projectBoqId &&
                  !boqLoading &&
                  lines.length === 0 && (
                    <tr>
                      <td colSpan={10}>
                        Keşifte kullanılabilir
                        kalem bulunamadı.
                      </td>
                    </tr>
                  )}

                {lines.map((line) => (
                  <tr key={line.key}>
                    <td
                      style={{
                        minWidth: 300,
                      }}
                    >
                      <strong>
                        {line.positionCode}
                      </strong>

                      <div>
                        {line.description}
                      </div>
                    </td>

                    <td>
                      {line.unit}
                    </td>

                    <td>
                      {formatQuantity(
                        line.contractQuantity
                      )}
                    </td>

                    <td
                      style={{
                        minWidth: 130,
                      }}
                    >
                      <input
                        type="number"
                        min={0}
                        max={
                          line.contractQuantity
                        }
                        step="0.0001"
                        value={
                          line.currentQuantity ||
                          ""
                        }
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            {
                              currentQuantity:
                                Number(
                                  event.target
                                    .value
                                ),
                            }
                          )
                        }
                      />
                    </td>

                    <td
                      style={{
                        minWidth: 160,
                      }}
                    >
                      <input
                        value={
                          line.location ??
                          ""
                        }
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            {
                              location:
                                event.target
                                  .value,
                            }
                          )
                        }
                        placeholder="Mahall"
                      />
                    </td>

                    <td
                      style={{
                        minWidth: 100,
                      }}
                    >
                      <input
                        value={
                          line.block ?? ""
                        }
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            {
                              block:
                                event.target
                                  .value,
                            }
                          )
                        }
                      />
                    </td>

                    <td
                      style={{
                        minWidth: 100,
                      }}
                    >
                      <input
                        value={
                          line.floor ?? ""
                        }
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            {
                              floor:
                                event.target
                                  .value,
                            }
                          )
                        }
                      />
                    </td>

                    <td
                      style={{
                        minWidth: 100,
                      }}
                    >
                      <input
                        value={
                          line.room ?? ""
                        }
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            {
                              room:
                                event.target
                                  .value,
                            }
                          )
                        }
                      />
                    </td>

                    <td
                      style={{
                        minWidth: 170,
                      }}
                    >
                      <input
                        value={
                          line.measurementReference ??
                          ""
                        }
                        onChange={(event) =>
                          updateLine(
                            line.key,
                            {
                              measurementReference:
                                event.target
                                  .value,
                            }
                          )
                        }
                        placeholder="Ataşman / çizim"
                      />
                    </td>

                    <td>
                      <strong>
                        {formatMoney(
                          Number(
                            line.currentQuantity ||
                              0
                          ) *
                            Number(
                              line.unitPrice ||
                                0
                            ),
                          selectedBoq
                            ?.currencyCode ||
                            "TRY"
                        )}
                      </strong>
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
          <div className="erp-form-grid">
            <div>
              <span>
                Kullanılan Kalem
              </span>

              <div
                style={{
                  marginTop: 6,
                  fontSize: 20,
                  fontWeight: 700,
                }}
              >
                {activeLineCount}
              </div>
            </div>

            <div>
              <span>
                Bu Dönem Metraj Tutarı
              </span>

              <div
                style={{
                  marginTop: 6,
                  fontSize: 24,
                  fontWeight: 800,
                }}
              >
                {formatMoney(
                  totalAmount,
                  selectedBoq
                    ?.currencyCode ||
                    "TRY"
                )}
              </div>
            </div>
          </div>
        </div>

        <div
          className="erp-actions"
          style={{ marginTop: 16 }}
        >
          <Link href="/metrajlar">
            Vazgeç
          </Link>

          <button
            type="submit"
            disabled={
              saving ||
              loading ||
              boqLoading ||
              !companyId ||
              !projectId ||
              !projectBoqId
            }
          >
            {saving
              ? "Metraj Kaydediliyor..."
              : "Taslak Metrajı Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
