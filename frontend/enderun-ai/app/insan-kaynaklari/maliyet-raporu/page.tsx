"use client";

import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
import { money } from "@/lib/format/turkish";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { Button } from "@/components/ui";
import {
  hrPayrollService,
  type PayrollCostReport,
} from "@/services/hr-payroll.service";

const MONTHS = [
  "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
  "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
];

const PAYROLL_STATUS_LABELS: Record<number, string> = {
  0: "Taslak",
  1: "Hesaplandı",
  2: "Onaylandı",
  3: "Ödendi",
};

const PAYROLL_STATUS_COLORS: Record<number, string> = {
  0: "gray",
  1: "blue",
  2: "yellow",
  3: "green",
};

/*
 * BORDRO ÖZETİ SÜTUNLARI (F4q) — `Row` bileşeninin yerine.
 *
 * Özet başlıksız etiket/tutar çiftleriydi. Sütuna çevrilince dışa
 * aktarma da kazandı: aylık bordro özetinin kırılımı artık dosyaya
 * çıkıyor ve eksi kalemler işaretiyle birlikte yazılıyor.
 *
 * ALT TOPLAM YOK: bu liste kendi içinde zaten toplamlar içeriyor
 * (brüt, kesinti, net, işveren maliyeti). Hepsini bir kez daha
 * toplamak anlamsız — hatta yanıltıcı — bir rakam verirdi.
 */
type SummaryRow = { label: string; value: number; strong?: boolean; negative?: boolean };

function summaryRows(totals: PayrollCostReport["totals"]): SummaryRow[] {
  return [
    { label: "Normal ücret", value: totals.normalWorkAmount },
    { label: "Fazla mesai", value: totals.overtimeAmount },
    { label: "Tatil çalışması", value: totals.holidayAmount },
    { label: "Toplam brüt kazanç", value: totals.totalEarnings, strong: true },
    { label: "SGK işçi payı", value: totals.sgkEmployee, negative: true },
    { label: "İşsizlik işçi payı", value: totals.unemploymentEmployee, negative: true },
    { label: "Gelir vergisi", value: totals.incomeTax, negative: true },
    { label: "Damga vergisi", value: totals.stampTax, negative: true },
    { label: "Avans ve diğer kesintiler", value: totals.advanceAndOther, negative: true },
    { label: "Toplam kesinti", value: totals.totalDeductions, strong: true, negative: true },
    { label: "Net ödenecek", value: totals.netPayable, strong: true },
    { label: "SGK işveren payı", value: totals.sgkEmployer },
    { label: "İşsizlik işveren payı", value: totals.unemploymentEmployer },
    { label: "İşverene toplam maliyet", value: totals.totalEmployerCost, strong: true },
  ];
}

const summaryColumns: DataTableColumn<SummaryRow>[] = [
  {
    key: "kalem",
    header: "Kalem",
    value: (row) => row.label,
    render: (row) => (row.strong ? <strong>{row.label}</strong> : row.label),
  },
  {
    key: "tutar",
    header: "Tutar",
    numeric: true,
    value: (row) => `${row.negative ? "−" : ""}${money(row.value)}`,
    render: (row) => {
      const text = `${row.negative ? "−" : ""}${money(row.value)}`;
      return row.strong ? <strong>{text}</strong> : text;
    },
  },
];


