"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog } from "@/components/ui";
import { usePermissions } from "@/lib/use-permissions";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  ISG_INCIDENT_SEVERITIES,
  ISG_INCIDENT_STATUSES,
  ISG_INCIDENT_TYPES,
  isgService,
  type IsgIncidentDetail,
  type IsgIncidentListItem,
} from "@/services/isg.service";
import { personnelService, type PersonnelListItem } from "@/services/personnel.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  projectSiteService,
  type ProjectSiteListItem,
} from "@/services/project-site.service";

const dateTimeFormat = new Intl.DateTimeFormat("tr-TR", {
  dateStyle: "short",
  timeStyle: "short",
});
const dateFormat = new Intl.DateTimeFormat("tr-TR");

function formatDateTime(value?: string | null) {
  return value ? dateTimeFormat.format(new Date(value)) : "—";
}

function formatDate(value?: string | null) {
  return value ? dateFormat.format(new Date(value)) : "—";
}

/** datetime-local alanı için yerel saat dilimini koruyan biçim. */
function toLocalInput(value: Date) {
  const offset = value.getTimezoneOffset() * 60000;
  return new Date(value.getTime() - offset).toISOString().slice(0, 16);
}

export default function IsgIncidentsPage() {
  const { has } = usePermissions();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);

  const [incidents, setIncidents] = useState<IsgIncidentListItem[]>([]);
  const [detail, setDetail] = useState<IsgIncidentDetail | null>(null);

  const [filterType, setFilterType] = useState("");
  const [filterStatus, setFilterStatus] = useState("");
  const [filterProject, setFilterProject] = useState("");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [pendingDelete, setPendingDelete] =
    useState<string | null>(null);
  const [notice, setNotice] = useState("");

  const [formOpen, setFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [sites, setSites] = useState<ProjectSiteListItem[]>([]);

  const [projectId, setProjectId] = useState("");
  const [projectSiteId, setProjectSiteId] = useState("");
  const [personnelId, setPersonnelId] = useState("");
  const [incidentDateTime, setIncidentDateTime] = useState(
    toLocalInput(new Date())
  );
  const [incidentType, setIncidentType] = useState("0");
  const [severity, setSeverity] = useState("1");
  const [description, setDescription] = useState("");
  const [rootCause, setRootCause] = useState("");
  const [actionTaken, setActionTaken] = useState("");
  const [lostWorkDays, setLostWorkDays] = useState("0");
  const [sgkNotified, setSgkNotified] = useState(false);
  const [sgkNotificationDate, setSgkNotificationDate] = useState("");
  const [sgkNotificationNumber, setSgkNotificationNumber] = useState("");
  const [status, setStatus] = useState("0");
  const [closureNote, setClosureNote] = useState("");

  const canManage = has("isg.incident.manage");

  useEffect(() => {
    void (async () => {
      try {
        const result = await companyService.getAll();
        setCompanies(result);
        setCompanyId(result[0]?.id ?? "");
      } catch (err) {
        setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
      }
    })();
  }, []);

  useEffect(() => {
    if (!companyId) return;

    let active = true;

    void Promise.all([
      projectService.getAll(companyId),
      personnelService.getAll({ companyId }),
    ])
      .then(([projectList, personnelList]) => {
        if (!active) return;
        setProjects(projectList);
        setPersonnel(personnelList);
      })
      .catch(() => {
        if (active) {
          setProjects([]);
          setPersonnel([]);
        }
      });

    return () => {
      active = false;
    };
  }, [companyId]);

  const load = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      setIncidents(
        await isgService.getIncidents({
          companyId,
          projectId: filterProject || undefined,
          incidentType: filterType === "" ? undefined : Number(filterType),
          status: filterStatus === "" ? undefined : Number(filterStatus),
        })
      );
    } catch (err) {
      setIncidents([]);
      setError(err instanceof Error ? err.message : "Kayıtlar alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId, filterProject, filterType, filterStatus]);

  useEffect(() => {
    // Filtreler arka arkaya değiştirilirken her seçim için ayrı istek
    // gitmesin.
    const timer = window.setTimeout(() => void load(), 200);
    return () => window.clearTimeout(timer);
  }, [load]);

  const loadSites = useCallback(async (selectedProjectId: string) => {
    if (!selectedProjectId) {
      setSites([]);
      return;
    }

    try {
      setSites(await projectSiteService.getAll(selectedProjectId));
    } catch {
      setSites([]);
    }
  }, []);

  function resetForm() {
    setEditingId(null);
    setProjectId("");
    setProjectSiteId("");
    setPersonnelId("");
    setIncidentDateTime(toLocalInput(new Date()));
    setIncidentType("0");
    setSeverity("1");
    setDescription("");
    setRootCause("");
    setActionTaken("");
    setLostWorkDays("0");
    setSgkNotified(false);
    setSgkNotificationDate("");
    setSgkNotificationNumber("");
    setStatus("0");
    setClosureNote("");
    setSites([]);
  }

  async function startEdit(id: string) {
    setError("");

    try {
      const record = await isgService.getIncident(id);

      setEditingId(record.id);
      setProjectId(record.projectId ?? "");
      await loadSites(record.projectId ?? "");
      setProjectSiteId(record.projectSiteId ?? "");
      setPersonnelId(record.personnelId ?? "");
      setIncidentDateTime(toLocalInput(new Date(record.incidentDateTime)));
      setIncidentType(String(record.incidentType));
      setSeverity(String(record.severity));
      setDescription(record.description);
      setRootCause(record.rootCause ?? "");
      setActionTaken(record.actionTaken ?? "");
      setLostWorkDays(String(record.lostWorkDays));
      setSgkNotified(record.sgkNotified);
      setSgkNotificationDate(
        record.sgkNotificationDate
          ? record.sgkNotificationDate.slice(0, 10)
          : ""
      );
      setSgkNotificationNumber(record.sgkNotificationNumber ?? "");
      setStatus(String(record.status));
      setClosureNote(record.closureNote ?? "");
      setFormOpen(true);
      setNotice("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kayıt açılamadı.");
    }
  }

  const validationErrors: string[] = [];
  if (formOpen) {
    if (!incidentDateTime) validationErrors.push("Olay tarih ve saatini girin.");
    if (!description.trim()) validationErrors.push("Olay açıklaması girin.");

    // "Bildirildi" deyip tarih yazmamak, denetimde ispatlanamayan bir
    // beyandır; backend de aynı kuralı uygular.
    if (sgkNotified && !sgkNotificationDate) {
      validationErrors.push("SGK bildirim tarihini girin.");
    }

    if (status === "2" && !actionTaken.trim()) {
      validationErrors.push("Kaydı kapatmak için alınan önlemi yazın.");
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (validationErrors.length > 0) {
      setError(validationErrors.join(" "));
      return;
    }

    setSaving(true);
    setError("");

    const payload = {
      companyId,
      projectId: projectId || null,
      projectSiteId: projectSiteId || null,
      personnelId: personnelId || null,
      incidentDateTime: new Date(incidentDateTime).toISOString(),
      incidentType: Number(incidentType),
      severity: Number(severity),
      description: description.trim(),
      rootCause: rootCause.trim() || null,
      actionTaken: actionTaken.trim() || null,
      lostWorkDays: Number(lostWorkDays) || 0,
      sgkNotified,
      sgkNotificationDate: sgkNotificationDate
        ? new Date(sgkNotificationDate).toISOString()
        : null,
      sgkNotificationNumber: sgkNotificationNumber.trim() || null,
      status: Number(status),
      closureNote: closureNote.trim() || null,
    };

    try {
      if (editingId) {
        await isgService.updateIncident(editingId, payload);
        setNotice("Kayıt güncellendi.");
      } else {
        await isgService.createIncident(payload);
        setNotice("Kayıt eklendi.");
      }

      setFormOpen(false);
      resetForm();
      setDetail(null);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kayıt kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function remove(id: string) {
    setPendingDelete(null);

    setError("");

    try {
      await isgService.deleteIncident(id);
      setNotice("Kayıt silindi.");
      if (detail?.id === id) setDetail(null);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kayıt silinemedi.");
    }
  }

  const overdueCount = incidents.filter(
    (incident) => incident.sgkNotificationOverdue
  ).length;

  return (
    <ErpShell
      design="redwood"
      title="Kaza ve Ramak Kala Defteri"
      description="İş kazası, ramak kala ve meslek hastalığı kayıtları; SGK bildirim takibi"
    >
      <div className="erp-page-toolbar">
        {/* Olay kaydı sahadan giriliyor. */}
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>

        <div>
          <strong>{incidents.length} kayıt</strong>
          {overdueCount > 0 && (
            <span className="erp-status red" style={{ marginLeft: "10px" }}>
              {overdueCount} SGK bildirimi gecikmiş
            </span>
          )}
          <small style={{ display: "block", marginTop: "4px" }}>
            İş kazası üç iş günü içinde SGK&apos;ya bildirilmek zorunda. Ramak
            kala bildirime tabi değil ama kayıt altına alınır.
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select
            value={companyId}
            onChange={(event) => {
              setCompanyId(event.target.value);
              setDetail(null);
              setFormOpen(false);
            }}
          >
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          {canManage && (
            <button
              type="button"
              className="erp-primary-button"
              onClick={() => {
                resetForm();
                setFormOpen(true);
                setNotice("");
              }}
            >
              + Yeni Kayıt
            </button>
          )}
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert">{notice}</div>}

      {formOpen && (
        <form className="erp-form-card" onSubmit={submit}>
          <div className="erp-form-header">
            <h2>{editingId ? "Kaydı Düzenle" : "Yeni Kaza / Ramak Kala"}</h2>
            <p>
              Personel alanı zorunlu değil: ramak kalada çoğu zaman kimse
              yaralanmaz.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Olay Türü *</span>
              <select
                value={incidentType}
                onChange={(event) => setIncidentType(event.target.value)}
              >
                {ISG_INCIDENT_TYPES.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Ağırlık *</span>
              <select
                value={severity}
                onChange={(event) => setSeverity(event.target.value)}
              >
                {ISG_INCIDENT_SEVERITIES.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Olay Tarihi ve Saati *</span>
              <input
                type="datetime-local"
                value={incidentDateTime}
                onChange={(event) => setIncidentDateTime(event.target.value)}
              />
            </label>

            <label>
              <span>Kayıp İş Günü</span>
              <input
                type="number"
                min="0"
                value={lostWorkDays}
                onChange={(event) => setLostWorkDays(event.target.value)}
              />
            </label>

            <label>
              <span>Proje</span>
              <select
                value={projectId}
                onChange={(event) => {
                  setProjectId(event.target.value);
                  setProjectSiteId("");
                  void loadSites(event.target.value);
                }}
              >
                <option value="">Proje seçilmedi</option>
                {projects.map((project) => (
                  <option key={project.id} value={project.id}>
                    {project.code} — {project.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Şantiye</span>
              <select
                value={projectSiteId}
                onChange={(event) => setProjectSiteId(event.target.value)}
                disabled={!projectId}
              >
                <option value="">Şantiye seçilmedi</option>
                {sites.map((site) => (
                  <option key={site.id} value={site.id}>
                    {site.code} — {site.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Personel</span>
              <select
                value={personnelId}
                onChange={(event) => setPersonnelId(event.target.value)}
              >
                <option value="">Personel yok / belirsiz</option>
                {personnel.map((person) => (
                  <option key={person.id} value={person.id}>
                    {person.fullName} — {person.employeeNumber}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Durum</span>
              <select
                value={status}
                onChange={(event) => setStatus(event.target.value)}
              >
                {ISG_INCIDENT_STATUSES.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            <label className="span-2">
              <span>Olay Açıklaması *</span>
              <input
                type="text"
                value={description}
                onChange={(event) => setDescription(event.target.value)}
              />
            </label>

            <label className="span-2">
              <span>Kök Neden</span>
              <input
                type="text"
                value={rootCause}
                onChange={(event) => setRootCause(event.target.value)}
              />
            </label>

            <label className="span-2">
              <span>Alınan Önlem</span>
              <input
                type="text"
                value={actionTaken}
                onChange={(event) => setActionTaken(event.target.value)}
              />
            </label>
          </div>

          <div className="erp-form-header" style={{ marginTop: "20px" }}>
            <h2>SGK Bildirimi</h2>
            <p>Yalnızca iş kazasında zorunludur.</p>
          </div>

          <div className="erp-form-grid">
            <label className="erp-check-label">
              <input
                type="checkbox"
                checked={sgkNotified}
                onChange={(event) => setSgkNotified(event.target.checked)}
              />
              <span>SGK&apos;ya bildirildi</span>
            </label>

            <label>
              <span>Bildirim Tarihi</span>
              <input
                type="date"
                value={sgkNotificationDate}
                onChange={(event) => setSgkNotificationDate(event.target.value)}
                disabled={!sgkNotified}
              />
            </label>

            <label>
              <span>Bildirim No</span>
              <input
                type="text"
                value={sgkNotificationNumber}
                onChange={(event) =>
                  setSgkNotificationNumber(event.target.value)
                }
                disabled={!sgkNotified}
              />
            </label>

            {status === "2" && (
              <label className="span-2">
                <span>Kapatma Notu</span>
                <input
                  type="text"
                  value={closureNote}
                  onChange={(event) => setClosureNote(event.target.value)}
                />
              </label>
            )}
          </div>

          <div className="erp-form-actions">
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => {
                setFormOpen(false);
                resetForm();
              }}
            >
              Vazgeç
            </button>

            <button type="submit" className="erp-primary-button" disabled={saving}>
              {saving ? "Kaydediliyor..." : "Kaydet"}
            </button>
          </div>
        </form>
      )}

      <div className="erp-table-card erp-mt">
        <div className="erp-table-header">
          <h2>Kayıt Defteri</h2>

          <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
            <select
              value={filterProject}
              onChange={(event) => setFilterProject(event.target.value)}
            >
              <option value="">Tüm projeler</option>
              {projects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code}
                </option>
              ))}
            </select>

            <select
              value={filterType}
              onChange={(event) => setFilterType(event.target.value)}
            >
              <option value="">Tüm türler</option>
              {ISG_INCIDENT_TYPES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>

            <select
              value={filterStatus}
              onChange={(event) => setFilterStatus(event.target.value)}
            >
              <option value="">Tüm durumlar</option>
              {ISG_INCIDENT_STATUSES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
        </div>

        {loading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : incidents.length === 0 ? (
          <div className="erp-empty-state">
            <p>Kayıt bulunamadı.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Tarih</th>
                  <th>Tür</th>
                  <th>Ağırlık</th>
                  <th>Yer</th>
                  <th>Personel</th>
                  <th>Kayıp Gün</th>
                  <th>SGK</th>
                  <th>Durum</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {incidents.map((incident) => (
                  <tr key={incident.id}>
                    <td>{formatDateTime(incident.incidentDateTime)}</td>
                    <td>{incident.incidentTypeName}</td>
                    <td>
                      <span className={`erp-status ${incident.severityColor}`}>
                        {incident.severityName}
                      </span>
                    </td>
                    <td>
                      {incident.projectCode ?? "—"}
                      {incident.siteName && <small>{incident.siteName}</small>}
                    </td>
                    <td>{incident.personnelName ?? "—"}</td>
                    <td>{incident.lostWorkDays}</td>
                    <td>
                      {incident.incidentType !== 0 ? (
                        <span className="erp-status gray">Gerekmiyor</span>
                      ) : incident.sgkNotified ? (
                        <span className="erp-status green">Bildirildi</span>
                      ) : incident.sgkNotificationOverdue ? (
                        <span className="erp-status red">Süre geçti</span>
                      ) : (
                        <span className="erp-status yellow">Bekliyor</span>
                      )}
                    </td>
                    <td>
                      <span
                        className={`erp-status ${
                          incident.status === 2 ? "green" : "yellow"
                        }`}
                      >
                        {incident.statusName}
                      </span>
                    </td>
                    <td>
                      <div className="erp-row-actions">
                        <button
                          type="button"
                          className="erp-secondary-button"
                          onClick={() =>
                            void isgService
                              .getIncident(incident.id)
                              .then(setDetail)
                              .catch((err: unknown) =>
                                setError(
                                  err instanceof Error
                                    ? err.message
                                    : "Kayıt açılamadı."
                                )
                              )
                          }
                        >
                          Detay
                        </button>

                        {canManage && (
                          <>
                            <button
                              type="button"
                              className="erp-secondary-button"
                              onClick={() => void startEdit(incident.id)}
                            >
                              Düzenle
                            </button>
                            <button
                              type="button"
                              className="erp-secondary-button"
                              onClick={() => setPendingDelete(incident.id)}
                            >
                              Sil
                            </button>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {detail && (
        <div className="erp-panel erp-mt">
          <div className="erp-panel-header">
            <h2>
              {detail.incidentTypeName} — {formatDateTime(detail.incidentDateTime)}
            </h2>
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => setDetail(null)}
            >
              Kapat
            </button>
          </div>

          {detail.sgkNotificationOverdue && (
            <div className="erp-alert error">
              Bu iş kazası SGK&apos;ya bildirilmemiş ve yasal süre geçti.
            </div>
          )}

          <div className="erp-detail-grid">
            <div>
              <span className="erp-stat-label">Ağırlık</span>
              <strong>{detail.severityName}</strong>
            </div>
            <div>
              <span className="erp-stat-label">Durum</span>
              <strong>{detail.statusName}</strong>
            </div>
            <div>
              <span className="erp-stat-label">Proje / Şantiye</span>
              <strong>
                {detail.projectCode ?? "—"}
                {detail.siteName ? ` / ${detail.siteName}` : ""}
              </strong>
            </div>
            <div>
              <span className="erp-stat-label">Personel</span>
              <strong>{detail.personnelName ?? "—"}</strong>
            </div>
            <div>
              <span className="erp-stat-label">Kayıp İş Günü</span>
              <strong>{detail.lostWorkDays}</strong>
            </div>
            <div>
              <span className="erp-stat-label">SGK Bildirimi</span>
              <strong>
                {detail.sgkNotified
                  ? `${formatDate(detail.sgkNotificationDate)} — ${
                      detail.sgkNotificationNumber ?? "no yok"
                    }`
                  : "Bildirilmedi"}
              </strong>
            </div>
            <div className="span-2">
              <span className="erp-stat-label">Olay Açıklaması</span>
              <strong>{detail.description}</strong>
            </div>
            <div className="span-2">
              <span className="erp-stat-label">Kök Neden</span>
              <strong>{detail.rootCause ?? "—"}</strong>
            </div>
            <div className="span-2">
              <span className="erp-stat-label">Alınan Önlem</span>
              <strong>{detail.actionTaken ?? "—"}</strong>
            </div>
            {detail.closureNote && (
              <div className="span-2">
                <span className="erp-stat-label">Kapatma Notu</span>
                <strong>{detail.closureNote}</strong>
              </div>
            )}
          </div>
        </div>
      )}
      <ConfirmDialog
        open={pendingDelete !== null}
        title="Kaza Kaydını Sil"
        description={"Olay kaydı kalıcı olarak silinecek. Bu işlem geri alınamaz; İSG kayıtları denetimde istenebiliyor."}
        confirmLabel="Kaydı Sil"
        error={error}
        onCancel={() => setPendingDelete(null)}
        onConfirm={() => void remove(pendingDelete!)}
      />
    </ErpShell>
  );
}
