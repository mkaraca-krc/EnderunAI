"use client";

import Link from "next/link";
import {
  SearchableSelect,
  type SearchableOption,
} from "@/components/ui";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { money, unitPrice } from "@/lib/format/turkish";
import {
  accountingAccountService,
  type AccountingAccountListItem,
} from "@/services/accounting-account.service";
import { branchService, type BranchListItem } from "@/services/branch.service";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import {
  warehouseService,
  type WarehouseListItem,
} from "@/services/warehouse.service";
import {
  DIRECTION_COLORS,
  InvoiceDirection,
  eInvoiceService,
  type ImportCommitResult,
  type ImportPreviewItem,
  type ImportPreviewResult,
} from "@/services/e-invoice.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

/** Her önizleme satırı için kullanıcının verdiği kararlar. */
type RowDecision = {
  selected: boolean;
  currentAccountId: string;
  createCurrentAccount: boolean;
  projectId: string;
  /** 0 Alış (Stok) / 1 Gider. Öneriyle dolu gelir, değiştirilebilir. */
  invoiceType: number;
  expenseAccountId: string;
  costCenterCode: string;
  warehouseId: string;
  /** İade faturasında bağlanacak orijinal fatura. */
  originalInvoiceId: string;
};

export default function EInvoiceImportPage() {
  /**
   * Düğme -> uç -> izin (EInvoiceImportController):
   *   POST e-invoice/import/preview -> accounting.create
   *   POST e-invoice/import/commit  -> accounting.create
   *
   * ÖNİZLEME DE create İSTİYOR (uçta öyle): XML'i okuyup eşleştirme
   * yapıyor, salt okuma değil. Kapı ucun istediğine eşit.
   */
  const actions = useModuleActions("accounting");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [accounts, setAccounts] = useState<CurrentAccountListItem[]>([]);
  const [expenseAccounts, setExpenseAccounts] = useState<AccountingAccountListItem[]>([]);
  const [warehouses, setWarehouses] = useState<WarehouseListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);

  const [companyId, setCompanyId] = useState("");
  const [files, setFiles] = useState<File[]>([]);

  const [preview, setPreview] = useState<ImportPreviewResult | null>(null);
  const [decisions, setDecisions] = useState<Record<string, RowDecision>>({});
  const [commitResult, setCommitResult] = useState<ImportCommitResult | null>(null);

  const [bulkProjectId, setBulkProjectId] = useState("");
  const [expanded, setExpanded] = useState<string | null>(null);

  const [reading, setReading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const fileInputRef = useRef<HTMLInputElement>(null);

  /**
   * Seçimi sıfırlar. Alanın kendi değeri de temizlenmeli: aynı dosya
   * ikinci kez seçilirse tarayıcı "değişmedi" deyip onChange'i hiç
   * tetiklemez ve seçim sessizce boş kalırdı.
   */
  const clearFiles = useCallback(() => {
    setFiles([]);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }, []);

  useEffect(() => {
    void (async () => {
      try {
        const result = await companyService.getAll();
        setCompanies(result);
        setCompanyId(result[0]?.id ?? "");
      } catch (err) {
        setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
      }
    })();
  }, []);

  const loadLookups = useCallback(async () => {
    if (!companyId) return;

    try {
      const [projectList, accountList, accountingList, warehouseList, branchList] =
        await Promise.all([
          projectService.getAll(companyId),
          currentAccountService.getAll(companyId),
          accountingAccountService.getAll({ companyId, isActive: true }),
          warehouseService.getAll({ companyId }),
          branchService.getAll(companyId).catch(() => [] as BranchListItem[]),
        ]);

      setProjects(projectList);
      setAccounts(accountList);
      // Fişe kayıt kabul eden 6xx/7xx hesaplar; grup hesabı seçilemez.
      setExpenseAccounts(
        accountingList.filter(
          (account) =>
            account.isPostingAllowed &&
            (account.code.startsWith("6") || account.code.startsWith("7"))
        )
      );
      setWarehouses(warehouseList.filter((warehouse) => warehouse.isActive));
      setBranches(branchList);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Proje/cari listesi alınamadı.");
    }
  }, [companyId]);

  useEffect(() => {
    void loadLookups();
  }, [loadLookups]);

  const handleRead = async () => {
    if (!companyId || files.length === 0) return;

    setReading(true);
    setError("");
    setCommitResult(null);

    try {
      const result = await eInvoiceService.preview(companyId, files);
      setPreview(result);

      // Aktarılabilir satırlar baştan seçili gelsin; eşleşen cari varsa
      // hazır dolu, yoksa "yeni cari oluştur" önerilir.
      const initial: Record<string, RowDecision> = {};

      result.items.forEach((item) => {
        if (!item.canImport) return;

        initial[item.token] = {
          selected: true,
          currentAccountId: item.matchedCurrentAccountId ?? "",
          createCurrentAccount: !item.matchedCurrentAccountId,
          projectId: "",
          // Öneri seçili gelir ama kararı kullanıcı verir; elektrik
          // sanılan kalem pekâlâ şantiyeye giden pano malzemesi olabilir.
          invoiceType: item.suggestedInvoiceType,
          expenseAccountId: item.suggestedExpenseAccountId ?? "",
          costCenterCode: "",
          warehouseId: "",
          // Eşleşen orijinal öneri olarak gelir; eşleşme yoksa iade
          // orijinaline bağlanmadan aktarılır (belge yine de gerçek).
          originalInvoiceId: item.matchedOriginalInvoiceId ?? "",
        };
      });

      setDecisions(initial);
    } catch (err) {
      setPreview(null);
      setError(err instanceof Error ? err.message : "Dosyalar okunamadı.");
    } finally {
      setReading(false);
    }
  };

  const updateDecision = (token: string, patch: Partial<RowDecision>) => {
    setDecisions((current) => ({
      ...current,
      [token]: { ...current[token], ...patch },
    }));
  };

  /** Toplu yüklemede her satıra tek tek proje seçmek yorucu olurdu. */
  const applyProjectToAll = (projectId: string) => {
    setBulkProjectId(projectId);

    setDecisions((current) => {
      const next: Record<string, RowDecision> = {};
      Object.entries(current).forEach(([token, decision]) => {
        next[token] = { ...decision, projectId };
      });
      return next;
    });
  };

  const searchExpenseAccounts = useCallback(
    async (query: string, signal: AbortSignal) => {
      const result = await accountingAccountService.search(
        { companyId, isActive: true, search: query, limit: 50 },
        signal
      );

      return {
        options: result.items.map((account) => ({
          id: account.id,
          code: account.code,
          title: account.name,
        })),
        total: result.total,
      };
    },
    [companyId]
  );

  const expenseAccountOptions = useMemo<SearchableOption[]>(
    () =>
      expenseAccounts.map((account) => ({
        id: account.id,
        code: account.code,
        title: account.name,
      })),
    [expenseAccounts]
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

  const selectedRows = useMemo(
    () =>
      (preview?.items ?? []).filter(
        (item) => item.canImport && decisions[item.token]?.selected
      ),
    [preview, decisions]
  );

  /**
   * Gider faturasında hesap zorunlu: hesapsız taslak kaydedilirse hata
   * günler sonra, fatura onaya geldiğinde ortaya çıkar.
   */
  const missingExpenseAccount = useMemo(
    () =>
      selectedRows.filter(
        (item) =>
          item.direction === InvoiceDirection.Purchase &&
          decisions[item.token]?.invoiceType === 1 &&
          !decisions[item.token]?.expenseAccountId
      ).length,
    [selectedRows, decisions]
  );

  /**
   * Masraf merkezi teknik olarak zorunlu değil (fiş şirket koduna
   * düşer) ama boş bırakılan gider hangi birime ait olduğu belli
   * olmadan raporlara girer; uyarmak gerekir.
   */
  const missingCostCenter = useMemo(
    () =>
      selectedRows.filter(
        (item) =>
          item.direction === InvoiceDirection.Purchase &&
          decisions[item.token]?.invoiceType === 1 &&
          !decisions[item.token]?.costCenterCode &&
          !decisions[item.token]?.projectId
      ).length,
    [selectedRows, decisions]
  );

  const missingAccount = useMemo(
    () =>
      selectedRows.filter((item) => {
        const decision = decisions[item.token];
        return !decision?.currentAccountId && !decision?.createCurrentAccount;
      }).length,
    [selectedRows, decisions]
  );

  const handleCommit = async () => {
    if (!companyId || selectedRows.length === 0) return;

    setSaving(true);
    setError("");

    try {
      const result = await eInvoiceService.commit(
        companyId,
        selectedRows.map((item) => {
          const decision = decisions[item.token];

          const isPurchase = item.direction === InvoiceDirection.Purchase;
          const isExpense = isPurchase && decision.invoiceType === 1;

          return {
            token: item.token,
            currentAccountId: decision.currentAccountId || null,
            createCurrentAccount: decision.createCurrentAccount,
            projectId: decision.projectId || null,
            invoiceType: isPurchase ? decision.invoiceType : 0,
            originalInvoiceId: item.isReturn
              ? decision.originalInvoiceId || null
              : null,
            expenseAccountId: isExpense ? decision.expenseAccountId || null : null,
            costCenterCode: isPurchase ? decision.costCenterCode || null : null,
            warehouseId: isExpense ? null : decision.warehouseId || null,
          };
        })
      );

      setCommitResult(result);
      // Kullanılan anahtarlar tükendi; aynı önizlemeden ikinci kez
      // aktarım yapılmasın.
      setPreview(null);
      setDecisions({});
      clearFiles();
      await loadLookups();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İçe aktarma başarısız.");
    } finally {
      setSaving(false);
    }
  };

  /**
   * Cari seçenekleri TEK YERDE: kod, ünvan ve vergi no üzerinden
   * aranıyor. Her çağrı yeri kendi eşlemesini yazsaydı bir ekranda
   * vergi numarasıyla bulunan cari diğerinde bulunamazdı.
   */
  const cariOptions = useMemo(
    () =>
      accounts.map((account) => ({
        id: account.id,
        code: account.code,
        title: account.title,
        extra: [account.shortName, account.taxNumber],
      })),
    [accounts]
  );

  return (
    <ErpShell
      design="redwood"
      title="E-Fatura İçe Aktar"
      description="UBL-TR 2.1 XML veya ZIP yükleyin; yön VKN'den belirlenir, gelen fatura alışa, giden fatura satışa düşer"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>1. Dosya Seçimi</h2>

          <select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>
        </div>

        <div style={{ padding: "16px", display: "grid", gap: "12px" }}>
          {/* Tarayıcının kendi dosya alanı gizli tutuluyor: görünümü
              tarayıcıya göre değişiyor ve yanındaki ana butonla
              karışıyordu. Seçimi açan tek ve net bir düğme var. */}
          <input
            ref={fileInputRef}
            type="file"
            multiple
            accept=".xml,.zip"
            style={{ display: "none" }}
            onChange={(e) => setFiles(Array.from(e.target.files ?? []))}
          />

          <div style={{ display: "flex", alignItems: "center", gap: "12px", flexWrap: "wrap" }}>
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => fileInputRef.current?.click()}
            >
              Dosyaları Seç
            </button>

            <span>
              {files.length === 0
                ? "Henüz dosya seçilmedi."
                : `${files.length} dosya seçildi`}
            </span>

            {files.length > 0 && (
              <button type="button" className="erp-secondary-button" onClick={clearFiles}>
                Seçimi Temizle
              </button>
            )}
          </div>

          {files.length > 0 && (
            <ul style={{ margin: 0, paddingLeft: "18px" }}>
              {files.map((file) => (
                <li key={`${file.name}-${file.size}-${file.lastModified}`}>
                  <small>{file.name}</small>
                </li>
              ))}
            </ul>
          )}

          <small>
            Tek XML, birden çok XML veya ZIP arşivi yükleyebilirsiniz. Okunamayan
            dosyalar atlanır, diğerleri işlenmeye devam eder.
          </small>

          <div style={{ display: "flex", alignItems: "center", gap: "12px" }}>
            {actions.can("create") && (
              <button
                type="button"
                className="erp-primary-button"
                disabled={!companyId || files.length === 0 || reading}
                onClick={() => void handleRead()}
              >
                {reading ? "Okunuyor..." : `Dosyaları Oku (${files.length})`}
              </button>
            )}

            {/* Buton pasifse sebebi yazsın; yoksa kullanıcı tıklamayı
                deneyip bir arıza olduğunu düşünüyor. */}
            {files.length === 0 && !reading && (
              <small>Okumak için önce dosya seçin.</small>
            )}
          </div>
        </div>
      </div>

      {commitResult && (
        <div className="erp-table-card">
          <div className="erp-table-header">
            <h2>Sonuç Özeti</h2>
          </div>

          <div style={{ padding: "16px", display: "grid", gap: "12px" }}>
            <div className="erp-alert success">
              {commitResult.createdCount} fatura içe aktarıldı
              {commitResult.skippedCount > 0
                ? `, ${commitResult.skippedCount} dosya atlandı.`
                : "."}
            </div>

            {commitResult.created.length > 0 && (
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Dosya</th>
                      <th>Yön</th>
                      <th>Belge No</th>
                      <th>Cari</th>
                      <th>Tutar</th>
                      <th>Kayıt</th>
                    </tr>
                  </thead>
                  <tbody>
                    {commitResult.created.map((row) => (
                      <tr key={row.invoiceId}>
                        <td>{row.fileName}</td>
                        <td>
                          <span className={`erp-status ${DIRECTION_COLORS[row.direction]}`}>
                            {row.directionName}
                          </span>
                        </td>
                        <td>
                          <strong>{row.internalNumber}</strong>
                          <small>{row.invoiceNumber}</small>
                        </td>
                        <td>
                          {row.currentAccountTitle}
                          {row.currentAccountCreated && <small>Yeni cari açıldı</small>}
                        </td>
                        <td>{money(row.grandTotal)}</td>
                        <td>
                          <Link
                            href={
                              row.direction === InvoiceDirection.Sales
                                ? `/muhasebe/satis-faturalari/${row.invoiceId}`
                                : `/muhasebe/faturalar/${row.invoiceId}`
                            }
                          >
                            Aç
                          </Link>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {commitResult.skipped.length > 0 && (
              <div>
                <strong>Atlananlar</strong>
                <ul>
                  {commitResult.skipped.map((row, index) => (
                    <li key={`${row.fileName}-${index}`}>
                      <strong>{row.fileName}:</strong> {row.reason}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        </div>
      )}

      {preview && (
        <div className="erp-table-card">
          <div className="erp-table-header">
            <h2>
              2. Önizleme — {preview.readableCount} aktarılabilir / {preview.totalFiles} dosya
            </h2>

            <select
              value={bulkProjectId}
              onChange={(e) => applyProjectToAll(e.target.value)}
            >
              <option value="">Projeyi hepsine uygula...</option>
              {projects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code} — {project.name}
                </option>
              ))}
            </select>
          </div>

          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th style={{ width: "36px" }}> </th>
                  <th>Dosya / Fatura</th>
                  <th>Yön</th>
                  <th>Karşı Taraf</th>
                  <th>Cari</th>
                  <th>Tip</th>
                  <th style={{ minWidth: "220px" }}>Gider Hesabı / Depo</th>
                  <th>Proje / Masraf Merkezi</th>
                  <th>Tutar</th>
                  <th>Kaynak</th>
                </tr>
              </thead>
              <tbody>
                {preview.items.map((item) => {
                  const decision = decisions[item.token];
                  const rowKey = item.token || item.fileName;

                  return (
                    <PreviewRow
                      key={rowKey}
                      item={item}
                      decision={decision}
                      projects={projects}
                      accounts={accounts}
                      cariOptions={cariOptions}
                      expenseAccountOptions={expenseAccountOptions}
                      searchExpenseAccounts={searchExpenseAccounts}
                      costCenterOptions={costCenterOptions}
                      warehouses={warehouses}
                      expanded={expanded === rowKey}
                      onToggleExpand={() =>
                        setExpanded(expanded === rowKey ? null : rowKey)
                      }
                      onChange={(patch) => updateDecision(item.token, patch)}
                    />
                  );
                })}
              </tbody>
            </table>
          </div>

          <div style={{ padding: "16px", display: "grid", gap: "10px" }}>
            {missingExpenseAccount > 0 && (
              <div className="erp-alert warning">
                {missingExpenseAccount} gider faturasında gider hesabı
                seçilmedi. Hesapsız gider faturası onaya geldiğinde fiş
                üretilemez.
              </div>
            )}

            {missingCostCenter > 0 && (
              <div className="erp-alert warning">
                {missingCostCenter} gider faturasında masraf merkezi ve proje
                boş. Gider hangi birime yazılacağı belli olmadan raporlara
                girer.
              </div>
            )}

            {missingAccount > 0 && (
              <div className="erp-alert warning">
                {missingAccount} faturada cari seçilmedi. Mevcut bir cari seçin
                veya &quot;yeni cari oluştur&quot; işaretleyin.
              </div>
            )}

            <div>
              {actions.can("create") && (
              <button
                type="button"
                className="erp-primary-button"
                disabled={
                  saving ||
                  selectedRows.length === 0 ||
                  missingExpenseAccount > 0 ||
                  missingAccount > 0
                }
                onClick={() => void handleCommit()}
              >
                {saving
                  ? "Aktarılıyor..."
                  : `Seçili ${selectedRows.length} Faturayı İçe Aktar`}
              </button>
              )}
            </div>
          </div>
        </div>
      )}

      {preview && preview.skipped.length > 0 && (
        <div className="erp-table-card">
          <div className="erp-table-header">
            <h2>Okunamayan Dosyalar ({preview.skipped.length})</h2>
          </div>

          <div style={{ padding: "16px" }}>
            <ul>
              {preview.skipped.map((row, index) => (
                <li key={`${row.fileName}-${index}`}>
                  <strong>{row.fileName}:</strong> {row.reason}
                </li>
              ))}
            </ul>
          </div>
        </div>
      )}
    </ErpShell>
  );
}

function PreviewRow({
  item,
  decision,
  projects,
  accounts,
  cariOptions,
  expenseAccountOptions,
  searchExpenseAccounts,
  costCenterOptions,
  warehouses,
  expanded,
  onToggleExpand,
  onChange,
}: {
  item: ImportPreviewItem;
  decision?: RowDecision;
  projects: ProjectListItem[];
  accounts: CurrentAccountListItem[];
  /** Aranabilir seçicinin seçenekleri — sayfada TEK yerde kuruluyor. */
  cariOptions: SearchableOption[];
  expenseAccountOptions: SearchableOption[];
  /** Hesap planı sunucudan aranıyor (1.114 satır). */
  searchExpenseAccounts: (
    query: string,
    signal: AbortSignal
  ) => Promise<{ options: SearchableOption[]; total: number }>;
  costCenterOptions: { code: string; label: string }[];
  warehouses: WarehouseListItem[];
  expanded: boolean;
  onToggleExpand: () => void;
  onChange: (patch: Partial<RowDecision>) => void;
}) {
  const isSales = item.direction === InvoiceDirection.Sales;
  const isPurchase = item.direction === InvoiceDirection.Purchase;
  const isExpense = isPurchase && decision?.invoiceType === 1;

  return (
    <>
      <tr style={item.canImport ? undefined : { opacity: 0.6 }}>
        <td>
          <input
            type="checkbox"
            disabled={!item.canImport}
            checked={decision?.selected ?? false}
            onChange={(e) => onChange({ selected: e.target.checked })}
          />
        </td>

        <td>
          <button
            type="button"
            onClick={onToggleExpand}
            style={{
              background: "none",
              border: "none",
              padding: 0,
              cursor: "pointer",
              textAlign: "left",
            }}
          >
            <strong>{item.invoiceNumber ?? "(numara yok)"}</strong>
            <small>{item.fileName}</small>
            <small>
              {item.issueDate ? dateFormat.format(new Date(item.issueDate)) : "—"} ·{" "}
              {item.lines.length} kalem
            </small>
          </button>
        </td>

        <td>
          <span className={`erp-status ${DIRECTION_COLORS[item.direction] ?? "gray"}`}>
            {item.directionName}
          </span>

          {item.isReturn && (
            <>
              <span className="erp-status red" style={{ marginTop: "4px" }}>
                İADE
              </span>
              <small>
                {item.matchedOriginalInvoiceNumber
                  ? `Orijinal: ${item.matchedOriginalInvoiceNumber}`
                  : item.referencedInvoiceNumber
                    ? `Atıf: ${item.referencedInvoiceNumber} (sistemde bulunamadı)`
                    : "Orijinal fatura belirtilmemiş"}
              </small>
            </>
          )}
        </td>

        <td>
          {item.counterpartyName ?? "—"}
          <small>VKN: {item.counterpartyTaxNumber ?? "—"}</small>
        </td>

        <td>
          {item.canImport ? (
            <>
              <SearchableSelect
                value={decision?.currentAccountId ?? ""}
                onChange={(next) =>
                  onChange({
                    currentAccountId: next,
                    createCurrentAccount: next === "",
                  })
                }
                options={cariOptions}
                emptyLabel="Yeni cari oluştur"
              />
              {!decision?.currentAccountId && (
                <small>
                  {item.counterpartyName ?? "—"} / {item.counterpartyTaxNumber ?? "—"}
                </small>
              )}
            </>
          ) : (
            "—"
          )}
        </td>

        <td>
          {item.canImport && isPurchase ? (
            <>
              <select
                value={String(decision?.invoiceType ?? 0)}
                onChange={(e) =>
                  onChange({
                    invoiceType: Number(e.target.value),
                    // Tip değişince diğer tipin alanı anlamsızlaşıyor.
                    expenseAccountId: "",
                    warehouseId: "",
                  })
                }
              >
                <option value="0">Alış (Stok)</option>
                <option value="1">Gider</option>
              </select>
              {item.suggestionReason && (
                <small>{item.suggestionReason}</small>
              )}
            </>
          ) : (
            "—"
          )}
        </td>

        <td>
          {!item.canImport || !isPurchase ? (
            "—"
          ) : isExpense ? (
            <>
              <SearchableSelect
                options={expenseAccountOptions}
                loadOptions={searchExpenseAccounts}
                value={decision?.expenseAccountId ?? ""}
                onChange={(next) => onChange({ expenseAccountId: next })}
                placeholder="Gider hesabı ara"
              />
              {item.suggestedExpenseAccountCode &&
                decision?.expenseAccountId === item.suggestedExpenseAccountId && (
                  <small>Öneri: {item.suggestedExpenseAccountCode}</small>
                )}
            </>
          ) : (
            <select
              value={decision?.warehouseId ?? ""}
              onChange={(e) => onChange({ warehouseId: e.target.value })}
            >
              <option value="">Depoya girmeyecek</option>
              {warehouses.map((warehouse) => (
                <option key={warehouse.id} value={warehouse.id}>
                  {warehouse.code} — {warehouse.name}
                </option>
              ))}
            </select>
          )}
        </td>

        <td>
          {item.canImport ? (
            <>
              <select
                value={decision?.projectId ?? ""}
                onChange={(e) => onChange({ projectId: e.target.value })}
              >
                <option value="">
                  {isSales ? "Proje yok" : "Projesiz (merkez)"}
                </option>
                {projects.map((project) => (
                  <option key={project.id} value={project.id}>
                    {project.code} — {project.name}
                  </option>
                ))}
              </select>

              {isPurchase && (
                <select
                  value={decision?.costCenterCode ?? ""}
                  onChange={(e) => onChange({ costCenterCode: e.target.value })}
                >
                  <option value="">Masraf merkezi seçilmedi</option>
                  {costCenterOptions.map((option) => (
                    <option key={option.code} value={option.code}>
                      {option.label}
                    </option>
                  ))}
                </select>
              )}
            </>
          ) : (
            "—"
          )}
        </td>

        <td>
          <strong>{money(item.grandTotal)}</strong>
          <small>KDV: {money(item.vatTotal)}</small>
          {item.withholdingAmount > 0 && (
            <small>Tevkifat: {money(item.withholdingAmount)}</small>
          )}
        </td>

        <td>
          <span
            className={`erp-status ${item.parseSource === 1 ? "yellow" : "gray"}`}
          >
            {item.parseSourceName}
          </span>
        </td>
      </tr>

      {(item.problems.length > 0 || item.requiresManualReview) && (
        <tr>
          <td colSpan={10} style={{ paddingTop: 0 }}>
            {item.requiresManualReview && (
              <div className="erp-alert warning">
                Bu fatura AI yedek okuyucuyla okundu veya tutarları şüpheli.
                Otomatik onaylanmaz; XML orijinaliyle karşılaştırıp onaylayın.
              </div>
            )}

            {item.problems.length > 0 && (
              <div className="erp-alert error">
                <ul style={{ margin: 0, paddingLeft: "18px" }}>
                  {item.problems.map((problem, index) => (
                    <li key={index}>{problem}</li>
                  ))}
                </ul>
              </div>
            )}
          </td>
        </tr>
      )}

      {expanded && item.lines.length > 0 && (
        <tr>
          <td colSpan={10}>
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Kalem</th>
                  <th>Miktar</th>
                  <th>Birim Fiyat</th>
                  <th>KDV %</th>
                  <th>Tutar</th>
                </tr>
              </thead>
              <tbody>
                {item.lines.map((line, index) => (
                  <tr key={index}>
                    <td>{line.description}</td>
                    <td>
                      {line.quantity} {line.unit}
                    </td>
                    <td>{unitPrice(line.unitPrice)}</td>
                    <td>{line.vatRate}</td>
                    <td>{money(line.lineSubtotal)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </td>
        </tr>
      )}
    </>
  );
}
