"use client";

import { Fragment, useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  CostCenterSelect,
  optionKey,
} from "@/components/finans/cost-center-select";
import { ChequeVoidDialog } from "@/components/finans/cheque-void-dialog";
import {
  COST_CENTER_KIND,
  resolveCostCenter,
} from "@/services/cost-center.service";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { amount as formatAmount, money, number as formatNumber } from "@/lib/format/turkish";
import { chequeMonthKey, summarizeCheques } from "@/lib/cheques/totals";
import { useModuleActions } from "@/lib/auth/module-actions";
import {
  Button,
  ConfirmDialog,
  Input,
  Modal,
  Select,
  TutarInput,
} from "@/components/ui";
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

const dateFormat = new Intl.DateTimeFormat("tr-TR");

/** Düzeltme kaydında SAAT de gerekiyor: aynı gün iki düzeltme olabilir. */
const dateTimeFormat = new Intl.DateTimeFormat("tr-TR", {
  dateStyle: "short",
  timeStyle: "short",
});

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
  /**
   * Düğme -> uç -> izin (ChequesController, FactoringController):
   *   POST   cheques                     -> finance.create
   *   PUT    cheques/{id}                -> finance.edit
   *   POST   cheques/{id}/replace        -> finance.edit
   *   POST   cheques/{id}/status         -> finance.edit
   *   POST   cheques/{id}/durum-geri-al  -> finance.approve
   *   POST   cheques/{id}/iptal          -> finance.approve
   *   POST   factoring                   -> finance.create
   *   POST   factoring/preview           -> finance.view  (kapı yok:
   *          önizleme okumadır, ekrandaki kullanıcıda zaten var)
   */
  const actions = useModuleActions("finance");

  /*
   * ÇEK İZİNLERİ AYRI ANAHTARLARDA. Düzenleme uçta artık
   * `cheque.edit` istiyor, `finance.edit` değil; kapanmış çekin
   * iptali ise `cheque.void-closed`. Düğmeyi ucun İSTEDİĞİ izinle
   * eşleştirmek zorunlu — ayrışırsa ya "görünür ama 403" ya da
   * "yetkisi var ama düğmeyi göremiyor" doğuyor.
   */
  const chequeActions = useModuleActions("cheque");
  const canVoidClosed = chequeActions.can("void-closed");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const [direction, setDirection] = useState<number>(ChequeDirection.Received);
  const [statusFilter, setStatusFilter] = useState("");
  const [projectFilter, setProjectFilter] = useState("");

  /** Merkez süzgeci — proje seçilmediğinde masraf merkezi kodu. */
  const [costCenterFilter, setCostCenterFilter] = useState("");
  const [costCenterFilterKey, setCostCenterFilterKey] = useState("");
  const [search, setSearch] = useState("");

  /*
   * Seçili masraf merkezinin anahtarı. Form projeyi ve kodu ayrı
   * tutmaya devam ediyor (sunucu sözleşmesi değişmedi); bu yalnız
   * seçicinin hangi satırda durduğunu biliyor.
   */
  const [costCenterKey, setCostCenterKey] = useState("");

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
  const [confirmMode, setConfirmMode] = useState<"reverse" | null>(null);
  const [confirmError, setConfirmError] = useState("");

  /*
   * İPTAL AYRI DİYALOG. Geri almada gerekçe serbest metin yeter; iptalde
   * neden SAYILABİLİR olmak zorunda (bkz. ChequeVoidDialog). Aynı
   * diyaloğa iki farklı sözleşme sığdırmak yerine ayrıldı.
   */
  const [showVoidDialog, setShowVoidDialog] = useState(false);
  const [voidError, setVoidError] = useState("");

  /** İptaller varsayılan gizli; kullanıcı açıkça isterse listeye girer. */
  const [showVoided, setShowVoided] = useState(false);

  /** Detayda "Değişiklik geçmişi" sekmesi ve muhasebe süzgeci. */
  const [showChangeLog, setShowChangeLog] = useState(false);
  const [onlyAccountingChanges, setOnlyAccountingChanges] = useState(false);

  /**
   * EŞZAMANLI DEĞİŞİKLİK UYARISI. Sunucu damgayı reddettiğinde hata
   * metnini göstermek yetmiyor — kullanıcının elindeki veri artık
   * eski; yenileme AÇIKÇA teklif ediliyor.
   */
  const [staleWarning, setStaleWarning] = useState("");

  const [showEditModal, setShowEditModal] = useState(false);
  const [editForm, setEditForm] = useState(emptyChequeForm);
  const [editError, setEditError] = useState("");
  const [editReason, setEditReason] = useState("");
  const [editCostCenterKey, setEditCostCenterKey] = useState("");

  /** Muhasebeyi etkileyen değişiklik onaylandı mı (iki aşamalı kayıt). */
  const [accountingConfirmed, setAccountingConfirmed] = useState(false);

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
          costCenterCode: costCenterFilter || undefined,
          search: search.trim() || undefined,
          includeVoided: showVoided,
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
  }, [
    companyId,
    direction,
    statusFilter,
    projectFilter,
    costCenterFilter,
    search,
    showVoided,
  ]);

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

  /*
   * GRUPLU LİSTE (F4h). Satırlar grup sırasında düzleştiriliyor;
   * gruplama kuralı ve ay toplamı `lib/cheques/totals.ts`ten geliyor —
   * ekran kendi hesabını yazsaydı üst toplamla ay toplamları yine
   * ayrışabilirdi (bu ekranda bir kez yaşandı).
   */
  const chequeRows = useMemo(
    () => monthGroups.flatMap((group) => group.rows),
    [monthGroups]
  );

  const groupIndex = useMemo(
    () => new Map(monthGroups.map((group) => [group.key, group])),
    [monthGroups]
  );

  const chequeGroupBy = {
    key: chequeMonthKey,
    label: (_rows: ChequeListItem[], key: string) => {
      const group = groupIndex.get(key);
      if (!group) return key;

      const voided = group.rows.length - group.count;

      return (
        `${group.label} · ${group.count} çek` +
        (voided > 0 ? ` · ${voided} iptal (toplam dışı)` : "")
      );
    },
    render: (_rows: ChequeListItem[], key: string) => {
      const group = groupIndex.get(key);
      if (!group) return key;

      const voided = group.rows.length - group.count;

      return (
        <>
          {group.label}
          <small className="!mt-0.5 block font-normal">
            {group.count} çek
            {voided > 0 ? ` · ${voided} iptal (toplam dışı)` : ""}
            {projectFilter
              ? ` · ${
                  projects.find((x) => x.id === projectFilter)?.code ??
                  "seçili proje"
                }`
              : ""}
          </small>
        </>
      );
    },
    summary: (_rows: ChequeListItem[], key: string) =>
      money(groupIndex.get(key)?.total ?? 0),
  };

  const chequeColumns: DataTableColumn<ChequeListItem>[] = [
    {
      key: "cek",
      header: "Çek No",
      value: (row) => `${row.chequeNumber} (${row.internalNumber})`,
      render: (row) => (
        <>
          <strong>{row.chequeNumber}</strong>
          <small>{row.internalNumber}</small>
        </>
      ),
    },
    {
      key: "banka",
      header: "Banka",
      value: (row) => (row.drawer ? `${row.bankName} — ${row.drawer}` : row.bankName),
      render: (row) => (
        <>
          {row.bankName}
          {row.drawer && <small>{row.drawer}</small>}
        </>
      ),
    },
    { key: "cari", header: "Cari", value: (row) => row.currentAccountTitle ?? "—" },
    {
      key: "proje",
      header: "Masraf merkezi",
      /*
        MERKEZ KENDİ ADIYLA GÖRÜNÜR. Önce yalnız proje kodu yazılıyordu
        ve merkeze işlenmiş çek "—" olarak duruyordu: rapor okuyan
        kişi bunu "atanmamış" sanıyordu, oysa masraf merkezi belliydi.
      */
      value: (row) =>
        row.projectCode ?? (row.costCenterCode ? `Merkez (${row.costCenterCode})` : "—"),
    },
    {
      key: "vade",
      header: "Vade",
      value: (row) =>
        `${dateFormat.format(new Date(row.dueDate))} · ` +
        (row.isOverdue
          ? `${Math.abs(row.daysToDue)} gün gecikmiş`
          : `${row.daysToDue} gün`),
      render: (row) => (
        <>
          {dateFormat.format(new Date(row.dueDate))}
          <small>
            {row.isOverdue
              ? `${Math.abs(row.daysToDue)} gün gecikmiş`
              : `${row.daysToDue} gün`}
          </small>
        </>
      ),
    },
    {
      key: "tutar",
      header: "Tutar",
      numeric: true,
      value: (row) =>
        row.currencyCode === "TRY"
          ? money(row.amount)
          : `${formatAmount(row.amount)} ${row.currencyCode} (${money(row.amountTry)})`,
      render: (row) => (
        <>
          <strong>
            {row.currencyCode === "TRY"
              ? money(row.amount)
              : `${formatAmount(row.amount)} ${row.currencyCode}`}
          </strong>
          {/* Dövizli çekte defter değeri de görünmeli: yalnızca döviz
              tutarı gösterilseydi liste toplamıyla satırlar tutmazdı. */}
          {row.currencyCode !== "TRY" && (
            <small className="rw-value-muted">
              {money(row.amountTry)} · kur {formatNumber(row.exchangeRate, 4)}
            </small>
          )}
        </>
      ),
    },
    {
      key: "durum",
      header: "Durum",
      value: (row) => CHEQUE_STATUS_LABELS[row.status] ?? row.statusName,
      render: (row) => (
        <span className={`erp-status ${CHEQUE_STATUS_COLORS[row.status] ?? "gray"}`}>
          {CHEQUE_STATUS_LABELS[row.status] ?? row.statusName}
        </span>
      ),
    },
  ];

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
   * EŞZAMANLI DEĞİŞİKLİK Mİ. Sunucu damga uyuşmazlığında kendi
   * cümlesini yolluyor; ekran onu tanıyıp AYRICA yenileme teklif
   * ediyor — kullanıcının elindeki veri artık eski, sadece hatayı
   * göstermek onu aynı hataya tekrar sürüklerdi.
   */
  function isStaleError(err: unknown): boolean {
    const message = err instanceof Error ? err.message : "";
    return (
      message.includes("başka bir kullanıcı") ||
      message.includes("Değişiklik damgası")
    );
  }

  async function handleStale(err: unknown): Promise<boolean> {
    if (!isStaleError(err)) return false;

    setStaleWarning(
      "Bu çek siz açıkken güncellendi. Ekrandaki bilgiler eski; " +
        "yenileyip tekrar deneyin."
    );

    return true;
  }

  /** Uyarıdan gelen yenileme: detay ve liste birlikte tazeleniyor. */
  async function refreshFromServer() {
    setStaleWarning("");

    if (detail) await openDetail(detail.id);
    await loadItems();
  }

  /** Son durum değişikliğini geri alır; gerekçe uçta da zorunlu. */
  async function runConfirmedAction(reason: string) {
    if (!detail || !confirmMode) return;

    setSaving(true);
    setConfirmError("");
    setError("");
    setNotice("");

    try {
      const updated = await chequeService.reverseStatus(detail.id, reason);

      setDetail(updated);
      setNotice("Durum geri alındı; banka hareketi ve fiş ters kayıtla dengelendi.");
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

  /**
   * ÇEK İPTALİ. Neden sayılabilir, damga zorunlu.
   *
   * Damga çekin AÇILDIĞI andaki hâlini taşıyor: arada çek ciro
   * edilmişse iptal artık "kapanmış çek iptali"dir ve ayrı yetki
   * ister — sunucu isteği reddediyor, sessizce uygulamıyor.
   */
  async function runVoid(input: { reasonKind: number; reason: string }) {
    if (!detail) return;

    setSaving(true);
    setVoidError("");
    setError("");
    setNotice("");

    try {
      const updated = await chequeService.void(detail.id, {
        reasonKind: input.reasonKind,
        reason: input.reason || null,
        rowVersion: detail.rowVersion,
      });

      setDetail(updated);
      setNotice("Çek iptal edildi; banka hareketi ve fişler ters kayıtla geri alındı.");
      setShowVoidDialog(false);

      await loadItems();
    } catch (err) {
      if (await handleStale(err)) {
        setShowVoidDialog(false);
      } else {
        setVoidError(err instanceof Error ? err.message : "Çek iptal edilemedi.");
      }
    } finally {
      setSaving(false);
    }
  }

  function openEditModal() {
    if (!detail) return;

    // Masraf merkezi anahtarı kayıttan kuruluyor: proje varsa proje,
    // yoksa merkez kodu. Boş bırakılsaydı düzenlemeye giren her çek
    // merkezini kaybederdi.
    setEditCostCenterKey(
      detail.projectId
        ? `${COST_CENTER_KIND.Project}:${detail.projectId}`
        : detail.costCenterCode
          ? `${COST_CENTER_KIND.Center}:${detail.costCenterCode}`
          : ""
    );

    setAccountingConfirmed(false);

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
    setEditReason("");
    setShowEditModal(true);
  }

  /**
   * MUHASEBEYİ ETKİLEYEN DEĞİŞİKLİKLER. Tutar, para birimi ve cari
   * değişince giriş fişi ters kayıtla kapanıp yenisi kesiliyor —
   * kullanıcı bunu bilerek onaylasın. Vade ya da açıklama
   * değişikliğinde fiş dokunulmadan kalıyor; orada onay sormak
   * yalnızca gürültü olurdu ve zamanla hiç okunmayan bir tıklamaya
   * dönüşürdü.
   */
  /**
   * İPTAL "KAPANMIŞ DURUMDAN" MI. Portföydeki (ya da yeni verilmiş)
   * çekte henüz para hareketi yok; tahsil/ödeme/karşılıksız hâlinde
   * ise iptal GERÇEKLEŞMİŞ bir hareketi storno eder. Uçtaki kuralın
   * aynısı — burada yalnız düğmeyi doğru göstermek için.
   */
  const voidFromClosedState = useMemo(() => {
    if (!detail) return false;

    const openStatus =
      detail.direction === ChequeDirection.Received
        ? ChequeStatus.Portfolio
        : ChequeStatus.Issued;

    return detail.status !== openStatus;
  }, [detail]);

  const accountingChanges = useMemo(() => {
    if (!detail) return [] as string[];

    const changes: string[] = [];
    const nextAmount = Number(editForm.amount);

    if (Number.isFinite(nextAmount) && nextAmount !== detail.amount) {
      changes.push(`Tutar: ${money(detail.amount)} → ${money(nextAmount)}`);
    }

    if (editForm.currencyCode !== detail.currencyCode) {
      changes.push(
        `Para birimi: ${detail.currencyCode} → ${editForm.currencyCode}`
      );
    }

    if ((editForm.currentAccountId || null) !== (detail.currentAccountId ?? null)) {
      const next = currentAccounts.find(
        (account) => account.id === editForm.currentAccountId
      );

      changes.push(
        `Cari: ${detail.currentAccountTitle ?? "—"} → ${next?.title ?? "—"}`
      );
    }

    /*
     * MASRAF MERKEZİ DE FİŞİ YENİLİYOR (kullanıcı kararı, 2026-08-21).
     * Fişin masraf merkezi kırılımı çekin proje/merkez alanlarından
     * çözülüyor; değişince giriş fişi ters kayıtla kapanıp yenisi
     * yeni kodla kesiliyor.
     */
    const centerBefore = detail.projectCode ?? detail.costCenterCode ?? "—";
    const centerAfter = editForm.projectId
      ? projects.find((project) => project.id === editForm.projectId)?.code ??
        "seçilen proje"
      : editForm.costCenterCode || "—";

    if (
      (editForm.projectId || null) !== (detail.projectId ?? null) ||
      (editForm.costCenterCode || null) !== (detail.costCenterCode ?? null)
    ) {
      changes.push(`Masraf merkezi: ${centerBefore} → ${centerAfter}`);
    }

    return changes;
  }, [detail, editForm, currentAccounts, projects]);

  async function submitEdit() {
    if (!detail) return;

    // Fişi ters kayıtla kapatacak bir değişiklik varsa ilk tıklama
    // KAYDETMİYOR: ne olacağını yazıp onay istiyor.
    if (accountingChanges.length > 0 && !accountingConfirmed) {
      setAccountingConfirmed(true);
      setEditError("");
      return;
    }

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
        currencyCode: editForm.currencyCode,

        // Damga: çek ekranda açıldığı andaki hâlini taşıyor. Arada
        // başkası kaydettiyse uç reddediyor; üzerine sessizce yazmıyor.
        rowVersion: detail.rowVersion,
        editReason: editReason.trim() || null,
      });

      setDetail(updated);
      setShowEditModal(false);
      setAccountingConfirmed(false);
      setNotice("Çek güncellendi.");
      await loadItems();
    } catch (err) {
      if (await handleStale(err)) {
        setShowEditModal(false);
      } else {
        setEditError(err instanceof Error ? err.message : "Güncelleme başarısız.");
      }
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
        `Çek kırdırıldı (${result.internalNumber}). Net ${money(result.netAmount)} ` +
          `banka hesabına girdi, ${money(result.totalDeductionAmount)} finansman gideri yazıldı.`
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
      design="redwood"
      title="Çek Defteri"
      description="Alınan ve verilen çekler, durum geçişleri ve otomatik muhasebe fişleri"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <Button variant="secondary" disabled={loading} onClick={() => void loadItems()}>Yenile</Button>
      </div>

      <div className="erp-page-toolbar">
        <div>
          <strong>{items.length} çek</strong>
          <small style={{ display: "block", marginTop: "4px" }}>
            Listelenen toplam: {money(listTotal)}
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

          {actions.can("create") && (
            <button
              type="button"
              className="erp-primary-button"
              onClick={() => setShowChequeForm((value) => !value)}
            >
              + Yeni {direction === ChequeDirection.Received ? "Alınan" : "Verilen"} Çek
            </button>
          )}
        </div>
      </div>

      {summary && (
        <div className="erp-stat-grid" style={{ marginBottom: "16px" }}>
          <div className="erp-stat-card">
            <span>Portföyde</span>
            <strong>{money(summary.receivedPortfolioAmount)}</strong>
          </div>
          <div className="erp-stat-card">
            <span>Bankada (tahsilde)</span>
            <strong>{money(summary.receivedAtBankAmount)}</strong>
          </div>
          <div className="erp-stat-card">
            <span>Faktoringde</span>
            <strong>{money(summary.receivedAtFactoringAmount)}</strong>
          </div>
          <div className="erp-stat-card">
            <span>Verilen (açık)</span>
            <strong>{money(summary.issuedOpenAmount)}</strong>
          </div>
          <div className="erp-stat-card">
            <span>Karşılıksız</span>
            <strong>{money(summary.receivedBouncedAmount)}</strong>
          </div>
        </div>
      )}

      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {/* EŞZAMANLI DEĞİŞİKLİK: hata metnini göstermek yetmiyor —
          ekrandaki veri artık eski. Yenileme AÇIKÇA teklif ediliyor,
          yoksa kullanıcı aynı hataya tekrar tekrar çarpar. */}
      {staleWarning && (
        <div className="erp-alert warning">
          {staleWarning}{" "}
          <button
            type="button"
            className="erp-secondary-button"
            style={{ marginLeft: "8px" }}
            onClick={() => void refreshFromServer()}
          >
            Sayfayı Yenile
          </button>
        </div>
      )}

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
                Masraf Merkezi *
                {/*
                  * TEK ALAN (çek paketi ek maddesi).
                  *
                  * Eskiden "Proje" ve "Masraf merkezi" AYRI iki alandı
                  * ve kullanıcı proje listesinde "Merkez"i arayıp
                  * bulamıyordu — Merkez ikinci alandaydı. Artık tek
                  * liste: Merkez en üstte, projeler altında.
                  *
                  * ZORUNLU ve varsayılan MERKEZ: çeklerin çoğu projeye
                  * özel değil, ofis giderleri merkeze yazılır.
                  */}
                <CostCenterSelect
                  companyId={companyId}
                  value={costCenterKey}
                  includeProjectId={chequeForm.projectId || undefined}
                  required
                  onChange={(option) => {
                    const resolved = resolveCostCenter(option);

                    setCostCenterKey(option ? optionKey(option) : "");
                    setChequeForm({
                      ...chequeForm,
                      projectId: resolved.projectId ?? "",
                      costCenterCode: resolved.costCenterCode ?? "",
                    });
                  }}
                />
                <small>
                  Her çek bir masraf merkezine yazılır; yoksa proje bazlı
                  nakit akışında hiç görünmez. Ofis kirası gibi projesi
                  olmayan çekler Merkez&apos;e yazılır.
                </small>
              </label>

              {/* TUTAR ORTAK BİLEŞENLE: Türkçe biçim, imleç korumalı,
                  hem virgül hem nokta ondalık. Alan durumu HAM sayının
                  metni olarak tutuluyor — kaydetme yolundaki
                  `Number(...)` çağrıları olduğu gibi çalışıyor. */}
              <label>
                Tutar
                <TutarInput
                  required
                  value={chequeForm.amount === "" ? null : Number(chequeForm.amount)}
                  onChange={(next) =>
                    setChequeForm({
                      ...chequeForm,
                      amount: next === null ? "" : String(next),
                    })
                  }
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
                            <TutarInput
                              value={row.amount === "" ? null : Number(row.amount)}
                              onChange={(next) =>
                                setAllocationRows((current) =>
                                  current.map((item, i) =>
                                    i === index
                                      ? {
                                          ...item,
                                          amount: next === null ? "" : String(next),
                                        }
                                      : item
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
                                    {invoice.invoiceNumber} — {money(invoice.grandTotal)}
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
                  Dağılım toplamı: {money(allocationTotal)}
                  {allocationDifference !== 0 && (
                    <strong className="rw-value-danger">
                      {" "}
                      · {money(Math.abs(allocationDifference))}{" "}
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

            {/* SÜZGEÇ DE MASRAF MERKEZİ: liste yalnız projeye göre
                süzülüyordu, merkeze işlenen çekler hiçbir süzgeçle
                ayrılamıyordu — "merkezin çekleri" sorusu cevapsızdı. */}
            <div className="w-56">
              <CostCenterSelect
                companyId={companyId}
                value={costCenterFilterKey}
                emptyLabel="Tüm masraf merkezleri"
                onChange={(option) => {
                  const resolved = resolveCostCenter(option);

                  setCostCenterFilterKey(option ? optionKey(option) : "");
                  setProjectFilter(resolved.projectId ?? "");
                  setCostCenterFilter(resolved.costCenterCode ?? "");
                }}
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

            {/* İPTALLER VARSAYILAN GİZLİ. Kayıt denetim izi için
                silinmiyor ama günlük listede gürültü; kullanıcı
                açıkça isterse geliyor ve üstü çizili görünüyor. */}
            <label className="flex items-center gap-2 text-sm text-slate-600">
              <input
                type="checkbox"
                checked={showVoided}
                onChange={(e) => setShowVoided(e.target.checked)}
              />
              İptalleri göster
            </label>
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
          <DataTable
            rows={chequeRows}
            columns={chequeColumns}
            rowKey={(row) => row.id}
            title="Çek Listesi"
            resetKey={`${direction}|${statusFilter}|${projectFilter}|${costCenterFilter}|${search}|${showVoided}`}
            rowProps={(row) => ({
              onClick: () => void openDetail(row.id),
              style: {
                cursor: "pointer",
                fontWeight: row.id === detail?.id ? 600 : undefined,
                // İptal edilen kayıt denetim izi için listede kalıyor
                // ama toplam dışı olduğu belli olsun.
                opacity: row.status === ChequeStatus.Voided ? 0.55 : undefined,
                textDecoration:
                  row.status === ChequeStatus.Voided ? "line-through" : undefined,
              },
            })}
            groupBy={chequeGroupBy}
          />

        )}
      </div>

      {detail && (
        <div className="erp-table-card" style={{ marginTop: "16px" }}>
          <div className="erp-table-header">
            <h2>
              {detail.internalNumber} — {detail.chequeNumber} ({money(detail.amount)})
            </h2>

            <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
              {detail.allowedNextStatuses.includes(ChequeStatus.Replaced) &&
                actions.can("edit") && (
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

              {canFactor && actions.can("create") && (
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
                          <td>{money(allocation.amount)}</td>
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
                  Yeni çek eski çekle aynı tutarda olur ({money(detail.amount)}).
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
                  {actions.can("edit") && (
                    <button type="submit" className="erp-primary-button" disabled={saving}>
                      {saving ? "İşleniyor..." : "Durumu Güncelle"}
                    </button>
                  )}
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
                <small className="rw-value-muted">
                  Yanlış durum geri alınır, baştan hatalı çek iptal
                  edilir. Çek silinmez; banka hareketi ve muhasebe fişi
                  ters kayıtla dengelenir.
                </small>

                <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                  {/*
                    DÜZENLENEBİLİRLİK KARARI SUNUCUDAN. Ekran kendi
                    kuralını yazsaydı (ör. "hareketi varsa kapalı") uçla
                    zamanla ayrışırdı; kapalıysa NEDENİ de sunucunun
                    cümlesiyle gösteriliyor.
                  */}
                  {chequeActions.can("edit") && (
                    <button
                      type="button"
                      className="erp-secondary-button"
                      disabled={saving || !detail.canEdit}
                      title={detail.canEdit ? undefined : detail.editBlockedReason ?? undefined}
                      onClick={openEditModal}
                    >
                      Çeki Düzenle
                    </button>
                  )}

                  {/* Durum geri alma ve iptal, uçta finance.approve
                      istiyor — düzenlemeden DAHA ağır bir yetki.
                      İkisi de muhasebe fişine ters kayıt üretiyor. */}
                  {actions.can("approve") && (
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
                  )}

                  {/*
                    KAPANMIŞ ÇEK İPTALİ AYRI YETKİ. Düğme gizlenmiyor,
                    KAPALI gösteriliyor ve nedeni yazıyor: gizlenseydi
                    kullanıcı işi yapamadığını görür ama sebebini
                    bilemez, destek çağrısı doğar.
                  */}
                  {actions.can("approve") && (
                    <button
                      type="button"
                      className="erp-secondary-button"
                      disabled={saving || (voidFromClosedState && !canVoidClosed)}
                      title={
                        voidFromClosedState && !canVoidClosed
                          ? `Bu çek "${detail.statusName}" durumunda. ` +
                            "Kapanmış çekin iptali ayrı bir yetki gerektiriyor " +
                            "(Çek — Kapanmış İptal)."
                          : undefined
                      }
                      onClick={() => {
                        setVoidError("");
                        setShowVoidDialog(true);
                      }}
                    >
                      Çeki İptal Et
                    </button>
                  )}
                </div>

                {voidFromClosedState && !canVoidClosed && (
                  <small className="rw-value-muted">
                    Çek &quot;{detail.statusName}&quot; durumunda; iptali
                    &quot;Çek — Kapanmış İptal&quot; yetkisi gerektiriyor.
                  </small>
                )}

                {chequeActions.can("edit") && !detail.canEdit && detail.editBlockedReason && (
                  <small className="rw-value-muted">
                    {detail.editBlockedReason}
                  </small>
                )}
              </div>
            ) : (
              <div className="erp-alert" style={{ marginTop: "16px" }}>
                Bu çek iptal edilmiş
                {detail.voidReasonName ? ` (${detail.voidReasonName})` : ""}.
                Mali etkileri ters kayıtla geri alındı; kayıt geçmiş için
                defterde duruyor.
                {/*
                  KAPANMIŞ DURUMDAN İPTAL AYRI ROZET: gerçekleşmiş bir
                  hareket storno edilmiş demektir. Sıradan bir iptalle
                  aynı görünseydi denetimde ayırt edilemezdi.
                */}
                {detail.voidedFromClosedState && (
                  <strong style={{ display: "block", marginTop: "6px" }}>
                    Kapanmış durumdan iptal — gerçekleşmiş hareket storno
                    edildi.
                  </strong>
                )}
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
                          <td className="num">
                            {money(preview.chequeAmount)}
                          </td>
                        </tr>
                        <tr>
                          <td>Komisyon (%{preview.commissionRate})</td>
                          <td className="num">
                            −{money(preview.commissionAmount)}
                          </td>
                        </tr>
                        <tr>
                          <td>BSMV (%{preview.bsmvRate})</td>
                          <td className="num">
                            −{money(preview.bsmvAmount)}
                          </td>
                        </tr>
                        <tr>
                          <td>Masraf</td>
                          <td className="num">
                            −{money(preview.expenseAmount)}
                          </td>
                        </tr>
                        <tr>
                          <td>
                            <strong>Net eldeki para</strong>
                          </td>
                          <td className="num">
                            <strong>{money(preview.netAmount)}</strong>
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

            {/*
              DEĞİŞİKLİK GEÇMİŞİ — ALAN BAZINDA.
              Hareket geçmişi çekin DURUMUNU anlatıyor; burası
              DÜZELTMELERİ. İkisi ayrı: "vade 15 Mart'tan 30 Mart'a
              çekildi" bir durum değişikliği değil ve hareket
              listesinde hiç görünmezdi.
            */}
            <div>
              <button
                type="button"
                className="erp-secondary-button"
                onClick={() => setShowChangeLog((current) => !current)}
              >
                Değişiklik Geçmişi ({detail.changeLog.length})
              </button>

              {showChangeLog && (
                <div style={{ marginTop: "12px" }}>
                  {detail.changeLog.length === 0 ? (
                    <div className="erp-alert">
                      Bu çekte kayıtlı bir düzeltme yok.
                    </div>
                  ) : (
                    <>
                      {/* MUHASEBEYİ ETKİLEYEN SÜZGECİ: denetimde sorulan
                          soru "fişi değiştiren ne oldu"; açıklama
                          düzeltmeleri o listeyi boğuyor. */}
                      <label className="mb-2 flex items-center gap-2 text-sm text-slate-600">
                        <input
                          type="checkbox"
                          checked={onlyAccountingChanges}
                          onChange={(e) =>
                            setOnlyAccountingChanges(e.target.checked)
                          }
                        />
                        Yalnız muhasebeyi etkileyenler
                      </label>

                      <div className="erp-table-wrap">
                        <table className="erp-table">
                          <thead>
                            <tr>
                              <th>Tarih</th>
                              <th>Alan</th>
                              <th>Eski</th>
                              <th>Yeni</th>
                              <th>Kullanıcı</th>
                              <th>Gerekçe</th>
                            </tr>
                          </thead>
                          <tbody>
                            {detail.changeLog
                              .filter(
                                (entry) =>
                                  !onlyAccountingChanges || entry.affectsAccounting
                              )
                              .map((entry) => (
                                <tr key={entry.id}>
                                  <td>
                                    {dateTimeFormat.format(
                                      new Date(entry.changedAtUtc)
                                    )}
                                  </td>
                                  <td>
                                    {entry.fieldLabel}
                                    {entry.affectsAccounting && (
                                      <strong
                                        style={{
                                          display: "block",
                                          fontSize: "11px",
                                          color: "var(--erp-accent)",
                                        }}
                                      >
                                        Muhasebeyi etkiler
                                      </strong>
                                    )}
                                  </td>
                                  <td>{entry.oldValue ?? "—"}</td>
                                  <td>{entry.newValue ?? "—"}</td>
                                  <td>{entry.changedByUserName ?? "—"}</td>
                                  <td>{entry.reason ?? "—"}</td>
                                </tr>
                              ))}
                          </tbody>
                        </table>
                      </div>
                    </>
                  )}
                </div>
              )}
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
        title="Son durumu geri al"
        description="Çek bir önceki durumuna döner. Muhasebe fişi ters kayıtla kapanır ve banka hareketi karşıt bir hareketle dengelenir; kayıtlar silinmez."
        confirmLabel="Geri Al"
        requireReason
        busy={saving}
        error={confirmError}
        onCancel={() => {
          setConfirmMode(null);
          setConfirmError("");
        }}
        onConfirm={(reason) => void runConfirmedAction(reason)}
      />

      {/* İPTAL: nedeni SAYILABİLİR olmak zorunda, o yüzden ayrı
          diyalog. key ile her açılışta temiz kuruluyor. */}
      <ChequeVoidDialog
        key={showVoidDialog ? `iptal-${detail?.id ?? ""}` : "iptal-kapali"}
        open={showVoidDialog}
        fromClosedState={voidFromClosedState}
        statusName={detail?.statusName ?? ""}
        busy={saving}
        error={voidError}
        onCancel={() => {
          setShowVoidDialog(false);
          setVoidError("");
        }}
        onConfirm={(input) => void runVoid(input)}
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
              {saving
                ? "Kaydediliyor…"
                : accountingChanges.length > 0 && accountingConfirmed
                  ? "Onayla ve Kaydet"
                  : "Kaydet"}
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

          <TutarInput
            label="Tutar"
            value={editForm.amount === "" ? null : Number(editForm.amount)}
            onChange={(next) =>
              setEditForm({
                ...editForm,
                amount: next === null ? "" : String(next),
              })
            }
          />

          {/* MASRAF MERKEZİ TEK ALAN: proje ya da Merkez. Girişte
              böyle soruluyor; düzenlemede "Proje" diye sorulsaydı
              merkeze işlenmiş çek düzenlenirken merkezini kaybederdi. */}
          <label className="block text-sm font-medium text-slate-700">
            Masraf merkezi
            <div className="mt-1.5">
              <CostCenterSelect
                companyId={companyId}
                value={editCostCenterKey}
                includeProjectId={detail?.projectId ?? null}
                required
                onChange={(option) => {
                  const resolved = resolveCostCenter(option);

                  setEditCostCenterKey(option ? optionKey(option) : "");
                  setEditForm((current) => ({
                    ...current,
                    projectId: resolved.projectId ?? "",
                    costCenterCode: resolved.costCenterCode ?? "",
                  }));
                }}
              />
            </div>
          </label>

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

          <div className="md:col-span-2">
            {/* Gerekçe denetim kaydına yazılıyor: aylar sonra "bu tutar
                neden değişmiş" sorusunun tek cevabı burası. */}
            <Input
              label="Düzeltme gerekçesi"
              value={editReason}
              onChange={(e) => setEditReason(e.target.value)}
            />
          </div>
        </div>

        {/*
          MUHASEBEYİ ETKİLEYEN DEĞİŞİKLİK ONAYI.
          Tutar / para birimi / cari değişince giriş fişi ters kayıtla
          kapanıp yenisi kesiliyor. Ne olacağı SAYIYLA yazılıyor; genel
          bir "emin misiniz" cümlesi okunmadan tıklanırdı.
        */}
        {accountingChanges.length > 0 && (
          <div
            className={accountingConfirmed ? "erp-alert warning" : "erp-alert"}
            style={{ marginTop: "12px" }}
          >
            <strong>Bu değişiklik muhasebe kaydını etkiliyor:</strong>
            <ul style={{ margin: "6px 0 0 18px" }}>
              {accountingChanges.map((change) => (
                <li key={change}>{change}</li>
              ))}
            </ul>
            <small className="rw-value-muted">
              Giriş fişi ters kayıtla kapanacak ve yeni tutarla yenisi
              kesilecek.
              {accountingConfirmed
                ? " Onaylamak için tekrar Kaydet'e basın."
                : ""}
            </small>
          </div>
        )}

        {editError && (
          <p className="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {editError}
          </p>
        )}
      </Modal>
    </ErpShell>
  );
}
