"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { Button, ConfirmDialog } from "@/components/ui";
import { money } from "@/lib/format/turkish";
import { ApiError } from "@/lib/api/api-client";
import {
  TOOL_SERVICE_DECISIONS,
  TOOL_SERVICE_STATUSES,
  ToolAssetStatus,
  toolAssetService,
  type ToolAssetCard,
} from "@/services/tool-asset.service";
import { personnelService, type PersonnelListItem } from "@/services/personnel.service";
import { projectService, type ProjectListItem } from "@/services/project.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function errorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return "Kart alınamadı.";
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

function labelOf(list: [number, string][], value: number) {
  return list.find(([key]) => key === value)?.[1] ?? "—";
}

function statusClass(status: number) {
  if (status === ToolAssetStatus.Scrapped) return "erp-status red";
  if (status === ToolAssetStatus.InService) return "erp-status orange";
  if (status === ToolAssetStatus.InUse) return "erp-status green";
  return "erp-status gray";
}

/**
 * Alet kartı: künye, zimmet durumu ve servis geçmişi.
 *
 * Servis geçmişi kartın asıl değeri — "bu alet kaç kez arızalandı,
 * toplam ne kadara mal oldu" sorusu ancak alet kalıcı bir varlık
 * olduğunda cevaplanabiliyor.
 */
