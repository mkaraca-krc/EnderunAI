"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  supplierInvoiceService,
  MATCH_STATUS_COLORS,
  MATCH_STATUS_LABELS,
  SUPPLIER_INVOICE_STATUS_COLORS,
  SUPPLIER_INVOICE_STATUS_LABELS,
  type SupplierInvoiceListItem,
} from "@/services/supplier-invoice.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

export default function SupplierInvoicesPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [items, setItems] = useState<SupplierInvoiceListItem[]>([]);

  const [companyId, setCompanyId] = useState("");
  const [status, setStatus] = useState("");
  const [search, setSearch] = useState("");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const loadCompanies = useCallback(async () => {
    try {
      const result = await companyService.getAll();
      setCompanies(result);
      setCompanyId(result[0]?.id ?? "");
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
      const result = await supplierInvoiceService.getAll({
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
    void loadCompanies();
  }, [loadCompanies]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadItems(), 300);
    return () => window.clearTimeout(timer);
  }, [loadItems]);

  const summary = useMemo(
    () => ({
      count: items.length,
      pending: items.filter((item) => item.status === 1).length,
      total: items.reduce((sum, item) => sum + item.grandTotal, 0),
    }),
    [items]
  );

  return (
    <ErpShell
      design="redwood"
      title="Tedarikçi Faturaları"
      description="Alış faturaları, 3 yönlü kontrol ve onayda otomatik muhasebe fişi"
    >
      <div className="erp-page-toolbar">
        <div>
          <strong>{summary.count} fatura</strong>
          {summary.pending > 0 && (
            <span className="erp-status yellow" style={{ marginLeft: "10px" }}>
              {summary.pending} onay bekliyor
            </span>
          )}
          <small style={{ display: "block", marginTop: "4px" }}>
            Toplam: {money(summary.total)}
          </small>
        </div>

        <Link className="erp-primary-button" href="/muhasebe/faturalar/yeni">
          + Yeni Fatura
        </Link>
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
              {Object.entries(SUPPLIER_INVOICE_STATUS_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>

            <input
              type="text"
              placeholder="Fatura no / tedarikçi ara..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
        </div>

        {loading ? (
          <div className="erp-loading">Faturalar yükleniyor...</div>
        ) : items.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Fatura bulunmuyor</strong>
            <p>Tedarikçiden gelen alış faturasını girerek başlayın.</p>
          </div>
        ) : (
          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Fatura No</th>
                  <th>Tip</th>
                  <th>Tedarikçi</th>
                  <th>Proje / Masraf Merkezi</th>
                  <th>Tarih</th>
                  <th>Tutar</th>
                  <th>3 Yönlü</th>
                  <th>Durum</th>
                  <th>Fiş</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <Link href={`/muhasebe/faturalar/${item.id}`}>
                        <strong>{item.invoiceNumber}</strong>
                      </Link>
                      <small>{item.internalNumber}</small>
                    </td>
                    <td>
                      <span
                        className={`erp-status ${item.invoiceType === 1 ? "yellow" : "gray"}`}
                      >
                        {item.invoiceTypeName}
                      </span>
                      {item.isReturn && (
                        <span className="erp-status red" style={{ marginLeft: "4px" }}>
                          İade
                        </span>
                      )}
                    </td>
                    <td>{item.supplierTitle}</td>
                    <td>
                      {item.projectId ? (
                        <>
                          {item.projectCode}
                          <small>{item.projectName}</small>
                        </>
                      ) : (
                        <>
                          {item.costCenterCode ?? "—"}
                          <small>Projesiz</small>
                        </>
                      )}
                    </td>
                    <td>{dateFormat.format(new Date(item.invoiceDate))}</td>
                    <td>
                      <strong>{money(item.grandTotal)}</strong>
                      <small>KDV: {money(item.vatTotal)}</small>
                    </td>
                    <td>
                      <span className={`erp-status ${MATCH_STATUS_COLORS[item.matchStatus] ?? "gray"}`}>
                        {MATCH_STATUS_LABELS[item.matchStatus] ?? "—"}
                      </span>
                    </td>
                    <td>
                      <span
                        className={`erp-status ${SUPPLIER_INVOICE_STATUS_COLORS[item.status] ?? "gray"}`}
                      >
                        {SUPPLIER_INVOICE_STATUS_LABELS[item.status] ?? "—"}
                      </span>
                      {item.requiresGmApproval && item.status === 1 && (
                        <small>GM onayı gerekli</small>
                      )}
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
