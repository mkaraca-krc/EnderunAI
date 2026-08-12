"use client";

import { Fragment, useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { summarizeCheques } from "@/lib/cheques/totals";
import { Button, ConfirmDialog, Input, Modal, Select } from "@/components/ui";
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
  currencyCode: "TRY",
  exchangeRate: "",
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
  const [projectFilter, setProjectFilter] = useState("");
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

  /**
   * Düzeltme/iptal akışları MODALDA yürüyor.
   *
   * Önce window.prompt kullanılıyordu: tarayıcının penceresi gerekçeyi
   * zorunlu tutamıyor, boş metni kabul ediyor ve hata mesajını aynı
   * yerde gösteremiyor. Modal açıkken arkadaki liste DOM'da kalıyor —
   * kullanıcı kapatınca bıraktığı yerde buluyor.
   */
  const [confirmMode, setConfirmMode] = useState<"reverse" | "void" | null>(null);
  const [confirmError, setConfirmError] = useState("");

  const [showEditModal, setShowEditModal] = useState(false);
  const [editForm, setEditForm] = useState(emptyChequeForm);
  const [editError, setEditError] = useState("");

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
          projectId: projectFilter || undefined,
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
  }, [companyId, direction, statusFilter, projectFilter, search]);

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
    void (async () => {
      // Ana ekrandaki "Verilen Çek" / "Alınan Çek" kısayolları buraya
      // yönü adresle taşıyor; kullanıcı listeyi ayrıca çevirmesin.
      const requested = new URLSearchParams(window.location.search).get("yon");

      if (requested === "verilen") setDirection(ChequeDirection.Issued);
      if (requested === "alinan") setDirection(ChequeDirection.Received);

      await loadCompanies();
    })();
  }, [loadCompanies]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadItems(), 300);
    return () => window.clearTimeout(timer);
  }, [loadItems]);

  useEffect(() => {
    void loadLookups();
  }, [loadLookups]);

  /**
   * Çekler VADE AYINA göre gruplanır ve toplamlar çıkarılır.
   *
   * Hesap `lib/cheques/totals.ts` içinde: üst satırdaki toplam ile ay
   * alt toplamları eskiden AYRI hesaplanıyordu ve kuralları farklıydı
   * — üst toplam iptal edilmiş çekleri sayıyordu. Tek kaynağa
   * alındı; artık üst toplam grupların toplamından türüyor ve
   * ayrışmaları yapısal olarak imkânsız.
   */
  const { listTotal, groups: monthGroups } = useMemo(
    () => summarizeCheques(items),
    [items]
  );

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
        currencyCode: chequeForm.currencyCode,
        // Boş bırakılırsa sunucu TCMB arşivinden çözer; arşivde de
        // yoksa dövizli çek kaydedilmez (kur uydurulmaz).
        exchangeRate: chequeForm.exchangeRate
          ? Number(chequeForm.exchangeRate.replace(",", "."))
          : null,
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

  /**
   * Son durum değişikliğini geri alır. Gerekçe zorunlu: uç da
   * gerekçesiz isteği reddediyor, buradaki sorma yalnızca kullanıcıyı
   * sunucu hatasıyla karşılaştırmamak için.
   */
  /**
   * Modaldan gelen onay: geri alma ya da iptal. İkisi de aynı yoldan
   * geçiyor çünkü fark yalnız çağrılan uç ve mesaj.
   */
  async function runConfirmedAction(reason: string) {
    if (!detail || !confirmMode) return;

    setSaving(true);
    setConfirmError("");
    setError("");
    setNotice("");

    try {
      const updated = confirmMode === "reverse"
        ? await chequeService.reverseStatus(detail.id, reason)
        : await chequeService.void(detail.id, reason);

      setDetail(updated);
      setNotice(
        confirmMode === "reverse"
          ? "Durum geri alındı; banka hareketi ve fiş ters kayıtla dengelendi."
          : "Çek iptal edildi; banka hareketi ve fişler ters kayıtla geri alındı."
      );

      setConfirmMode(null);

      // Modal kapanınca liste tazeleniyor: toplamlar ve durum rozeti
      // arkada eski haliyle kalmasın.
      await loadItems();
    } catch (err) {
      // Hata MODALDA kalıyor: kullanıcı gerekçeyi yeniden yazmadan
      // düzeltip tekrar deneyebilsin.
      setConfirmError(err instanceof Error ? err.message : "İşlem başarısız.");
    } finally {
      setSaving(false);
    }
  }

  function openEditModal() {
    if (!detail) return;

    setEditForm({
      chequeNumber: detail.chequeNumber,
      bankName: detail.bankName,
      bankBranch: detail.bankBranch ?? "",
      drawer: detail.drawer ?? "",
      currentAccountId: detail.currentAccountId ?? "",
      projectId: detail.projectId ?? "",
      costCenterCode: detail.costCenterCode ?? "",
      amount: String(detail.amount),
      currencyCode: detail.currencyCode,
      exchangeRate: String(detail.exchangeRate),
      issueDate: detail.issueDate.slice(0, 10),
      dueDate: detail.dueDate.slice(0, 10),
      description: detail.description ?? "",
    });

    setEditError("");
    setShowEditModal(true);
  }

  async function submitEdit() {
    if (!detail) return;

    setSaving(true);
    setEditError("");

    try {
      const amount = Number(editForm.amount);

      if (!Number.isFinite(amount) || amount <= 0) {
        throw new Error("Çek tutarı sıfırdan büyük olmalıdır.");
      }

      const updated = await chequeService.update(detail.id, {
        chequeNumber: editForm.chequeNumber.trim(),
        bankName: editForm.bankName.trim(),
        bankBranch: editForm.bankBranch.trim() || null,
        drawer: editForm.drawer.trim() || null,
        currentAccountId: editForm.currentAccountId || null,
        projectId: editForm.projectId || null,
        amount,
        issueDate: editForm.issueDate,
        dueDate: editForm.dueDate,
        progressPaymentId: detail.progressPaymentId ?? null,
        supplierInvoiceId: detail.supplierInvoiceId ?? null,
        description: editForm.description.trim() || null,
        costCenterCode: editForm.costCenterCode || null,
      });

      setDetail(updated);
      setShowEditModal(false);
      setNotice("Çek güncellendi.");
      await loadItems();
    } catch (err) {
      setEditError(err instanceof Error ? err.message : "Güncelleme başarısız.");
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
                Proje
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
                  Proje ya da masraf merkezinden BİRİ zorunlu: her çek bir
                  yere yazılmalı, yoksa proje bazlı nakit akışında hiç
                  görünmez. Ofis kirası gibi projesi olmayan çekler
                  Merkez&apos;e yazılır.
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
                Para birimi
                <select
                  value={chequeForm.currencyCode}
                  onChange={(e) =>
                    setChequeForm({ ...chequeForm, currencyCode: e.target.value })
                  }
                >
                  <option value="TRY">TRY</option>
                  <option value="USD">USD</option>
                  <option value="EUR">EUR</option>
                  <option value="GBP">GBP</option>
                </select>
              </label>

              {chequeForm.currencyCode !== "TRY" && (
                <label>
                  Kur (boşsa TCMB)
                  <input
                    type="text"
                    inputMode="decimal"
                    placeholder="Keşide günü kuru"
                    value={chequeForm.exchangeRate}
                    onChange={(e) =>
                      setChequeForm({ ...chequeForm, exchangeRate: e.target.value })
                    }
                  />
                  <small>
                    Boş bırakılırsa keşide tarihinin TCMB döviz alış kuru
                    kullanılır. Kur bulunamazsa çek kaydedilmez.
                  </small>
                </label>
              )}

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

          {/* Filtre çubuğu uygulamanın ORTAK bileşenleriyle: Select ve
              Input başka ekranlarda da kullanılan aynı sınıfları
              taşıyor (h-10, rounded-lg, marka turkuazı odak halkası).
              Buraya özel stil yazmak çek ekranını sistemin dışına
              düşürürdü. */}
          <div className="flex flex-wrap items-center gap-2">
            <div className="w-44">
              <Select
                aria-label="Çek yönü"
                value={String(direction)}
                onChange={(e) => {
                  setDirection(Number(e.target.value));
                  setStatusFilter("");
                  setDetail(null);
                }}
                options={[
                  {
                    value: String(ChequeDirection.Received),
                    label: "Alınan çekler",
                  },
                  {
                    value: String(ChequeDirection.Issued),
                    label: "Verilen çekler",
                  },
                ]}
              />
            </div>

            <div className="w-44">
              <Select
                aria-label="Çek durumu"
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                options={[
                  { value: "", label: "Tüm durumlar" },
                  ...statusOptions.map((status) => ({
                    value: String(status),
                    label: CHEQUE_STATUS_LABELS[status],
                  })),
                ]}
              />
            </div>

            <div className="w-56">
              <Select
                aria-label="Proje"
                value={projectFilter}
                onChange={(e) => setProjectFilter(e.target.value)}
                options={[
                  { value: "", label: "Tüm projeler" },
                  ...projects.map((project) => ({
                    value: project.id,
                    label: `${project.code} — ${project.name}`,
                  })),
                ]}
              />
            </div>

            {/* Arama ikonu kutunun İÇİNDE: Input'un kendi stili
                korunuyor, yalnız sol boşluk açılıyor. */}
            <div className="relative w-64">
              <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-400">
                <svg
                  width="15"
                  height="15"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  aria-hidden="true"
                >
                  <circle cx="11" cy="11" r="7" />
                  <path d="m20 20-3.5-3.5" />
                </svg>
              </span>

              <Input
                aria-label="Çek ara"
                className="pl-9"
                placeholder="Çek no / banka / keşideci ara..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
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
                {monthGroups.map((group) => (
                  <Fragment key={group.key}>
                    {/* AY BAŞLIĞI: proje filtresiyle birlikte
                        "bu projeye bu ay ne kadar" sorusunu
                        cevaplıyor. */}
                    {/* Ay bandı: marka turkuazının en açık tonu
                        (brand-50/100). Bağırmıyor ama detay
                        satırlarından net ayrışıyor; tablo yine aynı
                        ızgarada duruyor. */}
                    <tr className="bg-brand-50">
                      <td
                        colSpan={5}
                        className="border-t-2 border-brand-200 !py-2 font-bold text-brand-900"
                      >
                        {group.label}
                        <small className="!mt-0.5 block font-normal text-brand-800">
                          {group.count} çek
                          {group.rows.length > group.count
                            ? ` · ${group.rows.length - group.count} iptal (toplam dışı)`
                            : ""}
                          {projectFilter
                            ? ` · ${
                                projects.find((x) => x.id === projectFilter)?.code ??
                                "seçili proje"
                              }`
                            : ""}
                        </small>
                      </td>
                      <td className="border-t-2 border-brand-200 !py-2 text-right font-bold tabular-nums text-brand-900">
                        {money.format(group.total)}
                      </td>
                      <td className="border-t-2 border-brand-200 !py-2" />
                    </tr>

                    {group.rows.map((item) => (
                  <tr
                    key={item.id}
                    onClick={() => void openDetail(item.id)}
                    style={{
                      cursor: "pointer",
                      fontWeight: item.id === detail?.id ? 600 : undefined,
                      // İptal edilen kayıt denetim izi için listede
                      // kalıyor ama toplam dışı olduğu belli olsun.
                      opacity: item.status === ChequeStatus.Voided ? 0.55 : undefined,
                      textDecoration:
                        item.status === ChequeStatus.Voided
                          ? "line-through"
                          : undefined,
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
                      <strong>
                        {item.currencyCode === "TRY"
                          ? money.format(item.amount)
                          : item.amount.toLocaleString("tr-TR", {
                              style: "currency",
                              currency: item.currencyCode,
                            })}
                      </strong>
                      {/* Dövizli çekte defter değeri de görünmeli:
                          yalnızca döviz tutarı gösterilseydi liste
                          toplamıyla satırlar tutmazdı. */}
                      {item.currencyCode !== "TRY" && (
                        <small style={{ display: "block", color: "#64748b" }}>
                          {money.format(item.amountTry)} · kur{" "}
                          {item.exchangeRate.toLocaleString("tr-TR", {
                            maximumFractionDigits: 4,
                          })}
                        </small>
                      )}
                    </td>
                    <td>
                      <span className={`erp-status ${CHEQUE_STATUS_COLORS[item.status] ?? "gray"}`}>
                        {CHEQUE_STATUS_LABELS[item.status] ?? item.statusName}
                      </span>
                    </td>
                  </tr>
                    ))}
                  </Fragment>
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

            {/* DÜZELTME YOLU: yanlış işaretlenen bir durum ya da baştan
                yanlış girilmiş bir çek için. İkisi de SİLMEZ — fişi ters
                kayıtla kapatır, banka hareketini karşıt bir hareketle
                dengeler ve iz bırakır. */}
            {detail.status !== ChequeStatus.Voided ? (
              <div
                style={{
                  marginTop: "16px",
                  paddingTop: "16px",
                  borderTop: "1px solid var(--erp-border)",
                  display: "grid",
                  gap: "8px",
                }}
              >
                <strong style={{ fontSize: "13px" }}>Düzeltme</strong>
                <small style={{ color: "#64748b" }}>
                  Yanlış durum geri alınır, baştan hatalı çek iptal
                  edilir. Çek silinmez; banka hareketi ve muhasebe fişi
                  ters kayıtla dengelenir.
                </small>

                <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                  <button
                    type="button"
                    className="erp-secondary-button"
                    disabled={saving}
                    onClick={openEditModal}
                  >
                    Çeki Düzenle
                  </button>

                  <button
                    type="button"
                    className="erp-secondary-button"
                    disabled={saving}
                    onClick={() => {
                      setConfirmError("");
                      setConfirmMode("reverse");
                    }}
                  >
                    Son Durumu Geri Al
                  </button>

                  <button
                    type="button"
                    className="erp-secondary-button"
                    disabled={saving}
                    onClick={() => {
                      setConfirmError("");
                      setConfirmMode("void");
                    }}
                  >
                    Çeki İptal Et
                  </button>
                </div>
              </div>
            ) : (
              <div className="erp-alert" style={{ marginTop: "16px" }}>
                Bu çek iptal edilmiş. Mali etkileri ters kayıtla geri
                alındı; kayıt geçmiş için defterde duruyor.
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

      {/* ONAY DİYALOĞU: geri alınamaz işlemler için gerekçe ZORUNLU.
          Onay düğmesi gerekçe yazılmadan açılmıyor; hata modalda
          kalıyor ki kullanıcı yazdığını kaybetmesin. */}
      <ConfirmDialog
        // key ile her modda yeniden kuruluyor: bir önceki işlemin
        // gerekçesi yenisine yapışmasın.
        key={confirmMode ?? "kapali"}
        open={confirmMode !== null}
        title={
          confirmMode === "reverse"
            ? "Son durumu geri al"
            : "Çeki iptal et"
        }
        description={
          confirmMode === "reverse"
            ? "Çek bir önceki durumuna döner. Muhasebe fişi ters kayıtla kapanır ve banka hareketi karşıt bir hareketle dengelenir; kayıtlar silinmez."
            : "Çekin ürettiği bütün mali etkiler geri alınır ve çek iptal durumuna geçer. Kayıt denetim izi için listede kalır."
        }
        confirmLabel={
          confirmMode === "reverse" ? "Geri Al" : "İptal Et"
        }
        requireReason
        busy={saving}
        error={confirmError}
        onCancel={() => {
          setConfirmMode(null);
          setConfirmError("");
        }}
        onConfirm={(reason) => void runConfirmedAction(reason)}
      />

      {/* DÜZELTME: işlem görmüş çekte uç reddediyor — önce durumu geri
          almak gerekiyor. Ekran bunu engellemek yerine sunucunun
          gerekçesini gösteriyor. */}
      <Modal
        open={showEditModal}
        title="Çeki düzenle"
        description="Tutar ya da cari değişirse giriş fişi ters kayıtla kapanır ve yeni tutarla yenisi kesilir."
        onClose={() => setShowEditModal(false)}
        busy={saving}
        footer={
          <>
            <Button
              type="button"
              variant="secondary"
              disabled={saving}
              onClick={() => setShowEditModal(false)}
            >
              Vazgeç
            </Button>

            <Button
              type="button"
              disabled={saving}
              onClick={() => void submitEdit()}
            >
              {saving ? "Kaydediliyor…" : "Kaydet"}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2">
          <Input
            label="Çek numarası"
            value={editForm.chequeNumber}
            onChange={(e) =>
              setEditForm({ ...editForm, chequeNumber: e.target.value })
            }
          />

          <Input
            label="Banka"
            value={editForm.bankName}
            onChange={(e) =>
              setEditForm({ ...editForm, bankName: e.target.value })
            }
          />

          <Input
            label="Şube"
            value={editForm.bankBranch}
            onChange={(e) =>
              setEditForm({ ...editForm, bankBranch: e.target.value })
            }
          />

          <Input
            label="Keşideci"
            value={editForm.drawer}
            onChange={(e) =>
              setEditForm({ ...editForm, drawer: e.target.value })
            }
          />

          <Select
            label="Cari"
            value={editForm.currentAccountId}
            onChange={(e) =>
              setEditForm({ ...editForm, currentAccountId: e.target.value })
            }
            options={[
              { value: "", label: "Seçin" },
              ...currentAccounts.map((account) => ({
                value: account.id,
                label: account.title,
              })),
            ]}
          />

          <Input
            label="Tutar"
            type="number"
            min="0.01"
            step="0.01"
            value={editForm.amount}
            onChange={(e) =>
              setEditForm({ ...editForm, amount: e.target.value })
            }
          />

          <Select
            label="Proje"
            value={editForm.projectId}
            onChange={(e) =>
              setEditForm({ ...editForm, projectId: e.target.value })
            }
            options={[
              { value: "", label: "—" },
              ...projects.map((project) => ({
                value: project.id,
                label: `${project.code} — ${project.name}`,
              })),
            ]}
          />

          <Input
            label="Keşide tarihi"
            type="date"
            value={editForm.issueDate}
            onChange={(e) =>
              setEditForm({ ...editForm, issueDate: e.target.value })
            }
          />

          <Input
            label="Vade"
            type="date"
            value={editForm.dueDate}
            onChange={(e) =>
              setEditForm({ ...editForm, dueDate: e.target.value })
            }
          />

          <div className="md:col-span-2">
            <Input
              label="Açıklama"
              value={editForm.description}
              onChange={(e) =>
                setEditForm({ ...editForm, description: e.target.value })
              }
            />
          </div>
        </div>

        {editError && (
          <p className="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {editError}
          </p>
        )}
      </Modal>
    </ErpShell>
  );
}
