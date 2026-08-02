"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import DashboardStat from "@/components/dashboard/dashboard-stat";
import RecentProgressPayments from "@/components/dashboard/recent-progress-payments";
import AiManagementWidget from "@/components/dashboard/ai-management-widget";
import ProjectHealthWidget from "@/components/dashboard/project-health-widget";
import ProjectWorkflowWidget from "@/components/dashboard/project-workflow-widget";
import QuickActionsWidget from "@/components/dashboard/quick-actions-widget";
import FinanceSummaryWidget from "@/components/dashboard/finance-summary-widget";
import OperationsSummaryWidget from "@/components/dashboard/operations-summary-widget";
import ProfitabilityWidget from "@/components/dashboard/profitability-widget";
import NotificationCenterWidget from "@/components/dashboard/notification-center-widget";
import RecentActivitiesWidget, { type DashboardActivity } from "@/components/dashboard/recent-activities-widget";
import ExecutiveAiSummaryWidget from "@/components/dashboard/executive-ai-summary-widget";
import WorkTaskDashboardWidget from "@/components/tasks/work-task-dashboard-widget";

import { apiClient } from "@/lib/api/api-client";
import { accessRequestService } from "@/services/access-request.service";

import {
  aiAnalysisService,
  type AIAnalysisItem,
} from "@/services/ai-analysis.service";


import {
  financeDashboardService,
  type FinanceDashboard,
} from "@/services/finance-dashboard.service";


import {
  projectProfitabilityService,
  type ProjectProfitability,
} from "@/services/project-profitability.service";



import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  progressPaymentService,
  ProgressPaymentStatus,
  type ProgressPaymentListItem,
} from "@/services/progress-payment.service";

import {
  purchaseRequestService,
  type PurchaseRequestListItem,
} from "@/services/purchase-request.service";

import {
  purchaseOrderService,
  type PurchaseOrderListItem,
} from "@/services/purchase-order.service";

import {
  goodsReceiptService,
  type GoodsReceiptListItem,
} from "@/services/goods-receipt.service";

import {
  dashboardInventoryMovementService,
  type DashboardInventoryMovement,
} from "@/services/dashboard-inventory-movement.service";

import {
  rfqService,
  type RfqListItem,
} from "@/services/rfq.service";

import {
  inventoryService,
  type InventoryItemListItem,
} from "@/services/inventory.service";

import {
  personnelService,
  type PersonnelListItem,
} from "@/services/personnel.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  minimumFractionDigits: 0,
  maximumFractionDigits: 0,
});

const date = new Intl.DateTimeFormat("tr-TR");

const progressStatusLabels: Record<
  ProgressPaymentStatus,
  string
> = {
  [ProgressPaymentStatus.Draft]: "Taslak",
  [ProgressPaymentStatus.PendingApproval]:
    "Onay Bekliyor",
  [ProgressPaymentStatus.Approved]: "Onaylandı",
  [ProgressPaymentStatus.Posted]: "Kesinleşti",
  [ProgressPaymentStatus.Cancelled]: "İptal",
};

const progressStatusClasses: Record<
  ProgressPaymentStatus,
  DashboardActivity["statusTone"]
> = {
  [ProgressPaymentStatus.Draft]: "gray",
  [ProgressPaymentStatus.PendingApproval]: "yellow",
  [ProgressPaymentStatus.Approved]: "blue",
  [ProgressPaymentStatus.Posted]: "green",
  [ProgressPaymentStatus.Cancelled]: "red",
};

