"use client";

import { useEffect, useState } from "react";

import QuickCard from "@/components/dashboard/quick-card";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  projectWorkflowService,
  type ProjectWorkflowSummary,
} from "@/services/project-workflow.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  minimumFractionDigits: 0,
  maximumFractionDigits: 0,
});

function alertClass(severity: string) {
  if (severity === "danger") {
    return "dashboard-workflow-alert dashboard-workflow-alert-danger";
  }

  if (severity === "warning") {
    return "dashboard-workflow-alert dashboard-workflow-alert-warning";
  }

  return "dashboard-workflow-alert dashboard-workflow-alert-info";
}

export default function ProjectWorkflowWidget() {
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState("");
  const [summary, setSummary] =
    useState<ProjectWorkflowSummary | null>(null);

  const [loadingProjects, setLoadingProjects] = useState(true);
  const [loadingSummary, setLoadingSummary] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    async function loadProjects() {
      setLoadingProjects(true);
      setError("");

      try {
        const result = await projectService.getAll();
        setProjects(result);

        const firstActive =
          result.find((project) => project.status === 2) ??
          result[0];

        if (firstActive) {
          setSelectedProjectId(firstActive.id);
        }
      } catch {
        setProjects([]);
        setError("Projeler alınamadı.");
      } finally {
        setLoadingProjects(false);
      }
    }

    void loadProjects();
  }, []);

  useEffect(() => {
    if (!selectedProjectId) {
      setSummary(null);
      return;
    }

    async function loadSummary() {
      setLoadingSummary(true);
      setError("");

      try {
        const result =
          await projectWorkflowService.getSummary(
            selectedProjectId
          );

        setSummary(result);
      } catch {
        setSummary(null);
        setError("Proje süreç özeti alınamadı.");
      } finally {
        setLoadingSummary(false);
      }
    }

    void loadSummary();
  }, [selectedProjectId]);

  const pendingPayments =
    (summary?.payments.pendingApproval ?? 0) +
    (summary?.payments.approved ?? 0);

  return (
    <div className="erp-panel dashboard-project-workflow-widget">
      <div className="erp-panel-header">
        <div>
          <h2>Proje Süreç Durumu</h2>
          <p>
            Satın alma, depo, hakediş, ödeme ve muhasebe
            akışı
          </p>
        </div>

        <select
          value={selectedProjectId}
          disabled={loadingProjects || projects.length === 0}
          onChange={(event) =>
            setSelectedProjectId(event.target.value)
          }
          aria-label="Proje seçimi"
        >
          {projects.length === 0 ? (
            <option value="">
              {loadingProjects
                ? "Projeler yükleniyor..."
                : "Proje bulunamadı"}
            </option>
          ) : (
            projects.map((project) => (
              <option key={project.id} value={project.id}>
                {project.code} - {project.name}
              </option>
            ))
          )}
        </select>
      </div>

      {error ? (
        <div className="erp-warning">{error}</div>
      ) : null}

      {loadingSummary ? (
        <div className="erp-empty-state">
          Süreç özeti yükleniyor...
        </div>
      ) : null}

      {!loadingSummary && summary ? (
        <>
          <div className="erp-quick-grid">
            <QuickCard
              label="Açık Talep"
              value={
                summary.purchaseRequests.pending +
                summary.purchaseRequests.approved
              }
              href="/satin-alma"
            />

            <QuickCard
              label="Devam Eden RFQ"
              value={summary.rfqs.pending}
              href="/satin-alma/rfq"
            />

            <QuickCard
              label="Geciken Sipariş"
              value={summary.purchaseOrders.overdueDelivery}
              href="/satin-alma/siparis"
            />

            <QuickCard
              label="Taslak Mal Kabul"
              value={summary.goodsReceipts.draft}
              href="/depo/mal-kabul"
            />

            <QuickCard
              label="Aktif Rezervasyon"
              value={summary.inventory.activeReservations}
              href="/depo/rezervasyonlar"
            />

            <QuickCard
              label="Bekleyen Hakediş"
              value={summary.progressPayments.pendingApproval}
              href="/hakedis"
            />

            <QuickCard
              label="Bekleyen Ödeme"
              value={pendingPayments}
              href="/finans/odemeler"
            />

            <QuickCard
              label="Taslak Muhasebe Fişi"
              value={summary.accounting.draftVoucherCount}
              href="/muhasebe/fisler"
            />
          </div>

          <div className="dashboard-workflow-finance">
            <div>
              <span>Net Hakediş</span>
              <strong>
                {money.format(
                  summary.progressPayments.netPayableAmount
                )}
              </strong>
            </div>

            <div>
              <span>Ödenen Tutar</span>
              <strong>
                {money.format(summary.payments.paidAmount)}
              </strong>
            </div>

            <div>
              <span>Depo Çıkışı</span>
              <strong>
                {summary.inventory.issuedQuantity.toLocaleString(
                  "tr-TR"
                )}
              </strong>
            </div>

            <div>
              <span>Post Edilmiş Fiş</span>
              <strong>
                {summary.accounting.postedVoucherCount}
              </strong>
            </div>
          </div>

          <div className="dashboard-workflow-alerts">
            <div className="dashboard-workflow-alerts-header">
              <h3>Aksiyon Gerektiren Konular</h3>
              <span>{summary.alerts.length} uyarı</span>
            </div>

            {summary.alerts.length === 0 ? (
              <div className="dashboard-workflow-success">
                Bu proje için kritik süreç uyarısı bulunmuyor.
              </div>
            ) : (
              summary.alerts.map((alert) => (
                <div
                  key={alert.code}
                  className={alertClass(alert.severity)}
                >
                  <div>
                    <strong>{alert.title}</strong>
                    <p>{alert.message}</p>
                  </div>

                  <span>{alert.count}</span>
                </div>
              ))
            )}
          </div>
        </>
      ) : null}
    </div>
  );
}
