"use client";

import Link from "next/link";
import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { money } from "@/lib/format/turkish";
import { Button } from "@/components/ui";

import {
  accountingReportService,
  type GeneralLedgerReportResponse,
} from "@/services/accounting-report.service";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

type FilterForm = {
  companyId: string;
  startDate: string;
  endDate: string;
  accountCode: string;
  search: string;
};

const now = new Date();

const initialFilters: FilterForm = {
  companyId: "",
  startDate: `${now.getFullYear()}-01-01`,
  endDate: now.toISOString().slice(0, 10),
  accountCode: "",
  search: "",
};

const date = new Intl.DateTimeFormat("tr-TR");

const voucherTypeLabels: Record<number, string> = {
  0: "Mahsup",
  1: "Tahsil",
  2: "Tediye",
  3: "Açılış",
  4: "Kapanış",
};

function balanceLabel(value: number) {
  if (Math.abs(value) < 0.005) {
    return "0,00";
  }

  return `${money(Math.abs(value))} ${
    value > 0 ? "Borç" : "Alacak"
  }`;
}

export default function GeneralLedgerPage() {
  const [companies, setCompanies] = useState<
    CompanyListItem[]
  >([]);

  const [filters, setFilters] =
    useState<FilterForm>(initialFilters);

  const [report, setReport] =
    useState<GeneralLedgerReportResponse | null>(
      null
    );

  const [loadingCompanies, setLoadingCompanies] =
    useState(true);

  const [loadingReport, setLoadingReport] =
    useState(false);

  const [openAccounts, setOpenAccounts] = useState<
    Record<string, boolean>
  >({});

  const [error, setError] = useState("");

  useEffect(() => {
    async function loadCompanies() {
      setLoadingCompanies(true);
      setError("");

      try {
        const result = await companyService.getAll();

        setCompanies(result);

        const defaultCompany =
          result.find(
            (company) => company.isActive !== false
          ) ?? result[0];

        if (defaultCompany) {
          setFilters((current) => ({
            ...current,
            companyId: defaultCompany.id,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Şirketler alınamadı."
        );
      } finally {
        setLoadingCompanies(false);
      }
    }

    void loadCompanies();
  }, []);

  async function loadReport(
    currentFilters: FilterForm
  ) {
    if (!currentFilters.companyId) {
      setReport(null);
      return;
    }

    setLoadingReport(true);
    setError("");

    try {
      const result =
        await accountingReportService.getGeneralLedger({
          companyId: currentFilters.companyId,
          startDate:
            currentFilters.startDate || undefined,
          endDate:
            currentFilters.endDate || undefined,
          accountCode:
            currentFilters.accountCode || undefined,
          search:
            currentFilters.search || undefined,
        });

      setReport(result);

      setOpenAccounts(
        Object.fromEntries(
          result.accounts.map((account) => [
            account.accountingAccountId,
            true,
          ])
        )
      );
    } catch (err) {
      setReport(null);

      setError(
        err instanceof Error
          ? err.message
          : "Büyük Defter raporu alınamadı."
      );
    } finally {
      setLoadingReport(false);
    }
  }

  useEffect(() => {
    if (filters.companyId) {
      void loadReport(filters);
    }
  }, [filters.companyId]);

  function submit(event: FormEvent) {
    event.preventDefault();
    void loadReport(filters);
  }

  function clearFilters() {
    const cleared: FilterForm = {
      ...initialFilters,
      companyId: filters.companyId,
    };

    setFilters(cleared);
    void loadReport(cleared);
  }

  function toggleAccount(id: string) {
    setOpenAccounts((current) => ({
      ...current,
      [id]: !current[id],
    }));
  }

  const summary = useMemo(
    () =>
      report?.summary ?? {
        accountCount: 0,
        voucherCount: 0,
        lineCount: 0,
        totalDebit: 0,
        totalCredit: 0,
        difference: 0,
      },
    [report]
  );

  /** Süzgeç değişince sayfa 1'e döner. */
  const filterKey = JSON.stringify(filters);

  /*
   * SÜTUNLAR VERİ OLARAK (F4j).
   *
   * BAKİYE SÜTUNUNDA ALT TOPLAM YOK: bu YÜRÜYEN bakiye, satır satır
   * devreden bir sayı. Toplamak matematiksel olarak anlamsız bir
   * rakam üretirdi. Borç ve alacak toplanır, bakiye toplanmaz.
   */
  const ledgerColumns: DataTableColumn<
    GeneralLedgerReportResponse["accounts"][number]["lines"][number]
  >[] = [
    {
      key: "tarih",
      header: "Tarih",
      value: (row) => date.format(new Date(row.voucherDate)),
    },
    {
      key: "fis",
      header: "Fiş No",
      value: (row) =>
        row.referenceNumber
          ? `${row.voucherNumber} (Ref: ${row.referenceNumber})`
          : row.voucherNumber,
      render: (row) => (
        <>
          <Link
            href={`/muhasebe/fisler/${row.voucherId}`}
            style={{ fontWeight: 700, textDecoration: "none" }}
          >
            {row.voucherNumber}
          </Link>

          {row.referenceNumber && <small>Ref: {row.referenceNumber}</small>}
        </>
      ),
    },
    {
      key: "tip",
      header: "Fiş Tipi",
      value: (row) => voucherTypeLabels[row.voucherType] ?? "Bilinmiyor",
    },
    { key: "aciklama", header: "Açıklama", value: (row) => row.description ?? "—" },
    {
      key: "cari",
      header: "Cari",
      value: (row) =>
        [row.currentAccountCode, row.currentAccountTitle]
          .filter(Boolean)
          .join(" — ") || "—",
      render: (row) => (
        <>
          {row.currentAccountCode && <strong>{row.currentAccountCode}</strong>}
          <small>{row.currentAccountTitle ?? "—"}</small>
        </>
      ),
    },
    {
      key: "proje",
      header: "Proje",
      value: (row) =>
        [row.projectCode, row.projectName].filter(Boolean).join(" — ") || "—",
      render: (row) => (
        <>
          {row.projectCode && <strong>{row.projectCode}</strong>}
          <small>{row.projectName ?? "—"}</small>
        </>
      ),
    },
    {
      key: "masraf",
      header: "Masraf Merkezi",
      value: (row) => row.costCenterCode ?? "—",
    },
    { key: "belge", header: "Belge No", value: (row) => row.documentNumber ?? "—" },
    { key: "kaynak", header: "Kaynak", value: (row) => row.sourceModule ?? "MANUAL" },
    {
      key: "borc",
      header: "Borç",
      numeric: true,
      value: (row) => (row.debitAmount > 0 ? money(row.debitAmount) : "—"),
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.debitAmount, 0)),
    },
    {
      key: "alacak",
      header: "Alacak",
      numeric: true,
      value: (row) => (row.creditAmount > 0 ? money(row.creditAmount) : "—"),
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.creditAmount, 0)),
    },
    {
      key: "bakiye",
      header: "Bakiye",
      numeric: true,
      value: (row) => balanceLabel(row.balance),
      render: (row) => (
        <strong>{balanceLabel(row.balance)}</strong>
      ),
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title="Büyük Defter"
      description="Kesinleşmiş muhasebe hareketlerini hesap bazında inceleyin"
    >
      <div className="erp-toolbar">
        <div>
          <strong>Muhasebe Raporları</strong>

          <small>
            Açılış, dönem hareketleri ve yürüyen
            bakiye
          </small>
        </div>

        <div className="erp-actions">
          <Link
            href="/muhasebe/yevmiye"
            className="erp-secondary-button"
            style={{ textDecoration: "none" }}
          >
            Yevmiye Defteri
          </Link>

          <Link
            href="/muhasebe"
            className="erp-secondary-button"
            style={{ textDecoration: "none" }}
          >
            Muhasebeye Dön
          </Link>
        </div>
      </div>

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <form
        onSubmit={submit}
        className="erp-form-card"
      >
        <div className="erp-form-grid">
          <label>
            <span>Şirket *</span>

            <select
              required
              disabled={
                loadingCompanies || loadingReport
              }
              value={filters.companyId}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  companyId: event.target.value,
                }))
              }
            >
              <option value="">
                Şirket seçin
              </option>

              {companies.map((company) => (
                <option
                  key={company.id}
                  value={company.id}
                >
                  {company.code} - {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Başlangıç Tarihi</span>

            <input
              type="date"
              value={filters.startDate}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  startDate: event.target.value,
                }))
              }
            />
          </label>

          <label>
            <span>Bitiş Tarihi</span>

            <input
              type="date"
              value={filters.endDate}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  endDate: event.target.value,
                }))
              }
            />
          </label>

          <label>
            <span>Hesap Kodu</span>

            <input
              value={filters.accountCode}
              placeholder="Örn. 100, 120, 320"
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  accountCode: event.target.value,
                }))
              }
            />
          </label>

          <label className="span-2">
            <span>Genel Arama</span>

            <input
              value={filters.search}
              placeholder="Fiş no, hesap, cari, proje, açıklama veya belge no"
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  search: event.target.value,
                }))
              }
            />
          </label>
        </div>

        <div
          className="erp-actions"
          style={{
            justifyContent: "flex-end",
            marginTop: 16,
          }}
        >
          <button
            type="button"
            className="erp-secondary-button"
            disabled={loadingReport}
            onClick={clearFilters}
          >
            Temizle
          </button>

          <button
            type="submit"
            className="erp-primary-button"
            disabled={
              loadingReport || !filters.companyId
            }
          >
            {loadingReport
              ? "Rapor Hazırlanıyor..."
              : "Raporu Getir"}
          </button>
        </div>
      </form>

      <section
        style={{
          display: "grid",
          gridTemplateColumns:
            "repeat(auto-fit, minmax(190px, 1fr))",
          gap: 12,
          margin: "16px 0",
        }}
      >
        <Summary
          label="Hesap Sayısı"
          value={String(summary.accountCount)}
        />

        <Summary
          label="Fiş Sayısı"
          value={String(summary.voucherCount)}
        />

        <Summary
          label="Satır Sayısı"
          value={String(summary.lineCount)}
        />

        <Summary
          label="Toplam Borç"
          value={money(summary.totalDebit)}
        />

        <Summary
          label="Toplam Alacak"
          value={money(summary.totalCredit)}
        />

        <Summary
          label="Fark"
          value={money(summary.difference)}
        />
      </section>

      {loadingReport && (
        <div className="erp-form-card">
          Büyük Defter hazırlanıyor...
        </div>
      )}

      {!loadingReport &&
        report?.accounts.length === 0 && (
          <div className="erp-form-card">
            Seçilen filtrelerde kesinleşmiş
            muhasebe hareketi bulunamadı.
          </div>
        )}

      {!loadingReport &&
        report?.accounts.map((account) => {
          const open =
            openAccounts[
              account.accountingAccountId
            ] ?? true;

          return (
            <section
              key={account.accountingAccountId}
              className="erp-table-card"
              style={{ marginBottom: 16 }}
            >
              <div className="erp-toolbar">
                <div>
                  <strong>
                    {account.accountCode} -{" "}
                    {account.accountName}
                  </strong>

                  <small>
                    {account.lines.length} hareket
                  </small>
                </div>

                <div className="erp-actions">
                  <span className="erp-status gray">
                    Açılış:{" "}
                    {balanceLabel(
                      account.openingBalance
                    )}
                  </span>

                  <span className="erp-status green">
                    Kapanış:{" "}
                    {balanceLabel(
                      account.closingBalance
                    )}
                  </span>

                  <button
                    type="button"
                    className="erp-secondary-button"
                    onClick={() =>
                      toggleAccount(
                        account.accountingAccountId
                      )
                    }
                  >
                    {open ? "Daralt" : "Göster"}
                  </button>
                </div>
              
          <Button variant="secondary" disabled={loadingReport} onClick={() => void loadReport(filters)}>Yenile</Button>
        </div>

              {open && (
                <>
                  <div
                    style={{
                      display: "grid",
                      gridTemplateColumns:
                        "repeat(auto-fit, minmax(180px, 1fr))",
                      gap: 10,
                      padding: "0 16px 16px",
                    }}
                  >
                    <MiniSummary
                      label="Açılış Bakiyesi"
                      value={balanceLabel(
                        account.openingBalance
                      )}
                    />

                    <MiniSummary
                      label="Dönem Borç"
                      value={money(
                        account.periodDebit
                      )}
                    />

                    <MiniSummary
                      label="Dönem Alacak"
                      value={money(
                        account.periodCredit
                      )}
                    />

                    <MiniSummary
                      label="Kapanış Bakiyesi"
                      value={balanceLabel(
                        account.closingBalance
                      )}
                    />
                  </div>

                  <div style={{ overflowX: "auto" }}>
                    <DataTable
                      rows={account.lines}
                      columns={ledgerColumns}
                      rowKey={(row) => `${row.voucherId}-${row.lineNumber}`}
                      title={`Büyük Defter — ${account.accountCode}`}
                      resetKey={`${account.accountCode}|${filterKey}`}
                    />
                  </div>
                </>
              )}
            </section>
          );
        })}
    </ErpShell>
  );
}

function Summary({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div className="erp-form-card">
      <small>{label}</small>

      <strong
        style={{
          display: "block",
          marginTop: 8,
          fontSize: 21,
        }}
      >
        {value}
      </strong>
    </div>
  );
}

function MiniSummary({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div
      style={{
        border: "1px solid var(--erp-border)",
        borderRadius: 10,
        padding: 12,
      }}
    >
      <small>{label}</small>

      <strong
        style={{
          display: "block",
          marginTop: 6,
        }}
      >
        {value}
      </strong>
    </div>
  );
}
