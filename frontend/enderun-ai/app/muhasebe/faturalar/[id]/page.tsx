"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  supplierInvoiceService,
  MATCH_STATUS_COLORS,
  MATCH_STATUS_LABELS,
  SUPPLIER_INVOICE_STATUS_COLORS,
  SUPPLIER_INVOICE_STATUS_LABELS,
  type SupplierInvoiceDetail,
} from "@/services/supplier-invoice.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const dateFormat = new Intl.DateTimeFormat("tr-TR");

export default function SupplierInvoiceDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const invoiceId = params.id;

  const [invoice, setInvoice] = useState<SupplierInvoiceDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [processing, setProcessing] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      setInvoice(await supplierInvoiceService.getById(invoiceId));
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

  async function reject() {
    const reason = window.prompt("Ret gerekçesi:");
    if (!reason?.trim()) return;
    await runAction(() => supplierInvoiceService.reject(invoiceId, reason.trim()));
  }

  async function cancel() {
    if (!window.confirm("Bu faturayı iptal etmek istediğinize emin misiniz?")) return;
    await runAction(() => supplierInvoiceService.cancel(invoiceId));
  }

  return (
    <ErpShell
      title={invoice ? `Fatura ${invoice.invoiceNumber}` : "Tedarikçi Faturası"}
      description={invoice?.internalNumber ?? "Fatura detayı"}
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {loading ? (
        <div className="erp-panel erp-loading">Fatura yükleniyor...</div>
      ) : !invoice ? (
        <div className="erp-panel erp-empty-state">
          <strong>Fatura bulunamadı</strong>
        </div>
      ) : (
        <>
          <section className="erp-panel">
            <div className="erp-panel-header">
              <div>
                <h2>Genel Bilgiler</h2>
                <p>
                  <span
                    className={`erp-status ${
                      SUPPLIER_INVOICE_STATUS_COLORS[invoice.status] ?? "gray"
                    }`}
                  >
                    {SUPPLIER_INVOICE_STATUS_LABELS[invoice.status] ?? "—"}
                  </span>
                  <span
                    className={`erp-status ${
                      MATCH_STATUS_COLORS[invoice.matchStatus] ?? "gray"
                    }`}
                    style={{ marginLeft: "8px" }}
                  >
                    3 yönlü: {MATCH_STATUS_LABELS[invoice.matchStatus] ?? "—"}
                  </span>
                </p>
              </div>

              <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                {invoice.status === 0 && (
                  <button
                    type="button"
                    className="erp-primary-button"
                    disabled={processing}
                    onClick={() =>
                      void runAction(() => supplierInvoiceService.submit(invoiceId))
                    }
                  >
                    Onaya Gönder
                  </button>
                )}

                {invoice.status === 1 && (
                  <>
                    <button
                      type="button"
                      className="erp-primary-button"
                      disabled={processing}
                      onClick={() =>
                        void runAction(() => supplierInvoiceService.approve(invoiceId))
                      }
                    >
                      Onayla ve Fişleştir
                    </button>
                    <button
                      type="button"
                      className="erp-secondary-button"
                      disabled={processing}
                      onClick={() => void reject()}
                    >
                      Reddet
                    </button>
                  </>
                )}

                {(invoice.status === 0 || invoice.status === 1) && (
                  <button
                    type="button"
                    className="erp-secondary-button"
                    disabled={processing}
                    onClick={() => void cancel()}
                  >
                    İptal Et
                  </button>
                )}

                <button
                  type="button"
                  className="erp-secondary-button"
                  onClick={() => router.push("/muhasebe/faturalar")}
                >
                  Listeye Dön
                </button>
              </div>
            </div>

            {invoice.requiresGmApproval && invoice.status === 1 && (
              <div className="erp-alert warning">
                Bu fatura yalnızca Genel Müdür/Admin tarafından onaylanabilir.
                {invoice.matchNote ? ` ${invoice.matchNote}` : ""}
              </div>
            )}

            {invoice.matchNote && !invoice.requiresGmApproval && (
              <div className="erp-alert">{invoice.matchNote}</div>
            )}

            <div className="erp-form-grid">
              <label>
                <span>Tedarikçi</span>
                <input readOnly value={invoice.supplierTitle} />
              </label>
              <label>
                <span>Fatura Tipi</span>
                <input readOnly value={invoice.invoiceTypeName} />
              </label>
              <label>
                <span>Proje</span>
                <input
                  readOnly
                  value={
                    invoice.projectId
                      ? `${invoice.projectCode} — ${invoice.projectName}`
                      : "Projesiz (merkez gideri)"
                  }
                />
              </label>
              <label>
                <span>Masraf Merkezi</span>
                <input readOnly value={invoice.costCenterCode ?? "—"} />
              </label>
              {invoice.invoiceType === 0 && (
                <label>
                  <span>Depo</span>
                  <input readOnly value={invoice.warehouseName ?? "Depoya girmiyor"} />
                </label>
              )}
              <label>
                <span>Fatura Tarihi</span>
                <input readOnly value={dateFormat.format(new Date(invoice.invoiceDate))} />
              </label>
              <label>
                <span>Vade</span>
                <input
                  readOnly
                  value={
                    invoice.dueDate
                      ? dateFormat.format(new Date(invoice.dueDate))
                      : "—"
                  }
                />
              </label>
              <label>
                <span>Sipariş</span>
                <input readOnly value={invoice.purchaseOrderNumber ?? "—"} />
              </label>
              <label>
                <span>Mal Kabul</span>
                <input readOnly value={invoice.goodsReceiptNumber ?? "—"} />
              </label>
              <label>
                <span>Muhasebe Fişi</span>
                <input readOnly value={invoice.accountingVoucherNumber ?? "Henüz oluşmadı"} />
              </label>
              <label>
                <span>3 Yönlü Fark</span>
                <input readOnly value={money.format(invoice.matchDifferenceAmount)} />
              </label>
              {invoice.description && (
                <label className="span-2">
                  <span>Açıklama</span>
                  <input readOnly value={invoice.description} />
                </label>
              )}
              {invoice.rejectionReason && (
                <label className="span-2">
                  <span>Ret Gerekçesi</span>
                  <input readOnly value={invoice.rejectionReason} />
                </label>
              )}
            </div>
          </section>

          <section className="erp-panel erp-mt">
            <div className="erp-panel-header">
              <div>
                <h2>Kalemler</h2>
                <p>{invoice.items.length} kalem</p>
              </div>
            </div>

            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>#</th>
                    <th>{invoice.invoiceType === 0 ? "Stok Kartı / Depo" : "Gider Hesabı"}</th>
                    <th>Açıklama</th>
                    <th>Miktar</th>
                    <th>Birim Fiyat</th>
                    <th>KDV %</th>
                    <th>Ara Toplam</th>
                    <th>KDV</th>
                    <th>Toplam</th>
                  </tr>
                </thead>
                <tbody>
                  {invoice.items.map((item) => (
                    <tr key={item.id}>
                      <td>{item.lineNumber}</td>
                      <td>
                        {invoice.invoiceType === 0 ? (
                          item.inventoryItemId ? (
                            <>
                              <div>
                                {item.inventoryItemCode} — {item.inventoryItemName}
                              </div>
                              <small>
                                {item.warehouseName ??
                                  invoice.warehouseName ??
                                  "Depo seçilmedi"}
                              </small>
                            </>
                          ) : (
                            <small>Stok kartı yok</small>
                          )
                        ) : item.expenseAccountId ? (
                          <>
                            <div>
                              {item.expenseAccountCode} — {item.expenseAccountName}
                            </div>
                            <small>
                              {item.costCenterCode ??
                                invoice.costCenterCode ??
                                "Masraf merkezi yok"}
                            </small>
                          </>
                        ) : (
                          <small>Hesap seçilmedi</small>
                        )}
                      </td>
                      <td>{item.description}</td>
                      <td>
                        {item.quantity} {item.unit}
                      </td>
                      <td>{money.format(item.unitPrice)}</td>
                      <td>%{item.vatRate}</td>
                      <td>{money.format(item.lineSubtotal)}</td>
                      <td>{money.format(item.vatAmount)}</td>
                      <td>
                        <strong>{money.format(item.lineTotal)}</strong>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div
              className="erp-form-actions"
              style={{ justifyContent: "flex-end", textAlign: "right" }}
            >
              <div>
                <div>Ara toplam: {money.format(invoice.subtotal)}</div>
                <div>KDV: {money.format(invoice.vatTotal)}</div>
                <strong>Genel toplam: {money.format(invoice.grandTotal)}</strong>
              </div>
            </div>
          </section>

          {invoice.accountingVoucherId && (
            <section className="erp-panel erp-mt">
              <div className="erp-panel-header">
                <div>
                  <h2>Muhasebe Kaydı</h2>
                  <p>
                    Onayda otomatik oluşturuldu ve kesinleştirildi: 320 Satıcılar
                    (alacak) — maliyet + 191 İndirilecek KDV (borç).
                  </p>
                </div>

                <Link className="erp-secondary-button" href="/muhasebe/fisler">
                  Fişlere Git
                </Link>
              </div>
            </section>
          )}
        </>
      )}
    </ErpShell>
  );
}
