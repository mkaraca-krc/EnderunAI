"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  salesInvoiceService,
  SALES_INVOICE_STATUS_COLORS,
  SALES_INVOICE_STATUS_LABELS,
  type SalesInvoiceListItem,
} from "@/services/sales-invoice.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

export default function SalesInvoicesPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [items, setItems] = useState<SalesInvoiceListItem[]>([]);

  const [companyId, setCompanyId] = useState("");
  const [status, setStatus] = useState("");
  const [search, setSearch] = useState("");

  const [loading, setLoading] = useState(true);
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

  const loadItems = useCallback(async () => {
    if (!companyId) {
      setItems([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const result = await salesInvoiceService.getAll({
        companyId,
        status: status === "" ? undefined : Number(status),
        search: search.trim() || undefined,
      });
      setItems(result);
    } catch (err) {
      setItems([]);
      setError(err instanceof Error ? err.message : "Faturalar alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId, status, search]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadItems(), 300);
    return () => window.clearTimeout(timer);
  }, [loadItems]);

  const summary = useMemo(
    () => ({
      count: items.length,
      draft: items.filter((item) => item.status === 0).length,
      review: items.filter((item) => item.requiresManualReview).length,
      total: items.reduce((sum, item) => sum + item.grandTotal, 0),
    }),
    [items]
  );

  return (
    <ErpShell
      design="redwood"
      title="Satış Faturaları"
      description="Hakediş dışı satışlar; kesinleştiğinde 120/600/391 gelir fişi otomatik oluşur"
    >
      <div className="erp-page-toolbar">
        <div>
          <strong>{summary.count} fatura</strong>
          {summary.draft > 0 && (
            <span className="erp-status gray" style={{ marginLeft: "10px" }}>
              {summary.draft} taslak
            </span>
          )}
          {summary.review > 0 && (
            <span className="erp-status yellow" style={{ marginLeft: "10px" }}>
              {summary.review} kontrol bekliyor
            </span>
          )}
          <small style={{ display: "block", marginTop: "4px" }}>
            Toplam: {money(summary.total)}
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px" }}>
          <Link className="erp-secondary-button" href="/muhasebe/e-fatura-ice-aktar">
            E-Fatura İçe Aktar
          </Link>
          <Link className="erp-primary-button" href="/muhasebe/satis-faturalari/yeni">
            + Yeni Satış Faturası
          </Link>
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Fatura Listesi</h2>

          <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
            <select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>

            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">Tüm durumlar</option>
              {Object.entries(SALES_INVOICE_STATUS_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>

            <input
              type="text"
              placeholder="Fatura no / müşteri ara..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
        </div>

        {loading ? (
          <div className="erp-loading">Faturalar yükleniyor...</div>
        ) : items.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Satış faturası bulunmuyor</strong>
            <p>
              Hakediş dışı bir satış için yeni fatura kesin veya kestiğiniz
              e-faturanın XML&apos;ini içe aktarın.
            </p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Fatura No</th>
                  <th>Müşteri</th>
                  <th>Proje</th>
                  <th>Tarih</th>
                  <th>Tutar</th>
                  <th>Tahsil Edilecek</th>
                  <th>Durum</th>
                  <th>Fiş</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <Link href={`/muhasebe/satis-faturalari/${item.id}`}>
                        <strong>{item.officialInvoiceNumber ?? "(numara yok)"}</strong>
                      </Link>
                      <small>{item.internalNumber}</small>
                    </td>
                    <td>{item.customerTitle}</td>
                    <td>
                      {item.projectCode ?? "—"}
                      {item.projectName && <small>{item.projectName}</small>}
                    </td>
                    <td>{dateFormat.format(new Date(item.invoiceDate))}</td>
                    <td>
                      <strong>{money(item.grandTotal)}</strong>
                      <small>KDV: {money(item.vatTotal)}</small>
                    </td>
                    <td>
                      {money(item.netReceivableAmount)}
                      {item.withholdingAmount > 0 && (
                        <small>Tevkifat: {money(item.withholdingAmount)}</small>
                      )}
                    </td>
                    <td>
                      <span
                        className={`erp-status ${SALES_INVOICE_STATUS_COLORS[item.status] ?? "gray"}`}
                      >
                        {SALES_INVOICE_STATUS_LABELS[item.status] ?? "—"}
                      </span>
                      {item.requiresManualReview && <small>Elle kontrol gerekli</small>}
                    </td>
                    <td>{item.accountingVoucherNumber ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </ErpShell>
  );
}
