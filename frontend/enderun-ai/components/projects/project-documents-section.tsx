"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ApiError } from "@/lib/api/api-client";
import type { ProjectSiteListItem } from "@/services/project-site.service";
import {
  formatFileSize,
  projectDocumentService,
  SUGGESTED_DOCUMENT_FOLDERS,
  type ProjectDocumentListItem,
  type ProjectDocumentVersion,
} from "@/services/project-document.service";

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) {
    return error.message;
  }
  return "İşlem tamamlanamadı. Lütfen tekrar deneyin.";
}

const dateTimeFormat = new Intl.DateTimeFormat("tr-TR", {
  dateStyle: "short",
  timeStyle: "short",
});

type ProjectDocumentsSectionProps = {
  projectId: string;
  sites: ProjectSiteListItem[];
};

export default function ProjectDocumentsSection({
  projectId,
  sites,
}: ProjectDocumentsSectionProps) {
  const [documents, setDocuments] = useState<ProjectDocumentListItem[]>([]);
  const [folders, setFolders] = useState<string[]>(SUGGESTED_DOCUMENT_FOLDERS);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [folderFilter, setFolderFilter] = useState("");
  const [siteFilter, setSiteFilter] = useState("");
  const [search, setSearch] = useState("");

  const [uploadFolder, setUploadFolder] = useState(SUGGESTED_DOCUMENT_FOLDERS[0]);
  const [uploadSiteId, setUploadSiteId] = useState("");
  const [uploadDescription, setUploadDescription] = useState("");
  const [pendingFiles, setPendingFiles] = useState<File[]>([]);
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [dragActive, setDragActive] = useState(false);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  const [expandedVersionsId, setExpandedVersionsId] = useState<string | null>(null);
  const [versions, setVersions] = useState<ProjectDocumentVersion[]>([]);
  const [versionsLoading, setVersionsLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const [documentsResult, foldersResult] = await Promise.all([
        projectDocumentService.getAll(projectId, {
          siteId: siteFilter || undefined,
          folder: folderFilter || undefined,
          search: search.trim() || undefined,
        }),
        projectDocumentService.getFolders(projectId),
      ]);
      setDocuments(documentsResult);
      setFolders(foldersResult);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  }, [projectId, siteFilter, folderFilter, search]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 300);
    return () => window.clearTimeout(timer);
  }, [load]);

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(""), 3500);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const siteNameById = useMemo(
    () => new Map(sites.map((site) => [site.id, site.name])),
    [sites]
  );

  function addFiles(files: FileList | File[]) {
    setPendingFiles((current) => [...current, ...Array.from(files)]);
  }

  function removePendingFile(index: number) {
    setPendingFiles((current) => current.filter((_, i) => i !== index));
  }

  async function submitUpload() {
    if (pendingFiles.length === 0) {
      setError("En az bir dosya seçin.");
      return;
    }
    if (!uploadFolder.trim()) {
      setError("Klasör seçin.");
      return;
    }

    setUploading(true);
    setUploadProgress(0);
    setError("");

    try {
      const result = await projectDocumentService.upload(
        projectId,
        pendingFiles,
        uploadFolder.trim(),
        {
          projectSiteId: uploadSiteId || undefined,
          description: uploadDescription.trim() || undefined,
          onProgress: setUploadProgress,
        }
      );
      setNotice(result.message);
      setPendingFiles([]);
      setUploadDescription("");
      if (fileInputRef.current) fileInputRef.current.value = "";
      await load();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setUploading(false);
      setUploadProgress(0);
    }
  }

  async function toggleVersions(documentId: string) {
    if (expandedVersionsId === documentId) {
      setExpandedVersionsId(null);
      return;
    }

    setExpandedVersionsId(documentId);
    setVersionsLoading(true);
    try {
      const result = await projectDocumentService.getVersions(projectId, documentId);
      setVersions(result);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setVersionsLoading(false);
    }
  }

  async function removeDocument(documentId: string) {
    if (!window.confirm("Bu dosyayı silmek istediğinize emin misiniz?")) return;

    setError("");
    try {
      await projectDocumentService.delete(projectId, documentId);
      setNotice("Dosya silindi.");
      await load();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    }
  }

  return (
    <section className="erp-panel erp-mt">
      <div className="erp-panel-header">
        <div>
          <h2>Dosyalar</h2>
          <p>Sözleşme, çizim, şartname, saha fotoğrafı ve hakediş ekleri</p>
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      <div
        className={`project-file-dropzone${dragActive ? " active" : ""}`}
        onDragOver={(event) => {
          event.preventDefault();
          setDragActive(true);
        }}
        onDragLeave={() => setDragActive(false)}
        onDrop={(event) => {
          event.preventDefault();
          setDragActive(false);
          if (event.dataTransfer.files.length > 0) {
            addFiles(event.dataTransfer.files);
          }
        }}
        onClick={() => fileInputRef.current?.click()}
      >
        <input
          ref={fileInputRef}
          type="file"
          multiple
          hidden
          onChange={(event) => {
            if (event.target.files) addFiles(event.target.files);
          }}
        />
        <p>
          <strong>Dosyaları buraya sürükleyin</strong> ya da tıklayıp seçin
        </p>
        <small>pdf, dwg, dxf, xlsx, docx, jpg, png, zip, rar — dosya başına en fazla 100 MB</small>
      </div>

      {pendingFiles.length > 0 && (
        <div className="project-file-pending-list">
          {pendingFiles.map((file, index) => (
            <span className="project-file-pending-item" key={`${file.name}-${index}`}>
              {file.name} ({formatFileSize(file.size)})
              <button type="button" onClick={() => removePendingFile(index)} aria-label="Kaldır">
                ✕
              </button>
            </span>
          ))}
        </div>
      )}

      <div className="erp-form-grid" style={{ marginTop: "12px" }}>
        <label>
          <span>Klasör *</span>
          <select value={uploadFolder} onChange={(e) => setUploadFolder(e.target.value)}>
            {Array.from(new Set([...SUGGESTED_DOCUMENT_FOLDERS, ...folders])).map((name) => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
          </select>
        </label>

        {sites.length > 0 && (
          <label>
            <span>Şantiye (ops.)</span>
            <select value={uploadSiteId} onChange={(e) => setUploadSiteId(e.target.value)}>
              <option value="">Genel (şantiyeye özel değil)</option>
              {sites.map((site) => (
                <option key={site.id} value={site.id}>
                  {site.code} — {site.name}
                </option>
              ))}
            </select>
          </label>
        )}

        <label className="span-2">
          <span>Açıklama (ops.)</span>
          <input
            type="text"
            value={uploadDescription}
            onChange={(e) => setUploadDescription(e.target.value)}
            placeholder="Kısa açıklama..."
          />
        </label>
      </div>

      {uploading && (
        <div className="project-file-progress-bar">
          <div className="project-file-progress-fill" style={{ width: `${uploadProgress}%` }} />
          <span>{uploadProgress}%</span>
        </div>
      )}

      <div className="erp-form-actions">
        <button
          type="button"
          className="erp-primary-button"
          disabled={uploading || pendingFiles.length === 0}
          onClick={() => void submitUpload()}
        >
          {uploading ? "Yükleniyor..." : `${pendingFiles.length || ""} Dosyayı Yükle`.trim()}
        </button>
      </div>

      <div className="erp-form-grid" style={{ marginTop: "24px" }}>
        <label>
          <span>Klasör filtresi</span>
          <select value={folderFilter} onChange={(e) => setFolderFilter(e.target.value)}>
            <option value="">Tüm klasörler</option>
            {folders.map((name) => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
          </select>
        </label>

        {sites.length > 0 && (
          <label>
            <span>Şantiye filtresi</span>
            <select value={siteFilter} onChange={(e) => setSiteFilter(e.target.value)}>
              <option value="">Tümü</option>
              {sites.map((site) => (
                <option key={site.id} value={site.id}>
                  {site.code} — {site.name}
                </option>
              ))}
            </select>
          </label>
        )}

        <label>
          <span>Ara</span>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Dosya adı..."
          />
        </label>
      </div>

      {loading ? (
        <div className="erp-loading">Dosyalar yükleniyor...</div>
      ) : documents.length === 0 ? (
        <div className="erp-empty-state">
          <strong>Bu kriterlere uyan dosya yok</strong>
        </div>
      ) : (
        <div className="erp-project-list" style={{ marginTop: "16px" }}>
          {documents.map((doc) => (
            <div key={doc.id}>
              <div className="erp-project-list-item">
                <div>
                  <strong>{doc.fileName}</strong>
                  <span>
                    {doc.folder}
                    {doc.siteName ? ` · ${doc.siteName}` : ""} ·{" "}
                    {formatFileSize(doc.sizeBytes)} · v{doc.versionNumber}
                  </span>
                  <span>
                    {doc.uploadedByName} · {dateTimeFormat.format(new Date(doc.createdAtUtc))}
                    {doc.description ? ` · ${doc.description}` : ""}
                  </span>
                </div>

                <div style={{ display: "flex", gap: "8px", alignItems: "center", flexWrap: "wrap" }}>
                  <a
                    className="erp-secondary-button"
                    href={projectDocumentService.downloadUrl(projectId, doc.id)}
                  >
                    İndir
                  </a>
                  <button
                    type="button"
                    className="erp-secondary-button"
                    onClick={() => void toggleVersions(doc.id)}
                  >
                    {expandedVersionsId === doc.id ? "Sürümleri Gizle" : "Sürümler"}
                  </button>
                  <button
                    type="button"
                    className="erp-secondary-button"
                    onClick={() => void removeDocument(doc.id)}
                  >
                    Sil
                  </button>
                </div>
              </div>

              {expandedVersionsId === doc.id && (
                <div className="project-file-version-list">
                  {versionsLoading ? (
                    <div className="erp-loading">Sürümler yükleniyor...</div>
                  ) : (
                    versions.map((version) => (
                      <div className="project-file-version-item" key={version.id}>
                        <span>
                          v{version.versionNumber}
                          {version.isCurrentVersion ? " (güncel)" : ""} —{" "}
                          {formatFileSize(version.sizeBytes)} — {version.uploadedByName} —{" "}
                          {dateTimeFormat.format(new Date(version.createdAtUtc))}
                        </span>
                        <a
                          className="erp-secondary-button"
                          href={projectDocumentService.downloadUrl(projectId, version.id)}
                        >
                          İndir
                        </a>
                      </div>
                    ))
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
