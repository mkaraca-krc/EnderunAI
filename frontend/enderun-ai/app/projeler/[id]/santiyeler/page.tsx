"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { Fragment, FormEvent, useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { usePermissions } from "@/lib/use-permissions";
import {
  projectSiteService,
  type ProjectSiteDetail,
  type ProjectSiteListItem,
} from "@/services/project-site.service";

type EditDraft = {
  code: string;
  name: string;
  location: string;
  notes: string;
  isActive: boolean;
};

const emptyDraft: EditDraft = {
  code: "",
  name: "",
  location: "",
  notes: "",
  isActive: true,
};

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

/**
 * Projenin şantiye listesi.
 *
 * Bu ekran daha önce yoktu: şantiye oluşturma (/yeni) ve şantiye detayı
 * (/{siteId}) sayfaları vardı ama aradaki liste rotası eksikti ve proje
 * merkezindeki modül ızgarasında da bir giriş bulunmuyordu. Sonuç olarak
 * şantiye yönetimi çalışıyor ama bulunamıyordu.
 *
 * Personel atamaları satır açılarak gösteriliyor; liste ucu yalnızca
 * sayı döndürdüğü için ayrıntı ancak istendiğinde çekiliyor.
 */
export default function ProjectSitesPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;

  const { has } = usePermissions();
  const canCreate = has("sites.create");
  const canEdit = has("sites.edit");

  const [sites, setSites] = useState<ProjectSiteListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [saving, setSaving] = useState(false);
  const [reloadToken, setReloadToken] = useState(0);

  const [creating, setCreating] = useState(false);
  const [createDraft, setCreateDraft] = useState<EditDraft>(emptyDraft);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [editDraft, setEditDraft] = useState<EditDraft>(emptyDraft);

  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<ProjectSiteDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);

  const fetchSites = useCallback(
    () => projectSiteService.getAll(projectId),
    [projectId]
  );

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const data = await fetchSites();
        if (cancelled) return;

        setSites(data);
        setError("");
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Şantiyeler alınamadı.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [fetchSites, reloadToken]);

  async function handleCreate(event: FormEvent) {
    event.preventDefault();

    setSaving(true);
    setError("");
    setNotice("");

    try {
      await projectSiteService.create(projectId, {
        code: createDraft.code.trim(),
        name: createDraft.name.trim(),
        location: createDraft.location.trim() || null,
        notes: createDraft.notes.trim() || null,
      });

      setCreating(false);
      setCreateDraft(emptyDraft);
      setNotice("Şantiye oluşturuldu.");
      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Şantiye oluşturulamadı.");
    } finally {
      setSaving(false);
    }
  }

  async function handleUpdate(event: FormEvent) {
    event.preventDefault();
    if (!editingId) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      await projectSiteService.update(editingId, {
        code: editDraft.code.trim(),
        name: editDraft.name.trim(),
        location: editDraft.location.trim() || null,
        notes: editDraft.notes.trim() || null,
        isActive: editDraft.isActive,
      });

      setEditingId(null);
      setNotice("Şantiye güncellendi.");
      setReloadToken((value) => value + 1);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Şantiye güncellenemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function toggleAssignments(siteId: string) {
    if (expandedId === siteId) {
      setExpandedId(null);
      setDetail(null);
      return;
    }

    setExpandedId(siteId);
    setDetail(null);
    setDetailLoading(true);

    try {
      setDetail(await projectSiteService.getById(siteId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Şantiye detayı alınamadı.");
    } finally {
      setDetailLoading(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Şantiyeler"
      description="Projenin lokasyon kırılımı, personel atamaları ve depoları"
    >
      <div className="erp-project-breadcrumb">
        <Link href="/projeler">Projeler</Link>
        <span>›</span>
        <Link href={`/projeler/${projectId}`}>Proje Merkezi</Link>
        <span>›</span>
        <strong>Şantiyeler</strong>
      </div>

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      <div className="erp-toolbar">
        <div className="erp-toolbar-group">
          <strong>{sites.length} şantiye</strong>
        </div>

        {/* reloadToken vardı ama yalnızca bu sayfanın kendi
            işlemlerinden sonra artıyordu. */}
        <button
          type="button"
          className="erp-btn"
          disabled={loading}
          onClick={() => setReloadToken((value) => value + 1)}
        >
          Yenile
        </button>

        {canCreate && (
          <button
            type="button"
            className="erp-btn"
            onClick={() => {
              setCreating((value) => !value);
              setEditingId(null);
            }}
          >
            {creating ? "Vazgeç" : "+ Yeni Şantiye"}
          </button>
        )}
      </div>

      {creating && (
        <form className="erp-form-card" onSubmit={handleCreate}>
          <div className="erp-form-grid">
            <label>
              <span>Şantiye Kodu *</span>
              <input
                required
                value={createDraft.code}
                maxLength={30}
                placeholder="SANTIYE-1"
                onChange={(event) =>
                  setCreateDraft((d) => ({ ...d, code: event.target.value }))
                }
              />
            </label>

            <label>
              <span>Şantiye Adı *</span>
              <input
                required
                value={createDraft.name}
                onChange={(event) =>
                  setCreateDraft((d) => ({ ...d, name: event.target.value }))
                }
              />
            </label>

            <label>
              <span>Lokasyon</span>
              <input
                value={createDraft.location}
                placeholder="İlçe / mevki"
                onChange={(event) =>
                  setCreateDraft((d) => ({ ...d, location: event.target.value }))
                }
              />
            </label>

            <label>
              <span>Not</span>
              <input
                value={createDraft.notes}
                onChange={(event) =>
                  setCreateDraft((d) => ({ ...d, notes: event.target.value }))
                }
              />
            </label>
          </div>

          <div className="erp-form-actions">
            <button type="submit" className="erp-btn" disabled={saving}>
              {saving ? "Kaydediliyor..." : "Kaydet"}
            </button>
          </div>
        </form>
      )}

      {loading ? (
        <div className="erp-panel erp-loading">Şantiyeler yükleniyor...</div>
      ) : sites.length === 0 ? (
        <div className="erp-panel erp-empty-state">
          <strong>Bu projede henüz şantiye yok</strong>
          <p>
            Personelin görev yeri şantiye olarak atanabilmesi ve şantiye deposu
            açılabilmesi için önce en az bir şantiye tanımlanmalı.
          </p>
        </div>
      ) : (
        <div className="erp-table-card">
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Kod</th>
                  <th>Ad</th>
                  <th>Lokasyon</th>
                  <th>Personel</th>
                  <th>Depo</th>
                  <th>Durum</th>
                  <th>İşlem</th>
                </tr>
              </thead>
              <tbody>
                {sites.map((site) => (
                  <Fragment key={site.id}>
                    <tr>
                      <td>
                        <strong>{site.code}</strong>
                        <small>{formatDate(site.createdAtUtc)}</small>
                      </td>
                      <td>{site.name}</td>
                      <td>{site.location || "—"}</td>
                      <td>
                        <button
                          type="button"
                          className="erp-btn ghost"
                          onClick={() => void toggleAssignments(site.id)}
                        >
                          {site.assignmentCount} kişi
                        </button>
                      </td>
                      <td>{site.warehouseCount}</td>
                      <td>
                        <span
                          className={`erp-status ${
                            site.isActive ? "green" : "gray"
                          }`}
                        >
                          {site.isActive ? "Aktif" : "Pasif"}
                        </span>
                      </td>
                      <td>
                        <div style={{ display: "flex", gap: 6 }}>
                          {canEdit && (
                            <button
                              type="button"
                              className="erp-btn ghost"
                              onClick={() => {
                                setCreating(false);
                                setEditingId(site.id);
                                setEditDraft({
                                  code: site.code,
                                  name: site.name,
                                  location: site.location ?? "",
                                  notes: site.notes ?? "",
                                  isActive: site.isActive,
                                });
                              }}
                            >
                              Düzenle
                            </button>
                          )}
                          <Link
                            className="erp-btn ghost"
                            href={`/projeler/${projectId}/santiyeler/${site.id}`}
                          >
                            Detay
                          </Link>
                        </div>
                      </td>
                    </tr>

                    {editingId === site.id && (
                      <tr>
                        <td colSpan={7}>
                          <form className="erp-form-grid" onSubmit={handleUpdate}>
                            <label>
                              <span>Kod *</span>
                              <input
                                required
                                value={editDraft.code}
                                maxLength={30}
                                onChange={(event) =>
                                  setEditDraft((d) => ({
                                    ...d,
                                    code: event.target.value,
                                  }))
                                }
                              />
                            </label>

                            <label>
                              <span>Ad *</span>
                              <input
                                required
                                value={editDraft.name}
                                onChange={(event) =>
                                  setEditDraft((d) => ({
                                    ...d,
                                    name: event.target.value,
                                  }))
                                }
                              />
                            </label>

                            <label>
                              <span>Lokasyon</span>
                              <input
                                value={editDraft.location}
                                onChange={(event) =>
                                  setEditDraft((d) => ({
                                    ...d,
                                    location: event.target.value,
                                  }))
                                }
                              />
                            </label>

                            <label>
                              <span>Not</span>
                              <input
                                value={editDraft.notes}
                                onChange={(event) =>
                                  setEditDraft((d) => ({
                                    ...d,
                                    notes: event.target.value,
                                  }))
                                }
                              />
                            </label>

                            <label>
                              <span>Durum</span>
                              <select
                                value={editDraft.isActive ? "1" : "0"}
                                onChange={(event) =>
                                  setEditDraft((d) => ({
                                    ...d,
                                    isActive: event.target.value === "1",
                                  }))
                                }
                              >
                                <option value="1">Aktif</option>
                                <option value="0">Pasif</option>
                              </select>
                            </label>

                            <div className="erp-form-actions">
                              <button
                                type="submit"
                                className="erp-btn"
                                disabled={saving}
                              >
                                {saving ? "Kaydediliyor..." : "Güncelle"}
                              </button>
                              <button
                                type="button"
                                className="erp-btn ghost"
                                disabled={saving}
                                onClick={() => setEditingId(null)}
                              >
                                Vazgeç
                              </button>
                            </div>
                          </form>
                        </td>
                      </tr>
                    )}

                    {expandedId === site.id && (
                      <tr>
                        <td colSpan={7}>
                          {detailLoading ? (
                            <div className="erp-loading">
                              Personel atamaları yükleniyor...
                            </div>
                          ) : detail && detail.assignments.length > 0 ? (
                            <table className="erp-table">
                              <thead>
                                <tr>
                                  <th>Sicil</th>
                                  <th>Ad Soyad</th>
                                  <th>Görev</th>
                                  <th>Başlangıç</th>
                                  <th>Bitiş</th>
                                  <th>Durum</th>
                                </tr>
                              </thead>
                              <tbody>
                                {detail.assignments.map((assignment) => (
                                  <tr key={assignment.id}>
                                    <td>{assignment.employeeNumber}</td>
                                    <td>{assignment.fullName}</td>
                                    <td>
                                      {assignment.role ||
                                        assignment.jobTitle ||
                                        "—"}
                                    </td>
                                    <td>{formatDate(assignment.startDate)}</td>
                                    <td>{formatDate(assignment.endDate)}</td>
                                    <td>
                                      <span
                                        className={`erp-status ${
                                          assignment.isActive ? "green" : "gray"
                                        }`}
                                      >
                                        {assignment.isActive
                                          ? "Aktif"
                                          : "Kapandı"}
                                      </span>
                                    </td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          ) : (
                            <p>
                              Bu şantiyeye atanmış personel yok. Atama, şantiye
                              detayından veya personel kartındaki görev yeri
                              alanından yapılır.
                            </p>
                          )}
                        </td>
                      </tr>
                    )}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </ErpShell>
  );
}
