"use client";

import Link from "next/link";
import { FormEvent, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  Input,
  Select,
  StatCard,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import { apiClient } from "@/lib/api/api-client";
import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";
import {
  procurementApprovalService,
  type ProcurementApprovalDashboard,
  type ProcurementBudget,
} from "@/services/procurement-approval.service";
import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

type CurrentSession = {
  roles: string[];
  permissions: string[];
};

type PolicyForm = {
  purchasingLimit: string;
  financeLimit: string;
  requireBudget: boolean;
  note: string;
};

type BudgetForm = {
  projectId: string;
  name: string;
  periodStart: string;
  periodEnd: string;
  amountTry: string;
  warningThresholdPercent: string;
  isActive: boolean;
  note: string;
};

const emptyPolicy: PolicyForm = {
  purchasingLimit: "",
  financeLimit: "",
  requireBudget: true,
  note: "",
};

function currentYearBudget(): BudgetForm {
  const year = new Date().getFullYear();
  return {
    projectId: "",
    name: `${year} Satın Alma Bütçesi`,
    periodStart: `${year}-01-01`,
    periodEnd: `${year}-12-31`,
    amountTry: "",
    warningThresholdPercent: "80",
    isActive: true,
    note: "",
  };
}

function dateInput(value: string) {
  return value ? value.slice(0, 10) : "";
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

function formatDateTime(value?: string | null) {
  return value ? new Date(value).toLocaleString("tr-TR") : "—";
}

function formatTry(value?: number | null) {
  return money(value ?? 0);
}

function hasPermission(session: CurrentSession | null, permission: string) {
  return Boolean(
    session?.roles.some((role) => role.toLocaleLowerCase("tr-TR") === "admin") ||
      session?.permissions.includes(permission),
  );
}

export default function ProcurementBudgetApprovalPage() {
  const [session, setSession] = useState<CurrentSession | null>(null);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [dashboard, setDashboard] =
    useState<ProcurementApprovalDashboard | null>(null);
  const [policyForm, setPolicyForm] = useState<PolicyForm>(emptyPolicy);
  const [budgetForm, setBudgetForm] = useState<BudgetForm>(currentYearBudget);
  const [editingBudgetId, setEditingBudgetId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [savingPolicy, setSavingPolicy] = useState(false);
  const [savingBudget, setSavingBudget] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const activeCompanyProjects = useMemo(
    () =>
      projects.filter(
        (project) => project.companyId === companyId && project.status === 2,
      ),
    [companyId, projects],
  );

  const canConfigurePolicy = hasPermission(session, "system.users.manage");
  const canManageBudget =
    hasPermission(session, "purchasing.approve") ||
    hasPermission(session, "finance.approve");

  async function loadDashboard(selectedCompanyId: string, selectedProjectId = "") {
    if (!selectedCompanyId) {
      setDashboard(null);
      return;
    }

    setLoading(true);
    setError("");
    try {
      const result = await procurementApprovalService.getDashboard(
        selectedCompanyId,
        selectedProjectId || undefined,
      );
      setDashboard(result);
      setPolicyForm(
        result.policy
          ? {
              purchasingLimit: String(result.policy.purchasingApprovalLimitTry),
              financeLimit: String(result.policy.financeApprovalLimitTry),
              requireBudget: result.policy.requireBudget,
              note: result.policy.note ?? "",
            }
          : emptyPolicy,
      );
    } catch (err) {
      setDashboard(null);
      setError(
        err instanceof Error
          ? err.message
          : "Bütçe ve onay merkezi yüklenemedi.",
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
        const [companyItems, projectItems, currentSession] = await Promise.all([
          companyService.getAll(),
          projectService.getAll(),
          apiClient<CurrentSession>("auth/me"),
        ]);
        setCompanies(companyItems);
        setProjects(projectItems);
        setSession(currentSession);

        const initialCompanyId = companyItems[0]?.id ?? "";
        setCompanyId(initialCompanyId);
        if (initialCompanyId) {
          const result = await procurementApprovalService.getDashboard(initialCompanyId);
          setDashboard(result);
          setPolicyForm(
            result.policy
              ? {
                  purchasingLimit: String(
                    result.policy.purchasingApprovalLimitTry,
                  ),
                  financeLimit: String(result.policy.financeApprovalLimitTry),
                  requireBudget: result.policy.requireBudget,
                  note: result.policy.note ?? "",
                }
              : emptyPolicy,
          );
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Bütçe ve onay merkezi yüklenemedi.",
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
    setEditingBudgetId(null);
    setBudgetForm(currentYearBudget());
    void loadDashboard(value, "");
  }

  function changeProject(value: string) {
    setProjectId(value);
    setBudgetForm((current) => ({ ...current, projectId: value }));
    void loadDashboard(companyId, value);
  }

  async function savePolicy(event: FormEvent) {
    event.preventDefault();
    if (!companyId) return;

    const purchasingLimit = Number(policyForm.purchasingLimit);
    const financeLimit = Number(policyForm.financeLimit);
    if (!Number.isFinite(purchasingLimit) || !Number.isFinite(financeLimit)) {
      setError("Onay limitlerini sayısal olarak girin.");
      return;
    }

    setSavingPolicy(true);
    setError("");
    setSuccess("");
    try {
      await procurementApprovalService.configurePolicy(companyId, {
        purchasingApprovalLimitTry: purchasingLimit,
        financeApprovalLimitTry: financeLimit,
        requireBudget: policyForm.requireBudget,
        note: policyForm.note || null,
      });
      setSuccess("Şirket onay politikası kaydedildi.");
      await loadDashboard(companyId, projectId);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Onay politikası kaydedilemedi.");
    } finally {
      setSavingPolicy(false);
    }
  }

  async function saveBudget(event: FormEvent) {
    event.preventDefault();
    const selectedProjectId = budgetForm.projectId || projectId;
    if (!selectedProjectId) {
      setError("Bütçe için proje seçin.");
      return;
    }

    const amountTry = Number(budgetForm.amountTry);
    const threshold = Number(budgetForm.warningThresholdPercent);
    if (!Number.isFinite(amountTry) || !Number.isFinite(threshold)) {
      setError("Bütçe tutarı ve uyarı eşiğini sayısal olarak girin.");
      return;
    }

    setSavingBudget(true);
    setError("");
    setSuccess("");
    try {
      const payload = {
        name: budgetForm.name,
        periodStart: budgetForm.periodStart,
        periodEnd: budgetForm.periodEnd,
        amountTry,
        warningThresholdPercent: threshold,
        isActive: budgetForm.isActive,
        note: budgetForm.note || null,
      };
      if (editingBudgetId) {
        await procurementApprovalService.updateBudget(
          selectedProjectId,
          editingBudgetId,
          payload,
        );
      } else {
        await procurementApprovalService.createBudget(selectedProjectId, payload);
      }
      setSuccess(editingBudgetId ? "Proje bütçesi güncellendi." : "Proje bütçesi oluşturuldu.");
      setEditingBudgetId(null);
      setBudgetForm({ ...currentYearBudget(), projectId: selectedProjectId });
      await loadDashboard(companyId, projectId);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Proje bütçesi kaydedilemedi.");
    } finally {
      setSavingBudget(false);
    }
  }

  function editBudget(budget: ProcurementBudget) {
    setEditingBudgetId(budget.budgetId);
    setBudgetForm({
      projectId: budget.projectId,
      name: budget.name,
      periodStart: dateInput(budget.periodStart),
      periodEnd: dateInput(budget.periodEnd),
      amountTry: String(budget.amountTry),
      warningThresholdPercent: String(budget.warningThresholdPercent),
      isActive: budget.isActive,
      note: budget.note ?? "",
    });
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  return (
    <ErpShell
      design="redwood"
      title="Bütçe ve Onay Merkezi"
      description="Proje satın alma bütçeleri, yetki limitleri ve çok kademeli sipariş onayları"
    >
      {error ? (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      ) : null}
      {success ? (
        <div className="mb-5 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          {success}
        </div>
      ) : null}

      <Card className="mb-6">
        <CardHeader>
          <div>
            <h2 className="text-lg font-semibold text-slate-900">Çalışma Kapsamı</h2>
            <p className="mt-1 text-sm text-slate-500">
              Yalnız veri kapsamınız içindeki şirket ve projeler gösterilir.
            </p>
          </div>
        </CardHeader>
        <CardContent>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4 xl:items-end">
            <Select
              label="Şirket"
              value={companyId}
              onChange={(event) => changeCompany(event.target.value)}
              placeholder="Şirket seçin"
              options={companies.map((company) => ({
                value: company.id,
                label: `${company.code} · ${company.name}`,
              }))}
            />
            <Select
              label="Proje filtresi"
              value={projectId}
              onChange={(event) => changeProject(event.target.value)}
              placeholder="Tüm yetkili projeler"
              options={activeCompanyProjects.map((project) => ({
                value: project.id,
                label: `${project.code} · ${project.name}`,
              }))}
            />
            <Button
              variant="secondary"
              loading={loading}
              onClick={() => void loadDashboard(companyId, projectId)}
            >
              Yenile
            </Button>
          </div>
        </CardContent>
      </Card>

      {dashboard ? (
        <>
          {dashboard.warnings.map((warning) => (
            <div
              key={warning}
              className="mb-3 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
            >
              {warning}
            </div>
          ))}

          <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <StatCard title="Onay Bekleyen" value={dashboard.pendingApprovalCount} icon="⌛" />
            <StatCard
              title="İşlem Yapabileceklerim"
              value={dashboard.approvalsCurrentUserCanActOn}
              icon="✓"
            />
            <StatCard
              title="Bekleyen Tutar"
              value={formatTry(dashboard.pendingApprovalAmountTry)}
              icon="₺"
            />
            <StatCard title="Bütçe Uyarısı" value={dashboard.budgetWarningCount} icon="!" />
          </div>

          <div className="mb-6 grid gap-6 xl:grid-cols-2">
            <Card>
              <CardHeader>
                <div>
                  <h2 className="text-lg font-semibold text-slate-900">Şirket Onay Politikası</h2>
                  <p className="mt-1 text-sm text-slate-500">
                    Limitler TRY karşılığı üzerinden değerlendirilir.
                  </p>
                </div>
                {dashboard.policy ? <Badge variant="success">Yapılandırıldı</Badge> : <Badge variant="warning">Eksik</Badge>}
              </CardHeader>
              <CardContent>
                {canConfigurePolicy ? (
                  <form className="space-y-4" onSubmit={savePolicy}>
                    <Input
                      label="Satın alma onay limiti (TRY)"
                      type="number"
                      min="0.01"
                      step="0.01"
                      value={policyForm.purchasingLimit}
                      onChange={(event) =>
                        setPolicyForm((current) => ({
                          ...current,
                          purchasingLimit: event.target.value,
                        }))
                      }
                      required
                    />
                    <Input
                      label="Finans onay limiti (TRY)"
                      type="number"
                      min="0.01"
                      step="0.01"
                      value={policyForm.financeLimit}
                      onChange={(event) =>
                        setPolicyForm((current) => ({
                          ...current,
                          financeLimit: event.target.value,
                        }))
                      }
                      required
                    />
                    <label className="flex items-center gap-2 text-sm text-slate-700">
                      <input
                        type="checkbox"
                        checked={policyForm.requireBudget}
                        onChange={(event) =>
                          setPolicyForm((current) => ({
                            ...current,
                            requireBudget: event.target.checked,
                          }))
                        }
                      />
                      Aktif proje bütçesi olmadan onaya göndermeyi engelle
                    </label>
                    <label className="block text-sm font-medium text-slate-700">
                      Politika notu
                      <textarea
                        className="mt-1.5 min-h-20 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
                        value={policyForm.note}
                        maxLength={1000}
                        onChange={(event) =>
                          setPolicyForm((current) => ({ ...current, note: event.target.value }))
                        }
                      />
                    </label>
                    <Button type="submit" loading={savingPolicy}>Politikayı Kaydet</Button>
                  </form>
                ) : dashboard.policy ? (
                  <dl className="space-y-4 text-sm">
                    <div>
                      <dt className="text-slate-500">Satın alma limiti</dt>
                      <dd className="font-semibold text-slate-900">{formatTry(dashboard.policy.purchasingApprovalLimitTry)}</dd>
                    </div>
                    <div>
                      <dt className="text-slate-500">Finans limiti</dt>
                      <dd className="font-semibold text-slate-900">{formatTry(dashboard.policy.financeApprovalLimitTry)}</dd>
                    </div>
                    <div>
                      <dt className="text-slate-500">Bütçe zorunluluğu</dt>
                      <dd>{dashboard.policy.requireBudget ? "Aktif" : "Kapalı"}</dd>
                    </div>
                  </dl>
                ) : (
                  <EmptyState title="Onay politikası yok" description="Sistem yöneticisi şirket limitlerini tanımlamalıdır." />
                )}
                {dashboard.policy ? (
                  <p className="mt-4 text-xs text-slate-500">
                    Son güncelleme: {formatDateTime(dashboard.policy.updatedAtUtc)} · {dashboard.policy.updatedBy || "Sistem"}
                  </p>
                ) : null}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <div>
                  <h2 className="text-lg font-semibold text-slate-900">Proje Bütçesi</h2>
                  <p className="mt-1 text-sm text-slate-500">
                    Yalnız Projeler modülünde sizin açtığınız aktif projeler için
                    dönemsel satın alma bütçesi tanımlanır.
                  </p>
                </div>
              </CardHeader>
              <CardContent>
                {canManageBudget ? (
                  <form className="space-y-4" onSubmit={saveBudget}>
                    <Select
                      label="Proje"
                      value={budgetForm.projectId}
                      onChange={(event) =>
                        setBudgetForm((current) => ({ ...current, projectId: event.target.value }))
                      }
                      placeholder="Proje seçin"
                      options={activeCompanyProjects.map((project) => ({
                        value: project.id,
                        label: `${project.code} · ${project.name}`,
                      }))}
                      required
                    />
                    {activeCompanyProjects.length === 0 ? (
                      <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                        Bu şirkette bütçe tanımlanabilecek aktif proje yok. Önce{" "}
                        <Link
                          href="/projeler"
                          className="font-semibold underline underline-offset-2"
                        >
                          Projeler
                        </Link>{" "}
                        ekranından proje açın.
                      </div>
                    ) : null}
                    <Input
                      label="Bütçe adı"
                      value={budgetForm.name}
                      maxLength={200}
                      onChange={(event) => setBudgetForm((current) => ({ ...current, name: event.target.value }))}
                      required
                    />
                    <div className="grid gap-4 md:grid-cols-2">
                      <Input
                        label="Başlangıç"
                        type="date"
                        value={budgetForm.periodStart}
                        onChange={(event) => setBudgetForm((current) => ({ ...current, periodStart: event.target.value }))}
                        required
                      />
                      <Input
                        label="Bitiş"
                        type="date"
                        value={budgetForm.periodEnd}
                        onChange={(event) => setBudgetForm((current) => ({ ...current, periodEnd: event.target.value }))}
                        required
                      />
                    </div>
                    <div className="grid gap-4 md:grid-cols-2">
                      <Input
                        label="Bütçe tutarı (TRY)"
                        type="number"
                        min="0.01"
                        step="0.01"
                        value={budgetForm.amountTry}
                        onChange={(event) => setBudgetForm((current) => ({ ...current, amountTry: event.target.value }))}
                        required
                      />
                      <Input
                        label="Uyarı eşiği (%)"
                        type="number"
                        min="0.01"
                        max="100"
                        step="0.01"
                        value={budgetForm.warningThresholdPercent}
                        onChange={(event) => setBudgetForm((current) => ({ ...current, warningThresholdPercent: event.target.value }))}
                        required
                      />
                    </div>
                    <label className="flex items-center gap-2 text-sm text-slate-700">
                      <input
                        type="checkbox"
                        checked={budgetForm.isActive}
                        onChange={(event) => setBudgetForm((current) => ({ ...current, isActive: event.target.checked }))}
                      />
                      Bütçe aktif
                    </label>
                    <div className="flex flex-wrap gap-2">
                      <Button type="submit" loading={savingBudget}>
                        {editingBudgetId ? "Bütçeyi Güncelle" : "Bütçe Oluştur"}
                      </Button>
                      {editingBudgetId ? (
                        <Button
                          variant="secondary"
                          onClick={() => {
                            setEditingBudgetId(null);
                            setBudgetForm({ ...currentYearBudget(), projectId });
                          }}
                        >
                          Vazgeç
                        </Button>
                      ) : null}
                    </div>
                  </form>
                ) : (
                  <EmptyState title="Bütçe düzenleme yetkisi yok" description="Satın alma veya finans onay yetkisi gereklidir." />
                )}
              </CardContent>
            </Card>
          </div>

          <Card className="mb-6">
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Proje Bütçeleri</h2>
                <p className="mt-1 text-sm text-slate-500">Taahhüt; onay bekleyen, onaylı ve teslimata başlamış siparişlerin TRY karşılığıdır.</p>
              </div>
            </CardHeader>
            <CardContent>
              {dashboard.budgets.length === 0 ? (
                <EmptyState title="Bütçe bulunamadı" description="Seçili kapsam için proje bütçesi oluşturun." />
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Proje / Bütçe</TableHead>
                      <TableHead>Dönem</TableHead>
                      <TableHead>Tutar</TableHead>
                      <TableHead>Taahhüt</TableHead>
                      <TableHead>Kalan</TableHead>
                      <TableHead>Kullanım</TableHead>
                      <TableHead>Durum</TableHead>
                      <TableHead></TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {dashboard.budgets.map((budget) => (
                      <TableRow key={budget.budgetId}>
                        <TableCell>
                          <strong>{budget.projectCode} · {budget.name}</strong>
                          <div className="mt-1 text-xs text-slate-500">{budget.projectName}</div>
                        </TableCell>
                        <TableCell>{formatDate(budget.periodStart)} – {formatDate(budget.periodEnd)}</TableCell>
                        <TableCell>{formatTry(budget.amountTry)}</TableCell>
                        <TableCell>{formatTry(budget.committedAmountTry)}</TableCell>
                        <TableCell className={budget.remainingAmountTry < 0 ? "text-red-700" : ""}>{formatTry(budget.remainingAmountTry)}</TableCell>
                        <TableCell>%{budget.utilizationPercent.toLocaleString("tr-TR")}</TableCell>
                        <TableCell>
                          <Badge variant={budget.isExceeded ? "danger" : budget.isWarning ? "warning" : budget.isActive ? "success" : "default"}>
                            {budget.isExceeded ? "Aşıldı" : budget.isWarning ? "Uyarı" : budget.isActive ? "Aktif" : "Pasif"}
                          </Badge>
                        </TableCell>
                        <TableCell>
                          {canManageBudget ? <Button size="sm" variant="secondary" onClick={() => editBudget(budget)}>Düzenle</Button> : null}
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
                <h2 className="text-lg font-semibold text-slate-900">Sipariş Onay Kuyruğu</h2>
                <p className="mt-1 text-sm text-slate-500">Tutar limitine göre sıradaki yetkili adım gösterilir.</p>
              </div>
            </CardHeader>
            <CardContent>
              {dashboard.pendingApprovals.length === 0 ? (
                <EmptyState title="Bekleyen onay yok" description="Seçili kapsamda açık sipariş onayı bulunmuyor." />
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Sipariş</TableHead>
                      <TableHead>Proje / Tedarikçi</TableHead>
                      <TableHead>Tarih</TableHead>
                      <TableHead>TRY Karşılığı</TableHead>
                      <TableHead>Onay Adımı</TableHead>
                      <TableHead>Bütçe</TableHead>
                      <TableHead></TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {dashboard.pendingApprovals.map((approval) => (
                      <TableRow key={approval.purchaseOrderId}>
                        <TableCell><strong>{approval.orderNumber}</strong></TableCell>
                        <TableCell>
                          <strong>{approval.projectCode} · {approval.projectName}</strong>
                          <div className="mt-1 text-xs text-slate-500">{approval.supplierTitle}</div>
                        </TableCell>
                        <TableCell>{formatDate(approval.orderDate)}</TableCell>
                        <TableCell>{formatTry(approval.orderAmountTry)}</TableCell>
                        <TableCell>
                          <Badge variant={approval.canCurrentUserApprove ? "info" : "warning"}>
                            {approval.currentStageSequence}. {approval.currentStageName}
                          </Badge>
                          <div className="mt-1 text-xs text-slate-500">{approval.requiredAuthority}</div>
                        </TableCell>
                        <TableCell>
                          <Badge variant={approval.budgetWarning ? "warning" : "success"}>
                            {approval.budgetWarning ? "Kontrol gerekli" : "Uygun"}
                          </Badge>
                          {approval.budgetRemainingAfterOrderTry != null ? (
                            <div className="mt-1 text-xs text-slate-500">Kalan {formatTry(approval.budgetRemainingAfterOrderTry)}</div>
                          ) : null}
                        </TableCell>
                        <TableCell>
                          <Link
                            href={`/satin-alma/siparis/${approval.purchaseOrderId}`}
                            className="text-sm font-medium text-blue-700 hover:text-blue-900"
                          >
                            Siparişi Aç
                          </Link>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>
        </>
      ) : !loading ? (
        <EmptyState title="Şirket seçin" description="Bütçe ve onay verilerini görmek için bir şirket seçin." />
      ) : null}
    </ErpShell>
  );
}