export default function PayrollCostReportPage() {
  const now = new Date();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);

  const [report, setReport] = useState<PayrollCostReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const loadCompanies = useCallback(async () => {
    try {
      const result = await companyService.getAll();
      setCompanies(result);
      setCompanyId((current) => current || result[0]?.id || "");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
    }
  }, []);

  const loadReport = useCallback(async () => {
    if (!companyId) {
      setReport(null);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      setReport(await hrPayrollService.getCostReport(companyId, year, month));
    } catch (err) {
      setReport(null);
      setError(err instanceof Error ? err.message : "Maliyet raporu alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId, year, month]);

  useEffect(() => {
    void loadCompanies();
  }, [loadCompanies]);

  useEffect(() => {
    void loadReport();
  }, [loadReport]);

  const totals = report?.totals;

  /* SÜTUNLAR VERİ OLARAK (F4q). */
  const personnelColumns: DataTableColumn<
    NonNullable<typeof report>["personnel"][number]
  >[] = [
    {
      key: "personel",
      header: "Personel",
      value: (row) => `${row.fullName ?? "—"} (${row.employeeNumber})`,
      render: (row) => (
        <>
          <strong>{row.fullName ?? "—"}</strong>
          <small>{row.employeeNumber}</small>
        </>
      ),
    },
    { key: "gorev", header: "Görev", value: (row) => row.jobTitle ?? "—" },
    {
      key: "brut",
      header: "Brüt",
      numeric: true,
      value: (row) => money(row.grossSalary),
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.grossSalary, 0)),
    },
    {
      key: "mesai",
      header: "Fazla Mesai",
      numeric: true,
      value: (row) => (row.overtimeAmount > 0 ? money(row.overtimeAmount) : "—"),
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.overtimeAmount, 0)),
    },
    {
      key: "kesinti",
      header: "Kesinti",
      numeric: true,
      value: (row) => money(row.totalDeductions),
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.totalDeductions, 0)),
    },
    {
      key: "net",
      header: "Net",
      numeric: true,
      value: (row) => money(row.officialNetPayableAmount),
      render: (row) => <strong>{money(row.officialNetPayableAmount)}</strong>,
      footer: (rows) =>
        money(rows.reduce((sum, row) => sum + row.officialNetPayableAmount, 0)),
    },
    {
      key: "maliyet",
      header: "İşveren Maliyeti",
      numeric: true,
      value: (row) => money(row.totalEmployerCost),
      footer: (rows) =>
        money(rows.reduce((sum, row) => sum + row.totalEmployerCost, 0)),
    },
    {
      key: "durum",
      header: "Durum",
      value: (row) => PAYROLL_STATUS_LABELS[row.status] ?? "—",
      render: (row) => (
        <span className={`erp-status ${PAYROLL_STATUS_COLORS[row.status] ?? "gray"}`}>
          {PAYROLL_STATUS_LABELS[row.status] ?? "—"}
        </span>
      ),
    },
  ];

  const breakdownColumns: DataTableColumn<
    NonNullable<typeof report>["projectBreakdown"][number]
  >[] = [
    {
      key: "proje",
      header: "Proje",
      value: (row) => `${row.projectCode ?? "—"} — ${row.projectName}`,
      render: (row) => (
        <>
          <strong>{row.projectCode ?? "—"}</strong>
          <small>{row.projectName}</small>
        </>
      ),
    },
    {
      key: "santiye",
      header: "Şantiye",
      value: (row) => (row.siteCode ? `${row.siteCode} — ${row.siteName}` : "Şantiyesiz"),
      render: (row) =>
        row.siteCode ? (
          <>
            {row.siteCode}
            <small>{row.siteName}</small>
          </>
        ) : (
          "Şantiyesiz"
        ),
    },
    {
      key: "gun",
      header: "Gün",
      numeric: true,
      value: (row) => row.dayCount,
      footer: (rows) => rows.reduce((sum, row) => sum + row.dayCount, 0),
    },
    {
      key: "normal",
      header: "Normal",
      numeric: true,
      value: (row) => money(row.normalCost),
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.normalCost, 0)),
    },
    {
      key: "mesai",
      header: "Fazla Mesai",
      numeric: true,
      value: (row) => money(row.overtimeCost),
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.overtimeCost, 0)),
    },
    {
      key: "tatil",
      header: "Tatil",
      numeric: true,
      value: (row) => money(row.holidayCost),
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.holidayCost, 0)),
    },
    {
      key: "toplam",
      header: "Toplam",
      numeric: true,
      value: (row) => money(row.totalCost),
      render: (row) => <strong>{money(row.totalCost)}</strong>,
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.totalCost, 0)),
    },
  ];

  return (
    <ErpShell
      design="redwood"
      title="Bordro Maliyet Raporu"
      description="Aylık brütten işverene toplam maliyete kadar kırılım ve proje/şantiye dağılımı"
    >
      {/* Rapor bordro muhasebeleştirildikçe değişiyor. */}
      <div className="mb-4 flex justify-end">
        <Button variant="secondary" onClick={() => void loadReport()}>Yenile</Button>
      </div>
      <div className="erp-page-toolbar">
        <div>
          {report && (
            <>
              <strong>
                {report.personnelCount} personel · toplam işveren maliyeti{" "}
                {money(report.totals.totalEmployerCost)}
              </strong>
              <small style={{ display: "block", marginTop: "4px" }}>
                {report.paidCount} bordro ödenmiş
              </small>
            </>
          )}
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          <select value={String(month)} onChange={(e) => setMonth(Number(e.target.value))}>
            {MONTHS.map((name, index) => (
              <option key={name} value={String(index + 1)}>
                {name}
              </option>
            ))}
          </select>

          <input
            type="number"
            min={2020}
            max={2100}
            value={String(year)}
            onChange={(e) => setYear(Number(e.target.value))}
            style={{ width: "6rem" }}
          />
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      {loading ? (
        <div className="erp-loading">Rapor hazırlanıyor...</div>
      ) : !report || !totals ? (
        <div className="erp-empty-state">
          <strong>Veri yok</strong>
          <p>Seçili dönem için bordro bulunmuyor.</p>
        </div>
      ) : report.personnelCount === 0 ? (
        <div className="erp-empty-state">
          <strong>Bu dönemde bordro yok</strong>
          <p>
            {MONTHS[month - 1]} {year} için hesaplanmış bordro bulunmuyor.
          </p>
        </div>
      ) : (
        <>
          <div className="erp-table-card" style={{ marginBottom: "16px" }}>
            <div className="erp-table-header">
              <h2>
                {MONTHS[month - 1]} {year} Özeti
              </h2>
            </div>

            <DataTable
              rows={summaryRows(totals)}
              columns={summaryColumns}
              rowKey={(row) => row.label}
              title={`${MONTHS[month - 1]} ${year} Bordro Özeti`}
            />

            {(totals.incomeTaxExemption > 0 || totals.stampTaxExemption > 0) && (
              <div style={{ padding: "0 16px 16px" }}>
                <small>
                  Asgari ücret istisnası sayesinde kesilmeyen vergi:{" "}
                  {money(totals.incomeTaxExemption)} gelir,{" "}
                  {money(totals.stampTaxExemption)} damga.
                </small>
              </div>
            )}
          </div>

          <div className="erp-table-card" style={{ marginBottom: "16px" }}>
            <div className="erp-table-header">
              <h2>Personel Kırılımı</h2>
            </div>

            <DataTable
              rows={report.personnel}
              columns={personnelColumns}
              rowKey={(row) => row.personnelId}
              title="Personel Kırılımı"
              resetKey={`${companyId}|${year}|${month}`}
            />
          </div>

          <div className="erp-table-card">
            <div className="erp-table-header">
              <h2>Proje / Şantiye İşçilik Dağılımı</h2>
            </div>

            {report.projectBreakdown.length === 0 ? (
              <div className="erp-empty-state">
                <strong>Dağılım yok</strong>
                <p>
                  Bu dönemde projeye bağlı onaylı puantaj bulunmadığı için
                  işçilik maliyeti dağıtılamadı.
                </p>
              </div>
            ) : (
              <DataTable
                rows={report.projectBreakdown}
                columns={breakdownColumns}
                rowKey={(row) => `${row.projectId}-${row.projectSiteId ?? "genel"}`}
                title="Proje / Şantiye İşçilik Dağılımı"
                resetKey={`${companyId}|${year}|${month}`}
              />
            )}
          </div>
        </>
      )}
    </ErpShell>
  );
}
