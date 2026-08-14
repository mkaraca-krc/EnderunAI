"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { money } from "@/lib/format/turkish";
import {
  supplierInvoiceService,
  MATCH_STATUS_COLORS,
  MATCH_STATUS_LABELS,
  SUPPLIER_INVOICE_STATUS_COLORS,
  SUPPLIER_INVOICE_STATUS_LABELS,
  type SupplierInvoiceDetail,
} from "@/services/supplier-invoice.service";

const dateFormat = new Intl.DateTimeFormat("tr-TR");

export default function SupplierInvoiceDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const invoiceId = params.id;

  const [invoice, setInvoice] = useState<SupplierInvoiceDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [processing, setProcessing] = useState(false);
  const [error, setError] = useState("");
  const [confirming, setConfirming] = useState<"ret" | "iptal" | null>(null);
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

  async function reject(reason: string) {
    setConfirming(null);
    await runAction(() => supplierInvoiceService.reject(invoiceId, reason));
  }

  /**
   * Onaylanmış faturada iptal muhasebeye TERS FİŞLE yansır; gerekçe
   * hem fişte hem denetim izinde görünür, bu yüzden zorunlu.
   * Onaylanmamış faturada böyle bir iz doğmaz, gerekçe isteğe bağlı.
   */
  async function cancel(reason: string) {
    setConfirming(null);

    await runAction(() =>
      invoice?.status === 2
        ? supplierInvoiceService.cancel(invoiceId, reason)
        : supplierInvoiceService.cancel(invoiceId),
    );
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

    if (!returnForm.invoiceNumber.trim()) {
      setError("İade fatura numarası zorunludur.");
      return;
    }

    setProcessing(true);
    setError("");

    try {
      const created = await supplierInvoiceService.createReturn(invoiceId, {
        invoiceNumber: returnForm.invoiceNumber.trim(),
        invoiceDate: returnForm.invoiceDate,
        items,
        description: returnForm.description.trim() || null,
      });

      setShowReturnForm(false);
      setReturnQuantities({});
      router.push(`/muhasebe/faturalar/${created.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "İade faturası oluşturulamadı.");
    } finally {
      setProcessing(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
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
                      onClick={() => setConfirming("ret")}
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
                    onClick={() => setConfirming("iptal")}
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

                {invoice.status === 2 && !invoice.isReturn && (
                  <button
                    type="button"
                    className="erp-secondary-button"
                    disabled={processing}
                    onClick={() => setShowReturnForm((value) => !value)}
                  >
                    İade Faturası Oluştur
                  </button>
                )}
              </div>
            </div>

            {invoice.isReturn && (
              <div className="erp-alert warning">
                Bu belge bir İADE faturasıdır
                {invoice.originalInvoiceNumber
                  ? ` (${invoice.originalInvoiceNumber} numaralı faturanın iadesi)`
                  : ""}
                . Onaylandığında muhasebe fişi ters yönde kesilir ve stok
                kartlı kalemler depodan çıkar.
              </div>
            )}

            {invoice.reversalVoucherNumber && (
              <div className="erp-alert">
                Bu fatura iptal edildi; {invoice.reversalVoucherNumber} numaralı ters
                fiş kesildi. Orijinal fiş defterde durmaya devam eder.
              </div>
            )}

            {invoice.requiresGmApproval && invoice.status === 1 && (
              <div className="erp-alert warning">
                Bu fatura yalnızca Genel Müdür/Admin tarafından onaylanabilir.
                {invoice.matchNote ? ` ${invoice.matchNote}` : ""}
              </div>
            )}

            {invoice.matchNote && !invoice.requiresGmApproval && (
              <div className="erp-alert">{invoice.matchNote}</div>
            )}

            {showReturnForm && invoice.returnableItems.length > 0 && (
              <form
                onSubmit={createReturn}
                className="erp-form-card"
                style={{ marginBottom: "12px" }}
              >
                <div className="erp-form-header">
                  <h2>İade Faturası</h2>
                  <p>
                    Mal iadesinde faturayı iade eden taraf keser; numarası
                    bizim kestiğimiz belgeye aittir. Birim fiyat ve KDV oranı
                    orijinalden kopyalanır — iade, alışın aynası olmalı.
                  </p>
                </div>

                <div className="erp-form-grid">
                  <label>
                    <span>İade Fatura No *</span>
                    <input
                      required
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
                        <th>Daha önce iade</th>
                        <th>İade edilebilir</th>
                        <th>İade miktarı</th>
                      </tr>
                    </thead>
                    <tbody>
                      {invoice.returnableItems.map((item) => (
                        <tr key={item.itemId}>
                          <td>{item.description}</td>
                          <td>
                            {item.invoicedQuantity} {item.unit}
                          </td>
                          <td>{item.returnedQuantity}</td>
                          <td>
                            <strong>{item.returnableQuantity}</strong>
                          </td>
                          <td>
                            <input
                              type="number"
                              step="0.0001"
                              min="0"
                              max={item.returnableQuantity}
                              disabled={item.returnableQuantity <= 0}
                              value={returnQuantities[item.itemId] ?? ""}
                              onChange={(e) =>
                                setReturnQuantities((current) => ({
                                  ...current,
                                  [item.itemId]: e.target.value,
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
                  <button
                    type="submit"
                    className="erp-primary-button"
                    disabled={processing}
                  >
                    {processing ? "Oluşturuluyor..." : "İade Faturasını Oluştur"}
                  </button>
                </div>
              </form>
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
                <input readOnly value={money(invoice.matchDifferenceAmount)} />
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
                      <td>{money(item.unitPrice)}</td>
                      <td>%{item.vatRate}</td>
                      <td>{money(item.lineSubtotal)}</td>
                      <td>{money(item.vatAmount)}</td>
                      <td>
                        <strong>{money(item.lineTotal)}</strong>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div
              className="erp-form-actions rw-totals"
              style={{ justifyContent: "flex-end" }}
            >
              <div>
                <div>Ara toplam: {money(invoice.subtotal)}</div>
                <div>KDV: {money(invoice.vatTotal)}</div>
                <strong>Genel toplam: {money(invoice.grandTotal)}</strong>
              </div>
            </div>
          </section>

          {invoice.chequePayments.length > 0 && (
            <section className="erp-panel erp-mt">
              <div className="erp-panel-header">
                <div>
                  <h2>Bu Faturayı Ödeyen Çekler</h2>
                  <p>
                    Çek dağılımından gelir; ayrı bir ödeme defteri
                    tutulmuyor. Karşılanan: {money(invoice.chequeAllocatedAmount)}{" "}
                    · Kalan: {money(invoice.chequeRemainingAmount)}
                  </p>
                </div>
              </div>

              <div className="erp-table-wrap">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Çek</th>
                      <th>Vade</th>
                      <th>Durum</th>
                      <th>Tutar</th>
                    </tr>
                  </thead>
                  <tbody>
                    {invoice.chequePayments.map((payment) => (
                      <tr key={payment.chequeId}>
                        <td>
                          <strong>{payment.chequeNumber}</strong>
                          <small>{payment.internalNumber}</small>
                        </td>
                        <td>{dateFormat.format(new Date(payment.dueDate))}</td>
                        <td>{payment.statusName}</td>
                        <td>{money(payment.allocatedAmount)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}

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
      <ConfirmDialog
        open={confirming === "ret"}
        title="Fatura reddedilsin mi?"
        description="Gerekçe tedarikçi faturasının geçmişinde kalır."
        confirmLabel="Reddet"
        requireReason
        reasonLabel="Ret gerekçesi (zorunlu)"
        busy={processing}
        onCancel={() => setConfirming(null)}
        onConfirm={(reason) => void reject(reason)}
      />

      <ConfirmDialog
        open={confirming === "iptal"}
        title="Fatura iptal edilsin mi?"
        description={
          invoice?.status === 2
            ? "Onaylanmış fatura iptal edilince muhasebeye ters fiş kesilir; gerekçe hem fişte hem denetim izinde görünür."
            : "Fatura iptal edilir; muhasebe kaydı doğmadığı için ters fiş kesilmez."
        }
        confirmLabel="İptal Et"
        requireReason={invoice?.status === 2}
        showReason
        busy={processing}
        onCancel={() => setConfirming(null)}
        onConfirm={(reason) => void cancel(reason)}
      />

    </ErpShell>
  );
}
