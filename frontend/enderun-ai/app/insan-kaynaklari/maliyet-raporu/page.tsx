"use client";

import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
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

function Row({
  label,
  value,
  strong,
  negative,
}: {
  label: string;
  value: number;
  strong?: boolean;
  negative?: boolean;
}) {
  return (
    <tr>
      <td>{strong ? <strong>{label}</strong> : label}</td>
      <td style={{ textAlign: "right" }}>
        {strong ? (
          <strong>
            {negative ? "−" : ""}
            {money(value)}
          </strong>
        ) : (
          <>
            {negative ? "−" : ""}
            {money(value)}
          </>
        )}
      </td>
    </tr>
  );
}

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

            <div className="erp-table-wrap">
              <table className="erp-table">
                <tbody>
                  <Row label="Normal ücret" value={totals.normalWorkAmount} />
                  <Row label="Fazla mesai" value={totals.overtimeAmount} />
                  <Row label="Tatil çalışması" value={totals.holidayAmount} />
                  <Row label="Toplam brüt kazanç" value={totals.totalEarnings} strong />

                  <Row label="SGK işçi payı" value={totals.sgkEmployee} negative />
                  <Row
                    label="İşsizlik işçi payı"
                    value={totals.unemploymentEmployee}
                    negative
                  />
                  <Row label="Gelir vergisi" value={totals.incomeTax} negative />
                  <Row label="Damga vergisi" value={totals.stampTax} negative />
                  <Row
                    label="Avans ve diğer kesintiler"
                    value={totals.advanceAndOther}
                    negative
                  />
                  <Row label="Toplam kesinti" value={totals.totalDeductions} strong negative />

                  <Row label="Net ödenecek" value={totals.netPayable} strong />

                  <Row label="SGK işveren payı" value={totals.sgkEmployer} />
                  <Row
                    label="İşsizlik işveren payı"
                    value={totals.unemploymentEmployer}
                  />
                  <Row
                    label="İşverene toplam maliyet"
                    value={totals.totalEmployerCost}
                    strong
                  />
                </tbody>
              </table>
            </div>

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

            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Personel</th>
                    <th>Görev</th>
                    <th style={{ textAlign: "right" }}>Brüt</th>
                    <th style={{ textAlign: "right" }}>Fazla Mesai</th>
                    <th style={{ textAlign: "right" }}>Kesinti</th>
                    <th style={{ textAlign: "right" }}>Net</th>
                    <th style={{ textAlign: "right" }}>İşveren Maliyeti</th>
                    <th>Durum</th>
                  </tr>
                </thead>
                <tbody>
                  {report.personnel.map((person) => (
                    <tr key={person.personnelId}>
                      <td>
                        <strong>{person.fullName ?? "—"}</strong>
                        <small>{person.employeeNumber}</small>
                      </td>
                      <td>{person.jobTitle ?? "—"}</td>
                      <td style={{ textAlign: "right" }}>
                        {money(person.grossSalary)}
                      </td>
                      <td style={{ textAlign: "right" }}>
                        {person.overtimeAmount > 0
                          ? money(person.overtimeAmount)
                          : "—"}
                      </td>
                      <td style={{ textAlign: "right" }}>
                        {money(person.totalDeductions)}
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <strong>{money(person.officialNetPayableAmount)}</strong>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        {money(person.totalEmployerCost)}
                      </td>
                      <td>
                        <span
                          className={`erp-status ${
                            PAYROLL_STATUS_COLORS[person.status] ?? "gray"
                          }`}
                        >
                          {PAYROLL_STATUS_LABELS[person.status] ?? "—"}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
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
              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Proje</th>
                      <th>Şantiye</th>
                      <th style={{ textAlign: "right" }}>Gün</th>
                      <th style={{ textAlign: "right" }}>Normal</th>
                      <th style={{ textAlign: "right" }}>Fazla Mesai</th>
                      <th style={{ textAlign: "right" }}>Tatil</th>
                      <th style={{ textAlign: "right" }}>Toplam</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.projectBreakdown.map((row) => (
                      <tr key={`${row.projectId}-${row.projectSiteId ?? "genel"}`}>
                        <td>
                          <strong>{row.projectCode ?? "—"}</strong>
                          <small>{row.projectName}</small>
                        </td>
                        <td>
                          {row.siteCode ? (
                            <>
                              {row.siteCode}
                              <small>{row.siteName}</small>
                            </>
                          ) : (
                            "Şantiyesiz"
                          )}
                        </td>
                        <td style={{ textAlign: "right" }}>{row.dayCount}</td>
                        <td style={{ textAlign: "right" }}>
                          {money(row.normalCost)}
                        </td>
                        <td style={{ textAlign: "right" }}>
                          {money(row.overtimeCost)}
                        </td>
                        <td style={{ textAlign: "right" }}>
                          {money(row.holidayCost)}
                        </td>
                        <td style={{ textAlign: "right" }}>
                          <strong>{money(row.totalCost)}</strong>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </>
      )}
    </ErpShell>
  );
}
