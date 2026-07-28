import Link from "next/link";

import type { ProjectProfitability } from "@/services/project-profitability.service";

type ProfitabilityWidgetProps = {
  projects: ProjectProfitability[];
};

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  minimumFractionDigits: 0,
  maximumFractionDigits: 0,
});

function percent(value: number) {
  if (!Number.isFinite(value)) {
    return "0,0";
  }

  return value.toLocaleString("tr-TR", {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  });
}

function boundedWidth(value: number) {
  if (!Number.isFinite(value) || value <= 0) {
    return 4;
  }

  return Math.max(4, Math.min(100, value));
}

export default function ProfitabilityWidget({
  projects,
}: ProfitabilityWidgetProps) {
  const totals = projects.reduce(
    (result, project) => {
      result.revenue += project.revenue;
      result.materialCost += project.materialCost;
      result.laborCost += project.laborCost;
      result.subcontractorCost +=
        project.subcontractorCost;
      result.generalExpenseCost +=
        project.generalExpenseCost;
      result.otherCost += project.otherCost;
      result.totalCost += project.totalCost;
      result.profit += project.profit;

      return result;
    },
    {
      revenue: 0,
      materialCost: 0,
      laborCost: 0,
      subcontractorCost: 0,
      generalExpenseCost: 0,
      otherCost: 0,
      totalCost: 0,
      profit: 0,
    }
  );

  const profitMargin =
    totals.revenue > 0
      ? (totals.profit / totals.revenue) * 100
      : 0;

  const totalCostRate =
    totals.revenue > 0
      ? (totals.totalCost / totals.revenue) * 100
      : 0;

  const materialRate =
    totals.totalCost > 0
      ? (totals.materialCost / totals.totalCost) *
        100
      : 0;

  const laborRate =
    totals.totalCost > 0
      ? (totals.laborCost / totals.totalCost) * 100
      : 0;

  const subcontractorRate =
    totals.totalCost > 0
      ? (totals.subcontractorCost /
          totals.totalCost) *
        100
      : 0;

  const bestProject =
    projects.length > 0
      ? [...projects].sort(
          (a, b) => b.profit - a.profit
        )[0]
      : null;

  const riskyProjects = projects.filter(
    (project) => project.profit < 0
  );

  return (
    <section className="erp-panel dashboard-profitability-widget">
      <div className="erp-panel-header">
        <div>
          <h2>Proje Kârlılığı</h2>
          <p>Portföy gelir, maliyet ve kâr analizi</p>
        </div>

        <Link href="/projeler">
          Proje Merkezi
        </Link>
      </div>

      <div className="dashboard-profitability-values">
        <div>
          <span>Toplam Gelir</span>
          <strong>
            {money.format(totals.revenue)}
          </strong>
        </div>

        <div>
          <span>Toplam Maliyet</span>
          <strong>
            {money.format(totals.totalCost)}
          </strong>
        </div>

        <div>
          <span>Toplam Kâr</span>
          <strong
            className={
              totals.profit < 0
                ? "dashboard-negative-value"
                : ""
            }
          >
            {money.format(totals.profit)}
          </strong>
        </div>

        <div>
          <span>Kâr Marjı</span>
          <strong
            className={
              profitMargin < 0
                ? "dashboard-negative-value"
                : ""
            }
          >
            %{percent(profitMargin)}
          </strong>
        </div>
      </div>

      <div className="dashboard-profitability-chart">
        <div>
          <div className="dashboard-summary-heading">
            <span>Toplam Maliyet / Gelir</span>
            <strong>
              %{percent(totalCostRate)}
            </strong>
          </div>

          <div className="dashboard-summary-track">
            <span
              className={`dashboard-summary-bar ${
                totalCostRate > 100
                  ? "critical"
                  : "warning"
              }`}
              style={{
                width: `${boundedWidth(
                  totalCostRate
                )}%`,
              }}
            />
          </div>
        </div>

        <div>
          <div className="dashboard-summary-heading">
            <span>Kâr / Gelir</span>
            <strong>
              %{percent(profitMargin)}
            </strong>
          </div>

          <div className="dashboard-summary-track">
            <span
              className={`dashboard-summary-bar ${
                profitMargin < 0
                  ? "critical"
                  : "positive"
              }`}
              style={{
                width: `${boundedWidth(
                  Math.abs(profitMargin)
                )}%`,
              }}
            />
          </div>
        </div>
      </div>

      <div className="dashboard-cost-distribution">
        <div>
          <div className="dashboard-summary-heading">
            <span>Malzeme</span>
            <strong>
              %{percent(materialRate)}
            </strong>
          </div>

          <div className="dashboard-summary-track">
            <span
              className="dashboard-summary-bar neutral"
              style={{
                width: `${boundedWidth(
                  materialRate
                )}%`,
              }}
            />
          </div>
        </div>

        <div>
          <div className="dashboard-summary-heading">
            <span>İşçilik</span>
            <strong>
              %{percent(laborRate)}
            </strong>
          </div>

          <div className="dashboard-summary-track">
            <span
              className="dashboard-summary-bar positive"
              style={{
                width: `${boundedWidth(
                  laborRate
                )}%`,
              }}
            />
          </div>
        </div>

        <div>
          <div className="dashboard-summary-heading">
            <span>Taşeron</span>
            <strong>
              %{percent(subcontractorRate)}
            </strong>
          </div>

          <div className="dashboard-summary-track">
            <span
              className="dashboard-summary-bar warning"
              style={{
                width: `${boundedWidth(
                  subcontractorRate
                )}%`,
              }}
            />
          </div>
        </div>
      </div>

      <div className="dashboard-profitability-footer">
        <div>
          <span>En yüksek kâr sağlayan proje</span>
          <strong>
            {bestProject
              ? `${bestProject.projectName} — ${money.format(
                  bestProject.profit
                )}`
              : "Henüz kârlılık verisi bulunmuyor"}
          </strong>
        </div>

        <div>
          <span>Zarar gösteren proje</span>
          <strong
            className={
              riskyProjects.length > 0
                ? "dashboard-negative-value"
                : ""
            }
          >
            {riskyProjects.length}
          </strong>
        </div>
      </div>
    </section>
  );
}
