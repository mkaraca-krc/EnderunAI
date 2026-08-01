"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  Select,
  StatCard,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";
import {
  procurementDashboardService,
  type ProcurementDashboard,
} from "@/services/procurement-dashboard.service";
import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

const orderStatusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Onay Bekliyor",
  2: "Onaylandı",
  3: "Kısmi Teslim",
  4: "Tamamlandı",
  5: "İptal",
  6: "Reddedildi",
};

const receiptStatusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Stoklara İşlendi",
  2: "İptal",
};

function badgeVariant(status: number) {
  if (status === 4 || status === 1) return "success" as const;
  if (status === 2) return "info" as const;
  if (status === 3 || status === 0) return "warning" as const;
  return "danger" as const;
}

function alertVariant(severity: string) {
  if (severity === "danger") return "danger" as const;
  if (severity === "warning") return "warning" as const;
  return "info" as const;
}

function formatDate(value?: string | null) {
  return value
    ? new Date(value).toLocaleDateString("tr-TR")
    : "—";
}

function formatMoney(value: number, currency: string) {
  try {
    return new Intl.NumberFormat("tr-TR", {
      style: "currency",
      currency,
      maximumFractionDigits: 2,
    }).format(value);
  } catch {
    return `${value.toLocaleString("tr-TR", {
      maximumFractionDigits: 2,
    })} ${currency}`;
  }
}

function formatQuantity(value: number, unit: string) {
  return `${value.toLocaleString("tr-TR", {
    maximumFractionDigits: 4,
  })} ${unit}`;
}

