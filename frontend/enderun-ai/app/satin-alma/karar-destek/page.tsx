"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { amount, currencyMoney, percent } from "@/lib/format/turkish";
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
  procurementDecisionSupportService,
  type ProcurementDecisionSupport,
  type SupplierPerformance,
} from "@/services/procurement-decision-support.service";
import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

function formatMoney(value: number, currency = "TRY") {
  return currencyMoney(value, currency);
}

function formatPercent(value: number) {
  return percent(value);
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

function scoreVariant(score: number) {
  if (score >= 80) return "success" as const;
  if (score >= 60) return "warning" as const;
  return "danger" as const;
}

function alertVariant(severity: string) {
  if (severity === "danger") return "danger" as const;
  if (severity === "warning") return "warning" as const;
  return "info" as const;
}

function spendText(supplier: SupplierPerformance) {
  if (supplier.spendByCurrency.length === 0) return "—";

  return supplier.spendByCurrency
    .map((item) => formatMoney(item.orderTotal, item.currency))
    .join(" · ");
}

export default function ProcurementDecisionSupportPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [periodDays, setPeriodDays] = useState("365");
  const [report, setReport] =
    useState<ProcurementDecisionSupport | null>(null);
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

  async function loadReport(
    selectedCompanyId = companyId,
    selectedProjectId = projectId,
    selectedPeriodDays = periodDays,
  ) {
    setLoading(true);
    setError("");

    try {
      setReport(
        await procurementDecisionSupportService.getReport({
          companyId: selectedCompanyId || undefined,
          projectId: selectedProjectId || undefined,
          periodDays: Number(selectedPeriodDays),
        }),
      );
    } catch (err) {
      setReport(null);
      setError(
        err instanceof Error
          ? err.message
          : "Karar destek raporu alınamadı.",
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
        const [companyItems, projectItems, decisionReport] =
          await Promise.all([
            companyService.getAll(),
            projectService.getAll(),
            procurementDecisionSupportService.getReport({ periodDays: 365 }),
          ]);

        setCompanies(companyItems);
        setProjects(projectItems);
        setReport(decisionReport);
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Karar destek raporu yüklenemedi.",
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
    setPeriodDays("365");
    void loadReport("", "", "365");
  }

  return (
    <ErpShell
      design="redwood"
      title="Satın Alma Karar Destek"
      description="Tedarikçi performansı, kur normalize teklif karşılaştırması ve yönetici önerileri"
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
              Analiz Kapsamı
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Rapor yalnız yetkili şirket ve proje verilerini kullanır.
            </p>
          </div>
        </CardHeader>
        <CardContent>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5 xl:items-end">
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
            <Select
              label="Dönem"
              value={periodDays}
              onChange={(event) => setPeriodDays(event.target.value)}
              options={[
                { value: "90", label: "Son 90 gün" },
                { value: "180", label: "Son 180 gün" },
                { value: "365", label: "Son 1 yıl" },
                { value: "730", label: "Son 2 yıl" },
              ]}
            />
            <Button
              type="button"
              loading={loading}
              onClick={() => void loadReport()}
            >
              Analizi Getir
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
          title="Ortalama Tedarikçi Puanı"
          value={
            loading
              ? "…"
              : `${report?.summary.averageSupplierScore ?? 0}/100`
          }
          description={`${report?.summary.supplierCount ?? 0} tedarikçi`}
          icon="★"
        />
        <StatCard
          title="Teklif Yanıt Oranı"
          value={
            loading ? "…" : formatPercent(report?.summary.responseRate ?? 0)
          }
          description="RFQ davetlerine dönüş"
          icon="↗"
        />
        <StatCard
          title="Zamanında Teslim"
          value={
            loading
              ? "…"
              : formatPercent(report?.summary.onTimeDeliveryRate ?? 0)
          }
          description="Termin ölçülebilen siparişler"
          icon="◷"
        />
        <StatCard
          title="Kalite Oranı"
          value={
            loading ? "…" : formatPercent(report?.summary.qualityRate ?? 0)
          }
          description="Sorunsuz mal kabul kalemleri"
          icon="✓"
        />
      </div>

      {report?.alerts.length ? (
        <Card className="mb-6">
          <CardHeader>
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                Yönetim Uyarıları
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Aksiyon gerektiren performans ve fiyat sinyalleri
              </p>
            </div>
          </CardHeader>
          <CardContent>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {report.alerts.map((alert) => (
                <Link
                  key={alert.code}
                  href={alert.href}
                  className="rounded-xl border border-slate-200 p-4 transition hover:border-slate-300 hover:bg-slate-50"
                >
                  <div className="flex items-start justify-between gap-3">
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
          </CardContent>
        </Card>
      ) : null}

      <Card className="mb-6">
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                Tedarikçi Performans Sıralaması
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Fiyat %35; yanıt, teslim ve kalite geçmişi %65 ağırlıklıdır.
              </p>
            </div>
            <Badge variant="info">
              {report?.suppliers.length ?? 0} tedarikçi
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          {!report || report.suppliers.length === 0 ? (
            <EmptyState title="Performans verisi bulunamadı" />
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Sıra</TableHead>
                    <TableHead className="min-w-64">Tedarikçi</TableHead>
                    <TableHead>Puan</TableHead>
                    <TableHead>Yanıt</TableHead>
                    <TableHead>Teslim</TableHead>
                    <TableHead>Kalite</TableHead>
                    <TableHead>Sipariş</TableHead>
                    <TableHead className="min-w-48">Hacim</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {report.suppliers.map((supplier, index) => (
                    <TableRow key={supplier.supplierCurrentAccountId}>
                      <TableCell>
                        <strong>{index + 1}</strong>
                      </TableCell>
                      <TableCell>
                        <strong className="text-slate-900">
                          {supplier.supplierTitle}
                        </strong>
                        <span className="mt-1 block text-xs text-slate-500">
                          {supplier.supplierCode} · Son sipariş: {formatDate(supplier.lastOrderDate)}
                        </span>
                      </TableCell>
                      <TableCell>
                        <Badge variant={scoreVariant(supplier.performanceScore)}>
                          {supplier.performanceScore}/100
                        </Badge>
                        <span className="mt-1 block text-xs text-slate-500">
                          Güven: {supplier.confidence}
                        </span>
                      </TableCell>
                      <TableCell>
                        <strong>{formatPercent(supplier.responseRate)}</strong>
                        <span className="mt-1 block text-xs text-slate-500">
                          {supplier.responseCount}/{supplier.invitationCount}
                        </span>
                      </TableCell>
                      <TableCell>
                        <strong>
                          {formatPercent(supplier.onTimeDeliveryRate)}
                        </strong>
                        <span className="mt-1 block text-xs text-slate-500">
                          {supplier.onTimeDeliveryOrderCount}/{supplier.deliveryMeasuredOrderCount}
                        </span>
                      </TableCell>
                      <TableCell>
                        <strong>{formatPercent(supplier.qualityRate)}</strong>
                        <span className="mt-1 block text-xs text-slate-500">
                          {supplier.exceptionLineCount} sorunlu kalem
                        </span>
                      </TableCell>
                      <TableCell>
                        <strong>{supplier.totalOrderCount}</strong>
                        <span className="mt-1 block text-xs text-slate-500">
                          {supplier.overdueOpenOrderCount} geciken
                        </span>
                      </TableCell>
                      <TableCell className="text-sm">
                        {spendText(supplier)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                RFQ Karar Analizleri
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Teklifler kendi kurlarıyla TRY karşılığına çevrilerek karşılaştırılır.
              </p>
            </div>
            <Badge variant="info">
              Toplam fark: {formatMoney(
                report?.summary.comparedOfferSpreadTotalTry ?? 0,
                "TRY",
              )}
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          {!report || report.recentRfqComparisons.length === 0 ? (
            <EmptyState
              title="Karşılaştırılabilir RFQ bulunamadı"
              description="Aynı RFQ için en az iki tedarikçi teklifi gerekir."
            />
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>RFQ</TableHead>
                    <TableHead className="min-w-52">Proje</TableHead>
                    <TableHead>Teklif</TableHead>
                    <TableHead>En Düşük</TableHead>
                    <TableHead>Fiyat Farkı</TableHead>
                    <TableHead className="min-w-56">Önerilen</TableHead>
                    <TableHead>Kazanan</TableHead>
                    <TableHead></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {report.recentRfqComparisons.map((item) => (
                    <TableRow key={item.rfqId}>
                      <TableCell>
                        <strong>{item.rfqNumber}</strong>
                        <span className="mt-1 block text-xs text-slate-500">
                          {formatDate(item.issueDate)}
                        </span>
                      </TableCell>
                      <TableCell>
                        <strong className="text-slate-900">
                          {item.projectCode}
                        </strong>
                        <span className="mt-1 block text-xs text-slate-500">
                          {item.projectName}
                        </span>
                      </TableCell>
                      <TableCell>{item.quotationCount}</TableCell>
                      <TableCell>
                        {formatMoney(
                          item.lowestNormalizedTotal,
                          item.comparisonCurrency,
                        )}
                      </TableCell>
                      <TableCell>
                        <strong className="text-amber-700">
                          {formatMoney(item.offerSpread, item.comparisonCurrency)}
                        </strong>
                      </TableCell>
                      <TableCell>
                        <strong>{item.recommendedSupplierTitle}</strong>
                        <span className="mt-1 block text-xs text-slate-500">
                          {formatMoney(
                            item.recommendedNormalizedTotal,
                            item.comparisonCurrency,
                          )}
                        </span>
                        <Badge variant={scoreVariant(item.recommendedScore)}>
                          {item.recommendedScore}/100
                        </Badge>
                      </TableCell>
                      <TableCell>
                        {item.awardedSupplierTitle ?? "Henüz seçilmedi"}
                      </TableCell>
                      <TableCell>
                        <Link
                          href={`/satin-alma/rfq/${item.rfqId}/karsilastirma`}
                          className="text-sm font-medium text-blue-700 hover:text-blue-900"
                        >
                          İncele
                        </Link>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </CardContent>
      </Card>
    </ErpShell>
  );
}
