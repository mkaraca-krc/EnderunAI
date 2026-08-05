"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { branchService, type BranchListItem } from "@/services/branch.service";
import {
  cashAccountService,
  type CashAccount,
} from "@/services/cash-account.service";
import {
  chequeService,
  requiresCashAccount,
  CHEQUE_STATUS_COLORS,
  CHEQUE_STATUS_LABELS,
  ChequeDirection,
  ChequeStatus,
  type ChequeAllocationPayload,
  type ChequeDetail,
  type ChequeListItem,
  type ChequeSummary,
} from "@/services/cheque.service";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import { factoringService, type FactoringCalculation } from "@/services/factoring.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  supplierInvoiceService,
  type SupplierInvoiceListItem,
} from "@/services/supplier-invoice.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const dateFormat = new Intl.DateTimeFormat("tr-TR");

const today = () => new Date().toISOString().slice(0, 10);

const emptyChequeForm = {
  chequeNumber: "",
  bankName: "",
  bankBranch: "",
  drawer: "",
  currentAccountId: "",
  projectId: "",
  costCenterCode: "",
  amount: "",
  issueDate: today(),
  dueDate: today(),
  description: "",
};

/** Dağılım satırı: ya bir faturaya ya da proje/masraf merkezine bağlanır. */
type AllocationRow = {
  amount: string;
  /** Boşsa elle dağılım; doluysa proje/masraf merkezi faturadan gelir. */
  supplierInvoiceId: string;
  projectId: string;
  costCenterCode: string;
};

const emptyAllocationRow: AllocationRow = {
  amount: "",
  supplierInvoiceId: "",
  projectId: "",
  costCenterCode: "",
};

const emptyReplaceForm = {
  chequeNumber: "",
  bankName: "",
  bankBranch: "",
  dueDate: today(),
  movementDate: today(),
  description: "",
};

