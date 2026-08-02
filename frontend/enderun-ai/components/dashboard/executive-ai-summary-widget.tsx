import Link from "next/link";

import type { AIAnalysisItem } from "@/services/ai-analysis.service";
import type { FinanceDashboard } from "@/services/finance-dashboard.service";
import type { ProjectProfitability } from "@/services/project-profitability.service";

type ExecutiveAiSummaryWidgetProps = {
  activeProjects: number;
  riskyProjects: number;
  pendingProgressPayments: number;
  openPurchaseRequests: number;
  openRfqs: number;
  openOrders: number;
  criticalStock: number;
  finance: FinanceDashboard | null;
  profitability: ProjectProfitability[];
  aiAlerts: AIAnalysisItem[];
};

type SummaryItem = {
  text: string;
  tone: "positive" | "warning" | "critical" | "neutral";
};

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  minimumFractionDigits: 0,
  maximumFractionDigits: 0,
});

export default function ExecutiveAiSummaryWidget({
  activeProjects,
  riskyProjects,
  pendingProgressPayments,
  openPurchaseRequests,
  openRfqs,
  openOrders,
  criticalStock,
  finance,
  profitability,
  aiAlerts,
}: ExecutiveAiSummaryWidgetProps) {
  const lossProjects = profitability.filter(
    (project) => project.profit < 0
  );

  const totalProfit = profitability.reduce(
    (sum, project) => sum + project.profit,
    0
  );

  const items: SummaryItem[] = [];

  items.push({
    text: `${activeProjects} aktif proje takip ediliyor.`,
    tone: "neutral",
  });

  if (pendingProgressPayments > 0) {
    items.push({
      text: `${pendingProgressPayments} hakediş yönetici onayı bekliyor.`,
      tone: "warning",
    });
  }

  if (riskyProjects > 0) {
    items.push({
      text: `${riskyProjects} proje kritik sağlık durumunda.`,
      tone: "critical",
    });
  }

  if (lossProjects.length > 0) {
    items.push({
      text: `${lossProjects.length} proje zarar gösteriyor.`,
      tone: "critical",
    });
  } else if (profitability.length > 0) {
    items.push({
      text: `Proje portföyü toplam ${money.format(
        totalProfit
      )} kâr gösteriyor.`,
      tone: totalProfit >= 0 ? "positive" : "critical",
    });
  }

  if (criticalStock > 0) {
    items.push({
      text: `${criticalStock} stok kalemi minimum seviyede veya altında.`,
      tone: "critical",
    });
  }

  if (openPurchaseRequests > 0) {
    items.push({
      text: `${openPurchaseRequests} satın alma talebi işlem bekliyor.`,
      tone: "warning",
    });
  }

  if (openRfqs > 0 || openOrders > 0) {
    items.push({
      text: `${openRfqs} RFQ ve ${openOrders} açık sipariş devam ediyor.`,
      tone: "neutral",
    });
  }

  if (finance) {
    const cashDataAvailable = !finance.unavailableFields.includes(
      "bankBalance"
    );

    if (cashDataAvailable) {
      items.push({
        text: `Banka bakiyesi ${money.format(
          finance.bankBalance
        )}, net nakit ${money.format(finance.netCash)}.`,
        tone:
          finance.netCash >= 0
            ? "positive"
            : "critical",
      });

      if (finance.pendingPayments > 0) {
        items.push({
          text: `${money.format(
            finance.pendingPayments
          )} tutarında bekleyen ödeme bulunuyor.`,
          tone: "warning",
        });
      }

      if (finance.supplierDebt > 0) {
        items.push({
          text: `Açık tedarikçi borcu ${money.format(
            finance.supplierDebt
          )}.`,
          tone: "neutral",
        });
      }
    } else {
      items.push({
        text: "Kasa/banka ve tedarikçi bakiye verileri henüz uygulamaya bağlı değil.",
        tone: "neutral",
      });
    }
  }

  if (aiAlerts.length > 0) {
    items.push({
      text: `${aiAlerts.length} AI risk veya öneri kaydı bulunuyor.`,
      tone: "warning",
    });
  }

  const visibleItems = items.slice(0, 8);

  const criticalCount = visibleItems.filter(
    (item) => item.tone === "critical"
  ).length;

  const warningCount = visibleItems.filter(
    (item) => item.tone === "warning"
  ).length;

  return (
    <section className="dashboard-executive-ai-widget">
      <div className="dashboard-executive-ai-header">
        <div>
          <span className="dashboard-executive-ai-kicker">
            ENDERUN AI YÖNETİCİ ÖZETİ
          </span>

          <h2>Merhaba Mehmet</h2>

          <p>
            Güncel finans, proje, satın alma ve depo
            verileri değerlendirildi.
          </p>
        </div>

        <div className="dashboard-executive-ai-score">
          <span>Bugünkü durum</span>

          <strong>
            {criticalCount > 0
              ? "Kritik"
              : warningCount > 0
                ? "Dikkat"
                : "Normal"}
          </strong>

          <small>
            {criticalCount} kritik, {warningCount} uyarı
          </small>
        </div>
      </div>

      <div className="dashboard-executive-ai-list">
        {visibleItems.map((item, index) => (
          <div
            className={`dashboard-executive-ai-item ${item.tone}`}
            key={`${item.text}-${index}`}
          >
            <span className="dashboard-executive-ai-dot" />
            <p>{item.text}</p>
          </div>
        ))}
      </div>

      <div className="dashboard-executive-ai-footer">
        <Link href="/ai-asistan">
          AI Merkezi’ne Git
        </Link>

        <span>
          Veriler dashboard yenileme anına aittir.
        </span>
      </div>
    </section>
  );
}
