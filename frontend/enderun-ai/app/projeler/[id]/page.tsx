"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { projectService } from "@/services/project.service";

type Warehouse = {
  id: string;
  code: string;
  name: string;
  type: number;
  isActive: boolean;
};

type ProjectDetail = {
  id: string;
  companyName: string;
  branchName: string;
  employerName: string;
  code: string;
  name: string;
  contractNumber?: string | null;
  contractDate?: string | null;
  contractAmount?: number | null;
  currencyCode: string;
  vatRate: number;
  withholdingRate?: string | null;
  plannedStartDate?: string | null;
  plannedEndDate?: string | null;
  city?: string | null;
  district?: string | null;
  address?: string | null;
  status: number;
  healthStatus: number;
  warehouses: Warehouse[];
};

const modules = [
  { label: "Hakedişler", href: "/hakedis", icon: "▧", text: "Hakediş kayıtları ve kontrolleri" },
  { label: "Satın Alma", href: "/satin-alma", icon: "⌑", text: "Malzeme talepleri ve teklifler" },
  { label: "Personel", href: "/personel", icon: "♙", text: "Projeye bağlı personel" },
  { label: "Depolar", href: "/depo", icon: "⌂", text: "Şantiye deposu ve stoklar" },
  { label: "Finans", href: "/finans", icon: "₺", text: "Proje finansal görünümü" },
  { label: "Dokümanlar", href: "/dokumanlar", icon: "□", text: "Sözleşme ve proje evrakları" },
  { label: "AI Analizleri", href: "/ai-asistan", icon: "⌘", text: "Risk, eksik ve öneriler" },
];

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

function formatMoney(value?: number | null, currency = "TRY") {
  return value == null
    ? "—"
    : new Intl.NumberFormat("tr-TR", {
        style: "currency",
        currency,
      }).format(value);
}

export default function ProjectCenterPage() {
  const params = useParams<{ id: string }>();
  const [project, setProject] = useState<ProjectDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    async function load() {
      try {
        const result = await projectService.getById(params.id);
        setProject(result as ProjectDetail);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Proje yüklenemedi.");
      } finally {
        setLoading(false);
      }
    }

    if (params.id) load();
  }, [params.id]);

  return (
    <ErpShell
      title={project?.name ?? "Proje Merkezi"}
      description={
        project
          ? `${project.code} · ${project.employerName}`
          : "Proje bilgileri yükleniyor"
      }
    >
      <div className="erp-project-breadcrumb">
        <Link href="/projeler">Projeler</Link>
        <span>›</span>
        <strong>{project?.name ?? "Proje Merkezi"}</strong>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      {loading ? (
        <div className="erp-panel erp-loading">Proje yükleniyor...</div>
      ) : !project ? (
        <div className="erp-panel erp-empty-state">
          <strong>Proje bulunamadı</strong>
        </div>
      ) : (
        <>
          <section className="enderun-project-center-hero">
            <div className="enderun-project-center-title">
              <span className="erp-status green">Aktif Proje</span>
              <h2>{project.name}</h2>
              <p>{project.employerName}</p>
            </div>

            <div className="enderun-project-center-metrics">
              <div>
                <span>Sözleşme Bedeli</span>
                <strong>
                  {formatMoney(project.contractAmount, project.currencyCode)}
                </strong>
              </div>
              <div>
                <span>Şube</span>
                <strong>{project.branchName}</strong>
              </div>
              <div>
                <span>Şantiye Deposu</span>
                <strong>{project.warehouses.length}</strong>
              </div>
            </div>
          </section>

          <div className="enderun-project-center-tabs">
            <a className="active" href="#genel">Genel</a>
            {modules.map((module) => (
              <Link key={module.label} href={module.href}>
                {module.label}
              </Link>
            ))}
          </div>

          <section className="erp-panel" id="genel">
            <div className="erp-panel-header">
              <div>
                <h2>Proje Genel Bilgileri</h2>
                <p>Sözleşme ve işveren özeti</p>
              </div>
            </div>

            <div className="erp-detail-grid">
              <div><span>Proje Kodu</span><strong>{project.code}</strong></div>
              <div><span>Şirket</span><strong>{project.companyName}</strong></div>
              <div><span>İşveren</span><strong>{project.employerName}</strong></div>
              <div><span>Şube</span><strong>{project.branchName}</strong></div>
              <div><span>Sözleşme No</span><strong>{project.contractNumber || "—"}</strong></div>
              <div><span>Sözleşme Tarihi</span><strong>{formatDate(project.contractDate)}</strong></div>
              <div><span>Başlangıç</span><strong>{formatDate(project.plannedStartDate)}</strong></div>
              <div><span>Bitiş</span><strong>{formatDate(project.plannedEndDate)}</strong></div>
              <div><span>KDV</span><strong>%{project.vatRate}</strong></div>
              <div><span>Tevkifat</span><strong>{project.withholdingRate || "—"}</strong></div>
              <div className="span-2">
                <span>Şantiye Adresi</span>
                <strong>
                  {[project.address, project.district, project.city]
                    .filter(Boolean)
                    .join(", ") || "—"}
                </strong>
              </div>
            </div>
          </section>

          <div className="enderun-project-module-grid">
            {modules.map((module) => (
              <Link key={module.label} href={module.href}>
                <div className="enderun-project-module-icon">{module.icon}</div>
                <strong>{module.label}</strong>
                <span>{module.text}</span>
              </Link>
            ))}
          </div>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Proje Depoları</h2>
                <p>Projeye bağlı depo kayıtları</p>
              </div>
            </div>

            {project.warehouses.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Depo bulunmuyor</strong>
              </div>
            ) : (
              <div className="erp-project-list">
                {project.warehouses.map((warehouse) => (
                  <div className="erp-project-list-item" key={warehouse.id}>
                    <div>
                      <strong>{warehouse.name}</strong>
                      <span>{warehouse.code}</span>
                    </div>
                    <span className="erp-status green">
                      {warehouse.isActive ? "Aktif" : "Pasif"}
                    </span>
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
