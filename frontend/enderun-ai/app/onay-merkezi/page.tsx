"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";

import {
  progressPaymentService,
  ProgressPaymentStatus,
  type ProgressPaymentListItem,
} from "@/services/progress-payment.service";

import {
  purchaseOrderService,
  type PurchaseOrderListItem,
} from "@/services/purchase-order.service";

import {
  purchaseRequestService,
  type PurchaseRequestListItem,
} from "@/services/purchase-request.service";

import {
  rfqService,
  type RfqListItem,
} from "@/services/rfq.service";

import {
  dailyReportService,
  type PendingApprovalReport,
} from "@/services/daily-report.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
  minimumFractionDigits: 0,
  maximumFractionDigits: 0,
});

const date = new Intl.DateTimeFormat("tr-TR");

type ProcessingState = {
  type: string;
  id: string;
} | null;

export default function ApprovalCenterPage() {
  const [progressPayments, setProgressPayments] =
    useState<ProgressPaymentListItem[]>([]);

  const [purchaseOrders, setPurchaseOrders] =
    useState<PurchaseOrderListItem[]>([]);

  const [purchaseRequests, setPurchaseRequests] =
    useState<PurchaseRequestListItem[]>([]);

  const [rfqs, setRfqs] = useState<RfqListItem[]>([]);

  const [siteReports, setSiteReports] =
    useState<PendingApprovalReport[]>([]);

  const [loading, setLoading] = useState(true);
  const [processing, setProcessing] =
    useState<ProcessingState>(null);

  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const loadData = useCallback(async () => {
    setLoading(true);
    setError("");

    const results = await Promise.allSettled([
      progressPaymentService.getAll({
        status: ProgressPaymentStatus.PendingApproval,
      }),
      purchaseOrderService.getAll({ status: 1 }),
      purchaseRequestService.getAll({ status: 1 }),
      rfqService.getAll(),
      dailyReportService.getPendingApproval(),
    ]);

    const [
      progressResult,
      orderResult,
      requestResult,
      rfqResult,
      siteReportResult,
    ] = results;

    const errors: string[] = [];

    if (progressResult.status === "fulfilled") {
      setProgressPayments(progressResult.value);
    } else {
      setProgressPayments([]);
      errors.push("Hakedişler alınamadı.");
    }

    if (orderResult.status === "fulfilled") {
      setPurchaseOrders(orderResult.value);
    } else {
      setPurchaseOrders([]);
      errors.push("Siparişler alınamadı.");
    }

    if (requestResult.status === "fulfilled") {
      setPurchaseRequests(requestResult.value);
    } else {
      setPurchaseRequests([]);
      errors.push("Satın alma talepleri alınamadı.");
    }

    if (rfqResult.status === "fulfilled") {
      setRfqs(
        rfqResult.value.filter((item) => item.status < 4)
      );
    } else {
      setRfqs([]);
      errors.push("RFQ kayıtları alınamadı.");
    }

    // Saha raporu onay yetkisi olmayan kullanıcılar için bu bölüm
    // sessizce boş kalır — herkesin bu izne sahip olması beklenmez.
    if (siteReportResult.status === "fulfilled") {
      setSiteReports(siteReportResult.value);
    } else {
      setSiteReports([]);
    }

    if (errors.length > 0) {
      setError(errors.join(" "));
    }

    setLoading(false);
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const totalPending = useMemo(
    () =>
      progressPayments.length +
      purchaseOrders.length +
      purchaseRequests.length +
      rfqs.length +
      siteReports.length,
    [
      progressPayments,
      purchaseOrders,
      purchaseRequests,
      rfqs,
      siteReports,
    ]
  );

  async function runAction(
    type: string,
    id: string,
    action: () => Promise<unknown>,
    successMessage: string
  ) {
    setProcessing({ type, id });
    setMessage("");
    setError("");

    try {
      await action();
      setMessage(successMessage);
      await loadData();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İşlem tamamlanamadı."
      );
    } finally {
      setProcessing(null);
    }
  }

  async function cancelProgressPayment(id: string) {
    const reason = window.prompt(
      "Hakediş iptal gerekçesini yazın:"
    );

    if (reason === null) {
      return;
    }

    await runAction(
      "progress-cancel",
      id,
      () => progressPaymentService.cancel(id, reason),
      "Hakediş iptal edildi."
    );
  }

  async function rejectPurchaseOrder(id: string) {
    const reason = window.prompt(
      "Sipariş ret gerekçesini yazın:"
    );

    if (!reason?.trim()) {
      return;
    }

    await runAction(
      "order-reject",
      id,
      () => purchaseOrderService.reject(id, reason.trim()),
      "Satın alma siparişi reddedildi."
    );
  }

  async function cancelPurchaseRequest(id: string) {
    const reason = window.prompt(
      "Satın alma talebi iptal gerekçesini yazın:"
    );

    if (reason === null) {
      return;
    }

    await runAction(
      "request-cancel",
      id,
      () => purchaseRequestService.cancel(id, reason),
      "Satın alma talebi iptal edildi."
    );
  }

  async function approveSiteReport(item: PendingApprovalReport) {
    await runAction(
      "site-report-approve",
      item.id,
      () => dailyReportService.approve(item.projectSiteId, item.id),
      "Günlük saha raporu onaylandı."
    );
  }

  function isProcessing(type: string, id: string) {
    return (
      processing?.type === type &&
      processing.id === id
    );
  }

  return (
    <ErpShell
      title="Onay Merkezi"
      description="Hakediş, satın alma ve RFQ süreçlerini tek merkezden yönetin"
    >
      <section className="approval-center-hero">
        <div>
          <span>ENDERUN AI İŞ AKIŞI</span>
          <h2>Yönetici Onay Merkezi</h2>
          <p>
            Müdahale ve onay bekleyen tüm kayıtlar
            tek ekranda gösterilmektedir.
          </p>
        </div>

        <div className="approval-center-total">
          <span>Toplam bekleyen</span>
          <strong>{totalPending}</strong>
          <button
            type="button"
            className="erp-secondary-button"
            disabled={loading}
            onClick={() => void loadData()}
          >
            {loading ? "Yükleniyor..." : "Yenile"}
          </button>
        </div>
      </section>

      {message && (
        <div className="erp-alert success">
          {message}
        </div>
      )}

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <section className="approval-summary-grid">
        <SummaryCard
          label="Bekleyen Hakediş"
          value={progressPayments.length}
        />
        <SummaryCard
          label="Bekleyen Sipariş"
          value={purchaseOrders.length}
        />
        <SummaryCard
          label="Satın Alma Talebi"
          value={purchaseRequests.length}
        />
        <SummaryCard
          label="Devam Eden RFQ"
          value={rfqs.length}
        />
        <SummaryCard
          label="Onay Bekleyen Saha Raporu"
          value={siteReports.length}
        />
      </section>

      {loading ? (
        <div className="erp-panel">
          Onay kayıtları yükleniyor...
        </div>
      ) : (
        <div className="approval-sections">
          <ApprovalSection
            title="Onay Bekleyen Hakedişler"
            description="Yönetici onayına gönderilmiş hakediş kayıtları"
            emptyText="Onay bekleyen hakediş bulunmuyor."
          >
            {progressPayments.map((item) => (
              <article
                className="approval-item"
                key={item.id}
              >
                <div className="approval-item-main">
                  <span className="erp-status yellow">
                    Hakediş
                  </span>

                  <div>
                    <strong>
                      {item.progressPaymentNumber}
                    </strong>
                    <p>
                      {item.projectCode} — {item.projectName}
                    </p>
                    <small>
                      {date.format(
                        new Date(item.progressPaymentDate)
                      )}
                    </small>
                  </div>
                </div>

                <div className="approval-item-value">
                  <span>Net Ödenecek</span>
                  <strong>
                    {money.format(item.netPayableAmount)}
                  </strong>
                </div>

                <div className="approval-item-actions">
                  <Link
                    className="erp-secondary-button"
                    href={`/hakedis/${item.id}`}
                  >
                    Detay
                  </Link>

                  <button
                    type="button"
                    className="erp-primary-button"
                    disabled={processing !== null}
                    onClick={() =>
                      void runAction(
                        "progress-approve",
                        item.id,
                        () =>
                          progressPaymentService.approve(
                            item.id
                          ),
                        "Hakediş onaylandı."
                      )
                    }
                  >
                    {isProcessing(
                      "progress-approve",
                      item.id
                    )
                      ? "İşleniyor..."
                      : "Onayla"}
                  </button>

                  <button
                    type="button"
                    className="approval-danger-button"
                    disabled={processing !== null}
                    onClick={() =>
                      void cancelProgressPayment(item.id)
                    }
                  >
                    İptal
                  </button>
                </div>
              </article>
            ))}
          </ApprovalSection>

          <ApprovalSection
            title="Onay Bekleyen Satın Alma Siparişleri"
            description="Onaya gönderilmiş tedarikçi siparişleri"
            emptyText="Onay bekleyen sipariş bulunmuyor."
          >
            {purchaseOrders.map((item) => (
              <article
                className="approval-item"
                key={item.id}
              >
                <div className="approval-item-main">
                  <span className="erp-status blue">
                    Sipariş
                  </span>

                  <div>
                    <strong>{item.orderNumber}</strong>
                    <p>{item.supplierTitle}</p>
                    <small>
                      {item.projectCode} — {item.projectName}
                    </small>
                  </div>
                </div>

                <div className="approval-item-value">
                  <span>Sipariş Toplamı</span>
                  <strong>
                    {money.format(item.grandTotal)}
                  </strong>
                </div>

                <div className="approval-item-actions">
                  <Link
                    className="erp-secondary-button"
                    href={`/satin-alma/siparis/${item.id}`}
                  >
                    Detay
                  </Link>

                  <button
                    type="button"
                    className="erp-primary-button"
                    disabled={processing !== null}
                    onClick={() =>
                      void runAction(
                        "order-approve",
                        item.id,
                        () =>
                          purchaseOrderService.approve(
                            item.id
                          ),
                        "Satın alma siparişi onaylandı."
                      )
                    }
                  >
                    {isProcessing(
                      "order-approve",
                      item.id
                    )
                      ? "İşleniyor..."
                      : "Onayla"}
                  </button>

                  <button
                    type="button"
                    className="approval-danger-button"
                    disabled={processing !== null}
                    onClick={() =>
                      void rejectPurchaseOrder(item.id)
                    }
                  >
                    Reddet
                  </button>
                </div>
              </article>
            ))}
          </ApprovalSection>

          <ApprovalSection
            title="Onay Bekleyen Satın Alma Talepleri"
            description="Proje ve şantiyelerden gönderilen talepler"
            emptyText="Onay bekleyen satın alma talebi bulunmuyor."
          >
            {purchaseRequests.map((item) => (
              <article
                className="approval-item"
                key={item.id}
              >
                <div className="approval-item-main">
                  <span className="erp-status yellow">
                    Talep
                  </span>

                  <div>
                    <strong>{item.requestNumber}</strong>
                    <p>
                      {item.projectCode} — {item.projectName}
                    </p>
                    <small>
                      {item.requestedByName} ·{" "}
                      {item.itemCount} kalem
                    </small>
                  </div>
                </div>

                <div className="approval-item-value">
                  <span>Toplam Miktar</span>
                  <strong>{item.totalQuantity}</strong>
                </div>

                <div className="approval-item-actions">
                  <Link
                    className="erp-secondary-button"
                    href={`/satin-alma/${item.id}`}
                  >
                    Detay
                  </Link>

                  <button
                    type="button"
                    className="erp-primary-button"
                    disabled={processing !== null}
                    onClick={() =>
                      void runAction(
                        "request-approve",
                        item.id,
                        () =>
                          purchaseRequestService.approve(
                            item.id
                          ),
                        "Satın alma talebi onaylandı."
                      )
                    }
                  >
                    {isProcessing(
                      "request-approve",
                      item.id
                    )
                      ? "İşleniyor..."
                      : "Onayla"}
                  </button>

                  <button
                    type="button"
                    className="approval-danger-button"
                    disabled={processing !== null}
                    onClick={() =>
                      void cancelPurchaseRequest(item.id)
                    }
                  >
                    İptal
                  </button>
                </div>
              </article>
            ))}
          </ApprovalSection>

          <ApprovalSection
            title="Devam Eden RFQ Süreçleri"
            description="Teklif toplama ve karşılaştırma süreçleri"
            emptyText="Devam eden RFQ bulunmuyor."
          >
            {rfqs.map((item) => (
              <article
                className="approval-item"
                key={item.id}
              >
                <div className="approval-item-main">
                  <span className="erp-status blue">
                    RFQ
                  </span>

                  <div>
                    <strong>{item.rfqNumber}</strong>
                    <p>{item.title}</p>
                    <small>
                      {item.supplierCount} tedarikçi ·{" "}
                      {item.responseCount} teklif
                    </small>
                  </div>
                </div>

                <div className="approval-item-value">
                  <span>Kalem</span>
                  <strong>{item.itemCount}</strong>
                </div>

                <div className="approval-item-actions">
                  <Link
                    className="erp-secondary-button"
                    href={`/satin-alma/rfq/${item.id}`}
                  >
                    Detay
                  </Link>

                  <Link
                    className="erp-primary-button"
                    href={`/satin-alma/rfq/${item.id}/karsilastirma`}
                  >
                    Karşılaştır
                  </Link>
                </div>
              </article>
            ))}
          </ApprovalSection>

          <ApprovalSection
            title="Onay Bekleyen Günlük Saha Raporları"
            description="Şef/formen tarafından girilen taslak raporlar"
            emptyText="Onay bekleyen saha raporu bulunmuyor."
          >
            {siteReports.map((item) => (
              <article
                className="approval-item"
                key={item.id}
              >
                <div className="approval-item-main">
                  <span className="erp-status yellow">
                    Saha Raporu
                  </span>

                  <div>
                    <strong>
                      {item.siteCode} — {item.siteName}
                    </strong>
                    <p>
                      {item.projectCode} — {item.projectName}
                    </p>
                    <small>
                      {date.format(new Date(item.reportDate))}
                    </small>
                  </div>
                </div>

                <div className="approval-item-value">
                  <span>Toplam Personel</span>
                  <strong>{item.totalHeadcount}</strong>
                </div>

                <div className="approval-item-actions">
                  <Link
                    className="erp-secondary-button"
                    href={`/projeler/${item.projectId}/santiyeler/${item.projectSiteId}`}
                  >
                    Detay
                  </Link>

                  <button
                    type="button"
                    className="erp-primary-button"
                    disabled={processing !== null}
                    onClick={() => void approveSiteReport(item)}
                  >
                    {isProcessing("site-report-approve", item.id)
                      ? "İşleniyor..."
                      : "Onayla"}
                  </button>
                </div>
              </article>
            ))}
          </ApprovalSection>
        </div>
      )}
    </ErpShell>
  );
}

function SummaryCard({
  label,
  value,
}: {
  label: string;
  value: number;
}) {
  return (
    <div className="approval-summary-card">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function ApprovalSection({
  title,
  description,
  emptyText,
  children,
}: {
  title: string;
  description: string;
  emptyText: string;
  children: React.ReactNode;
}) {
  const items = Array.isArray(children)
    ? children
    : [children];

  const hasItems = items.some(Boolean);

  return (
    <section className="erp-panel approval-section">
      <div className="erp-panel-header">
        <div>
          <h2>{title}</h2>
          <p>{description}</p>
        </div>
      </div>

      {hasItems ? (
        <div className="approval-item-list">
          {children}
        </div>
      ) : (
        <div className="erp-empty-state">
          {emptyText}
        </div>
      )}
    </section>
  );
}
