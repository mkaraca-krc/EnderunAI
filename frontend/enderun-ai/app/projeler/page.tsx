"use client";

import Link from "next/link";
import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { useModuleActions } from "@/lib/auth/module-actions";
import { Button, Drawer } from "@/components/ui";
import { amount } from "@/lib/format/turkish";
import { matchesSearch } from "@/lib/search/fold";
import { companyService, CompanyListItem } from "@/services/company.service";
import { branchService, BranchListItem } from "@/services/branch.service";
import {
  currentAccountService,
  CurrentAccountListItem,
} from "@/services/current-account.service";
import {
  projectService,
  ProjectListItem,
  ProjectStatus,
  PROJECT_STATUS_LABELS,
  PROJECT_STATUS_BADGE_COLOR,
} from "@/services/project.service";

const initialForm = {
  companyId: "",
  branchId: "",
  employerCurrentAccountId: "",
  code: "",
  name: "",
  contractNumber: "",
  contractDate: "",
  contractAmount: "",
  currencyCode: "TRY",
  vatRate: "20",
  withholdingRate: "",
  increaseRate: "0",
  cashRetentionRate: "0",
  withholdingTaxRate: "0",
  materialDeductionRate: "0",
  plannedStartDate: "",
  plannedEndDate: "",
  city: "",
  district: "",
  address: "",
  status: String(ProjectStatus.Kesif),
};

