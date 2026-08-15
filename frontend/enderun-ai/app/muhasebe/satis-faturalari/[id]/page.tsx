"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { money, unitPrice } from "@/lib/format/turkish";
import {
  salesInvoiceService,
  PARSE_SOURCE_LABELS,
  SALES_INVOICE_STATUS_COLORS,
  SALES_INVOICE_STATUS_LABELS,
  SalesInvoiceStatus,
  type SalesInvoiceDetail,
} from "@/services/sales-invoice.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

export default function SalesInvoiceDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const invoiceId = params.id;

  const [invoice, setInvoice] = useState<SalesInvoiceDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [processing, setProcessing] = useState(false);
  const [error, setError] = useState("");
  const [cancelling, setCancelling] = useState(false);
  const [notice, setNotice] = useState("");

  const [showReturnForm, setShowReturnForm] = useState(false);
  const [returnForm, setReturnForm] = useState({
    invoiceNumber: "",
    invoiceDate: new Date().toISOString().slice(0, 10),
    description: "",
  });
  const [returnQuantities, setReturnQuantities] = useState<Record<string, string>>({});

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

  async function createReturn(event: React.FormEvent) {
    event.preventDefault();
    if (!invoice) return;

    const items = Object.entries(returnQuantities)
      .map(([itemId, quantity]) => ({
        originalItemId: itemId,
        quantity: Number(quantity) || 0,
      }))
      .filter((item) => item.quantity > 0);

    if (items.length === 0) {
      setError("İade edilecek en az bir kalem için miktar girin.");
      return;
    }

    setProcessing(true);
    setError("");

    try {
      const created = await salesInvoiceService.createReturn(invoice.id, {
        invoiceNumber: returnForm.invoiceNumber.trim(),
        invoiceDate: returnForm.invoiceDate,
        items,
        description: returnForm.description.trim() || null,
      });

      setShowReturnForm(false);
      setReturnQuantities({});
      router.push(`/muhasebe/satis-faturalari/${created.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "İade faturası oluşturulamadı.");
    } finally {
      setProcessing(false);
    }
  }

  if (loading) {
    return (
      <ErpShell design="redwood" title="Satış Faturası" description="Yükleniyor">
        <div className="erp-loading">Fatura yükleniyor...</div>
      </ErpShell>
    );
  }

  if (!invoice) {
    return (
      <ErpShell design="redwood" title="Satış Faturası" description="Bulunamadı">
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
      design="redwood"
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

          {/* Kesinleşmiş fatura da iptal edilebilir: fiş silinmez, ters
              kaydı kesilir ve ikisi de defterde kalır. */}
          <button
            type="button"
            className="erp-secondary-button"
            disabled={processing || invoice.status === 2}
            onClick={() => setCancelling(true)}
          >
            İptal Et
          </button>

          {invoice.status === 1 && !invoice.isReturn && (
            <button
              type="button"
              className="erp-secondary-button"
              disabled={processing}
              onClick={() => setShowReturnForm((value) => !value)}
            >
              İade Faturası Oluştur
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

      {invoice.isReturn && (
        <div className="erp-alert warning">
          Bu belge bir İADE faturasıdır
          {invoice.originalInvoiceNumber
            ? ` (${invoice.originalInvoiceNumber} numaralı faturanın iadesi)`
            : ""}
          . Kesinleştiğinde 610 Satıştan İadeler hesabı borçlandırılır;
          brüt satış rakamı olduğu gibi kalır.
        </div>
      )}

      {invoice.reversalVoucherNumber && (
        <div className="erp-alert">
          Bu fatura iptal edildi; {invoice.reversalVoucherNumber} numaralı ters fiş
          kesildi. Orijinal fiş defterde durmaya devam eder.
        </div>
      )}

      {showReturnForm && (
        <form onSubmit={createReturn} className="erp-form-card">
          <div className="erp-form-header">
            <h2>İade Faturası</h2>
            <p>
              Müşteriden mal iadesi. Birim fiyat ve KDV oranı orijinalden
              kopyalanır; tevkifat varsa iade edilen paya oranla taşınır.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>İade Fatura No</span>
              <input
                value={returnForm.invoiceNumber}
                onChange={(e) =>
                  setReturnForm({ ...returnForm, invoiceNumber: e.target.value })
                }
              />
            </label>

            <label>
              <span>İade Tarihi *</span>
              <input
                required
                type="date"
                value={returnForm.invoiceDate}
                onChange={(e) =>
                  setReturnForm({ ...returnForm, invoiceDate: e.target.value })
                }
              />
            </label>

            <label className="span-2">
              <span>Açıklama</span>
              <input
                value={returnForm.description}
                onChange={(e) =>
                  setReturnForm({ ...returnForm, description: e.target.value })
                }
              />
            </label>
          </div>

          <div className="erp-table-wrap">
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Kalem</th>
                  <th>Faturadaki</th>
                  <th>İade miktarı</th>
                </tr>
              </thead>
              <tbody>
                {invoice.items.map((item) => (
                  <tr key={item.id}>
                    <td>{item.description}</td>
                    <td>
                      {item.quantity} {item.unit}
                    </td>
                    <td>
                      <input
                        type="number"
                        step="0.0001"
                        min="0"
                        max={item.quantity}
                        value={returnQuantities[item.id] ?? ""}
                        onChange={(e) =>
                          setReturnQuantities((current) => ({
                            ...current,
                            [item.id]: e.target.value,
                          }))
                        }
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="erp-form-actions">
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() => setShowReturnForm(false)}
            >
              Vazgeç
            </button>
            <button type="submit" className="erp-primary-button" disabled={processing}>
              {processing ? "Oluşturuluyor..." : "İade Faturasını Oluştur"}
            </button>
          </div>
        </form>
      )}

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
                  <td>{unitPrice(item.unitPrice)}</td>
                  <td>{item.vatRate}</td>
                  <td>{money(item.lineSubtotal)}</td>
                  <td>{money(item.vatAmount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="rw-totals" style={{ padding: "16px" }}>
          <div>Ara toplam: {money(invoice.subtotal)}</div>
          <div>KDV: {money(invoice.vatTotal)}</div>
          {invoice.withholdingAmount > 0 && (
            <div>Tevkifat: -{money(invoice.withholdingAmount)}</div>
          )}
          <strong>Tahsil edilecek: {money(invoice.netReceivableAmount)}</strong>
        </div>
      </div>

      {invoice.cancellationReason && (
        <div className="erp-alert warning">
          İptal gerekçesi: {invoice.cancellationReason}
        </div>
      )}
      {invoice && (
        <ConfirmDialog
          open={cancelling}
          title="Satış faturası iptal edilsin mi?"
          description={
            invoice.status === 1
              ? "Kesilmiş faturanın iptali muhasebeye ters fişle yansır; gerekçe fişte görünür."
              : "Fatura iptal edilir."
          }
          confirmLabel="İptal Et"
          requireReason
          reasonLabel="İptal gerekçesi (zorunlu)"
          busy={processing}
          onCancel={() => setCancelling(false)}
          onConfirm={(reason) => {
            setCancelling(false);
            void runAction(() => salesInvoiceService.cancel(invoice.id, reason));
          }}
        />
      )}

    </ErpShell>
  );
}
