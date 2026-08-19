"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";
import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import { Button } from "@/components/ui";
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

const columns: DataTableColumn<ProgressPaymentListItem>[] = [
  {
    key: "hakedis",
    header: "Hakediş",
    value: (item) => item.progressPaymentNumber,
    render: (item) => (
      <>
        <strong>{item.progressPaymentNumber}</strong>
        <small>{item.itemCount} poz satırı</small>
      </>
    ),
  },
  {
    key: "proje",
    header: "Proje",
    value: (item) => `${item.projectCode} ${item.projectName}`,
    render: (item) => (
      <>
        <strong>{item.projectCode}</strong>
        <small>{item.projectName}</small>
      </>
    ),
  },
  { key: "donem", header: "Dönem", value: (item) => item.periodNumber },
  {
    key: "tarih",
    header: "Tarih",
    value: (item) =>
      dateFormatter.format(new Date(item.progressPaymentDate)),
  },
  {
    key: "budonem",
    header: "Bu Dönem",
    numeric: true,
    value: (item) => item.currentAmount,
    render: (item) => money(item.currentAmount),
  },
  {
    key: "kumulatif",
    header: "Kümülatif",
    numeric: true,
    value: (item) => item.cumulativeAmount,
    render: (item) => money(item.cumulativeAmount),
  },
  {
    key: "net",
    header: "Net Ödeme",
    numeric: true,
    value: (item) => item.netPayableAmount,
    render: (item) => <strong>{money(item.netPayableAmount)}</strong>,
  },
  {
    key: "durum",
    header: "Durum",
    value: (item) => statusLabels[item.status],
    render: (item) => (
      <span className={`erp-status ${statusClasses[item.status]}`}>
        {statusLabels[item.status]}
      </span>
    ),
  },
  {
    key: "detay",
    header: "",
    value: () => "",
    render: (item) => <Link href={`/hakedis/${item.id}`}>Detay</Link>,
  },
];

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
      
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>
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
        <DataTable
            rows={items}
            columns={columns}
            rowKey={(item) => item.id}
            loading={loading}
            title="Hakedişler"
            emptyText="Henüz hakediş kaydı bulunmuyor."
            resetKey={`${companyId}|${projectId}`}
          />
      </div>
    </ErpShell>
  );
}