export default function ProjectsPage() {
  /**
   * Düğme -> uç -> izin (ProjectsController):
   *   POST projects      -> projects.create
   *   PUT  projects/{id} -> projects.edit
   *
   * ROTA KAPISI BURADA YETMİYOR: "/projeler" yalnız projects.view
   * istiyor, çünkü ekranın asıl işi listeyi GÖSTERMEK. Proje açmak ve
   * düzenlemek ayrı yetkiler.
   *
   * "Kaydet" AYNI DÜĞME İKİ AYRI UÇ: düzenlemede PUT, yenide POST.
   */
  const actions = useModuleActions("projects");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [accounts, setAccounts] = useState<CurrentAccountListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [form, setForm] = useState(initialForm);
  const [showForm, setShowForm] = useState(false);
  const [editingProjectId, setEditingProjectId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [includeArchived, setIncludeArchived] = useState(false);
  const [search, setSearch] = useState("");

  /**
   * ARAMA EKLENDİ: bu listede yalnızca statü süzgeci vardı. Proje adı
   * ya da kodu bilinen bir kaydı bulmanın yolu, listeyi gözle taramaktı.
   * Kod, ad, işveren, şube ve sözleşme no birlikte aranıyor.
   */
  /*
   * SÜTUNLAR VERİ OLARAK (F4f). Eylem sütunu `actions` ve
   * `editProject` üzerine kapandığı için dizi BELLEĞE ALINMIYOR
   * (F4b desen kararı): bayat kapanış, düğmenin yanlış kayıt üzerinde
   * çalışması demek olurdu.
   */
  const projectColumns: DataTableColumn<ProjectListItem>[] = [
    { key: "kod", header: "Kod", value: (row) => row.code },
    {
      key: "proje",
      header: "Proje",
      value: (row) => `${row.name} — ${row.companyName}`,
      render: (row) => (
        <>
          <strong>{row.name}</strong>
          <small>{row.companyName}</small>
        </>
      ),
    },
    {
      key: "statu",
      header: "Statü",
      value: (row) =>
        (PROJECT_STATUS_LABELS[row.status] ?? "Bilinmiyor") +
        (row.isArchived ? " (Arşiv)" : ""),
      render: (row) => (
        <>
          <span
            className={`erp-status ${
              PROJECT_STATUS_BADGE_COLOR[row.status] ?? "gray"
            }`}
          >
            {PROJECT_STATUS_LABELS[row.status] ?? "Bilinmiyor"}
          </span>
          {row.isArchived && (
            <span className="erp-status gray" style={{ marginLeft: 4 }}>
              Arşiv
            </span>
          )}
        </>
      ),
    },
    { key: "isveren", header: "İşveren", value: (row) => row.employerName || "—" },
    { key: "sube", header: "Şube", value: (row) => row.branchName },
    {
      key: "sozlesme",
      header: "Sözleşme",
      numeric: true,
      /*
       * `toLocaleString` ondalık hane sayısını sabitlemiyordu:
       * 1.500.000 ile 1.500.000,5 aynı sütunda yan yana çıkıyordu.
       * Paylaşılan biçimleyici iki hane yazar.
       */
      value: (row) =>
        row.contractAmount === null || row.contractAmount === undefined
          ? "—"
          : `${amount(row.contractAmount)} ${row.currencyCode}`,
      render: (row) => (
        <>
          <span>
            {row.contractAmount === null || row.contractAmount === undefined
              ? "—"
              : `${amount(row.contractAmount)} ${row.currencyCode}`}
          </span>
          <small>{row.contractNumber || "—"}</small>
        </>
      ),
    },
    {
      key: "depo",
      header: "Depo",
      value: (row) => `${row.warehouseCount} depo`,
      render: (row) => (
        <span className="erp-status blue">{row.warehouseCount} depo</span>
      ),
    },
    {
      key: "islem",
      header: "İşlem",
      value: () => "",
      render: (row) => (
        <div className="erp-actions">
          {actions.can("edit") && (
            <button type="button" onClick={() => editProject(row.id)}>
              Düzenle
            </button>
          )}

          <Link className="erp-row-link" href={`/projeler/${row.id}`}>
            Proje Merkezini Aç →
          </Link>
        </div>
      ),
    },
  ];

  const visibleProjects = useMemo(
    () =>
      projects.filter((project) => {
        if (statusFilter !== "" && String(project.status) !== statusFilter) {
          return false;
        }

        return matchesSearch(
          search,
          project.code,
          project.name,
          project.employerName,
          project.branchName,
          project.contractNumber,
        );
      }),
    [projects, statusFilter, search]
  );

  const loadData = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [companyData, branchData, accountData, projectData] =
        await Promise.all([
          companyService.getAll(),
          branchService.getAll(),
          currentAccountService.getAll(),
          projectService.getAll(undefined, includeArchived),
        ]);

      setCompanies(companyData);
      setBranches(branchData);
      setAccounts(accountData);
      setProjects(projectData);

      setForm((current) => ({
        ...current,
        companyId: current.companyId || companyData[0]?.id || "",
      }));
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Projeler yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [includeArchived]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const filteredBranches = useMemo(
    () => branches.filter((branch) => branch.companyId === form.companyId),
    [branches, form.companyId]
  );

  const approvedCustomers = useMemo(
    () =>
      accounts.filter(
        (account) =>
          account.companyId === form.companyId &&
          account.status === 2 &&
          (account.roles & 1) === 1
      ),
    [accounts, form.companyId]
  );

  useEffect(() => {
    setForm((current) => ({
      ...current,
      branchId: filteredBranches.some(
        (branch) => branch.id === current.branchId
      )
        ? current.branchId
        : filteredBranches[0]?.id || "",
      employerCurrentAccountId: approvedCustomers.some(
        (account) => account.id === current.employerCurrentAccountId
      )
        ? current.employerCurrentAccountId
        : approvedCustomers[0]?.id || "",
    }));
  }, [filteredBranches, approvedCustomers]);

  /** Paneli kapatır ve formu boşaltır; seçili şirket korunur. */
  function closeForm() {
    setShowForm(false);
    setEditingProjectId(null);
    setForm({ ...initialForm, companyId: form.companyId });
  }

  async function saveProject(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setMessage("");
    setError("");

    try {
      const financialPayload = {
        name: form.name.trim(),
        employerCurrentAccountId: form.employerCurrentAccountId || null,
        status: Number(form.status),
        contractNumber: form.contractNumber || null,
        contractDate: form.contractDate || null,
        contractAmount: form.contractAmount
          ? Number(form.contractAmount)
          : null,
        currencyCode: form.currencyCode,
        vatRate: Number(form.vatRate),
        withholdingRate: form.withholdingRate || null,
        increaseRate: Number(form.increaseRate || 0),
        cashRetentionRate: Number(form.cashRetentionRate || 0),
        withholdingTaxRate: Number(form.withholdingTaxRate || 0),
        materialDeductionRate: Number(form.materialDeductionRate || 0),
        plannedStartDate: form.plannedStartDate || null,
        plannedEndDate: form.plannedEndDate || null,
        city: form.city || null,
        district: form.district || null,
        address: form.address || null,
      };

      const result = editingProjectId
        ? await projectService.update(editingProjectId, financialPayload)
        : await projectService.create({
            companyId: form.companyId,
            branchId: form.branchId,
            code: form.code.trim().toUpperCase(),
            ...financialPayload,
          });

      const response = result as { message?: string };
      setMessage(
        response.message ??
          (editingProjectId
            ? "Proje bilgileri güncellendi."
            : "Proje ve şantiye deposu oluşturuldu.")
      );
      setEditingProjectId(null);
      setShowForm(false);
      setForm({
        ...initialForm,
        companyId: form.companyId,
      });
      await loadData();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Proje kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function editProject(projectId: string) {
    setSaving(true);
    setMessage("");
    setError("");

    try {
      const detail = (await projectService.getById(projectId)) as {
        id: string;
        companyId?: string;
        branchId?: string;
        employerCurrentAccountId?: string;
        code: string;
        name: string;
        contractNumber?: string | null;
        contractDate?: string | null;
        contractAmount?: number | null;
        currencyCode: string;
        vatRate: number;
        withholdingRate?: string | null;
        increaseRate?: number;
        cashRetentionRate?: number;
        withholdingTaxRate?: number;
        materialDeductionRate?: number;
        plannedStartDate?: string | null;
        plannedEndDate?: string | null;
        city?: string | null;
        district?: string | null;
        address?: string | null;
        status?: number;
      };

      const listProject = projects.find((x) => x.id === projectId);

      setEditingProjectId(projectId);
      setForm({
        companyId: detail.companyId || listProject?.companyId || "",
        branchId: detail.branchId || listProject?.branchId || "",
        employerCurrentAccountId:
          detail.employerCurrentAccountId ||
          listProject?.employerCurrentAccountId ||
          "",
        code: detail.code || "",
        name: detail.name || "",
        contractNumber: detail.contractNumber || "",
        contractDate: detail.contractDate
          ? detail.contractDate.slice(0, 10)
          : "",
        contractAmount:
          detail.contractAmount == null
            ? ""
            : String(detail.contractAmount),
        currencyCode: detail.currencyCode || "TRY",
        vatRate: String(detail.vatRate ?? 20),
        withholdingRate: detail.withholdingRate || "",
        increaseRate: String(detail.increaseRate ?? 0),
        cashRetentionRate: String(detail.cashRetentionRate ?? 0),
        withholdingTaxRate: String(detail.withholdingTaxRate ?? 0),
        materialDeductionRate: String(
          detail.materialDeductionRate ?? 0
        ),
        plannedStartDate: detail.plannedStartDate
          ? detail.plannedStartDate.slice(0, 10)
          : "",
        plannedEndDate: detail.plannedEndDate
          ? detail.plannedEndDate.slice(0, 10)
          : "",
        city: detail.city || "",
        district: detail.district || "",
        address: detail.address || "",
        status: String(detail.status ?? listProject?.status ?? ProjectStatus.Kesif),
      });

      setShowForm(true);
      window.scrollTo({ top: 0, behavior: "smooth" });
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Proje bilgileri yüklenemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      title="Projeler"
      description="Onaylı cari ve şube üzerinden proje açın"
      design="redwood"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <div className="erp-actions">
          {/* Proje listesi teklif kazanıldığında ve sözleşme
              açıldığında dışarıdan büyüyor. */}
          <Button variant="secondary" disabled={loading} onClick={() => void loadData()}>Yenile</Button>

          {actions.can("create") && (
            <button
              type="button"
              className="erp-primary-button"
              onClick={() => setShowForm(true)}
            >
              + Yeni Proje
            </button>
          )}
        </div>
      </div>

      {message && <div className="erp-alert success">{message}</div>}
      {error && <div className="erp-alert error">{error}</div>}

      <Drawer
        open={showForm}
        title={editingProjectId ? "Projeyi Düzenle" : "Yeni Proje"}
        description="İşveren yalnızca onaylanmış müşteri cari kartından seçilebilir."
        onClose={closeForm}
        busy={saving}
        size="xl"
        footer={
          <div className="flex justify-end gap-3">
            <Button
              type="button"
              variant="secondary"
              onClick={closeForm}
              disabled={saving}
            >
              Vazgeç
            </Button>

            {(editingProjectId
              ? actions.can("edit")
              : actions.can("create")) && (
              <Button
                type="submit"
                form="proje-formu"
                loading={saving}
                disabled={
                  !form.companyId ||
                  !form.branchId ||
                  (Number(form.status) !== ProjectStatus.Kesif &&
                    !form.employerCurrentAccountId)
                }
              >
                {editingProjectId
                  ? "Değişiklikleri Kaydet"
                  : "Projeyi ve Depoyu Oluştur"}
              </Button>
            )}
          </div>
        }
      >
        <form id="proje-formu" onSubmit={saveProject}>
          <div className="erp-form-grid">
            <label>
              <span>Şirket *</span>
              <select
                required
                disabled={Boolean(editingProjectId)}
                value={form.companyId}
                onChange={(event) =>
                  setForm({ ...form, companyId: event.target.value })
                }
              >
                <option value="">Şirket seçin</option>
                {companies.map((company) => (
                  <option key={company.id} value={company.id}>
                    {company.code} — {company.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Şube *</span>
              <select
                required
                disabled={Boolean(editingProjectId)}
                value={form.branchId}
                onChange={(event) =>
                  setForm({ ...form, branchId: event.target.value })
                }
              >
                <option value="">Şube seçin</option>
                {filteredBranches.map((branch) => (
                  <option key={branch.id} value={branch.id}>
                    {branch.code} — {branch.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Proje Statüsü *</span>
              <select
                required
                value={form.status}
                onChange={(event) =>
                  setForm({ ...form, status: event.target.value })
                }
              >
                {Object.entries(PROJECT_STATUS_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>

            <label className="span-2">
              <span>
                İşveren Cari Kartı
                {Number(form.status) !== ProjectStatus.Kesif ? " *" : ""}
              </span>
              <select
                required={Number(form.status) !== ProjectStatus.Kesif}
                value={form.employerCurrentAccountId}
                onChange={(event) =>
                  setForm({
                    ...form,
                    employerCurrentAccountId: event.target.value,
                  })
                }
              >
                <option value="">
                  {Number(form.status) === ProjectStatus.Kesif
                    ? "Henüz belirlenmedi (opsiyonel)"
                    : "Onaylı müşteri seçin"}
                </option>
                {approvedCustomers.map((account) => (
                  <option key={account.id} value={account.id}>
                    {account.code} — {account.title}
                  </option>
                ))}
              </select>
              {Number(form.status) === ProjectStatus.Kesif && (
                <small>
                  Keşif/Teklif aşamasında işveren henüz kesinleşmemiş
                  olabilir; Aktif&apos;e geçerken zorunlu hale gelir.
                </small>
              )}
            </label>

            <label>
              <span>Proje Kodu *</span>
              <input
                required
                disabled={Boolean(editingProjectId)}
                value={form.code}
                onChange={(event) =>
                  setForm({
                    ...form,
                    code: event.target.value.toUpperCase(),
                  })
                }
              />
            </label>

            <label>
              <span>Proje Adı *</span>
              <input
                required
                value={form.name}
                onChange={(event) =>
                  setForm({ ...form, name: event.target.value })
                }
              />
            </label>

            <label>
              <span>Sözleşme No</span>
              <input
                value={form.contractNumber}
                onChange={(event) =>
                  setForm({ ...form, contractNumber: event.target.value })
                }
              />
            </label>

            <label>
              <span>Sözleşme Tarihi</span>
              <input
                type="date"
                value={form.contractDate}
                onChange={(event) =>
                  setForm({ ...form, contractDate: event.target.value })
                }
              />
            </label>

            <label>
              <span>Sözleşme Bedeli</span>
              <input
                type="number"
                min="0"
                step="0.01"
                value={form.contractAmount}
                onChange={(event) =>
                  setForm({ ...form, contractAmount: event.target.value })
                }
              />
            </label>

            <label>
              <span>Para Birimi</span>
              <select
                value={form.currencyCode}
                onChange={(event) =>
                  setForm({ ...form, currencyCode: event.target.value })
                }
              >
                <option>TRY</option>
                <option>USD</option>
                <option>EUR</option>
                <option>GBP</option>
              </select>
            </label>

            <label>
              <span>KDV Oranı</span>
              <input
                type="number"
                min="0"
                max="100"
                step="0.01"
                value={form.vatRate}
                onChange={(event) =>
                  setForm({ ...form, vatRate: event.target.value })
                }
              />
            </label>

            <label>
              <span>Tevkifat</span>
              <input
                placeholder="4/10"
                value={form.withholdingRate}
                onChange={(event) =>
                  setForm({ ...form, withholdingRate: event.target.value })
                }
              />
            </label>

            <label>
              <span>Artış Yüzdesi</span>
              <input
                type="number"
                min="0"
                max="100"
                step="0.01"
                value={form.increaseRate}
                onChange={(event) =>
                  setForm({ ...form, increaseRate: event.target.value })
                }
              />
            </label>

            <label>
              <span>Nakit Teminat Kesintisi %</span>
              <input
                type="number"
                min="0"
                max="100"
                step="0.01"
                value={form.cashRetentionRate}
                onChange={(event) =>
                  setForm({
                    ...form,
                    cashRetentionRate: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Stopaj Kesintisi %</span>
              <input
                type="number"
                min="0"
                max="100"
                step="0.01"
                value={form.withholdingTaxRate}
                onChange={(event) =>
                  setForm({
                    ...form,
                    withholdingTaxRate: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Malzeme Kesintisi %</span>
              <input
                type="number"
                min="0"
                max="100"
                step="0.01"
                value={form.materialDeductionRate}
                onChange={(event) =>
                  setForm({
                    ...form,
                    materialDeductionRate: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Başlangıç</span>
              <input
                type="date"
                value={form.plannedStartDate}
                onChange={(event) =>
                  setForm({
                    ...form,
                    plannedStartDate: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Bitiş</span>
              <input
                type="date"
                value={form.plannedEndDate}
                onChange={(event) =>
                  setForm({
                    ...form,
                    plannedEndDate: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>İl</span>
              <input
                value={form.city}
                onChange={(event) =>
                  setForm({ ...form, city: event.target.value })
                }
              />
            </label>

            <label>
              <span>İlçe</span>
              <input
                value={form.district}
                onChange={(event) =>
                  setForm({ ...form, district: event.target.value })
                }
              />
            </label>

            <label className="span-2">
              <span>Şantiye Adresi</span>
              <textarea
                rows={3}
                value={form.address}
                onChange={(event) =>
                  setForm({ ...form, address: event.target.value })
                }
              />
            </label>
          </div>

          {approvedCustomers.length === 0 && (
            <div className="erp-alert error">
              Bu şirkette onaylanmış müşteri rolünde cari kart bulunmuyor.
            </div>
          )}
        </form>
      </Drawer>

      <div className="erp-table-card">
        <div className="rw-filters">
          <label className="rw-filter-search">
            <span>Ara</span>
            <input
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Proje kodu, adı, işveren, şube veya sözleşme no"
              aria-label="Proje ara"
            />
          </label>

          <label>
            <span>Statü</span>
            <select
              value={statusFilter}
              onChange={(event) => setStatusFilter(event.target.value)}
              aria-label="Statüye göre süz"
            >
              <option value="">Tümü</option>
              {Object.entries(PROJECT_STATUS_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>

          {/* Arşivli proje aktif listeden düşer; buradan geri getirilebilir. */}
          <label className="rw-check">
            <input
              type="checkbox"
              checked={includeArchived}
              onChange={(event) => setIncludeArchived(event.target.checked)}
            />
            <span>Arşiv dahil</span>
          </label>

          <span className="rw-filter-summary" data-testid="proje-sayisi">
            {visibleProjects.length !== projects.length
              ? `${visibleProjects.length} / ${projects.length} proje`
              : `${projects.length} proje`}
          </span>
        </div>

        {loading ? (
          <div className="erp-loading">Projeler yükleniyor...</div>
        ) : projects.length === 0 ? (
          <div className="erp-empty-state">
            <div className="erp-empty-icon">◈</div>
            <strong>Henüz proje bulunmuyor</strong>
            <p>Onaylı müşteri cari kartı üzerinden ilk projeyi açın.</p>
          </div>
        ) : visibleProjects.length === 0 ? (
          <div className="erp-empty-state">
            <div className="erp-empty-icon">◈</div>
            <strong>Süzgece uyan proje yok</strong>
            <p>Arama metnini kısaltın ya da statü süzgecini temizleyin.</p>
          </div>
        ) : (
          <DataTable
            rows={visibleProjects}
            columns={projectColumns}
            rowKey={(row) => row.id}
            title="Projeler"
            resetKey={`${search}|${statusFilter}|${includeArchived}`}
          />
        )}
      </div>
    </ErpShell>
  );
}
