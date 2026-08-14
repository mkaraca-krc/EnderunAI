"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  progressPaymentService,
  ProgressPaymentStatus,
  type ProgressPaymentListItem,
} from "@/services/progress-payment.service";

const statusLabels: Record<ProgressPaymentStatus, string> = {
  [ProgressPaymentStatus.Draft]: "Taslak",
  [ProgressPaymentStatus.PendingApproval]: "Onay Bekliyor",
  [ProgressPaymentStatus.Approved]: "Onaylandı",
  [ProgressPaymentStatus.Posted]: "Kesinleşti",
  [ProgressPaymentStatus.Cancelled]: "İptal",
};

const statusClasses: Record<ProgressPaymentStatus, string> = {
  [ProgressPaymentStatus.Draft]: "gray",
  [ProgressPaymentStatus.PendingApproval]: "yellow",
  [ProgressPaymentStatus.Approved]: "blue",
  [ProgressPaymentStatus.Posted]: "green",
  [ProgressPaymentStatus.Cancelled]: "red",
};

const dateFormatter = new Intl.DateTimeFormat("tr-TR");

export default function ProgressPaymentsPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [items, setItems] = useState<ProgressPaymentListItem[]>([]);

  const [companyId, setCompanyId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [status, setStatus] = useState("");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const filteredProjects = useMemo(
    () =>
      projects.filter(
        (project) => !companyId || project.companyId === companyId
      ),
    [projects, companyId]
  );

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [companyRows, projectRows, paymentRows] = await Promise.all([
        companyService.getAll(),
        projectService.getAll(),
        progressPaymentService.getAll({
          companyId: companyId || undefined,
          projectId: projectId || undefined,
          status: status === "" ? undefined : Number(status),
        }),
      ]);

      setCompanies(companyRows);
      setProjects(projectRows);
      setItems(paymentRows);

      if (!companyId && companyRows.length === 1) {
        setCompanyId(companyRows[0].id);
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Hakediş listesi yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [companyId, projectId, status]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (
      projectId &&
      !filteredProjects.some((project) => project.id === projectId)
    ) {
      setProjectId("");
    }
  }, [filteredProjects, projectId]);

  return (
    <ErpShell design="redwood" title="Hakediş Yönetimi">
      <div className="erp-toolbar">
        <div>
          <strong>{loading ? "…" : items.length} hakediş</strong>
          <small>
            Proje bazlı hakediş kayıtlarını, dönemleri ve ödeme durumlarını yönetin.
          </small>
        </div>

        <Link href="/hakedis/yeni" className="erp-primary-link">
          + Yeni Hakediş
        </Link>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      <div className="erp-form-card">
        <div className="erp-form-grid">
          <label>
            <span>Şirket</span>
            <select
              value={companyId}
              onChange={(event) => {
                setCompanyId(event.target.value);
                setProjectId("");
              }}
            >
              <option value="">Tüm şirketler</option>
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.code} — {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Proje</span>
            <select
              value={projectId}
              onChange={(event) => setProjectId(event.target.value)}
            >
              <option value="">Tüm projeler</option>
              {filteredProjects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code} — {project.name}
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
              <option value="">Tüm durumlar</option>
              {Object.entries(statusLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>
        </div>
      </div>

      <div className="erp-table-card">
        <table className="erp-table">
          <thead>
            <tr>
              <th>Hakediş</th>
              <th>Proje</th>
              <th>Dönem</th>
              <th>Tarih</th>
              <th>Bu Dönem</th>
              <th>Kümülatif</th>
              <th>Net Ödeme</th>
              <th>Durum</th>
              <th></th>
            </tr>
          </thead>

          <tbody>
            {!loading && items.length === 0 && (
              <tr>
                <td colSpan={9}>
                  <div className="erp-empty-state">
                    <strong>Henüz hakediş kaydı bulunmuyor.</strong>
                    <p>
                      İlk hakedişi oluşturarak proje ilerleme ve ödeme takibini başlatın.
                    </p>
                    <Link href="/hakedis/yeni">Yeni Hakediş Oluştur</Link>
                  </div>
                </td>
              </tr>
            )}

            {items.map((item) => (
              <tr key={item.id}>
                <td>
                  <strong>{item.progressPaymentNumber}</strong>
                  <small>{item.itemCount} poz satırı</small>
                </td>

                <td>
                  <strong>{item.projectCode}</strong>
                  <small>{item.projectName}</small>
                </td>

                <td>{item.periodNumber}</td>

                <td>
                  {dateFormatter.format(
                    new Date(item.progressPaymentDate)
                  )}
                </td>

                <td>{money(item.currentAmount)}</td>

                <td>{money(item.cumulativeAmount)}</td>

                <td>
                  <strong>{money(item.netPayableAmount)}</strong>
                </td>

                <td>
                  <span
                    className={`erp-status ${
                      statusClasses[item.status]
                    }`}
                  >
                    {statusLabels[item.status]}
                  </span>
                </td>

                <td>
                  <Link href={`/hakedis/${item.id}`}>Detay</Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </ErpShell>
  );
}