export default function DashboardPage() {
  const [projects, setProjects] = useState<
    ProjectListItem[]
  >([]);

  const [progressPayments, setProgressPayments] =
    useState<ProgressPaymentListItem[]>([]);

  const [purchaseRequests, setPurchaseRequests] =
    useState<PurchaseRequestListItem[]>([]);

  const [purchaseOrders, setPurchaseOrders] =
    useState<PurchaseOrderListItem[]>([]);

  const [goodsReceipts, setGoodsReceipts] =
    useState<GoodsReceiptListItem[]>([]);

  const [stockMovements, setStockMovements] =
    useState<DashboardInventoryMovement[]>([]);

  const [rfqs, setRfqs] = useState<RfqListItem[]>([]);

  const [inventory, setInventory] = useState<
    InventoryItemListItem[]
  >([]);

  const [personnel, setPersonnel] = useState<
    PersonnelListItem[]
  >([]);

  const [loading, setLoading] = useState(true);
  const [warnings, setWarnings] = useState<string[]>([]);

  const [aiAlerts, setAiAlerts] =
    useState<AIAnalysisItem[]>([]);

  const [finance,setFinance] =
    useState<FinanceDashboard | null>(null);

  const [profitability,setProfitability] =
    useState<ProjectProfitability[]>([]);

  const [pendingAccessRequests, setPendingAccessRequests] = useState(0);

  async function loadDashboard() {
    setLoading(true);
    setWarnings([]);

    const results = await Promise.allSettled([
      projectService.getAll(),
      progressPaymentService.getAll(),
      purchaseRequestService.getAll(),
      purchaseOrderService.getAll(),
      goodsReceiptService.getAll(),
      dashboardInventoryMovementService.getAll(),
      rfqService.getAll(),
      inventoryService.getItems(),
      personnelService.getAll(),
      aiAnalysisService.getDashboard(),
      financeDashboardService.getDashboard(),
      projectProfitabilityService.getSummary(),
    ]);

    const newWarnings: string[] = [];

    const [
      projectResult,
      progressResult,
      requestResult,
      orderResult,
      goodsReceiptResult,
      stockMovementResult,
      rfqResult,
      inventoryResult,
      personnelResult,
      aiResult,
      financeResult,
      profitabilityResult,
    ] = results;

    if (projectResult.status === "fulfilled") {
      setProjects(projectResult.value);
    } else {
      setProjects([]);
      newWarnings.push("Proje verileri alınamadı.");
    }

    if (progressResult.status === "fulfilled") {
      setProgressPayments(progressResult.value);
    } else {
      setProgressPayments([]);
      newWarnings.push("Hakediş verileri alınamadı.");
    }

    if (requestResult.status === "fulfilled") {
      setPurchaseRequests(requestResult.value);
    } else {
      setPurchaseRequests([]);
      newWarnings.push(
        "Satın alma talepleri alınamadı."
      );
    }

    if (orderResult.status === "fulfilled") {
      setPurchaseOrders(orderResult.value);
    } else {
      setPurchaseOrders([]);
      newWarnings.push("Sipariş verileri alınamadı.");
    }

    if (goodsReceiptResult.status === "fulfilled") {
      setGoodsReceipts(goodsReceiptResult.value);
    } else {
      setGoodsReceipts([]);
      newWarnings.push("Mal kabul verileri alınamadı.");
    }

    if (stockMovementResult.status === "fulfilled") {
      setStockMovements(stockMovementResult.value);
    } else {
      setStockMovements([]);
      newWarnings.push("Stok hareketleri alınamadı.");
    }

    if (rfqResult.status === "fulfilled") {
      setRfqs(rfqResult.value);
    } else {
      setRfqs([]);
      newWarnings.push("RFQ verileri alınamadı.");
    }

    if (inventoryResult.status === "fulfilled") {
      setInventory(inventoryResult.value);
    } else {
      setInventory([]);
      newWarnings.push("Stok verileri alınamadı.");
    }

    if (personnelResult.status === "fulfilled") {
      setPersonnel(personnelResult.value);
    } else {
      setPersonnel([]);
      newWarnings.push("Personel verileri alınamadı.");
    }

    if (financeResult.status === "fulfilled") {
      setFinance(financeResult.value);
    } else {
      setFinance(null);
      newWarnings.push(
        "Finans dashboard verileri alınamadı."
      );
    }


    setWarnings(newWarnings);
    setLoading(false);
  }

  useEffect(() => {
    void loadDashboard();
  }, []);

  useEffect(() => {
    let active = true;

    void apiClient<{ roles: string[] }>("auth/me")
      .then((session) => {
        if (!active) return;
        if (
          !session.roles.includes("Admin") &&
          !session.roles.includes("Genel Müdür")
        ) {
          return;
        }
        return accessRequestService.getAll(false);
      })
      .then((requests) => {
        if (active && requests) {
          setPendingAccessRequests(requests.length);
        }
      })
      .catch(() => {
        if (active) setPendingAccessRequests(0);
      });

    return () => {
      active = false;
    };
  }, []);

  const bestProfitProject =
    profitability.length > 0
      ? [...profitability]
          .sort(
            (a, b) =>
              b.profit - a.profit
          )[0]
      : null;


  const metrics = useMemo(() => {
    const activeProjects = projects.filter(
      (x) => x.status === 2
    );

    const riskyProjects = projects.filter(
      (x) => x.healthStatus === 2
    );

    const contractTotal = projects.reduce(
      (sum, x) => sum + (x.contractAmount ?? 0),
      0
    );

    const currentProgressTotal =
      progressPayments
        .filter(
          (x) =>
            x.status !==
            ProgressPaymentStatus.Cancelled
        )
        .reduce(
          (sum, x) => sum + x.currentAmount,
          0
        );

    const priceDifferenceTotal =
      progressPayments
        .filter(
          (x) =>
            x.status !==
            ProgressPaymentStatus.Cancelled
        )
        .reduce(
          (sum, x) =>
            sum + x.priceDifferenceAmount,
          0
        );

    const netPayableTotal =
      progressPayments
        .filter(
          (x) =>
            x.status !==
            ProgressPaymentStatus.Cancelled
        )
        .reduce(
          (sum, x) => sum + x.netPayableAmount,
          0
        );

    const pendingProgressPayments =
      progressPayments.filter(
        (x) =>
          x.status ===
          ProgressPaymentStatus.PendingApproval
      );

    const openPurchaseRequests =
      purchaseRequests.filter(
        (x) => ![5, 6, 7].includes(x.status)
      );

    const openOrders = purchaseOrders.filter(
      (x) => ![5, 6].includes(x.status)
    );

    const openRfqs = rfqs.filter(
      (x) => x.status < 4
    );

    const criticalStock = inventory.filter(
      (x) =>
        x.isActive &&
        x.availableStock <= x.minimumStock
    );

    const activePersonnel = personnel.filter(
      (x) => x.isActive
    );

    return {
      activeProjects,
      riskyProjects,
      contractTotal,
      currentProgressTotal,
      priceDifferenceTotal,
      netPayableTotal,
      pendingProgressPayments,
      openPurchaseRequests,
      openOrders,
      openRfqs,
      criticalStock,
      activePersonnel,
    };
  }, [
    projects,
    progressPayments,
    purchaseRequests,
    purchaseOrders,
    rfqs,
    inventory,
    personnel,
  ]);

  const recentProgressPayments = useMemo(
    () =>
      [...progressPayments]
        .sort(
          (a, b) =>
            new Date(b.progressPaymentDate).getTime() -
            new Date(a.progressPaymentDate).getTime()
        )
        .slice(0, 6),
    [progressPayments]
  );

  const attentionItems = useMemo(() => {
    const result: {
      type: "red" | "yellow" | "blue";
      title: string;
      description: string;
      href: string;
    }[] = [];

    if (
      metrics.pendingProgressPayments.length > 0
    ) {
      result.push({
        type: "yellow",
        title: "Onay bekleyen hakedişler",
        description:
          `${metrics.pendingProgressPayments.length} hakediş yönetici onayı bekliyor.`,
        href: "/hakedis",
      });
    }

    if (metrics.riskyProjects.length > 0) {
      result.push({
        type: "red",
        title: "Riskli projeler",
        description:
          `${metrics.riskyProjects.length} proje kırmızı sağlık durumunda.`,
        href: "/projeler",
      });
    }

    if (metrics.criticalStock.length > 0) {
      result.push({
        type: "red",
        title: "Kritik stok",
        description:
          `${metrics.criticalStock.length} stok kalemi minimum seviyede veya altında.`,
        href: "/depo-stok",
      });
    }

    if (metrics.openPurchaseRequests.length > 0) {
      result.push({
        type: "blue",
        title: "Açık satın alma talepleri",
        description:
          `${metrics.openPurchaseRequests.length} talep işlem bekliyor.`,
        href: "/satin-alma",
      });
    }

    if (metrics.openRfqs.length > 0) {
      result.push({
        type: "blue",
        title: "Devam eden RFQ",
        description:
          `${metrics.openRfqs.length} RFQ süreci devam ediyor.`,
        href: "/satin-alma/rfq",
      });
    }

    const noPriceDifference =
      progressPayments.filter(
        (x) =>
          x.status !==
            ProgressPaymentStatus.Cancelled &&
          x.currentAmount > 0 &&
          x.priceDifferenceAmount === 0
      );

    if (noPriceDifference.length > 0) {
      result.push({
        type: "yellow",
        title: "Fiyat farkı kontrolü",
        description:
          `${noPriceDifference.length} hakedişte fiyat farkı sıfır görünüyor.`,
        href: "/fiyat-farki",
      });
    }

    return result.slice(0, 6);
  }, [metrics, progressPayments]);

  const recentActivities = useMemo<DashboardActivity[]>(() => {
    const progressActivities: DashboardActivity[] =
      progressPayments.map((item) => ({
        id: item.id,
        type: "progress-payment",
        title: item.projectName,
        description: `${item.projectCode} projesi hakedişi`,
        documentNumber: item.progressPaymentNumber,
        activityDate: item.progressPaymentDate,
        href: `/hakedis/${item.id}`,
        statusLabel: progressStatusLabels[item.status],
        statusTone: progressStatusClasses[item.status],
      }));

    const orderStatus = {
      0: ["Taslak", "gray"],
      1: ["Onay Bekliyor", "yellow"],
      2: ["Onaylandı", "blue"],
      3: ["Kısmi Teslim", "yellow"],
      4: ["Tamamlandı", "green"],
      5: ["İptal", "red"],
      6: ["Reddedildi", "red"],
    } as const;

    const orderActivities: DashboardActivity[] =
      purchaseOrders.map((item) => {
        const status = orderStatus[item.status];

        return {
          id: item.id,
          type: "purchase-order",
          title: item.supplierTitle,
          description: `${item.projectCode} · ${item.projectName}`,
          documentNumber: item.orderNumber,
          activityDate: item.orderDate,
          href: `/satin-alma/siparis/${item.id}`,
          statusLabel: status[0],
          statusTone: status[1],
        };
      });

    const receiptStatus = {
      0: ["Taslak", "gray"],
      1: ["Post Edildi", "green"],
      2: ["İptal", "red"],
    } as const;

    const movementType = {
      0: "Stok Girişi",
      1: "Stok Çıkışı",
      2: "Transfer Çıkışı",
      3: "Transfer Girişi",
    } as const;

    const stockActivities: DashboardActivity[] =
      stockMovements.map((item) => ({
        id: item.id,
        type: "stock-movement",
        title: item.itemName,
        description:
          `${item.warehouseName}` +
          (item.projectName
            ? ` · ${item.projectName}`
            : ""),
        documentNumber: item.referenceNumber,
        activityDate: item.movementDate,
        href: "/depo-stok/hareketler",
        statusLabel:
          movementType[
            item.type as keyof typeof movementType
          ] ?? "Stok Hareketi",
        statusTone:
          item.type === 1 || item.type === 2
            ? "yellow"
            : "green",
      }));

    const receiptActivities: DashboardActivity[] =
      goodsReceipts.map((item) => {
        const status = receiptStatus[item.status];

        return {
          id: item.id,
          type: "goods-receipt",
          title: item.supplierTitle,
          description: `${item.warehouseCode} · ${item.warehouseName}`,
          documentNumber: item.receiptNumber,
          activityDate: item.receiptDate,
          href: `/depo-stok/mal-kabul/${item.id}`,
          statusLabel: status[0],
          statusTone: status[1],
        };
      });

    return [
      ...progressActivities,
      ...orderActivities,
      ...receiptActivities,
      ...stockActivities,
    ]
      .sort(
        (a, b) =>
          new Date(b.activityDate).getTime() -
          new Date(a.activityDate).getTime()
      )
      .slice(0, 10);
  }, [
    progressPayments,
    purchaseOrders,
    goodsReceipts,
    stockMovements,
  ]);

  return (
    <ErpShell
      title="Yönetici Dashboard"
      description="Enderun AI operasyon ve finans yönetim merkezi"
    >
      <ExecutiveAiSummaryWidget
        activeProjects={metrics.activeProjects.length}
        riskyProjects={metrics.riskyProjects.length}
        pendingProgressPayments={
          metrics.pendingProgressPayments.length
        }
        openPurchaseRequests={
          metrics.openPurchaseRequests.length
        }
        openRfqs={metrics.openRfqs.length}
        openOrders={metrics.openOrders.length}
        criticalStock={metrics.criticalStock.length}
        finance={finance}
        profitability={profitability}
        aiAlerts={aiAlerts}
      />

      <section className="enderun-dashboard-hero">
        <div>
          <span className="enderun-dashboard-kicker">
            ENDERUN AI YÖNETİM SİSTEMİ
          </span>

          <h2>Şirket genel görünümü</h2>

          <p>
            Projeler, hakedişler, satın alma,
            stok ve personel verileri canlı olarak
            özetlenmektedir.
          </p>
        </div>

        <div className="enderun-dashboard-hero-actions">
          <button
            type="button"
            className="erp-secondary-button"
            disabled={loading}
            onClick={() => void loadDashboard()}
          >
            {loading ? "Yükleniyor..." : "Verileri Yenile"}
          </button>

          <Link
            href="/hakedis/yeni"
            className="erp-primary-button"
          >
            Yeni Hakediş
          </Link>
        </div>
      </section>

      {warnings.length > 0 && (
        <div className="erp-alert error">
          Bazı modüller yüklenemedi:{" "}
          {warnings.join(" ")}
        </div>
      )}

      <section className="enderun-dashboard-stats">
        <DashboardStat
          icon="▣"
          label="Aktif Proje"
          value={String(
            metrics.activeProjects.length
          )}
          note={`${projects.length} toplam proje`}
          href="/projeler"
        />

        <DashboardStat
          icon="₺"
          label="Toplam Sözleşme"
          value={money.format(
            metrics.contractTotal
          )}
          note="Kayıtlı proje sözleşmeleri"
          href="/projeler"
        />

        <DashboardStat
          icon="▧"
          label="Toplam Hakediş"
          value={money.format(
            metrics.currentProgressTotal
          )}
          note={`${progressPayments.length} hakediş kaydı`}
          href="/hakedis"
        />

        <DashboardStat
          icon="∆"
          label="Fiyat Farkı"
          value={money.format(
            metrics.priceDifferenceTotal
          )}
          note="İptal olmayan hakedişler"
          href="/fiyat-farki"
        />

        <DashboardStat
          icon="✓"
          label="Net Ödenecek"
          value={money.format(
            metrics.netPayableTotal
          )}
          note="Hakediş net toplamı"
          href="/hakedis"
        />

        <DashboardStat
          icon="⌛"
          label="Onay Bekleyen"
          value={String(
            metrics.pendingProgressPayments.length
          )}
          note="Hakediş onay süreci"
          href="/hakedis"
        />

        <DashboardStat
          icon="⌑"
          label="Satın Alma Talebi"
          value={String(
            metrics.openPurchaseRequests.length
          )}
          note="Açık talepler"
          href="/satin-alma"
        />

        <DashboardStat
          icon="≋"
          label="Devam Eden RFQ"
          value={String(metrics.openRfqs.length)}
          note={`${metrics.openOrders.length} açık sipariş`}
          href="/satin-alma/rfq"
        />

        <DashboardStat
          icon="!"
          label="Kritik Stok"
          value={String(
            metrics.criticalStock.length
          )}
          note="Minimum stok altında"
          href="/depo-stok"
        />

        <DashboardStat
          icon="★"
          label="Proje Kârlılık"
          value={money.format(
            bestProfitProject?.profit ?? 0
          )}
          note={
            bestProfitProject
              ? `${bestProfitProject.projectName} - %${bestProfitProject.profitMargin}`
              : "Henüz veri yok"
          }
          href="/projeler"
        />


        <DashboardStat
          icon="♙"
          label="Aktif Personel"
          value={String(
            metrics.activePersonnel.length
          )}
          note={`${personnel.length} kayıtlı personel`}
          href="/personel"
        />


        <DashboardStat
          icon="₺"
          label="Tedarikçi Borcu"
          value={money.format(
            finance?.supplierDebt ?? 0
          )}
          note="Fatura/cari modülü devreye girince dolacak"
          href="/cariler"
          unavailable={
            finance?.unavailableFields.includes("payables") ?? false
          }
        />

        <DashboardStat
          icon="🏦"
          label="Banka Bakiyesi"
          value={money.format(
            finance?.bankBalance ?? 0
          )}
          note="Kasa/banka modülü devreye girince dolacak"
          href="/finans"
          unavailable={
            finance?.unavailableFields.includes("bankBalance") ?? false
          }
        />

        <DashboardStat
          icon="⌛"
          label="Bekleyen Ödeme"
          value={money.format(
            finance?.pendingPayments ?? 0
          )}
          note="Kasa/banka modülü devreye girince dolacak"
          href="/finans"
          unavailable={
            finance?.unavailableFields.includes("todayPayments") ?? false
          }
        />

        <DashboardStat
          icon="₿"
          label="Net Nakit"
          value={money.format(
            finance?.netCash ?? 0
          )}
          note="Kasa/banka modülü devreye girince dolacak"
          href="/finans"
          unavailable={
            finance?.unavailableFields.includes("netCashChange") ?? false
          }
        />
      </section>

      <section className="dashboard-summary-grid">
        <FinanceSummaryWidget finance={finance} />

        <OperationsSummaryWidget
          openPurchaseRequests={
            metrics.openPurchaseRequests.length
          }
          openRfqs={metrics.openRfqs.length}
          openOrders={metrics.openOrders.length}
          criticalStock={metrics.criticalStock.length}
        />

        <ProfitabilityWidget
          projects={profitability}
        />
      </section>

      <div className="mb-6">
        <WorkTaskDashboardWidget />
      </div>

      <NotificationCenterWidget
        pendingProgressPayments={
          metrics.pendingProgressPayments.length
        }
        openPurchaseRequests={
          metrics.openPurchaseRequests.length
        }
        openRfqs={metrics.openRfqs.length}
        openOrders={metrics.openOrders.length}
        criticalStock={metrics.criticalStock.length}
        riskyProjects={metrics.riskyProjects.length}
        pendingAccessRequests={pendingAccessRequests}
      />

      <RecentActivitiesWidget
        activities={recentActivities}
      />

      <section className="enderun-dashboard-layout">
        <div>
          <RecentProgressPayments
            items={recentProgressPayments}
          />

          <ProjectWorkflowWidget />

      <ProjectHealthWidget
            totalProjects={projects.length}
            activeProjects={metrics.activeProjects.length}
            riskyProjects={metrics.riskyProjects.length}
            activePersonnel={metrics.activePersonnel.length}
          />
        </div>

        <aside>
          <AiManagementWidget
            alerts={aiAlerts}
          />

          <QuickActionsWidget />
        </aside>
      </section>
    </ErpShell>
  );
}
