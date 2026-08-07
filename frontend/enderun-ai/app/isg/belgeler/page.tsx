"use client";

import { FormEvent, useCallback, useEffect, useRef, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { usePermissions } from "@/lib/use-permissions";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  ISG_SITE_DOCUMENT_TYPES,
  isgService,
  type IsgSiteDocument,
} from "@/services/isg.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  projectSiteService,
  type ProjectSiteListItem,
} from "@/services/project-site.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function formatDate(value?: string | null) {
  return value ? dateFormat.format(new Date(value)) : "—";
}

function formatSize(bytes: number) {
  if (bytes <= 0) return "—";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toLocaleString("tr-TR", {
    maximumFractionDigits: 1,
  })} MB`;
}

export default function IsgSiteDocumentsPage() {
  const { has } = usePermissions();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [projects, setProjects] = useState<ProjectListItem[]>([]);

  const [documents, setDocuments] = useState<IsgSiteDocument[]>([]);
  const [filterProject, setFilterProject] = useState("");
  const [filterType, setFilterType] = useState("");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [formOpen, setFormOpen] = useState(false);
  const [saving, setSaving] = useState(false);

  const [sites, setSites] = useState<ProjectSiteListItem[]>([]);
  const [projectId, setProjectId] = useState("");
  const [projectSiteId, setProjectSiteId] = useState("");
  const [documentType, setDocumentType] = useState("0");
  const [title, setTitle] = useState("");
  const [issueDate, setIssueDate] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [validUntil, setValidUntil] = useState("");
  const [notes, setNotes] = useState("");
  const [file, setFile] = useState<File | null>(null);

  const canCreate = has("isg.create");
  const canDelete = has("isg.delete");

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

    void projectService
      .getAll(companyId)
      .then((result) => {
        if (active) setProjects(result);
      })
      .catch(() => {
        if (active) setProjects([]);
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
      setDocuments(
        await isgService.getSiteDocuments({
          companyId,
          projectId: filterProject || undefined,
          documentType: filterType === "" ? undefined : Number(filterType),
        })
      );
    } catch (err) {
      setDocuments([]);
      setError(err instanceof Error ? err.message : "Belgeler alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId, filterProject, filterType]);

  useEffect(() => {
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
    setProjectId("");
    setProjectSiteId("");
    setDocumentType("0");
    setTitle("");
    setIssueDate(new Date().toISOString().slice(0, 10));
    setValidUntil("");
    setNotes("");
    setFile(null);
    setSites([]);

    // input.value sıfırlanmazsa aynı dosya ikinci kez seçilemez.
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  const validationErrors: string[] = [];
  if (formOpen) {
    if (!projectId) validationErrors.push("Proje seçin.");
    if (!title.trim()) validationErrors.push("Belge başlığı girin.");
    if (!issueDate) validationErrors.push("Belge tarihi girin.");
    if (!file) validationErrors.push("Belge dosyası seçin.");
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (validationErrors.length > 0) {
      setError(validationErrors.join(" "));
      return;
    }

    setSaving(true);
    setError("");

    try {
      await isgService.uploadSiteDocument({
        companyId,
        projectId,
        projectSiteId: projectSiteId || null,
        documentType: Number(documentType),
        title: title.trim(),
        issueDate,
        validUntil: validUntil || null,
        notes: notes.trim() || null,
        file: file as File,
      });

      setNotice("Belge yüklendi.");
      setFormOpen(false);
      resetForm();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Belge yüklenemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function remove(id: string) {
    if (!window.confirm("Belge kaydı silinsin mi?")) return;

    setError("");

    try {
      await isgService.deleteSiteDocument(id);
      setNotice("Belge silindi.");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Belge silinemedi.");
    }
  }

  const expiredCount = documents.filter(
    (document) => document.validityColor === "red"
  ).length;

  return (
    <ErpShell
      title="Saha İSG Belgeleri"
      description="Risk değerlendirmesi, acil durum planı, kurul tutanağı, denetim ve KKD zimmet formları"
    >
      <div className="erp-page-toolbar">
        <div>
          <strong>{documents.length} belge</strong>
          {expiredCount > 0 && (
            <span className="erp-status red" style={{ marginLeft: "10px" }}>
              {expiredCount} süresi doldu
            </span>
          )}
          <small style={{ display: "block", marginTop: "4px" }}>
            Süresi dolmuş risk değerlendirmesi denetimde belge yokluğuyla aynı
            sonucu doğurur.
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
          >
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          {canCreate && (
            <button
              type="button"
              className="erp-primary-button"
              onClick={() => {
                resetForm();
                setFormOpen((open) => !open);
                setNotice("");
              }}
            >
              {formOpen ? "Formu Kapat" : "+ Belge Yükle"}
            </button>
          )}
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert">{notice}</div>}

      {formOpen && (
        <form className="erp-form-card" onSubmit={submit}>
          <div className="erp-form-header">
            <h2>Yeni Saha Belgesi</h2>
            <p>
              Geçerlilik bitişi girilen belgeler panelde ve Hızır brifinginde
              süre takibine girer.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Proje *</span>
              <select
                value={projectId}
                onChange={(event) => {
                  setProjectId(event.target.value);
                  setProjectSiteId("");
                  void loadSites(event.target.value);
                }}
              >
                <option value="">Proje seçin</option>
                {projects.map((project) => (
                  <option key={project.id} value={project.id}>
                    {project.code} — {project.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Şantiye (ops.)</span>
              <select
                value={projectSiteId}
                onChange={(event) => setProjectSiteId(event.target.value)}
                disabled={!projectId}
              >
                <option value="">Proje geneli</option>
                {sites.map((site) => (
                  <option key={site.id} value={site.id}>
                    {site.code} — {site.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Belge Türü *</span>
              <select
                value={documentType}
                onChange={(event) => setDocumentType(event.target.value)}
              >
                {ISG_SITE_DOCUMENT_TYPES.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Başlık *</span>
              <input
                type="text"
                value={title}
                onChange={(event) => setTitle(event.target.value)}
              />
            </label>

            <label>
              <span>Belge Tarihi *</span>
              <input
                type="date"
                value={issueDate}
                onChange={(event) => setIssueDate(event.target.value)}
              />
            </label>

            <label>
              <span>Geçerlilik Bitişi</span>
              <input
                type="date"
                value={validUntil}
                onChange={(event) => setValidUntil(event.target.value)}
              />
              <small>Boş bırakılırsa süresiz sayılır.</small>
            </label>

            <label className="span-2">
              <span>Not</span>
              <input
                type="text"
                value={notes}
                onChange={(event) => setNotes(event.target.value)}
              />
            </label>
          </div>

          <div className="erp-form-actions" style={{ justifyContent: "flex-start" }}>
            <input
              ref={fileInputRef}
              type="file"
              style={{ display: "none" }}
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            />

            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => fileInputRef.current?.click()}
            >
              Dosya Seç
            </button>

            <span style={{ marginLeft: "10px" }}>
              {file ? file.name : "Dosya seçilmedi"}
            </span>
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
              {saving ? "Yükleniyor..." : "Belgeyi Yükle"}
            </button>
          </div>
        </form>
      )}

      <div className="erp-table-card erp-mt">
        <div className="erp-table-header">
          <h2>Belgeler</h2>

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
              {ISG_SITE_DOCUMENT_TYPES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
        </div>

        {loading ? (
          <div className="erp-loading">Yükleniyor...</div>
        ) : documents.length === 0 ? (
          <div className="erp-empty-state">
            <p>Belge bulunamadı.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Belge</th>
                  <th>Tür</th>
                  <th>Proje / Şantiye</th>
                  <th>Tarih</th>
                  <th>Geçerlilik</th>
                  <th>Durum</th>
                  <th>Dosya</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {documents.map((document) => (
                  <tr key={document.id}>
                    <td>
                      <strong>{document.title}</strong>
                      {document.notes && <small>{document.notes}</small>}
                    </td>
                    <td>{document.documentTypeName}</td>
                    <td>
                      {document.projectCode}
                      <small>{document.siteName ?? "Proje geneli"}</small>
                    </td>
                    <td>{formatDate(document.issueDate)}</td>
                    <td>{formatDate(document.validUntil)}</td>
                    <td>
                      <span className={`erp-status ${document.validityColor}`}>
                        {document.validityStatusName}
                      </span>
                      {typeof document.daysRemaining === "number" && (
                        <small>{document.daysRemaining} gün</small>
                      )}
                    </td>
                    <td>
                      <a
                        className="erp-row-link"
                        href={isgService.siteDocumentDownloadUrl(document.id)}
                      >
                        İndir
                      </a>
                      <small>
                        {document.originalFileName} · {formatSize(document.sizeBytes)}
                      </small>
                    </td>
                    <td>
                      {canDelete && (
                        <button
                          type="button"
                          className="erp-secondary-button"
                          onClick={() => void remove(document.id)}
                        >
                          Sil
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </ErpShell>
  );
}