export default function ChequeRegisterPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const [direction, setDirection] = useState<number>(ChequeDirection.Received);
  const [statusFilter, setStatusFilter] = useState("");
  const [search, setSearch] = useState("");

  const [items, setItems] = useState<ChequeListItem[]>([]);
  const [summary, setSummary] = useState<ChequeSummary | null>(null);
  const [detail, setDetail] = useState<ChequeDetail | null>(null);

  const [currentAccounts, setCurrentAccounts] = useState<CurrentAccountListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [cashAccounts, setCashAccounts] = useState<CashAccount[]>([]);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [showChequeForm, setShowChequeForm] = useState(false);
  const [chequeForm, setChequeForm] = useState(emptyChequeForm);

  const [statusForm, setStatusForm] = useState({
    toStatus: "",
    movementDate: today(),
    cashAccountId: "",
    description: "",
  });

  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [supplierInvoices, setSupplierInvoices] = useState<SupplierInvoiceListItem[]>([]);
  const [allocationRows, setAllocationRows] = useState<AllocationRow[]>([]);

  const [showReplaceForm, setShowReplaceForm] = useState(false);
  const [replaceForm, setReplaceForm] = useState(emptyReplaceForm);

  const [showFactoringForm, setShowFactoringForm] = useState(false);
  const [factoringForm, setFactoringForm] = useState({
    cashAccountId: "",
    factoringCurrentAccountId: "",
    projectId: "",
    transactionDate: today(),
    commissionRate: "",
    commissionAmount: "",
    bsmvRate: "5",
    expenseAmount: "0",
    description: "",
  });
  const [preview, setPreview] = useState<FactoringCalculation | null>(null);

  const loadCompanies = useCallback(async () => {
    try {
      const result = await companyService.getAll();
      setCompanies(result);
      setCompanyId((current) => current || result[0]?.id || "");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
    }
  }, []);

  const loadItems = useCallback(async () => {
    if (!companyId) {
      setItems([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const [list, summaryResult] = await Promise.all([
        chequeService.getAll({
          companyId,
          direction,
          status: statusFilter === "" ? undefined : Number(statusFilter),
          search: search.trim() || undefined,
        }),
        chequeService.getSummary(companyId),
      ]);

      setItems(list);
      setSummary(summaryResult);
    } catch (err) {
      setItems([]);
      setError(err instanceof Error ? err.message : "Çekler alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId, direction, statusFilter, search]);

  const loadLookups = useCallback(async () => {
    if (!companyId) return;

    try {
      const [carilerResult, projectsResult, cashResult, branchResult, invoiceResult] =
        await Promise.all([
          currentAccountService.getAll(companyId),
          projectService.getAll(companyId),
          cashAccountService.getAll({ companyId }),
          branchService.getAll(companyId).catch(() => [] as BranchListItem[]),
          supplierInvoiceService
            .getAll({ companyId })
            .catch(() => [] as SupplierInvoiceListItem[]),
        ]);

      setCurrentAccounts(carilerResult);
      setProjects(projectsResult);
      setCashAccounts(cashResult);
      setBranches(branchResult);
      // Çekin ödediği faturalar buradan seçilir; iptal/ret edilmişler
      // ödenecek borç değildir.
      setSupplierInvoices(
        invoiceResult.filter((invoice) => invoice.status !== 3 && invoice.status !== 4)
      );
    } catch {
      // Yardımcı listeler alınamazsa liste ekranı çalışmaya devam eder.
    }
  }, [companyId]);

  useEffect(() => {
    void loadCompanies();
  }, [loadCompanies]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadItems(), 300);
    return () => window.clearTimeout(timer);
  }, [loadItems]);

  useEffect(() => {
    void loadLookups();
  }, [loadLookups]);

  const statusOptions = useMemo(
    () =>
      direction === ChequeDirection.Received
        ? [
            ChequeStatus.Portfolio,
            ChequeStatus.AtBank,
            ChequeStatus.AtFactoring,
            ChequeStatus.Collected,
            ChequeStatus.Bounced,
          ]
        : [ChequeStatus.Issued, ChequeStatus.Paid, ChequeStatus.Returned],
    [direction]
  );

  const listTotal = useMemo(
    () => items.reduce((sum, item) => sum + item.amount, 0),
    [items]
  );

  /**
   * Masraf merkezi seçenekleri: Merkez (şube kodu) ve projeler. Serbest
   * metin bırakılsaydı aynı şantiye farklı yazımlarla birden çok masraf
   * merkezi gibi görünürdü.
   */
  const costCenterOptions = useMemo(() => {
    const options: { code: string; label: string }[] = [];

    for (const branch of branches) {
      if (branch.costCenterCode) {
        options.push({
          code: branch.costCenterCode,
          label: `${branch.costCenterCode} — ${branch.name}`,
        });
      }
    }

    for (const project of projects) {
      options.push({ code: project.code, label: `${project.code} — ${project.name}` });
    }

    return options;
  }, [branches, projects]);

  /** Çekin ödeyebileceği faturalar: yalnızca seçili carinin faturaları. */
  const allocatableInvoices = useMemo(
    () =>
      supplierInvoices.filter(
        (invoice) =>
          direction === ChequeDirection.Issued &&
          (!chequeForm.currentAccountId ||
            invoice.supplierCurrentAccountId === chequeForm.currentAccountId)
      ),
    [supplierInvoices, direction, chequeForm.currentAccountId]
  );

  const allocationTotal = useMemo(
    () => allocationRows.reduce((sum, row) => sum + (Number(row.amount) || 0), 0),
    [allocationRows]
  );

  const allocationDifference = useMemo(
    () => Math.round(((Number(chequeForm.amount) || 0) - allocationTotal) * 100) / 100,
    [chequeForm.amount, allocationTotal]
  );

  async function openDetail(id: string) {
    setError("");
    setShowFactoringForm(false);
    setShowReplaceForm(false);
    setReplaceForm(emptyReplaceForm);
    setPreview(null);

    try {
      const result = await chequeService.getById(id);
      setDetail(result);
      setStatusForm({
        toStatus: result.allowedNextStatuses[0] !== undefined
          ? String(result.allowedNextStatuses[0])
          : "",
        movementDate: today(),
        cashAccountId: "",
        description: "",
      });
    } catch (err) {
      setDetail(null);
      setError(err instanceof Error ? err.message : "Çek bilgisi alınamadı.");
    }
  }

  async function submitCheque(event: React.FormEvent) {
    event.preventDefault();
    if (!companyId) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const created = await chequeService.create({
        companyId,
        direction,
        chequeNumber: chequeForm.chequeNumber.trim(),
        bankName: chequeForm.bankName.trim(),
        bankBranch: chequeForm.bankBranch.trim() || null,
        drawer: chequeForm.drawer.trim() || null,
        currentAccountId: chequeForm.currentAccountId || null,
        projectId: chequeForm.projectId || null,
        costCenterCode: chequeForm.costCenterCode || null,
        amount: Number(chequeForm.amount),
        currencyCode: "TRY",
        issueDate: chequeForm.issueDate,
        dueDate: chequeForm.dueDate,
        description: chequeForm.description.trim() || null,
        allocations: buildAllocationPayload(),
      });

      setNotice(`${created.internalNumber} kaydedildi ve muhasebe fişi üretildi.`);
      setShowChequeForm(false);
      setChequeForm(emptyChequeForm);
      setAllocationRows([]);
      await loadItems();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Çek kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  /**
   * Dağılım satırlarını API biçimine çevirir. Boş liste "dağılım yok"
   * demektir ve çek tek parça işlenir.
   */
  function buildAllocationPayload(): ChequeAllocationPayload[] | null {
    const rows = allocationRows.filter((row) => Number(row.amount) > 0);

    if (rows.length === 0) return null;

    return rows.map((row) => ({
      amount: Number(row.amount),
      // Fatura seçiliyse proje/masraf merkezi gönderilmez: backend
      // bunları faturadan türetir, iki kaynak olmaz.
      supplierInvoiceId: row.supplierInvoiceId || null,
      projectId: row.supplierInvoiceId ? null : row.projectId || null,
      costCenterCode: row.supplierInvoiceId ? null : row.costCenterCode || null,
    }));
  }

  async function submitReplace(event: React.FormEvent) {
    event.preventDefault();
    if (!detail) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const replacement = await chequeService.replace(detail.id, {
        chequeNumber: replaceForm.chequeNumber.trim(),
        dueDate: replaceForm.dueDate,
        movementDate: replaceForm.movementDate,
        bankName: replaceForm.bankName.trim() || null,
        bankBranch: replaceForm.bankBranch.trim() || null,
        description: replaceForm.description.trim() || null,
      });

      setNotice(
        `${detail.chequeNumber} ertelendi; yerine ${replacement.chequeNumber} ` +
          `düzenlendi (yeni vade ${dateFormat.format(new Date(replacement.dueDate))}).`
      );

      setShowReplaceForm(false);
      setReplaceForm(emptyReplaceForm);
      setDetail(replacement);
      await loadItems();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Çek ertelenemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function submitStatus(event: React.FormEvent) {
    event.preventDefault();
    if (!detail || statusForm.toStatus === "") return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const updated = await chequeService.changeStatus(detail.id, {
        toStatus: Number(statusForm.toStatus),
        movementDate: statusForm.movementDate,
        cashAccountId: statusForm.cashAccountId || null,
        description: statusForm.description.trim() || null,
      });

      setDetail(updated);
      setStatusForm({
        toStatus: updated.allowedNextStatuses[0] !== undefined
          ? String(updated.allowedNextStatuses[0])
          : "",
        movementDate: today(),
        cashAccountId: "",
        description: "",
      });
      setNotice("Çek durumu güncellendi.");
      await loadItems();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Durum güncellenemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function runPreview() {
    if (!detail) return;

    try {
      const result = await factoringService.preview({
        chequeAmount: detail.amount,
        commissionRate: factoringForm.commissionRate
          ? Number(factoringForm.commissionRate)
          : null,
        commissionAmount: factoringForm.commissionAmount
          ? Number(factoringForm.commissionAmount)
          : null,
        bsmvRate: factoringForm.bsmvRate ? Number(factoringForm.bsmvRate) : null,
        expenseAmount: Number(factoringForm.expenseAmount) || 0,
      });

      setPreview(result);
      setError("");
    } catch (err) {
      setPreview(null);
      setError(err instanceof Error ? err.message : "Kesinti hesaplanamadı.");
    }
  }

  async function submitFactoring(event: React.FormEvent) {
    event.preventDefault();
    if (!detail) return;

    setSaving(true);
    setError("");
    setNotice("");

    try {
      const result = await factoringService.create({
        chequeId: detail.id,
        cashAccountId: factoringForm.cashAccountId,
        factoringCurrentAccountId: factoringForm.factoringCurrentAccountId || null,
        projectId: factoringForm.projectId || null,
        transactionDate: factoringForm.transactionDate,
        commissionRate: factoringForm.commissionRate
          ? Number(factoringForm.commissionRate)
          : null,
        commissionAmount: factoringForm.commissionAmount
          ? Number(factoringForm.commissionAmount)
          : null,
        bsmvRate: factoringForm.bsmvRate ? Number(factoringForm.bsmvRate) : null,
        expenseAmount: Number(factoringForm.expenseAmount) || 0,
        description: factoringForm.description.trim() || null,
      });

      setNotice(
        `Çek kırdırıldı (${result.internalNumber}). Net ${money.format(result.netAmount)} ` +
          `banka hesabına girdi, ${money.format(result.totalDeductionAmount)} finansman gideri yazıldı.`
      );
      setShowFactoringForm(false);
      setPreview(null);
      await Promise.all([loadItems(), openDetail(detail.id)]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Çek kırdırılamadı.");
    } finally {
      setSaving(false);
    }
  }

  const canFactor =
    detail !== null &&
    detail.direction === ChequeDirection.Received &&
    detail.status === ChequeStatus.Portfolio;

  const statusNeedsCashAccount =
    detail !== null &&
    statusForm.toStatus !== "" &&
    requiresCashAccount(detail.status, Number(statusForm.toStatus));

  return (
    <ErpShell
      title="Çek Defteri"
      description="Alınan ve verilen çekler, durum geçişleri ve otomatik muhasebe fişleri"
    >
      <div className="erp-page-toolbar">
        <div>
          <strong>{items.length} çek</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            Listelenen toplam: {money.format(listTotal)}
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          <button
            type="button"
            className="erp-primary-button"
            onClick={() => setShowChequeForm((value) => !value)}
          >
            + Yeni {direction === ChequeDirection.Received ? "Alınan" : "Verilen"} Çek
          </button>
        </div>
      </div>

      {summary && (
        <div className="erp-stat-grid" style={{ marginBottom: "16px" }}>
          <div className="erp-stat-card">
            <span>Portföyde</span>
            <strong>{money.format(summary.receivedPortfolioAmount)}</strong>
          </div>
          <div className="erp-stat-card">
            <span>Bankada (tahsilde)</span>
            <strong>{money.format(summary.receivedAtBankAmount)}</strong>
          </div>
          <div className="erp-stat-card">
            <span>Faktoringde</span>
            <strong>{money.format(summary.receivedAtFactoringAmount)}</strong>
          </div>
          <div className="erp-stat-card">
            <span>Verilen (açık)</span>
            <strong>{money.format(summary.issuedOpenAmount)}</strong>
          </div>
          <div className="erp-stat-card">
            <span>Karşılıksız</span>
            <strong>{money.format(summary.receivedBouncedAmount)}</strong>
          </div>
        </div>
      )}

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {showChequeForm && (
        <div className="erp-table-card" style={{ marginBottom: "16px" }}>
          <div className="erp-table-header">
            <h2>
              Yeni {direction === ChequeDirection.Received ? "Alınan" : "Verilen"} Çek
            </h2>
          </div>

          <form onSubmit={submitCheque} style={{ padding: "16px", display: "grid", gap: "12px" }}>
            <div className="erp-form-grid">
              <label>
                Çek no
                <input
                  required
                  value={chequeForm.chequeNumber}
                  onChange={(e) =>
                    setChequeForm({ ...chequeForm, chequeNumber: e.target.value })
                  }
                />
              </label>

              <label>
                Banka
                <input
                  required
                  value={chequeForm.bankName}
                  onChange={(e) => setChequeForm({ ...chequeForm, bankName: e.target.value })}
                />
              </label>

              <label>
                Şube
                <input
                  value={chequeForm.bankBranch}
                  onChange={(e) =>
                    setChequeForm({ ...chequeForm, bankBranch: e.target.value })
                  }
                />
              </label>

              <label>
                Keşideci
                <input
                  value={chequeForm.drawer}
                  onChange={(e) => setChequeForm({ ...chequeForm, drawer: e.target.value })}
                />
              </label>

              <label>
                {direction === ChequeDirection.Received ? "Çeki veren cari" : "Çekin verildiği cari"}
                <select
                  required
                  value={chequeForm.currentAccountId}
                  onChange={(e) =>
                    setChequeForm({ ...chequeForm, currentAccountId: e.target.value })
                  }
                >
                  <option value="">Seçin...</option>
                  {currentAccounts.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.code} — {item.title}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                Proje (opsiyonel)
                <select
                  value={chequeForm.projectId}
                  onChange={(e) => setChequeForm({ ...chequeForm, projectId: e.target.value })}
                >
                  <option value="">—</option>
                  {projects.map((project) => (
                    <option key={project.id} value={project.id}>
                      {project.code} — {project.name}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                Masraf merkezi
                <select
                  value={chequeForm.costCenterCode}
                  onChange={(e) =>
                    setChequeForm({ ...chequeForm, costCenterCode: e.target.value })
                  }
                >
                  <option value="">Proje kodu kullanılsın</option>
                  {costCenterOptions.map((option) => (
                    <option key={option.code} value={option.code}>
                      {option.label}
                    </option>
                  ))}
                </select>
                <small>
                  Ofis kirası gibi projesi olmayan çekler Merkez&apos;e yazılır.
                </small>
              </label>

              <label>
                Tutar
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  required
                  value={chequeForm.amount}
                  onChange={(e) => setChequeForm({ ...chequeForm, amount: e.target.value })}
                />
              </label>

              <label>
                Keşide tarihi
                <input
                  type="date"
                  required
                  value={chequeForm.issueDate}
                  onChange={(e) => setChequeForm({ ...chequeForm, issueDate: e.target.value })}
                />
              </label>

              <label>
                Vade
                <input
                  type="date"
                  required
                  value={chequeForm.dueDate}
                  onChange={(e) => setChequeForm({ ...chequeForm, dueDate: e.target.value })}
                />
              </label>

              <label style={{ gridColumn: "1 / -1" }}>
                Açıklama
                <input
                  value={chequeForm.description}
                  onChange={(e) =>
                    setChequeForm({ ...chequeForm, description: e.target.value })
                  }
                />
              </label>
            </div>

            <div className="erp-form-header" style={{ marginTop: "6px" }}>
              <h3>Dağılım (opsiyonel)</h3>
              <p>
                Tek çek birden fazla projeye/Merkeze bölünebilir. Fatura
                seçilirse proje ve masraf merkezi faturadan gelir — en
                doğrusu budur, çünkü dağılım tahmin değil belgeye dayanır.
                Boş bırakılırsa çek tek parça işlenir.
              </p>
            </div>

            {allocationRows.length > 0 && (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Tutar</th>
                      {direction === ChequeDirection.Issued && <th>Ödenen fatura</th>}
                      <th>Proje</th>
                      <th>Masraf merkezi</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {allocationRows.map((row, index) => {
                      const invoiceSelected = row.supplierInvoiceId !== "";

                      return (
                        <tr key={index}>
                          <td>
                            <input
                              type="number"
                              step="0.01"
                              min="0"
                              value={row.amount}
                              onChange={(e) =>
                                setAllocationRows((current) =>
                                  current.map((item, i) =>
                                    i === index ? { ...item, amount: e.target.value } : item
                                  )
                                )
                              }
                            />
                          </td>

                          {direction === ChequeDirection.Issued && (
                            <td>
                              <select
                                value={row.supplierInvoiceId}
                                onChange={(e) =>
                                  setAllocationRows((current) =>
                                    current.map((item, i) =>
                                      i === index
                                        ? {
                                            ...item,
                                            supplierInvoiceId: e.target.value,
                                            projectId: "",
                                            costCenterCode: "",
                                          }
                                        : item
                                    )
                                  )
                                }
                              >
                                <option value="">Fatura seçilmedi</option>
                                {allocatableInvoices.map((invoice) => (
                                  <option key={invoice.id} value={invoice.id}>
                                    {invoice.invoiceNumber} — {money.format(invoice.grandTotal)}
                                  </option>
                                ))}
                              </select>
                            </td>
                          )}

                          <td>
                            <select
                              disabled={invoiceSelected}
                              value={row.projectId}
                              onChange={(e) =>
                                setAllocationRows((current) =>
                                  current.map((item, i) =>
                                    i === index ? { ...item, projectId: e.target.value } : item
                                  )
                                )
                              }
                            >
                              <option value="">
                                {invoiceSelected ? "Faturadan" : "—"}
                              </option>
                              {projects.map((project) => (
                                <option key={project.id} value={project.id}>
                                  {project.code}
                                </option>
                              ))}
                            </select>
                          </td>

                          <td>
                            <select
                              disabled={invoiceSelected}
                              value={row.costCenterCode}
                              onChange={(e) =>
                                setAllocationRows((current) =>
                                  current.map((item, i) =>
                                    i === index
                                      ? { ...item, costCenterCode: e.target.value }
                                      : item
                                  )
                                )
                              }
                            >
                              <option value="">
                                {invoiceSelected ? "Faturadan" : "—"}
                              </option>
                              {costCenterOptions.map((option) => (
                                <option key={option.code} value={option.code}>
                                  {option.code}
                                </option>
                              ))}
                            </select>
                          </td>

                          <td>
                            <button
                              type="button"
                              className="erp-secondary-button"
                              onClick={() =>
                                setAllocationRows((current) =>
                                  current.filter((_, i) => i !== index)
                                )
                              }
                            >
                              Sil
                            </button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}

            <div
              style={{
                display: "flex",
                gap: "12px",
                alignItems: "center",
                flexWrap: "wrap",
              }}
            >
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() =>
                  setAllocationRows((current) => [...current, { ...emptyAllocationRow }])
                }
              >
                + Dağılım Satırı
              </button>

              {allocationRows.length > 0 && (
                <span>
                  Dağılım toplamı: {money.format(allocationTotal)}
                  {allocationDifference !== 0 && (
                    <strong style={{ color: "#b91c1c" }}>
                      {" "}
                      · {money.format(Math.abs(allocationDifference))}{" "}
                      {allocationDifference > 0 ? "eksik" : "fazla"}
                    </strong>
                  )}
                </span>
              )}
            </div>

            <div style={{ display: "flex", gap: "8px" }}>
              <button type="submit" className="erp-primary-button" disabled={saving}>
                {saving ? "Kaydediliyor..." : "Kaydet"}
              </button>
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => {
                  setShowChequeForm(false);
                  setAllocationRows([]);
                }}
              >
                Vazgeç
              </button>
            </div>
          </form>
        </div>
      )}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Çek Listesi</h2>

          <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
            <select
              value={String(direction)}
              onChange={(e) => {
                setDirection(Number(e.target.value));
                setStatusFilter("");
                setDetail(null);
              }}
            >
              <option value={String(ChequeDirection.Received)}>Alınan çekler</option>
              <option value={String(ChequeDirection.Issued)}>Verilen çekler</option>
            </select>

            <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
              <option value="">Tüm durumlar</option>
              {statusOptions.map((status) => (
                <option key={status} value={String(status)}>
                  {CHEQUE_STATUS_LABELS[status]}
                </option>
              ))}
            </select>

            <input
              type="text"
              placeholder="Çek no / banka / keşideci ara..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
        </div>

        {loading ? (
          <div className="erp-loading">Çekler yükleniyor...</div>
        ) : items.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Çek bulunmuyor</strong>
            <p>Bu filtreye uyan çek kaydı yok.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Çek No</th>
                  <th>Banka</th>
                  <th>Cari</th>
                  <th>Proje</th>
                  <th>Vade</th>
                  <th style={{ textAlign: "right" }}>Tutar</th>
                  <th>Durum</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr
                    key={item.id}
                    onClick={() => void openDetail(item.id)}
                    style={{
                      cursor: "pointer",
                      fontWeight: item.id === detail?.id ? 600 : undefined,
                    }}
                  >
                    <td>
                      <strong>{item.chequeNumber}</strong>
                      <small>{item.internalNumber}</small>
                    </td>
                    <td>
                      {item.bankName}
                      {item.drawer && <small>{item.drawer}</small>}
                    </td>
                    <td>{item.currentAccountTitle ?? "—"}</td>
                    <td>{item.projectCode ?? "—"}</td>
                    <td>
                      {dateFormat.format(new Date(item.dueDate))}
                      <small>
                        {item.isOverdue
                          ? `${Math.abs(item.daysToDue)} gün gecikmiş`
                          : `${item.daysToDue} gün`}
                      </small>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <strong>{money.format(item.amount)}</strong>
                    </td>
                    <td>
                      <span className={`erp-status ${CHEQUE_STATUS_COLORS[item.status] ?? "gray"}`}>
                        {CHEQUE_STATUS_LABELS[item.status] ?? item.statusName}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {detail && (
        <div className="erp-table-card" style={{ marginTop: "16px" }}>
          <div className="erp-table-header">
            <h2>
              {detail.internalNumber} — {detail.chequeNumber} ({money.format(detail.amount)})
            </h2>

            <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
              {detail.allowedNextStatuses.includes(ChequeStatus.Replaced) && (
                <button
                  type="button"
                  className="erp-secondary-button"
                  onClick={() => {
                    setShowReplaceForm((value) => !value);
                    setReplaceForm({
                      ...emptyReplaceForm,
                      bankName: detail.bankName,
                      bankBranch: detail.bankBranch ?? "",
                    });
                  }}
                >
                  Ertele / Değiştir
                </button>
              )}

              {canFactor && (
                <button
                  type="button"
                  className="erp-primary-button"
                  onClick={() => setShowFactoringForm((value) => !value)}
                >
                  Çeki Kırdır (Faktoring)
                </button>
              )}
            </div>
          </div>

          <div style={{ padding: "16px", display: "grid", gap: "16px" }}>
            <div>
              <span className={`erp-status ${CHEQUE_STATUS_COLORS[detail.status] ?? "gray"}`}>
                {CHEQUE_STATUS_LABELS[detail.status] ?? detail.statusName}
              </span>
              <small style={{ display: "block", marginTop: "6px" }}>
                {detail.bankName}
                {detail.bankBranch ? ` / ${detail.bankBranch}` : ""} · Vade:{" "}
                {dateFormat.format(new Date(detail.dueDate))}
                {detail.currentAccountTitle ? ` · ${detail.currentAccountTitle}` : ""}
                {detail.costCenterCode ? ` · Masraf merkezi: ${detail.costCenterCode}` : ""}
              </small>

              {detail.renewalCount > 0 && (
                <div
                  className={detail.renewalCount >= 2 ? "erp-alert warning" : "erp-alert"}
                  style={{ marginTop: "8px" }}
                >
                  Bu çek {detail.renewalCount} kez ertelendi
                  {detail.replacesChequeNumber
                    ? ` (önceki çek: ${detail.replacesChequeNumber})`
                    : ""}
                  .
                  {detail.renewalCount >= 2 &&
                    " Tekrarlayan erteleme tahsilat sorununun habercisi olabilir."}
                </div>
              )}

              {detail.replacedByChequeNumber && (
                <div className="erp-alert" style={{ marginTop: "8px" }}>
                  Bu çek ertelendi; yerine {detail.replacedByChequeNumber} numaralı çek
                  düzenlendi.
                </div>
              )}

              {detail.allocations.length > 0 && (
                <div style={{ marginTop: "10px" }}>
                  <strong>Dağılım</strong>
                  <table className="erp-table" style={{ marginTop: "6px" }}>
                    <thead>
                      <tr>
                        <th>Tutar</th>
                        <th>Proje / Masraf merkezi</th>
                        <th>Fatura</th>
                      </tr>
                    </thead>
                    <tbody>
                      {detail.allocations.map((allocation) => (
                        <tr key={allocation.id}>
                          <td>{money.format(allocation.amount)}</td>
                          <td>
                            {allocation.projectCode ?? allocation.costCenterCode ?? "—"}
                          </td>
                          <td>
                            {allocation.supplierInvoiceNumber ??
                              allocation.salesInvoiceNumber ??
                              "—"}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            {showReplaceForm && (
              <form onSubmit={submitReplace} style={{ display: "grid", gap: "12px" }}>
                <h3>Çek Erteleme / Değişim</h3>

                <div className="erp-alert">
                  Yeni çek eski çekle aynı tutarda olur ({money.format(detail.amount)}).
                  Vade farkı varsa ayrı bir fatura/dekont ile kaydedilmelidir.
                </div>

                <div className="erp-form-grid">
                  <label>
                    Yeni çek numarası
                    <input
                      required
                      value={replaceForm.chequeNumber}
                      onChange={(e) =>
                        setReplaceForm({ ...replaceForm, chequeNumber: e.target.value })
                      }
                    />
                  </label>

                  <label>
                    Yeni vade
                    <input
                      type="date"
                      required
                      value={replaceForm.dueDate}
                      onChange={(e) =>
                        setReplaceForm({ ...replaceForm, dueDate: e.target.value })
                      }
                    />
                  </label>

                  <label>
                    İşlem tarihi
                    <input
                      type="date"
                      required
                      value={replaceForm.movementDate}
                      onChange={(e) =>
                        setReplaceForm({ ...replaceForm, movementDate: e.target.value })
                      }
                    />
                  </label>

                  <label>
                    Banka
                    <input
                      value={replaceForm.bankName}
                      onChange={(e) =>
                        setReplaceForm({ ...replaceForm, bankName: e.target.value })
                      }
                    />
                  </label>

                  <label>
                    Şube
                    <input
                      value={replaceForm.bankBranch}
                      onChange={(e) =>
                        setReplaceForm({ ...replaceForm, bankBranch: e.target.value })
                      }
                    />
                  </label>

                  <label>
                    Açıklama
                    <input
                      value={replaceForm.description}
                      onChange={(e) =>
                        setReplaceForm({ ...replaceForm, description: e.target.value })
                      }
                    />
                  </label>
                </div>

                <div style={{ display: "flex", gap: "8px" }}>
                  <button type="submit" className="erp-primary-button" disabled={saving}>
                    {saving ? "İşleniyor..." : "Ertele ve Yeni Çeki Aç"}
                  </button>
                  <button
                    type="button"
                    className="erp-secondary-button"
                    onClick={() => setShowReplaceForm(false)}
                  >
                    Vazgeç
                  </button>
                </div>
              </form>
            )}

            {detail.allowedNextStatuses.length > 0 ? (
              <form onSubmit={submitStatus} style={{ display: "grid", gap: "12px" }}>
                <div className="erp-form-grid">
                  <label>
                    Yeni durum
                    <select
                      required
                      value={statusForm.toStatus}
                      onChange={(e) =>
                        setStatusForm({ ...statusForm, toStatus: e.target.value })
                      }
                    >
                      {detail.allowedNextStatuses.map((status) => (
                        <option key={status} value={String(status)}>
                          {CHEQUE_STATUS_LABELS[status] ?? status}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    İşlem tarihi
                    <input
                      type="date"
                      required
                      value={statusForm.movementDate}
                      onChange={(e) =>
                        setStatusForm({ ...statusForm, movementDate: e.target.value })
                      }
                    />
                  </label>

                  <label>
                    Kasa / banka {statusNeedsCashAccount ? "(zorunlu)" : "(opsiyonel)"}
                    <select
                      required={statusNeedsCashAccount}
                      value={statusForm.cashAccountId}
                      onChange={(e) =>
                        setStatusForm({ ...statusForm, cashAccountId: e.target.value })
                      }
                    >
                      <option value="">—</option>
                      {cashAccounts.map((account) => (
                        <option key={account.id} value={account.id}>
                          {account.code} — {account.name}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Açıklama
                    <input
                      value={statusForm.description}
                      onChange={(e) =>
                        setStatusForm({ ...statusForm, description: e.target.value })
                      }
                    />
                  </label>
                </div>

                <div>
                  <button type="submit" className="erp-primary-button" disabled={saving}>
                    {saving ? "İşleniyor..." : "Durumu Güncelle"}
                  </button>
                </div>
              </form>
            ) : (
              <div className="erp-alert">
                Bu çek nihai durumda; başka bir duruma geçirilemez.
              </div>
            )}

            {showFactoringForm && canFactor && (
              <form onSubmit={submitFactoring} style={{ display: "grid", gap: "12px" }}>
                <h3>Çek Kırdırma</h3>

                <div className="erp-form-grid">
                  <label>
                    Net paranın gireceği hesap
                    <select
                      required
                      value={factoringForm.cashAccountId}
                      onChange={(e) =>
                        setFactoringForm({ ...factoringForm, cashAccountId: e.target.value })
                      }
                    >
                      <option value="">Seçin...</option>
                      {cashAccounts.map((account) => (
                        <option key={account.id} value={account.id}>
                          {account.code} — {account.name}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Faktoring şirketi (opsiyonel)
                    <select
                      value={factoringForm.factoringCurrentAccountId}
                      onChange={(e) =>
                        setFactoringForm({
                          ...factoringForm,
                          factoringCurrentAccountId: e.target.value,
                        })
                      }
                    >
                      <option value="">—</option>
                      {currentAccounts.map((item) => (
                        <option key={item.id} value={item.id}>
                          {item.code} — {item.title}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    İşlem tarihi
                    <input
                      type="date"
                      required
                      value={factoringForm.transactionDate}
                      onChange={(e) =>
                        setFactoringForm({
                          ...factoringForm,
                          transactionDate: e.target.value,
                        })
                      }
                    />
                  </label>

                  <label>
                    Komisyon oranı (%)
                    <input
                      type="number"
                      step="0.0001"
                      value={factoringForm.commissionRate}
                      onChange={(e) =>
                        setFactoringForm({
                          ...factoringForm,
                          commissionRate: e.target.value,
                        })
                      }
                    />
                  </label>

                  <label>
                    Komisyon tutarı (oran yerine)
                    <input
                      type="number"
                      step="0.01"
                      value={factoringForm.commissionAmount}
                      onChange={(e) =>
                        setFactoringForm({
                          ...factoringForm,
                          commissionAmount: e.target.value,
                        })
                      }
                    />
                  </label>

                  <label>
                    BSMV oranı (%)
                    <input
                      type="number"
                      step="0.01"
                      value={factoringForm.bsmvRate}
                      onChange={(e) =>
                        setFactoringForm({ ...factoringForm, bsmvRate: e.target.value })
                      }
                    />
                  </label>

                  <label>
                    Masraf
                    <input
                      type="number"
                      step="0.01"
                      value={factoringForm.expenseAmount}
                      onChange={(e) =>
                        setFactoringForm({
                          ...factoringForm,
                          expenseAmount: e.target.value,
                        })
                      }
                    />
                  </label>

                  <label>
                    Finansman giderinin projesi
                    <select
                      value={factoringForm.projectId}
                      onChange={(e) =>
                        setFactoringForm({ ...factoringForm, projectId: e.target.value })
                      }
                    >
                      <option value="">Çekin projesi</option>
                      {projects.map((project) => (
                        <option key={project.id} value={project.id}>
                          {project.code} — {project.name}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label style={{ gridColumn: "1 / -1" }}>
                    Açıklama
                    <input
                      value={factoringForm.description}
                      onChange={(e) =>
                        setFactoringForm({
                          ...factoringForm,
                          description: e.target.value,
                        })
                      }
                    />
                  </label>
                </div>

                {preview && (
                  <div className="erp-table-wrap">
                    <table className="erp-table">
                      <tbody>
                        <tr>
                          <td>Çek tutarı</td>
                          <td style={{ textAlign: "right" }}>
                            {money.format(preview.chequeAmount)}
                          </td>
                        </tr>
                        <tr>
                          <td>Komisyon (%{preview.commissionRate})</td>
                          <td style={{ textAlign: "right" }}>
                            −{money.format(preview.commissionAmount)}
                          </td>
                        </tr>
                        <tr>
                          <td>BSMV (%{preview.bsmvRate})</td>
                          <td style={{ textAlign: "right" }}>
                            −{money.format(preview.bsmvAmount)}
                          </td>
                        </tr>
                        <tr>
                          <td>Masraf</td>
                          <td style={{ textAlign: "right" }}>
                            −{money.format(preview.expenseAmount)}
                          </td>
                        </tr>
                        <tr>
                          <td>
                            <strong>Net eldeki para</strong>
                          </td>
                          <td style={{ textAlign: "right" }}>
                            <strong>{money.format(preview.netAmount)}</strong>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>
                )}

                <div style={{ display: "flex", gap: "8px" }}>
                  <button
                    type="button"
                    className="erp-secondary-button"
                    onClick={() => void runPreview()}
                  >
                    Kesintileri Hesapla
                  </button>
                  <button type="submit" className="erp-primary-button" disabled={saving}>
                    {saving ? "İşleniyor..." : "Kırdır"}
                  </button>
                </div>
              </form>
            )}

            <div>
              <h3>Hareket Geçmişi</h3>
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Tarih</th>
                      <th>Geçiş</th>
                      <th>Açıklama</th>
                      <th>Kasa/Banka</th>
                      <th>Fiş</th>
                    </tr>
                  </thead>
                  <tbody>
                    {detail.movements.map((movement) => (
                      <tr key={movement.id}>
                        <td>{dateFormat.format(new Date(movement.movementDate))}</td>
                        <td>
                          {movement.fromStatusName
                            ? `${movement.fromStatusName} → ${movement.toStatusName}`
                            : movement.toStatusName}
                        </td>
                        <td>{movement.description}</td>
                        <td>{movement.cashAccountName ?? "—"}</td>
                        <td>{movement.accountingVoucherNumber ?? "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </div>
      )}
    </ErpShell>
  );
}
