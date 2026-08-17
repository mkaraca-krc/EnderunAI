"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { ConfirmDialog } from "@/components/ui";
import { money } from "@/lib/format/turkish";

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

/*
 * ONAY EKRANINDA TUTAR YUVARLANMAZ.
 *
 * Burada gösterilen rakam kullanıcının üzerine "Onayla" bastığı
 * rakam. Kuruşsuz biçim özet kartı içindir; onay anında
 * 1.234.567,89 ₺ tutarındaki bir hakedişi "1.234.568 ₺" diye
 * göstermek, onaylanan tutarla gösterilen tutarı ayırır.
 */
const date = new Intl.DateTimeFormat("tr-TR");

type ProcessingState = {
  type: string;
  id: string;
} | null;

const REJECTION_TEXT = {
  "progress-cancel": {
    title: "Hakedişi İptal Et",
    description:
      "Hakediş iptal edilecek. İptal geri alınamaz ve gerekçe kayda geçer; " +
      "aylar sonra sorulan ilk şey bu olur.",
    confirmLabel: "Hakedişi İptal Et",
  },
  "order-reject": {
    title: "Siparişi Reddet",
    description:
      "Satın alma siparişi reddedilecek. Gerekçe talebi açan kişiye gider.",
    confirmLabel: "Siparişi Reddet",
  },
  "request-cancel": {
    title: "Satın Alma Talebini İptal Et",
    description:
      "Talep iptal edilecek. Gerekçe talebi açan kişiye gider.",
    confirmLabel: "Talebi İptal Et",
  },
} as const;

export default function ApprovalCenterPage() {
  /**
   * ONAY MERKEZİ DÖRT MODÜLÜN ONAYINI TEK EKRANDA TOPLUYOR — her
   * bölümün kapısı kendi ucundan geliyor:
   *   POST progress-payments/{id}/approve      -> hakedis.approve
   *   POST progress-payments/{id}/cancel       -> hakedis.DELETE
   *   POST purchase-orders/{id}/approve        -> purchasing-orders.approve
   *   POST purchase-orders/{id}/reject         -> purchasing-orders.approve
   *   POST purchase-requests/{id}/approve      -> purchasing-requests.approve
   *   POST purchase-requests/{id}/cancel       -> purchasing-requests.DELETE
   *   POST .../daily-reports/{id}/approve      -> site-reports.approve
   *
   * TEK BİR "onay yetkisi" YOK. Ekranın kendisi bir kapıya bağlanamaz:
   * yalnız satın alma onayı olan kullanıcı hakediş bölümünü görmemeli
   * ama sipariş bölümünü görmeli. Bölüm bölüm kapılandı.
   *
   * REDDETMEK ONAYLAMAKLA aynı yetkide, İPTAL ETMEK delete'te — iptal
   * defter izi bırakıyor, ret akışı sonlandırıyor.
   */
  const hakedisActions = useModuleActions("hakedis");
  const orderActions = useModuleActions("purchasing-orders");
  const requestActions = useModuleActions("purchasing-requests");
  const reportActions = useModuleActions("site-reports");

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
  /** Gerekçe bekleyen reddetme/iptal işlemi. */
  const [rejection, setRejection] = useState<{
    kind: "progress-cancel" | "order-reject" | "request-cancel";
    id: string;
  } | null>(null);

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

  /**
   * Reddetme/iptal işlemleri — üçü de gerekçe İSTER.
   *
   * Eskiden üçü de window.prompt kullanıyordu ve İKİSİ boş gerekçeyi
   * kabul ediyordu: `reason === null` yalnızca "Vazgeç"i yakalıyor,
   * boş kutuya OK denince metin "" olarak geçiyordu. Yani bir hakediş
   * gerekçesiz iptal edilebiliyordu. Üçüncüsü (sipariş reddi)
   * `!reason?.trim()` ile doğru kontrol ediyordu — aynı ekranda üç
   * farklı davranış vardı.
   *
   * ConfirmDialog'da onay düğmesi gerekçe yazılmadan açılmıyor;
   * kural artık üçünde de aynı ve tek yerde.
   */
  async function runRejection(reason: string) {
    if (!rejection) return;

    const trimmed = reason.trim();
    setRejection(null);

    if (rejection.kind === "progress-cancel") {
      await runAction(
        "progress-cancel",
        rejection.id,
        () => progressPaymentService.cancel(rejection.id, trimmed),
        "Hakediş iptal edildi."
      );
      return;
    }

    if (rejection.kind === "order-reject") {
      await runAction(
        "order-reject",
        rejection.id,
        () => purchaseOrderService.reject(rejection.id, trimmed),
        "Satın alma siparişi reddedildi."
      );
      return;
    }

    await runAction(
      "request-cancel",
      rejection.id,
      () => purchaseRequestService.cancel(rejection.id, trimmed),
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
      design="redwood"
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
                    {money(item.netPayableAmount)}
                  </strong>
                </div>

                <div className="approval-item-actions">
                  <Link
                    className="erp-secondary-button"
                    href={`/hakedis/${item.id}`}
                  >
                    Detay
                  </Link>

                  {hakedisActions.can("approve") && (
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
                  )}

                  {/* Hakediş İPTALİ onaydan farklı yetkide: uç
                      hakedis.DELETE istiyor (defter izi bırakıyor). */}
                  {hakedisActions.can("delete") && (
                    <button
                      type="button"
                      className="approval-danger-button"
                      disabled={processing !== null}
                      onClick={() =>
                        setRejection({ kind: "progress-cancel", id: item.id })
                      }
                    >
                      İptal
                    </button>
                  )}
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
                    {money(item.grandTotal)}
                  </strong>
                </div>

                <div className="approval-item-actions">
                  <Link
                    className="erp-secondary-button"
                    href={`/satin-alma/siparis/${item.id}`}
                  >
                    Detay
                  </Link>

                  {orderActions.can("approve") && (
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
                  )}

                  {orderActions.can("approve") && (
                    <button
                      type="button"
                      className="approval-danger-button"
                      disabled={processing !== null}
                      onClick={() =>
                        setRejection({ kind: "order-reject", id: item.id })
                      }
                    >
                      Reddet
                    </button>
                  )}
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

                  {requestActions.can("approve") && (
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
                  )}

                  {/* Talep İPTALİ purchasing-requests.DELETE istiyor. */}
                  {requestActions.can("delete") && (
                    <button
                      type="button"
                      className="approval-danger-button"
                      disabled={processing !== null}
                      onClick={() =>
                        setRejection({ kind: "request-cancel", id: item.id })
                      }
                    >
                      İptal
                    </button>
                  )}
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

                  {reportActions.can("approve") && (
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
                  )}
                </div>
              </article>
            ))}
          </ApprovalSection>
        </div>
      )}
      {rejection && (
        <ConfirmDialog
          key={`${rejection.kind}-${rejection.id}`}
          open
          title={REJECTION_TEXT[rejection.kind].title}
          description={REJECTION_TEXT[rejection.kind].description}
          confirmLabel={REJECTION_TEXT[rejection.kind].confirmLabel}
          requireReason
          busy={processing !== null}
          error={error}
          onCancel={() => setRejection(null)}
          onConfirm={(reason) => void runRejection(reason)}
        />
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