export default function ToolAssetCardPage() {
  /**
   * Düğme -> uç -> izin (ToolAssetsController):
   *   POST tool-assets/{id}/assign -> personnel.CREATE (zimmet açıyor)
   *   POST tool-assets/{id}/return -> personnel.EDIT   (zimmeti kapatıyor)
   *
   * ZİMMET VERMEK create, İADE ALMAK edit: uç öyle ayırmış. Sezgiye
   * ters görünüyor ama iade mevcut zimmet kaydını güncelliyor, yeni
   * kayıt açmıyor.
   */
  const actions = useModuleActions("personnel");

  const params = useParams<{ id: string }>();
  const assetId = params.id;

  const [card, setCard] = useState<ToolAssetCard | null>(null);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [reloadKey, setReloadKey] = useState(0);

  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);

  const [assignOpen, setAssignOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [returnOpen, setReturnOpen] = useState(false);
  const [assignForm, setAssignForm] = useState({
    personnelId: "",
    projectId: "",
    assignmentDate: today(),
    plannedReturnDate: "",
    conditionAtAssignment: "",
    notes: "",
  });

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const result = await toolAssetService.getCard(assetId);
        if (!cancelled) {
          setCard(result);
          setError("");
        }
      } catch (err) {
        if (!cancelled) setError(errorMessage(err));
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [assetId, reloadKey]);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const [people, projectList] = await Promise.all([
        personnelService.getAll().catch(() => [] as PersonnelListItem[]),
        projectService.getAll().catch(() => [] as ProjectListItem[]),
      ]);

      if (cancelled) return;

      setPersonnel(people);
      setProjects(projectList);
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  async function submitAssign(event: React.FormEvent) {
    event.preventDefault();

    if (!assignForm.personnelId) {
      setError("Zimmeti alacak personeli seçin.");
      return;
    }

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      const result = await toolAssetService.assign(assetId, {
        personnelId: assignForm.personnelId,
        projectId: assignForm.projectId || null,
        assignmentDate: assignForm.assignmentDate,
        plannedReturnDate: assignForm.plannedReturnDate || null,
        conditionAtAssignment: assignForm.conditionAtAssignment || null,
        notes: assignForm.notes || null,
      });

      setSuccess(`${result.message} Tutanağı yazdırabilirsiniz.`);
      setAssignOpen(false);
      setAssignForm((prev) => ({
        ...prev,
        personnelId: "",
        conditionAtAssignment: "",
        notes: "",
      }));
      setReloadKey((key) => key + 1);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  /**
   * Zimmeti kapat, aleti depoya al.
   *
   * Durum notu İSTEĞE BAĞLI ama kayda değer: hasarlı dönen bir alet
   * için "hangi durumda geldi" sorusunun tek cevabı bu alan.
   * `showReason` tam bunun için var — window.prompt ile sorulduğunda
   * seçenek "ya zorla ya hiç sorma" idi ve iki ayrı pencere açıyordu.
   */
  async function submitReturn(condition: string) {
    setReturnOpen(false);
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      const result = await toolAssetService.returnAsset(assetId, {
        returnDate: today(),
        conditionAtReturn: condition || null,
      });

      setSuccess(result.message);
      setReloadKey((key) => key + 1);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  const asset = card?.asset;

  const warrantyActive =
    asset?.warrantyEndDate != null &&
    new Date(asset.warrantyEndDate) >= new Date();

  return (
    <ErpShell
      design="redwood"
      title={asset ? `${asset.code} — ${asset.name}` : "Alet Kartı"}
      description="Künye, zimmet ve servis geçmişi"
    >
      {error && <div className="erp-alert error">{error}</div>}
      {success && <div className="erp-alert success">{success}</div>}

      <div className="erp-page-toolbar">
        {/* Zimmet ve servis hareketleri başka ekranlardan işleniyor. */}
        <Button variant="secondary" disabled={saving} onClick={() => setReloadKey((key) => key + 1)}>Yenile</Button>

        <Link className="erp-secondary-button" href="/demirbas">
          Demirbaş Listesi
        </Link>

        {asset && asset.status !== ToolAssetStatus.Scrapped && (
          <Link
            className="erp-primary-button"
            href={`/demirbas/servis?assetId=${asset.id}`}
          >
            Servis Talebi Aç
          </Link>
        )}
      </div>

      {!card ? (
        <div className="erp-panel erp-loading">Kart yükleniyor...</div>
      ) : (
        <>
          <div className="erp-quick-grid">
            <div className="erp-panel">
              <small style={{ display: "block", marginBottom: 4 }}>Durum</small>
              <strong>
                <span className={statusClass(asset!.status)}>
                  {asset!.statusName}
                </span>
              </strong>
              {asset!.assignedPersonnelName && (
                <small style={{ display: "block" }}>
                  Zimmetli: {asset!.assignedPersonnelName}
                </small>
              )}
            </div>

            <div className="erp-panel">
              <small style={{ display: "block", marginBottom: 4 }}>
                Kaç Kez Arızalandı
              </small>
              <strong>{card.serviceCount}</strong>
              {card.lastServiceDate && (
                <small style={{ display: "block" }}>
                  son: {dateFormat.format(new Date(card.lastServiceDate))}
                </small>
              )}
            </div>

            <div className="erp-panel">
              <small style={{ display: "block", marginBottom: 4 }}>
                Toplam Servis Masrafı
              </small>
              <strong>{money(card.serviceTotalCost)}</strong>
              {asset!.purchaseCost != null && asset!.purchaseCost > 0 && (
                <small style={{ display: "block" }}>
                  alım bedelinin %
                  {Math.round(
                    (card.serviceTotalCost / asset!.purchaseCost) * 100
                  )}
                  {"\u2019"}i
                </small>
              )}
            </div>

            <div className="erp-panel">
              <small style={{ display: "block", marginBottom: 4 }}>Garanti</small>
              <strong>
                {asset!.warrantyEndDate
                  ? dateFormat.format(new Date(asset!.warrantyEndDate))
                  : "—"}
              </strong>
              {asset!.warrantyEndDate && (
                <small style={{ display: "block" }}>
                  {warrantyActive ? "sürüyor" : "doldu"}
                </small>
              )}
            </div>
          </div>

          <section className="erp-table-card" style={{ marginTop: 16 }}>
            <div className="erp-table-header">
              <h2>Zimmet</h2>

              <div style={{ display: "flex", gap: 8 }}>
                {asset!.status !== ToolAssetStatus.Scrapped &&
                  asset!.status !== ToolAssetStatus.InService &&
                  actions.can("create") && (
                    <button
                      type="button"
                      className="erp-secondary-button"
                      onClick={() => setAssignOpen((open) => !open)}
                    >
                      {card.assignment ? "Devret" : "Zimmet Ver"}
                    </button>
                  )}

                {card.assignment && (
                  <>
                    <Link
                      className="erp-secondary-button"
                      href={`/insan-kaynaklari/zimmetler/${card.assignment.id}/tutanak`}
                      target="_blank"
                    >
                      Tutanak
                    </Link>
                    {actions.can("edit") && (
                      <button
                        type="button"
                        className="erp-secondary-button"
                        onClick={() => setReturnOpen(true)}
                        disabled={saving}
                      >
                        İade Al
                      </button>
                    )}
                  </>
                )}
              </div>
            </div>

            {card.assignment ? (
              <div className="erp-detail-grid" style={{ padding: "12px 16px" }}>
                <div>
                  <span>Zimmetli Personel</span>
                  <strong>{card.assignment.personnelName ?? "—"}</strong>
                </div>
                <div>
                  <span>Zimmet Tarihi</span>
                  <strong>
                    {dateFormat.format(new Date(card.assignment.assignmentDate))}
                  </strong>
                </div>
                <div>
                  <span>Planlanan İade</span>
                  <strong>
                    {card.assignment.plannedReturnDate
                      ? dateFormat.format(
                          new Date(card.assignment.plannedReturnDate)
                        )
                      : "Süresiz"}
                  </strong>
                </div>
              </div>
            ) : (
              <div className="erp-empty-state">
                <strong>Alet kimseye zimmetli değil</strong>
              </div>
            )}

            {assignOpen && (
              <form onSubmit={submitAssign} style={{ padding: "0 16px 16px" }}>
                {card.assignment && (
                  <p style={{ fontSize: 13, marginTop: 0 }}>
                    Alet {card.assignment.personnelName} üzerinde. Kaydedince o
                    zimmet iade olarak kapanır, yenisi açılır — geçmiş korunur.
                  </p>
                )}

                <div className="erp-form-grid">
                  <label>
                    Personel *
                    <select
                      value={assignForm.personnelId}
                      onChange={(event) =>
                        setAssignForm((prev) => ({
                          ...prev,
                          personnelId: event.target.value,
                        }))
                      }
                      required
                    >
                      <option value="">Seçiniz</option>
                      {personnel.map((person) => (
                        <option key={person.id} value={person.id}>
                          {person.fullName}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Proje
                    <select
                      value={assignForm.projectId}
                      onChange={(event) =>
                        setAssignForm((prev) => ({
                          ...prev,
                          projectId: event.target.value,
                        }))
                      }
                    >
                      <option value="">Projesiz</option>
                      {projects.map((project) => (
                        <option key={project.id} value={project.id}>
                          {project.code} — {project.name}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Teslim Tarihi *
                    <input
                      type="date"
                      value={assignForm.assignmentDate}
                      onChange={(event) =>
                        setAssignForm((prev) => ({
                          ...prev,
                          assignmentDate: event.target.value,
                        }))
                      }
                      required
                    />
                  </label>

                  <label>
                    Planlanan İade
                    <input
                      type="date"
                      value={assignForm.plannedReturnDate}
                      onChange={(event) =>
                        setAssignForm((prev) => ({
                          ...prev,
                          plannedReturnDate: event.target.value,
                        }))
                      }
                    />
                  </label>

                  <label>
                    Teslim Anındaki Durum
                    <input
                      value={assignForm.conditionAtAssignment}
                      onChange={(event) =>
                        setAssignForm((prev) => ({
                          ...prev,
                          conditionAtAssignment: event.target.value,
                        }))
                      }
                      placeholder="Sağlam"
                    />
                  </label>

                  <label>
                    Not
                    <input
                      value={assignForm.notes}
                      onChange={(event) =>
                        setAssignForm((prev) => ({
                          ...prev,
                          notes: event.target.value,
                        }))
                      }
                    />
                  </label>
                </div>

                <div style={{ marginTop: 12, display: "flex", gap: 8 }}>
                  {actions.can("create") && (
                    <button
                      type="submit"
                      className="erp-primary-button"
                      disabled={saving}
                    >
                      {saving ? "Kaydediliyor..." : "Kaydet"}
                    </button>
                  )}
                  <button
                    type="button"
                    className="erp-secondary-button"
                    onClick={() => setAssignOpen(false)}
                  >
                    Vazgeç
                  </button>
                </div>
              </form>
            )}
          </section>

          <section className="erp-table-card" style={{ marginTop: 16 }}>
            <div className="erp-table-header">
              <h2>Künye</h2>
            </div>
            <div className="erp-detail-grid" style={{ padding: "12px 16px" }}>
              <div>
                <span>Marka / Model</span>
                <strong>
                  {[asset!.brand, asset!.model].filter(Boolean).join(" ") || "—"}
                </strong>
              </div>
              <div>
                <span>Seri No</span>
                <strong>{asset!.serialNumber ?? "—"}</strong>
              </div>
              <div>
                <span>Alım Tarihi</span>
                <strong>
                  {asset!.purchaseDate
                    ? dateFormat.format(new Date(asset!.purchaseDate))
                    : "—"}
                </strong>
              </div>
              <div>
                <span>Alım Bedeli</span>
                <strong>
                  {asset!.purchaseCost != null
                    ? money(asset!.purchaseCost)
                    : "—"}
                </strong>
              </div>
            </div>
            {asset!.notes && (
              <p style={{ padding: "0 16px 16px", fontSize: 13 }}>{asset!.notes}</p>
            )}
          </section>

          <section className="erp-table-card" style={{ marginTop: 16 }}>
            <div className="erp-table-header">
              <h2>Servis Geçmişi</h2>
              <small>{card.history.length} kayıt</small>
            </div>

            {card.history.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Bu alet hiç servise gitmemiş</strong>
              </div>
            ) : (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Talep No</th>
                      <th>Tarih</th>
                      <th>Arıza</th>
                      <th>Karar</th>
                      <th>Durum</th>
                      <th>Proje</th>
                      <th>Maliyet</th>
                    </tr>
                  </thead>
                  <tbody>
                    {card.history.map((row) => (
                      <tr key={row.id}>
                        <td>{row.requestNumber}</td>
                        <td>{dateFormat.format(new Date(row.requestDate))}</td>
                        <td>{row.faultDescription}</td>
                        <td>{labelOf(TOOL_SERVICE_DECISIONS, row.decision)}</td>
                        <td>{labelOf(TOOL_SERVICE_STATUSES, row.status)}</td>
                        <td>{row.projectCode ?? "Merkez"}</td>
                        <td>
                          {row.serviceCost > 0
                            ? money(row.serviceCost)
                            : "—"}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}
      <ConfirmDialog
        open={returnOpen}
        title="Zimmeti Kapat"
        description={
          "Alet depoya alınacak ve zimmet kapanacak. İade anındaki " +
          "durumu yazarsanız kayda geçer."
        }
        confirmLabel="Zimmeti Kapat"
        showReason
        reasonLabel="İade anındaki durum (isteğe bağlı)"
        busy={saving}
        error={error}
        onCancel={() => setReturnOpen(false)}
        onConfirm={(condition) => void submitReturn(condition)}
      />
    </ErpShell>
  );
}
