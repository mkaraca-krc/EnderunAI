"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  salesInvoiceService,
  PARSE_SOURCE_LABELS,
  SALES_INVOICE_STATUS_COLORS,
  SALES_INVOICE_STATUS_LABELS,
  SalesInvoiceStatus,
  type SalesInvoiceDetail,
} from "@/services/sales-invoice.service";

const money = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY" });
const dateFormat = new Intl.DateTimeFormat("tr-TR");

export default function SalesInvoiceDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const invoiceId = params.id;

  const [invoice, setInvoice] = useState<SalesInvoiceDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [processing, setProcessing] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      setInvoice(await salesInvoiceService.getById(invoiceId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fatura alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [invoiceId]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(""), 4000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  async function runAction(action: () => Promise<{ message: string }>) {
    setProcessing(true);
    setError("");
    try {
      const result = await action();
      setNotice(result.message);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşlem tamamlanamadı.");
    } finally {
      setProcessing(false);
    }
  }

  if (loading) {
    return (
      <ErpShell title="Satış Faturası" description="Yükleniyor">
        <div className="erp-loading">Fatura yükleniyor...</div>
      </ErpShell>
    );
  }

  if (!invoice) {
    return (
      <ErpShell title="Satış Faturası" description="Bulunamadı">
        <div className="erp-alert error">{error || "Fatura bulunamadı."}</div>
        <Link className="erp-secondary-button" href="/muhasebe/satis-faturalari">
          Listeye dön
        </Link>
      </ErpShell>
    );
  }

  const isDraft = invoice.status === SalesInvoiceStatus.Draft;

  return (
    <ErpShell
      title={`Satış Faturası ${invoice.officialInvoiceNumber ?? invoice.internalNumber}`}
      description={`${invoice.customerTitle} — ${dateFormat.format(new Date(invoice.invoiceDate))}`}
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {invoice.requiresManualReview && (
        <div className="erp-alert warning">
          Bu fatura AI yedek okuyucuyla okundu veya tutarları şüpheli. Kesinleştirmeden
          önce orijinal XML ile karşılaştırın.
        </div>
      )}

      <div className="erp-page-toolbar">
        <div>
          <span
            className={`erp-status ${SALES_INVOICE_STATUS_COLORS[invoice.status] ?? "gray"}`}
          >
            {SALES_INVOICE_STATUS_LABELS[invoice.status] ?? "—"}
          </span>
          <small style={{ display: "block", marginTop: "4px" }}>
            Sistem no: {invoice.internalNumber}
            {invoice.parseSource !== null && invoice.parseSource !== undefined && (
              <> · {PARSE_SOURCE_LABELS[invoice.parseSource] ?? "—"}</>
            )}
            {invoice.hasSourceXml && <> · Orijinal XML saklandı</>}
          </small>
        </div>

        <div style={{ display: "flex", gap: "8px" }}>
          {isDraft && (
            <button
              type="button"
              className="erp-primary-button"
              disabled={processing}
              onClick={() => void runAction(() => salesInvoiceService.post(invoice.id))}
            >
              Kesinleştir ve Fiş Oluştur
            </button>
          )}

          {isDraft && (
            <button
              type="button"
              className="erp-secondary-button"
              disabled={processing}
              onClick={() => {
                const reason = window.prompt("İptal gerekçesi:");
                if (!reason) return;
                void runAction(() => salesInvoiceService.cancel(invoice.id, reason));
              }}
            >
              İptal Et
            </button>
          )}

          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => router.push("/muhasebe/satis-faturalari")}
          >
            Listeye dön
          </button>
        </div>
      </div>

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Fatura Bilgileri</h2>
        </div>

        <div className="erp-detail-grid" style={{ padding: "16px" }}>
          <div>
            <small>Müşteri</small>
            <strong>{invoice.customerTitle}</strong>
          </div>
          <div>
            <small>Proje</small>
            <strong>
              {invoice.projectCode ? `${invoice.projectCode} — ${invoice.projectName}` : "Projesiz"}
            </strong>
          </div>
          <div>
            <small>Resmi Fatura No</small>
            <strong>{invoice.officialInvoiceNumber ?? "Girilmedi"}</strong>
          </div>
          <div>
            <small>Vade</small>
            <strong>
              {invoice.dueDate ? dateFormat.format(new Date(invoice.dueDate)) : "—"}
            </strong>
          </div>
          <div>
            <small>Muhasebe Fişi</small>
            <strong>{invoice.accountingVoucherNumber ?? "Henüz oluşmadı"}</strong>
          </div>
          <div>
            <small>Açıklama</small>
            <strong>{invoice.description ?? "—"}</strong>
          </div>
        </div>
      </div>

      <div className="erp-table-card">
        <div className="erp-table-header">
          <h2>Kalemler</h2>
        </div>

        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>#</th>
                <th>Açıklama</th>
                <th>Miktar</th>
                <th>Birim Fiyat</th>
                <th>KDV %</th>
                <th>Tutar</th>
                <th>KDV</th>
              </tr>
            </thead>
            <tbody>
              {invoice.items.map((item) => (
                <tr key={item.id}>
                  <td>{item.lineNumber}</td>
                  <td>{item.description}</td>
                  <td>
                    {item.quantity} {item.unit}
                  </td>
                  <td>{money.format(item.unitPrice)}</td>
                  <td>{item.vatRate}</td>
                  <td>{money.format(item.lineSubtotal)}</td>
                  <td>{money.format(item.vatAmount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div style={{ padding: "16px", textAlign: "right" }}>
          <div>Ara toplam: {money.format(invoice.subtotal)}</div>
          <div>KDV: {money.format(invoice.vatTotal)}</div>
          {invoice.withholdingAmount > 0 && (
            <div>Tevkifat: -{money.format(invoice.withholdingAmount)}</div>
          )}
          <strong>Tahsil edilecek: {money.format(invoice.netReceivableAmount)}</strong>
        </div>
      </div>

      {invoice.cancellationReason && (
        <div className="erp-alert warning">
          İptal gerekçesi: {invoice.cancellationReason}
        </div>
      )}
    </ErpShell>
  );
}
