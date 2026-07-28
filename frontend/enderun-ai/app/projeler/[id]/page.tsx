"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { projectService } from "@/services/project.service";

import {
  projectProfitabilityService,
  type ProjectProfitability,
} from "@/services/project-profitability.service";

import {
  projectDailyReportService,
  type ProjectDailyReport,
} from "@/services/project-daily-report.service";

import {
  projectSiteAnalysisService,
  type ProjectSiteAnalysisResponse,
} from "@/services/project-site-analysis.service";




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
  increaseRate: number;
  cashRetentionRate: number;
  withholdingTaxRate: number;
  materialDeductionRate: number;
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
  { label: "Depo & Stok", href: "/depo-stok", icon: "⌂", text: "Şantiye deposu ve stoklar" },
  { label: "Finans", href: "/finans", icon: "₺", text: "Proje finansal görünümü" },
  { label: "Kesinti Politikası", href: "kesintiler", icon: "%", text: "Hakediş otomatik kesinti kuralları" },
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

function formatPercentage(value?: number | null) {
  return `%${new Intl.NumberFormat("tr-TR", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4,
  }).format(value ?? 0)}`;
}

export default function ProjectCenterPage() {
  const params = useParams<{ id: string }>();
  const [project, setProject] = useState<ProjectDetail | null>(null);

  const [profitability, setProfitability] =
    useState<ProjectProfitability | null>(null);

  const [dailyReports, setDailyReports] =
    useState<ProjectDailyReport[]>([]);


  const [siteAnalysis, setSiteAnalysis] =
    useState<ProjectSiteAnalysisResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    async function load() {
      setLoading(true);
      setError("");

      try {
        const result = await projectService.getById(params.id);
        setProject(result as ProjectDetail);
      } catch (err) {
        setProject(null);
        setError(
          err instanceof Error
            ? err.message
            : "Proje yüklenemedi."
        );
        setLoading(false);
        return;
      }

      const [
        profitabilityResult,
        dailyReportResult,
        siteAnalysisResult,
      ] = await Promise.allSettled([
        projectProfitabilityService.getById(params.id),
        projectDailyReportService.getByProject(params.id),
        projectSiteAnalysisService.getById(params.id),
      ]);

      if (profitabilityResult.status === "fulfilled") {
        setProfitability(profitabilityResult.value);
      } else {
        setProfitability(null);
        console.warn(
          "Proje karlılık verisi yüklenemedi:",
          profitabilityResult.reason
        );
      }

      if (dailyReportResult.status === "fulfilled") {
        setDailyReports(dailyReportResult.value);
      } else {
        setDailyReports([]);
        console.warn(
          "Proje günlükleri yüklenemedi:",
          dailyReportResult.reason
        );
      }

      if (siteAnalysisResult.status === "fulfilled") {
        setSiteAnalysis(siteAnalysisResult.value);
      } else {
        setSiteAnalysis(null);
        console.warn(
          "AI şantiye analizi yüklenemedi:",
          siteAnalysisResult.reason
        );
      }

      setLoading(false);
    }

    if (params.id) {
      load();
    }
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
              <Link
                key={module.label}
                href={
                  module.href === "kesintiler"
                    ? `/projeler/${project.id}/kesintiler`
                    : module.href
                }
              >
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

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Finansal Sözleşme Oranları</h2>
                <p>
                  Hakediş hesaplamalarında varsayılan olarak kullanılacak
                  proje oranları
                </p>
              </div>

              <Link
                href={`/projeler/${project.id}/kesintiler`}
                className="erp-button secondary"
              >
                Kesinti Politikasını Aç
              </Link>
            </div>

            <div className="erp-detail-grid">
              <div>
                <span>Sözleşme Artış Oranı</span>
                <strong>
                  {formatPercentage(project.increaseRate)}
                </strong>
              </div>

              <div>
                <span>Nakit Teminat Kesintisi</span>
                <strong>
                  {formatPercentage(project.cashRetentionRate)}
                </strong>
              </div>

              <div>
                <span>Stopaj Kesintisi</span>
                <strong>
                  {formatPercentage(project.withholdingTaxRate)}
                </strong>
              </div>

              <div>
                <span>Malzeme Kesintisi</span>
                <strong>
                  {formatPercentage(project.materialDeductionRate)}
                </strong>
              </div>

              <div>
                <span>KDV Oranı</span>
                <strong>
                  {formatPercentage(project.vatRate)}
                </strong>
              </div>

              <div>
                <span>Tevkifat Oranı</span>
                <strong>
                  {project.withholdingRate || "—"}
                </strong>
              </div>
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Proje Karlılık Analizi</h2>
                <p>Gelir, maliyet ve kârlılık durumu</p>
              </div>
            </div>

            {!profitability ? (
              <div className="erp-empty-state">
                <strong>Karlılık verisi bulunamadı</strong>
              </div>
            ) : (
              <div className="erp-detail-grid">

                <div>
                  <span>Gelir</span>
                  <strong>
                    {formatMoney(
                      profitability.revenue,
                      project.currencyCode
                    )}
                  </strong>
                </div>

                <div>
                  <span>Toplam Maliyet</span>
                  <strong>
                    {formatMoney(
                      profitability.totalCost,
                      project.currencyCode
                    )}
                  </strong>
                </div>

                <div>
                  <span>Kar</span>
                  <strong>
                    {formatMoney(
                      profitability.profit,
                      project.currencyCode
                    )}
                  </strong>
                </div>

                <div>
                  <span>Kar Marjı</span>
                  <strong>
                    %{profitability.profitMargin}
                  </strong>
                </div>

                <div>
                  <span>Malzeme</span>
                  <strong>
                    {formatMoney(
                      profitability.materialCost,
                      project.currencyCode
                    )}
                  </strong>
                </div>

                <div>
                  <span>İşçilik</span>
                  <strong>
                    {formatMoney(
                      profitability.laborCost,
                      project.currencyCode
                    )}
                  </strong>
                </div>

              </div>
            )}
          </section>



          <section className="erp-panel erp-mt">

            <div className="erp-panel-header">
              <div>
                <h2>Proje Şantiye Günlükleri</h2>
                <p>Saha ilerleme ve günlük operasyon kayıtları</p>
              </div>
            </div>


            {dailyReports.length === 0 ? (

              <div className="erp-empty-state">
                Günlük rapor bulunmuyor.
              </div>

            ) : (

              <div className="erp-project-list">

                {dailyReports.map(report => (

                  <div
                    className="erp-project-list-item"
                    key={report.id}
                  >

                    <div>

                      <strong>
                        {formatDate(report.reportDate)}
                      </strong>

                      <span>
                        {report.summary}
                      </span>

                      <span>
                        Personel: {report.workerCount}
                      </span>

                    </div>


                    <div>

                      <span>
                        {report.weather}
                      </span>

                    </div>

                  </div>

                ))}

              </div>

            )}

          </section>



          <section className="erp-panel erp-mt">

            <div className="erp-panel-header">
              <div>
                <h2>AI Şantiye Analizi</h2>
                <p>Günlük saha verilerine göre yapay zeka değerlendirmesi</p>
              </div>
            </div>


            {!siteAnalysis ? (

              <div className="erp-empty-state">
                AI analizi bulunamadı.
              </div>

            ) : (

              <div className="erp-project-list">

                {siteAnalysis.items.map((item,index)=>(

                  <div
                    className="erp-project-list-item"
                    key={index}
                  >

                    <div>
                      <strong>
                        {item.title}
                      </strong>

                      <span>
                        {item.message}
                      </span>
                    </div>

                    <span>
                      {item.module}
                    </span>

                  </div>

                ))}

              </div>

            )}

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
