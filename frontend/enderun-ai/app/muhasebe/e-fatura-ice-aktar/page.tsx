"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import {
  DIRECTION_COLORS,
  InvoiceDirection,
  eInvoiceService,
  type ImportCommitResult,
  type ImportPreviewItem,
  type ImportPreviewResult,
} from "@/services/e-invoice.service";

const money = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY" });
const dateFormat = new Intl.DateTimeFormat("tr-TR");

/** Her önizleme satırı için kullanıcının verdiği kararlar. */
type RowDecision = {
  selected: boolean;
  currentAccountId: string;
  createCurrentAccount: boolean;
  projectId: string;
};

export default function EInvoiceImportPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [accounts, setAccounts] = useState<CurrentAccountListItem[]>([]);

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
      const [projectList, accountList] = await Promise.all([
        projectService.getAll(companyId),
        currentAccountService.getAll(companyId),
      ]);
      setProjects(projectList);
      setAccounts(accountList);
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

  const selectedRows = useMemo(
    () =>
      (preview?.items ?? []).filter(
        (item) => item.canImport && decisions[item.token]?.selected
      ),
    [preview, decisions]
  );

  const missingProject = useMemo(
    () =>
      selectedRows.filter(
        (item) =>
          item.direction === InvoiceDirection.Purchase &&
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

          return {
            token: item.token,
            currentAccountId: decision.currentAccountId || null,
            createCurrentAccount: decision.createCurrentAccount,
            projectId: decision.projectId || null,
          };
        })
      );

      setCommitResult(result);
      // Kullanılan anahtarlar tükendi; aynı önizlemeden ikinci kez
      // aktarım yapılmasın.
      setPreview(null);
      setDecisions({});
      setFiles([]);
      await loadLookups();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İçe aktarma başarısız.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <ErpShell
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
          <input
            type="file"
            multiple
            accept=".xml,.zip"
            onChange={(e) => setFiles(Array.from(e.target.files ?? []))}
          />

          <small>
            Tek XML, birden çok XML veya ZIP arşivi yükleyebilirsiniz. Okunamayan
            dosyalar atlanır, diğerleri işlenmeye devam eder.
          </small>

          <div>
            <button
              type="button"
              className="erp-primary-button"
              disabled={!companyId || files.length === 0 || reading}
              onClick={() => void handleRead()}
            >
              {reading ? "Okunuyor..." : `Dosyaları Oku (${files.length})`}
            </button>
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
                        <td>{money.format(row.grandTotal)}</td>
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
                  <th>Proje</th>
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
            {missingProject > 0 && (
              <div className="erp-alert warning">
                {missingProject} alış faturasında proje seçilmedi. Alış faturası
                muhasebe fişinde masraf merkezi zorunludur.
              </div>
            )}

            {missingAccount > 0 && (
              <div className="erp-alert warning">
                {missingAccount} faturada cari seçilmedi. Mevcut bir cari seçin
                veya &quot;yeni cari oluştur&quot; işaretleyin.
              </div>
            )}

            <div>
              <button
                type="button"
                className="erp-primary-button"
                disabled={
                  saving ||
                  selectedRows.length === 0 ||
                  missingProject > 0 ||
                  missingAccount > 0
                }
                onClick={() => void handleCommit()}
              >
                {saving
                  ? "Aktarılıyor..."
                  : `Seçili ${selectedRows.length} Faturayı İçe Aktar`}
              </button>
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
  expanded,
  onToggleExpand,
  onChange,
}: {
  item: ImportPreviewItem;
  decision?: RowDecision;
  projects: ProjectListItem[];
  accounts: CurrentAccountListItem[];
  expanded: boolean;
  onToggleExpand: () => void;
  onChange: (patch: Partial<RowDecision>) => void;
}) {
  const isSales = item.direction === InvoiceDirection.Sales;

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
        </td>

        <td>
          {item.counterpartyName ?? "—"}
          <small>VKN: {item.counterpartyTaxNumber ?? "—"}</small>
        </td>

        <td>
          {item.canImport ? (
            <>
              <select
                value={decision?.currentAccountId ?? ""}
                onChange={(e) =>
                  onChange({
                    currentAccountId: e.target.value,
                    createCurrentAccount: e.target.value === "",
                  })
                }
              >
                <option value="">Yeni cari oluştur</option>
                {accounts.map((account) => (
                  <option key={account.id} value={account.id}>
                    {account.title}
                    {account.taxNumber ? ` (${account.taxNumber})` : ""}
                  </option>
                ))}
              </select>
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
          {item.canImport ? (
            <select
              value={decision?.projectId ?? ""}
              onChange={(e) => onChange({ projectId: e.target.value })}
            >
              <option value="">{isSales ? "Proje yok" : "Proje seçin"}</option>
              {projects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code} — {project.name}
                </option>
              ))}
            </select>
          ) : (
            "—"
          )}
        </td>

        <td>
          <strong>{money.format(item.grandTotal)}</strong>
          <small>KDV: {money.format(item.vatTotal)}</small>
          {item.withholdingAmount > 0 && (
            <small>Tevkifat: {money.format(item.withholdingAmount)}</small>
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
          <td colSpan={8} style={{ paddingTop: 0 }}>
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
          <td colSpan={8}>
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
                    <td>{money.format(line.unitPrice)}</td>
                    <td>{line.vatRate}</td>
                    <td>{money.format(line.lineSubtotal)}</td>
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
