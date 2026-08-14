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
import { money } from "@/lib/format/turkish";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import PositionPicker, {
  type PickedPosition,
} from "@/components/engineering/position-picker";
import PositionSuggestButton from "@/components/engineering/position-suggest-button";
import {
  projectBoqService,
  ProjectBoqItemType,
  type ProjectBoqItemRequest,
} from "@/services/project-boq.service";

type BoqLine = ProjectBoqItemRequest & {
  key: string;
  /** Seçili pozun ekranda gösterilen kısa hâli; sunucuya gitmez. */
  positionLabel?: string;
  /** Fiyatın nereden geldiği ya da neden boş olduğu; sunucuya gitmez. */
  priceNote?: string;
};

function newLine(): BoqLine {
  return {
    key: crypto.randomUUID(),
    engineeringPositionId: null,
    positionCode: "",
    description: "",
    unit: "",
    contractQuantity: 0,
    unitPrice: 0,
    itemType: ProjectBoqItemType.Mixed,
    category: "",
    notes: "",
    positionLabel: "",
    priceNote: "",
  };
}

export default function NewProjectBoqPage() {
  const router = useRouter();

  const [companies, setCompanies] =
    useState<CompanyListItem[]>([]);

  const [projects, setProjects] =
    useState<ProjectListItem[]>([]);


  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  const [error, setError] = useState("");

  const [companyId, setCompanyId] = useState("");
  const [projectId, setProjectId] = useState("");

  const [boqNumber, setBoqNumber] = useState("");
  const [name, setName] = useState("");

  const [revisionNumber, setRevisionNumber] =
    useState(1);

  const [currencyCode, setCurrencyCode] =
    useState("TRY");

  const [description, setDescription] =
    useState("");

  const [notes, setNotes] = useState("");

  const [lines, setLines] = useState<BoqLine[]>([
    newLine(),
  ]);

  useEffect(() => {
    async function load() {
      setLoading(true);
      setError("");

      try {
        const [companyRows, projectRows] = await Promise.all([
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
            : "Keşif ekranı yüklenemedi."
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

  const totalAmount = useMemo(
    () =>
      lines.reduce(
        (sum, line) =>
          sum +
          Number(line.contractQuantity || 0) *
            Number(line.unitPrice || 0),
        0
      ),
    [lines]
  );

  function updateLine(
    key: string,
    changes: Partial<BoqLine>
  ) {
    setLines((current) =>
      current.map((line) =>
        line.key === key
          ? { ...line, ...changes }
          : line
      )
    );
  }

  /**
   * Poz seçilince kalem doldurulur. Fiyat kütüphaneden gelirse
   * malzeme/montaj ayrı ayrı yazılır; gelmezse alanlar BOŞ bırakılır —
   * sıfır fiyat doldurmak sessiz bir hata olurdu.
   */
  function selectPosition(key: string, position: PickedPosition | null) {
    if (!position) {
      updateLine(key, {
        engineeringPositionId: null,
        positionCode: "",
        positionLabel: "",
        priceNote: "",
      });

      return;
    }

    const patch: Partial<BoqLine> = {
      engineeringPositionId: position.id,
      positionCode: position.officialCode || position.code,
      positionLabel: `${position.officialCode || position.code} — ${position.name}`,
      description: position.name,
      unit: position.unit,
      category: position.category ?? "",
      priceNote: position.priceExplanation ?? "",
    };

    if (position.materialPrice != null)
      patch.materialUnitPrice = position.materialPrice;

    if (position.laborPrice != null)
      patch.laborUnitPrice = position.laborPrice;

    // Kitap bileşen vermediyse toplam fiyat malzemeye yazılır;
    // mevcut davranışla aynı, tek fiyatlı kitaplar böyle çalışıyor.
    if (position.materialPrice == null
        && position.laborPrice == null
        && position.unitPrice != null) {
      patch.materialUnitPrice = position.unitPrice;
    }

    updateLine(key, patch);
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

    if (!boqNumber.trim()) {
      setError("Keşif numarası zorunludur.");
      return;
    }

    if (!name.trim()) {
      setError("Keşif adı zorunludur.");
      return;
    }

    // Tamamen boş satırlar yok sayılır: icmal Excel'den aktarılacaksa
    // kalem girmeden kaydedilebilmeli, sahte satır girdirmek gereksiz.
    const filled = lines.filter(
      (line) =>
        line.positionCode.trim() ||
        line.description.trim() ||
        line.unit.trim() ||
        Number(line.contractQuantity) > 0 ||
        Number(line.unitPrice) > 0
    );

    const invalidLine = filled.find(
      (line) =>
        !line.positionCode.trim() ||
        !line.description.trim() ||
        !line.unit.trim() ||
        Number(line.contractQuantity) < 0 ||
        Number(line.unitPrice) < 0
    );

    if (invalidLine) {
      setError(
        "Keşif kalemlerinde poz, açıklama, birim, miktar ve fiyat bilgilerini kontrol edin."
      );
      return;
    }

    setSaving(true);

    try {
      const result =
        await projectBoqService.create({
          companyId,
          projectId,
          boqNumber: boqNumber.trim(),
          name: name.trim(),
          revisionNumber,
          currencyCode:
            currencyCode.trim().toUpperCase(),
          description:
            description.trim() || null,
          notes:
            notes.trim() || null,
          items: filled.map((line) => ({
            engineeringPositionId:
              line.engineeringPositionId || null,
            positionCode:
              line.positionCode.trim(),
            description:
              line.description.trim(),
            unit:
              line.unit.trim(),
            contractQuantity:
              Number(line.contractQuantity || 0),
            unitPrice:
              Number(line.unitPrice || 0),
            itemType:
              line.itemType,
            category:
              line.category?.trim() || null,
            notes:
              line.notes?.trim() || null,
          })),
        });

      router.push(`/kesifler/${result.id}`);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Keşif kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Yeni Keşif"
      description="Proje sözleşme keşfini ve keşif kalemlerini oluşturun."
    >
      <div className="erp-toolbar">
        <div>
          <strong>Yeni Keşif Oluştur</strong>
          <small>
            Keşif toplamı kalemlerden otomatik hesaplanır.
          </small>
        </div>

        <Link href="/kesifler">
          Keşif Listesine Dön
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
                disabled={!companyId}
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
              <span>Keşif No *</span>

              <input
                required
                value={boqNumber}
                onChange={(event) =>
                  setBoqNumber(
                    event.target.value.toUpperCase()
                  )
                }
                placeholder="Örn. KESIF-001"
              />
            </label>

            <label>
              <span>Keşif Adı *</span>

              <input
                required
                value={name}
                onChange={(event) =>
                  setName(event.target.value)
                }
                placeholder="Ana Sözleşme Keşfi"
              />
            </label>

            <label>
              <span>Revizyon No *</span>

              <input
                required
                type="number"
                min={1}
                value={revisionNumber}
                onChange={(event) =>
                  setRevisionNumber(
                    Number(event.target.value)
                  )
                }
              />
            </label>

            <label>
              <span>Para Birimi *</span>

              <select
                required
                value={currencyCode}
                onChange={(event) =>
                  setCurrencyCode(event.target.value)
                }
              >
                <option value="TRY">TRY</option>
                <option value="USD">USD</option>
                <option value="EUR">EUR</option>
                <option value="GBP">GBP</option>
              </select>
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

        <div
          className="erp-table-card"
          style={{ marginTop: 16 }}
        >
          <div className="erp-toolbar">
            <div>
              <strong>Keşif Kalemleri</strong>
              <small>
                {lines.length} kalem ·{" "}
                {money(totalAmount)}
              </small>
              <small style={{ display: "block" }}>
                Kalemleri Excel&apos;den aktaracaksanız burayı boş
                bırakabilirsiniz; kaydettikten sonra icmal ekranından
                dosyanızı yükleyin.
              </small>
            </div>

            <button
              type="button"
              onClick={addLine}
            >
              + Kalem Ekle
            </button>
          </div>

          <div style={{ overflowX: "auto" }}>
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Poz</th>
                  <th>Birim</th>
                  <th>Miktar</th>
                  <th>Birim Fiyat</th>
                  <th>Tür</th>
                  <th>Kategori</th>
                  <th>Toplam</th>
                  <th></th>
                </tr>
              </thead>

              <tbody>
                {lines.map((line) => (
                  <tr key={line.key}>
                    <td style={{ minWidth: 320 }}>
                      <PositionPicker
                        value={line.engineeringPositionId}
                        label={line.positionLabel}
                        onPick={(position) =>
                          selectPosition(line.key, position)
                        }
                      />

                      <PositionSuggestButton
                        companyId={companyId}
                        description={line.description}
                        onPick={(position) =>
                          selectPosition(line.key, position)
                        }
                      />

                      {line.priceNote && (
                        <small>{line.priceNote}</small>
                      )}
                    </td>

                    <td>
                      <input
                        value={line.unit}
                        onChange={(event) =>
                          updateLine(line.key, {
                            unit: event.target.value,
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
                        step="0.000001"
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
                      <select
                        value={line.itemType}
                        onChange={(event) =>
                          updateLine(line.key, {
                            itemType:
                              Number(
                                event.target.value
                              ) as ProjectBoqItemType,
                          })
                        }
                      >
                        <option
                          value={ProjectBoqItemType.Mixed}
                        >
                          Karma
                        </option>

                        <option
                          value={
                            ProjectBoqItemType.Material
                          }
                        >
                          Malzeme
                        </option>

                        <option
                          value={
                            ProjectBoqItemType.Labor
                          }
                        >
                          İşçilik
                        </option>
                      </select>
                    </td>

                    <td>
                      <input
                        value={line.category ?? ""}
                        onChange={(event) =>
                          updateLine(line.key, {
                            category:
                              event.target.value,
                          })
                        }
                      />
                    </td>

                    <td>
                      <strong>
                        {money(
                          Number(
                            line.contractQuantity || 0
                          ) *
                            Number(
                              line.unitPrice || 0
                            )
                        )}
                      </strong>
                    </td>

                    <td>
                      <button
                        type="button"
                        disabled={lines.length === 1}
                        onClick={() =>
                          removeLine(line.key)
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
        </div>

        <div
          className="erp-form-card"
          style={{ marginTop: 16 }}
        >
          <div className="erp-form-grid">
            <div>
              <span>Kalem Sayısı</span>
              <div
                style={{
                  marginTop: 6,
                  fontSize: 20,
                  fontWeight: 700,
                }}
              >
                {lines.length}
              </div>
            </div>

            <div>
              <span>Keşif Toplamı</span>
              <div
                style={{
                  marginTop: 6,
                  fontSize: 24,
                  fontWeight: 800,
                }}
              >
                {money(totalAmount)}
              </div>
            </div>
          </div>
        </div>

        <div
          className="erp-actions"
          style={{ marginTop: 16 }}
        >
          <Link href="/kesifler">
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
              ? "Keşif Kaydediliyor..."
              : "Taslak Keşfi Kaydet"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
