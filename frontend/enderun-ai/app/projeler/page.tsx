"use client";

import Link from "next/link";
import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { companyService, CompanyListItem } from "@/services/company.service";
import { branchService, BranchListItem } from "@/services/branch.service";
import {
  currentAccountService,
  CurrentAccountListItem,
} from "@/services/current-account.service";
import {
  projectService,
  ProjectListItem,
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
  plannedStartDate: "",
  plannedEndDate: "",
  city: "",
  district: "",
  address: "",
};

export default function ProjectsPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [accounts, setAccounts] = useState<CurrentAccountListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [form, setForm] = useState(initialForm);
  const [showForm, setShowForm] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const loadData = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [companyData, branchData, accountData, projectData] =
        await Promise.all([
          companyService.getAll(),
          branchService.getAll(),
          currentAccountService.getAll(),
          projectService.getAll(),
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
  }, []);

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

  async function createProject(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setMessage("");
    setError("");

    try {
      const result = await projectService.create({
        companyId: form.companyId,
        branchId: form.branchId,
        employerCurrentAccountId: form.employerCurrentAccountId,
        code: form.code.trim().toUpperCase(),
        name: form.name.trim(),
        contractNumber: form.contractNumber || null,
        contractDate: form.contractDate || null,
        contractAmount: form.contractAmount
          ? Number(form.contractAmount)
          : null,
        currencyCode: form.currencyCode,
        vatRate: Number(form.vatRate),
        withholdingRate: form.withholdingRate || null,
        plannedStartDate: form.plannedStartDate || null,
        plannedEndDate: form.plannedEndDate || null,
        city: form.city || null,
        district: form.district || null,
        address: form.address || null,
      });

      const response = result as { message?: string };
      setMessage(
        response.message ?? "Proje ve şantiye deposu oluşturuldu."
      );
      setShowForm(false);
      setForm({
        ...initialForm,
        companyId: form.companyId,
      });
      await loadData();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Proje oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      title="Projeler"
      description="Onaylı cari ve şube üzerinden proje açın"
    >
      <div className="erp-page-toolbar">
        <div>
          <strong>{projects.length} proje</strong>
          <span> kayıtlı</span>
        </div>

        <button
          type="button"
          className="erp-primary-button"
          onClick={() => setShowForm((value) => !value)}
        >
          {showForm ? "Formu Kapat" : "+ Yeni Proje"}
        </button>
      </div>

      {message && <div className="erp-alert success">{message}</div>}
      {error && <div className="erp-alert error">{error}</div>}

      {showForm && (
        <form className="erp-form-card" onSubmit={createProject}>
          <div className="erp-form-header">
            <h2>Yeni Proje</h2>
            <p>
              İşveren yalnızca onaylanmış müşteri cari kartından seçilebilir.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Şirket *</span>
              <select
                required
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

            <label className="span-2">
              <span>İşveren Cari Kartı *</span>
              <select
                required
                value={form.employerCurrentAccountId}
                onChange={(event) =>
                  setForm({
                    ...form,
                    employerCurrentAccountId: event.target.value,
                  })
                }
              >
                <option value="">Onaylı müşteri seçin</option>
                {approvedCustomers.map((account) => (
                  <option key={account.id} value={account.id}>
                    {account.code} — {account.title}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Proje Kodu *</span>
              <input
                required
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

          <div className="erp-form-actions">
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => setShowForm(false)}
            >
              Vazgeç
            </button>

            <button
              type="submit"
              className="erp-primary-button"
              disabled={
                saving ||
                !form.companyId ||
                !form.branchId ||
                !form.employerCurrentAccountId
              }
            >
              {saving
                ? "Oluşturuluyor..."
                : "Projeyi ve Depoyu Oluştur"}
            </button>
          </div>
        </form>
      )}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Proje Listesi</h2>
        </div>

        {loading ? (
          <div className="erp-loading">Projeler yükleniyor...</div>
        ) : projects.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Henüz proje bulunmuyor</strong>
            <p>Onaylı müşteri cari kartı üzerinden ilk projeyi açın.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Kod</th>
                  <th>Proje</th>
                  <th>İşveren</th>
                  <th>Şube</th>
                  <th>Sözleşme</th>
                  <th>Depo</th>
                  <th>İşlem</th>
                </tr>
              </thead>
              <tbody>
                {projects.map((project) => (
                  <tr key={project.id}>
                    <td>
                      <strong>{project.code}</strong>
                    </td>
                    <td>
                      <strong>{project.name}</strong>
                      <small>{project.companyName}</small>
                    </td>
                    <td>{project.employerName}</td>
                    <td>{project.branchName}</td>
                    <td>
                      <span>
                        {project.contractAmount?.toLocaleString("tr-TR") ??
                          "—"}{" "}
                        {project.currencyCode}
                      </span>
                      <small>{project.contractNumber || "—"}</small>
                    </td>
                    <td>
                      <span className="erp-status blue">
                        {project.warehouseCount} depo
                      </span>
                    </td>
                    <td>
                      <Link
                        className="erp-row-link"
                        href={`/projeler/${project.id}`}
                      >
                        Proje Merkezini Aç →
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </ErpShell>
  );
}
