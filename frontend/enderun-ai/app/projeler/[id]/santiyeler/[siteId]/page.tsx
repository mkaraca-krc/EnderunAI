"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import {
  projectSiteService,
  type AssignablePersonnelItem,
  type ProjectSiteDetail,
} from "@/services/project-site.service";

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

export default function ProjectSiteDetailPage() {
  const params = useParams<{ id: string; siteId: string }>();

  const [site, setSite] = useState<ProjectSiteDetail | null>(null);
  const [assignable, setAssignable] = useState<AssignablePersonnelItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [assigning, setAssigning] = useState(false);
  const [assignError, setAssignError] = useState("");
  const [form, setForm] = useState({
    personnelId: "",
    role: "",
    startDate: new Date().toISOString().slice(0, 10),
  });

  async function load() {
    setLoading(true);
    setError("");

    try {
      const [siteResult, assignableResult] = await Promise.all([
        projectSiteService.getById(params.siteId),
        projectSiteService.getAssignablePersonnel(params.siteId),
      ]);

      setSite(siteResult);
      setAssignable(assignableResult);
    } catch (err) {
      setSite(null);
      setError(
        err instanceof Error ? err.message : "Şantiye yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (params.siteId) {
      void load();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params.siteId]);

  function update(key: keyof typeof form, value: string) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  async function assign(event: React.FormEvent) {
    event.preventDefault();

    setAssigning(true);
    setAssignError("");

    try {
      await projectSiteService.assignPersonnel(params.siteId, {
        personnelId: form.personnelId,
        startDate: form.startDate,
        role: form.role || null,
      });

      setForm({
        personnelId: "",
        role: "",
        startDate: new Date().toISOString().slice(0, 10),
      });

      await load();
    } catch (err) {
      setAssignError(
        err instanceof Error ? err.message : "Personel atanamadı."
      );
    } finally {
      setAssigning(false);
    }
  }

  async function closeAssignment(assignmentId: string) {
    try {
      await projectSiteService.closeAssignment(assignmentId);
      await load();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Atama kapatılamadı."
      );
    }
  }

  return (
    <ErpShell
      title={site?.name ?? "Şantiye Detayı"}
      description={
        site ? `${site.projectCode} · ${site.projectName}` : "Şantiye bilgileri yükleniyor"
      }
    >
      <div className="erp-project-breadcrumb">
        <Link href="/projeler">Projeler</Link>
        <span>›</span>
        <Link href={`/projeler/${params.id}`}>{site?.projectName ?? "Proje"}</Link>
        <span>›</span>
        <strong>{site?.name ?? "Şantiye"}</strong>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      {loading ? (
        <div className="erp-panel erp-loading">Şantiye yükleniyor...</div>
      ) : !site ? (
        <div className="erp-panel erp-empty-state">
          <strong>Şantiye bulunamadı</strong>
        </div>
      ) : (
        <>
          <section className="erp-panel" id="genel">
            <div className="erp-panel-header">
              <div>
                <h2>Şantiye Bilgileri</h2>
                <p>{site.code}</p>
              </div>

              <span className={`erp-status ${site.isActive ? "green" : "gray"}`}>
                {site.isActive ? "Aktif" : "Pasif"}
              </span>
            </div>

            <div className="erp-detail-grid">
              <div><span>Şantiye Kodu</span><strong>{site.code}</strong></div>
              <div><span>Şantiye Adı</span><strong>{site.name}</strong></div>
              <div><span>Konum</span><strong>{site.location || "—"}</strong></div>
              <div><span>Notlar</span><strong>{site.notes || "—"}</strong></div>
              <div><span>Oluşturulma</span><strong>{formatDate(site.createdAtUtc)}</strong></div>
              <div><span>Depo Sayısı</span><strong>{site.warehouses.length}</strong></div>
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Personel Ata</h2>
                <p>Şantiyeye yeni personel görevlendirmesi ekleyin</p>
              </div>
            </div>

            {assignError && <div className="erp-alert error">{assignError}</div>}

            <form className="erp-form-card" onSubmit={assign}>
              <div className="erp-form-grid">
                <label>
                  <span>Personel *</span>
                  <select
                    required
                    value={form.personnelId}
                    onChange={(e) => update("personnelId", e.target.value)}
                  >
                    <option value="">Seçin</option>
                    {assignable.map((person) => (
                      <option key={person.id} value={person.id}>
                        {person.employeeNumber} — {person.fullName}
                        {person.jobTitle ? ` (${person.jobTitle})` : ""}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  <span>Görev</span>
                  <input
                    className="erp-input"
                    value={form.role}
                    onChange={(e) => update("role", e.target.value)}
                  />
                </label>

                <label>
                  <span>Başlangıç Tarihi *</span>
                  <input
                    className="erp-input"
                    type="date"
                    required
                    value={form.startDate}
                    onChange={(e) => update("startDate", e.target.value)}
                  />
                </label>
              </div>

              <div className="erp-actions">
                <button type="submit" disabled={assigning || !form.personnelId}>
                  {assigning ? "Atanıyor..." : "Personeli Ata"}
                </button>
              </div>
            </form>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Atanmış Personel</h2>
                <p>{site.assignments.length} kayıt</p>
              </div>
            </div>

            {site.assignments.length === 0 ? (
              <div className="erp-empty-state">
                Henüz personel ataması yapılmamış.
              </div>
            ) : (
              <div className="erp-project-list">
                {site.assignments.map((assignment) => (
                  <div className="erp-project-list-item" key={assignment.id}>
                    <div>
                      <strong>
                        {assignment.employeeNumber} · {assignment.fullName}
                      </strong>
                      <span>
                        {assignment.role || "Görev belirtilmedi"}
                        {assignment.jobTitle ? ` · ${assignment.jobTitle}` : ""}
                      </span>
                      <span>
                        {formatDate(assignment.startDate)} —{" "}
                        {assignment.endDate ? formatDate(assignment.endDate) : "devam ediyor"}
                      </span>
                    </div>

                    {assignment.isActive && !assignment.endDate ? (
                      <button
                        type="button"
                        className="erp-button secondary"
                        onClick={() => void closeAssignment(assignment.id)}
                      >
                        Atamayı Kapat
                      </button>
                    ) : (
                      <span className="erp-status gray">Kapatıldı</span>
                    )}
                  </div>
                ))}
              </div>
            )}
          </section>
        </>
      )}
    </ErpShell>
  );
}
