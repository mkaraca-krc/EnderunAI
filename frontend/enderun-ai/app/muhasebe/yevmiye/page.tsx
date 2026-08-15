"use client";

import Link from "next/link";
import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { amount, money } from "@/lib/format/turkish";
import { Button } from "@/components/ui";

import {
  accountingReportService,
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
          <table
            className="erp-table"
            style={{ minWidth: 1550 }}
          >
            <thead>
              <tr>
                <th>Tarih</th>
                <th>Fiş No</th>
                <th>Fiş Tipi</th>
                <th>Satır</th>
                <th>Hesap</th>
                <th>Açıklama</th>
                <th>Cari Hesap</th>
                <th>Proje</th>
                <th>Masraf Merkezi</th>
                <th>Belge No</th>
                <th>Belge Tarihi</th>
                <th>Kaynak</th>
                <th className="num">Kur</th>
                <th className="num">Borç</th>
                <th className="num">Alacak</th>
              </tr>
            </thead>

            <tbody>
              {!loadingReport &&
                report?.lines.length === 0 && (
                  <tr>
                    <td
                      colSpan={15}
                      style={{
                        textAlign: "center",
                        padding: 30,
                      }}
                    >
                      Seçilen filtrelerde kesinleşmiş
                      muhasebe kaydı bulunamadı.
                    </td>
                  </tr>
                )}

              {loadingReport && (
                <tr>
                  <td
                    colSpan={15}
                    style={{
                      textAlign: "center",
                      padding: 30,
                    }}
                  >
                    Yevmiye kayıtları yükleniyor...
                  </td>
                </tr>
              )}

              {report?.lines.map((line) => (
                <tr
                  key={`${line.voucherId}-${line.lineNumber}`}
                >
                  <td>
                    {date.format(
                      new Date(line.voucherDate)
                    )}
                  </td>

                  <td>
                    <Link
                      href={`/muhasebe/fisler/${line.voucherId}`}
                      style={{
                        fontWeight: 700,
                        textDecoration: "none",
                      }}
                    >
                      {line.voucherNumber}
                    </Link>

                    {line.referenceNumber && (
                      <small>
                        Ref: {line.referenceNumber}
                      </small>
                    )}
                  </td>

                  <td>
                    {voucherTypeLabels[
                      line.voucherType
                    ] ?? "Bilinmiyor"}
                  </td>

                  <td>{line.lineNumber}</td>

                  <td>
                    <strong>
                      {line.accountCode}
                    </strong>

                    <small>
                      {line.accountName}
                    </small>
                  </td>

                  <td>
                    {line.lineDescription ??
                      line.voucherDescription ??
                      "—"}
                  </td>

                  <td>
                    {line.currentAccountCode && (
                      <strong>
                        {line.currentAccountCode}
                      </strong>
                    )}

                    <small>
                      {line.currentAccountTitle ??
                        "—"}
                    </small>
                  </td>

                  <td>
                    {line.projectCode && (
                      <strong>
                        {line.projectCode}
                      </strong>
                    )}

                    <small>
                      {line.projectName ?? "—"}
                    </small>
                  </td>

                  <td>
                    {line.costCenterCode ?? "—"}
                  </td>

                  <td>
                    {line.documentNumber ?? "—"}
                  </td>

                  <td>
                    {line.documentDate
                      ? date.format(
                          new Date(
                            line.documentDate
                          )
                        )
                      : "—"}
                  </td>

                  <td>
                    {line.sourceModule ?? "MANUAL"}
                  </td>

                  <td>
                    {line.currencyCode} /{" "}
                    {amount(
                      line.exchangeRate
                    )}
                  </td>

                  <td
                    className="num"
                    style={{ fontWeight: line.debitAmountLocal > 0 ? 700 : 400, }}
                  >
                    {line.debitAmountLocal > 0
                      ? money(
                          line.debitAmountLocal
                        )
                      : "—"}
                  </td>

                  <td
                    className="num"
                    style={{ fontWeight: line.creditAmountLocal > 0 ? 700 : 400, }}
                  >
                    {line.creditAmountLocal > 0
                      ? money(
                          line.creditAmountLocal
                        )
                      : "—"}
                  </td>
                </tr>
              ))}
            </tbody>

            {report &&
              report.lines.length > 0 && (
                <tfoot>
                  <tr>
                    <td colSpan={13}>
                      <strong>GENEL TOPLAM</strong>
                    </td>

                    <td
                      className="num"
                    >
                      <strong>
                        {money(
                          summary.totalDebit
                        )}
                      </strong>
                    </td>

                    <td
                      className="num"
                    >
                      <strong>
                        {money(
                          summary.totalCredit
                        )}
                      </strong>
                    </td>
                  </tr>
                </tfoot>
              )}
          </table>
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