export default function ProcurementReportsPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [dashboard, setDashboard] = useState<ProcurementDashboard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const projectOptions = useMemo(
    () =>
      projects
        .filter((project) => !companyId || project.companyId === companyId)
        .map((project) => ({
          value: project.id,
          label: `${project.code} · ${project.name}`,
        })),
    [companyId, projects],
  );

  async function loadDashboard(
    selectedCompanyId = companyId,
    selectedProjectId = projectId,
  ) {
    setLoading(true);
    setError("");

    try {
      setDashboard(
        await procurementDashboardService.getDashboard({
          companyId: selectedCompanyId || undefined,
          projectId: selectedProjectId || undefined,
        }),
      );
    } catch (err) {
      setDashboard(null);
      setError(
        err instanceof Error
          ? err.message
          : "Satın alma raporu alınamadı.",
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    async function initialize() {
      setLoading(true);
      setError("");

      try {
        const [companyItems, projectItems, report] = await Promise.all([
          companyService.getAll(),
          projectService.getAll(),
          procurementDashboardService.getDashboard(),
        ]);

        setCompanies(companyItems);
        setProjects(projectItems);
        setDashboard(report);
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Satın alma raporu yüklenemedi.",
        );
      } finally {
        setLoading(false);
      }
    }

    void initialize();
  }, []);

  function changeCompany(value: string) {
    setCompanyId(value);
    setProjectId("");
  }

  function clearFilters() {
    setCompanyId("");
    setProjectId("");
    void loadDashboard("", "");
  }

  return (
    <ErpShell
      title="Satın Alma Raporları"
      description="Talep, RFQ, sipariş, mal kabul ve stok giriş süreçlerinin yönetici özeti"
    >
      {error ? (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      ) : null}

      <Card className="mb-6">
        <CardHeader>
          <div>
            <h2 className="text-lg font-semibold text-slate-900">
              Rapor Kapsamı
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Yalnız yetkiniz bulunan şirket ve projeler listelenir.
            </p>
          </div>
        </CardHeader>

        <CardContent>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4 xl:items-end">
            <Select
              label="Şirket"
              value={companyId}
              onChange={(event) => changeCompany(event.target.value)}
              placeholder="Tüm yetkili şirketler"
              options={companies.map((company) => ({
                value: company.id,
                label: `${company.code} · ${company.name}`,
              }))}
            />

            <Select
              label="Proje"
              value={projectId}
              onChange={(event) => setProjectId(event.target.value)}
              placeholder="Tüm yetkili projeler"
              options={projectOptions}
            />

            <Button
              type="button"
              loading={loading}
              onClick={() => void loadDashboard()}
            >
              Raporu Getir
            </Button>

            <Button
              type="button"
              variant="secondary"
              disabled={loading}
              onClick={clearFilters}
            >
              Filtreleri Temizle
            </Button>
          </div>
        </CardContent>
      </Card>

      <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <StatCard
          title="Açık Satın Alma Talebi"
          value={loading ? "…" : dashboard?.purchaseRequests.open ?? 0}
          description={`${dashboard?.purchaseRequests.criticalOpen ?? 0} kritik`}
          icon="⌑"
        />
        <StatCard
          title="Devam Eden RFQ"
          value={
            loading
              ? "…"
              : (dashboard?.rfqs.sent ?? 0) +
                (dashboard?.rfqs.responsesReceived ?? 0)
          }
          description={`${dashboard?.rfqs.responseOverdue ?? 0} süresi geçen`}
          icon="≋"
        />
        <StatCard
          title="Açık Sipariş"
          value={loading ? "…" : dashboard?.purchaseOrders.open ?? 0}
          description={`${dashboard?.purchaseOrders.overdueDelivery ?? 0} geciken`}
          icon="▤"
        />
        <StatCard
          title="Taslak Mal Kabul"
          value={loading ? "…" : dashboard?.goodsReceipts.draft ?? 0}
          description={`${dashboard?.goodsReceipts.exceptionLineCount ?? 0} sorunlu kalem`}
          icon="▣"
        />
      </div>

      <div className="mb-6 grid gap-6 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                Sipariş Tutarları
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Her para birimi ayrı raporlanır.
              </p>
            </div>
          </CardHeader>
          <CardContent>
            {!dashboard || dashboard.orderValues.length === 0 ? (
              <EmptyState title="Sipariş tutarı bulunamadı" />
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Para Birimi</TableHead>
                    <TableHead>Toplam</TableHead>
                    <TableHead>Açık</TableHead>
                    <TableHead>Tamamlanan</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {dashboard.orderValues.map((item) => (
                    <TableRow key={item.currency}>
                      <TableCell>
                        <strong>{item.currency}</strong>
                      </TableCell>
                      <TableCell>
                        {formatMoney(item.totalAmount, item.currency)}
                      </TableCell>
                      <TableCell>
                        {formatMoney(item.activeAmount, item.currency)}
                      </TableCell>
                      <TableCell>
                        {formatMoney(item.completedAmount, item.currency)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                Stoklara İşlenen Mal Kabul Miktarları
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Miktarlar birim bazında ayrı gösterilir.
              </p>
            </div>
          </CardHeader>
          <CardContent>
            {!dashboard || dashboard.receiptQuantities.length === 0 ? (
              <EmptyState title="İşlenmiş mal kabul bulunamadı" />
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Birim</TableHead>
                    <TableHead>Kabul</TableHead>
                    <TableHead>Red</TableHead>
                    <TableHead>Hasarlı</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {dashboard.receiptQuantities.map((item) => (
                    <TableRow key={item.unit}>
                      <TableCell>
                        <strong>{item.unit}</strong>
                      </TableCell>
                      <TableCell>
                        {formatQuantity(item.acceptedQuantity, item.unit)}
                      </TableCell>
                      <TableCell>
                        {formatQuantity(item.rejectedQuantity, item.unit)}
                      </TableCell>
                      <TableCell>
                        {formatQuantity(item.damagedQuantity, item.unit)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
      </div>

      <Card className="mb-6">
        <CardHeader>
          <div className="flex items-center justify-between gap-4">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                Aksiyon Gerektiren Konular
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Gecikme, onay ve mal kabul istisnaları
              </p>
            </div>
            <Badge variant={dashboard?.alerts.length ? "warning" : "success"}>
              {dashboard?.alerts.length ?? 0} uyarı
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          {!dashboard || dashboard.alerts.length === 0 ? (
            <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-4 text-sm text-emerald-700">
              Seçili kapsamda aksiyon gerektiren satın alma kaydı bulunmuyor.
            </div>
          ) : (
            <div className="grid gap-3 md:grid-cols-2">
              {dashboard.alerts.map((alert) => (
                <Link
                  key={alert.code}
                  href={alert.href}
                  className="rounded-xl border border-slate-200 p-4 transition hover:border-slate-300 hover:bg-slate-50"
                >
                  <div className="flex items-start justify-between gap-4">
                    <div>
                      <strong className="text-sm text-slate-900">
                        {alert.title}
                      </strong>
                      <p className="mt-1 text-sm text-slate-500">
                        {alert.message}
                      </p>
                    </div>
                    <Badge variant={alertVariant(alert.severity)}>
                      {alert.count}
                    </Badge>
                  </div>
                </Link>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <div className="grid gap-6 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between gap-4">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Son Siparişler
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Seçili kapsamdaki son 10 sipariş
                </p>
              </div>
              <Link className="text-sm font-medium text-blue-700" href="/satin-alma/siparis">
                Tümünü Aç
              </Link>
            </div>
          </CardHeader>
          <CardContent>
            {!dashboard || dashboard.recentPurchaseOrders.length === 0 ? (
              <EmptyState title="Sipariş bulunamadı" />
            ) : (
              <div className="space-y-3">
                {dashboard.recentPurchaseOrders.map((order) => (
                  <Link
                    key={order.id}
                    href={`/satin-alma/siparis/${order.id}`}
                    className="block rounded-xl border border-slate-200 p-4 transition hover:bg-slate-50"
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <strong className="text-sm text-slate-900">
                          {order.orderNumber}
                        </strong>
                        <p className="mt-1 text-sm text-slate-600">
                          {order.supplierTitle}
                        </p>
                        <p className="mt-1 text-xs text-slate-500">
                          {order.projectCode} · {order.projectName} · {formatDate(order.orderDate)}
                        </p>
                      </div>
                      <div className="text-right">
                        <Badge variant={badgeVariant(order.status)}>
                          {orderStatusLabels[order.status] ?? "Bilinmiyor"}
                        </Badge>
                        <p className="mt-2 text-sm font-semibold text-slate-900">
                          {formatMoney(order.grandTotal, order.currency)}
                        </p>
                      </div>
                    </div>
                  </Link>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <div className="flex items-center justify-between gap-4">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Son Mal Kabuller
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Seçili kapsamdaki son 10 mal kabul
                </p>
              </div>
              <Link className="text-sm font-medium text-blue-700" href="/depo-stok/mal-kabul">
                Tümünü Aç
              </Link>
            </div>
          </CardHeader>
          <CardContent>
            {!dashboard || dashboard.recentGoodsReceipts.length === 0 ? (
              <EmptyState title="Mal kabul bulunamadı" />
            ) : (
              <div className="space-y-3">
                {dashboard.recentGoodsReceipts.map((receipt) => (
                  <Link
                    key={receipt.id}
                    href={`/depo-stok/mal-kabul/${receipt.id}`}
                    className="block rounded-xl border border-slate-200 p-4 transition hover:bg-slate-50"
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <strong className="text-sm text-slate-900">
                          {receipt.receiptNumber}
                        </strong>
                        <p className="mt-1 text-sm text-slate-600">
                          {receipt.supplierTitle}
                        </p>
                        <p className="mt-1 text-xs text-slate-500">
                          {receipt.projectCode} · {receipt.warehouseName} · {formatDate(receipt.receiptDate)}
                        </p>
                      </div>
                      <div className="text-right">
                        <Badge variant={badgeVariant(receipt.status)}>
                          {receiptStatusLabels[receipt.status] ?? "Bilinmiyor"}
                        </Badge>
                        <p className="mt-2 text-xs text-slate-500">
                          {receipt.itemCount} kalem
                          {receipt.exceptionLineCount > 0
                            ? ` · ${receipt.exceptionLineCount} sorunlu`
                            : ""}
                        </p>
                      </div>
                    </div>
                  </Link>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      {dashboard ? (
        <p className="mt-5 text-right text-xs text-slate-400">
          Son güncelleme: {new Date(dashboard.generatedAtUtc).toLocaleString("tr-TR")}
        </p>
      ) : null}
    </ErpShell>
  );
}
