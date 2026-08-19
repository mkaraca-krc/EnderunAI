"use client";

import Link from "next/link";
import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";

import ErpShell from "@/components/erp/erp-shell";
import { amount, money } from "@/lib/format/turkish";
import { Button } from "@/components/ui";

import {
  accountingReportService,
  type JournalReportLine,
  type JournalReportResponse,
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
const currentYear = now.getFullYear();

const initialFilters: FilterForm = {
  companyId: "",
  startDate: `${currentYear}-01-01`,
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

export default function JournalPage() {
  const [companies, setCompanies] = useState<
    CompanyListItem[]
  >([]);

  const [filters, setFilters] =
    useState<FilterForm>(initialFilters);

  const [report, setReport] =
    useState<JournalReportResponse | null>(null);

  const [loadingCompanies, setLoadingCompanies] =
    useState(true);

  const [loadingReport, setLoadingReport] =
    useState(false);

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
        await accountingReportService.getJournal({
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
    } catch (err) {
      setReport(null);

      setError(
        err instanceof Error
          ? err.message
          : "Yevmiye Defteri alınamadı."
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

  const summary = useMemo(() => {
    return (
      report?.summary ?? {
        voucherCount: 0,
        lineCount: 0,
        totalDebit: 0,
        totalCredit: 0,
        difference: 0,
      }
    );
  }, [report]);

  /*
   * ALT TOPLAM RAPORUN KENDİ ÖZETİNDEN GELİR, satırları toplayarak
   * değil. Rapor borç/alacak toplamını sunucuda hesaplıyor; burada
   * yeniden toplamak iki ayrı gerçek üretme riski taşırdı.
   */
  const columns: DataTableColumn<JournalReportLine>[] = [
    {
      key: "tarih",
      header: "Tarih",
      value: (line) => date.format(new Date(line.voucherDate)),
    },
    {
      key: "fis",
      header: "Fiş No",
      value: (line) =>
        line.referenceNumber
          ? `${line.voucherNumber} (Ref: ${line.referenceNumber})`
          : line.voucherNumber,
      render: (line) => (
        <>
          <Link
            href={`/muhasebe/fisler/${line.voucherId}`}
            style={{ fontWeight: 700, textDecoration: "none" }}
          >
            {line.voucherNumber}
          </Link>
          {line.referenceNumber && <small>Ref: {line.referenceNumber}</small>}
        </>
      ),
    },
    {
      key: "fisTipi",
      header: "Fiş Tipi",
      value: (line) => voucherTypeLabels[line.voucherType] ?? "Bilinmiyor",
    },
    { key: "satir", header: "Satır", numeric: true, value: (line) => line.lineNumber },
    {
      key: "hesap",
      header: "Hesap",
      value: (line) => `${line.accountCode} ${line.accountName}`,
      render: (line) => (
        <>
          <strong>{line.accountCode}</strong>
          <small>{line.accountName}</small>
        </>
      ),
    },
    {
      key: "aciklama",
      header: "Açıklama",
      value: (line) =>
        line.lineDescription ?? line.voucherDescription ?? "—",
    },
    {
      key: "cari",
      header: "Cari Hesap",
      value: (line) =>
        [line.currentAccountCode, line.currentAccountTitle]
          .filter(Boolean)
          .join(" ") || "—",
      render: (line) => (
        <>
          {line.currentAccountCode && <strong>{line.currentAccountCode}</strong>}
          <small>{line.currentAccountTitle ?? "—"}</small>
        </>
      ),
    },
    {
      key: "proje",
      header: "Proje",
      value: (line) =>
        [line.projectCode, line.projectName].filter(Boolean).join(" ") || "—",
      render: (line) => (
        <>
          {line.projectCode && <strong>{line.projectCode}</strong>}
          <small>{line.projectName ?? "—"}</small>
        </>
      ),
    },
    {
      key: "masraf",
      header: "Masraf Merkezi",
      value: (line) => line.costCenterCode ?? "—",
    },
    {
      key: "belgeNo",
      header: "Belge No",
      value: (line) => line.documentNumber ?? "—",
    },
    {
      key: "belgeTarihi",
      header: "Belge Tarihi",
      value: (line) =>
        line.documentDate ? date.format(new Date(line.documentDate)) : "—",
    },
    {
      key: "kaynak",
      header: "Kaynak",
      value: (line) => line.sourceModule ?? "MANUAL",
    },
    {
      key: "kur",
      header: "Kur",
      value: (line) => `${line.currencyCode} / ${amount(line.exchangeRate)}`,
    },
    {
      key: "borc",
      header: "Borç",
      numeric: true,
      value: (line) => (line.debitAmountLocal > 0 ? line.debitAmountLocal : ""),
      render: (line) =>
        line.debitAmountLocal > 0 ? (
          <strong>{money(line.debitAmountLocal)}</strong>
        ) : (
          "—"
        ),
      footer: () => <strong>{money(summary.totalDebit)}</strong>,
    },
    {
      key: "alacak",
      header: "Alacak",
      numeric: true,
      value: (line) =>
        line.creditAmountLocal > 0 ? line.creditAmountLocal : "",
      render: (line) =>
        line.creditAmountLocal > 0 ? (
          <strong>{money(line.creditAmountLocal)}</strong>
        ) : (
          "—"
        ),
      footer: () => <strong>{money(summary.totalCredit)}</strong>,
    },
  ];


  return (
    <ErpShell
      design="redwood"
      title="Yevmiye Defteri"
      description="Kesinleşmiş muhasebe fişlerinin tarih ve hesap bazlı dökümü"
    >
      <div className="erp-toolbar">
        <div>
          <strong>Muhasebe Raporları</strong>
          <small>
            Yalnızca kesinleşmiş fişler rapora dahil edilir.
          </small>
        </div>

        <div className="erp-actions">
          <Link
            href="/muhasebe/fisler"
            className="erp-secondary-button"
            style={{ textDecoration: "none" }}
          >
            Muhasebe Fişleri
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
              placeholder="Fiş no, açıklama, hesap, cari, proje veya belge no"
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
            "repeat(auto-fit, minmax(200px, 1fr))",
          gap: 12,
          margin: "16px 0",
        }}
      >
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

      <section className="erp-table-card">
        <div className="erp-toolbar">
          <div>
            <strong>Yevmiye Kayıtları</strong>

            <small>
              {report?.lines.length ?? 0} satır
            </small>
          </div>
        
          <Button variant="secondary" disabled={loadingReport} onClick={() => void loadReport(filters)}>Yenile</Button>
        </div>

        <div style={{ overflowX: "auto" }}>
          <DataTable
              rows={report?.lines ?? []}
              columns={columns}
              rowKey={(line) => `${line.voucherId}-${line.lineNumber}`}
              loading={loadingReport}
              title="Yevmiye Defteri"
              emptyText="Seçilen filtrelerde kesinleşmiş muhasebe kaydı bulunamadı."
              footerLabel="GENEL TOPLAM"
              printMeta={
                <>
                  {filters.startDate} – {filters.endDate}
                  {filters.accountCode && ` · hesap ${filters.accountCode}`}
                  {filters.search && ` · arama "${filters.search}"`}
                </>
              }
              resetKey={`${filters.companyId}|${filters.startDate}|${filters.endDate}|${filters.accountCode}|${filters.search}`}
            />
        </div>
      </section>
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
          fontSize: 22,
        }}
      >
        {value}
      </strong>
    </div>
  );
}
