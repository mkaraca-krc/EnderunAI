"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { currencyMoney, percent, quantity } from "@/lib/format/turkish";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  StatCard,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import {
  purchaseOrderService,
  type PurchaseOrderDetail,
} from "@/services/purchase-order.service";
import {
  procurementApprovalService,
  type PurchaseOrderApprovalContext,
} from "@/services/procurement-approval.service";

import {
  reportService,
} from "@/services/report.service";
import {
  brandMismatch,
  requestedBrandLabel,
} from "@/lib/purchasing/requested-brand";

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Onay Bekliyor",
  2: "Onaylandı",
  3: "Kısmi Teslim",
  4: "Tamamlandı",
  5: "İptal",
  6: "Reddedildi",
};

function statusVariant(status: number) {
  if (status === 2 || status === 4) return "success" as const;
  if (status === 1 || status === 3) return "warning" as const;
  if (status === 5 || status === 6) return "danger" as const;
  return "default" as const;
}

function formatDate(value?: string | null) {
  return value
    ? new Date(value).toLocaleDateString("tr-TR")
    : "—";
}

function formatDateTime(value?: string | null) {
  return value
    ? new Date(value).toLocaleString("tr-TR")
    : "—";
}

function formatNumber(value: number) {
  return quantity(value);
}

function formatMoney(value: number, currency: string) {
  return currencyMoney(value, currency);
}

/**
 * Onay/red/iptal akışı prompt + confirm ikilisiyle yürüyordu:
 * önce gerekçe soruluyor, sonra ayrı bir pencerede onay isteniyordu.
 * Gerekçe zorunlu olduğu halde prompt boş geçilebiliyor, kod bunu
 * ancak sonradan yakalayıp hata yazıyordu — kullanıcı iki pencere
 * kapattıktan sonra baştan başlıyordu. ConfirmDialog gerekçe
 * yazılmadan onay düğmesini açmıyor.
 */
type OrderAction = "submit" | "approve" | "reject" | "cancel";

const ACTION_DIALOGS: Record<
  OrderAction,
  { title: string; description: string; confirmLabel: string; reason: boolean }
> = {
  submit: {
    title: "Sipariş onaya gönderilsin mi?",
    description:
      "Sipariş onaycıya düşer ve onaylanana kadar üzerinde değişiklik yapılamaz.",
    confirmLabel: "Onaya Gönder",
    reason: false,
  },
  approve: {
    title: "Sipariş onaylansın mı?",
    description:
      "Onaydan sonra sipariş tedarikçiye gönderilebilir ve bütçeye işlenir.",
    confirmLabel: "Onayla",
    reason: false,
  },
  reject: {
    title: "Sipariş reddedilsin mi?",
    description:
      "Gerekçe talep sahibine gider; neyin yanlış olduğunu buradan öğrenir.",
    confirmLabel: "Reddet",
    reason: true,
  },
  cancel: {
    title: "Sipariş iptal edilsin mi?",
    description:
      "İptal edilen sipariş yeniden açılamaz; gerekçe kayda geçer.",
    confirmLabel: "İptal Et",
    reason: true,
  },
};


export default function PurchaseOrderDetailPage() {
  const params = useParams<{ id: string }>();

  const [order, setOrder] =
    useState<PurchaseOrderDetail | null>(null);
  const [approvalContext, setApprovalContext] =
    useState<PurchaseOrderApprovalContext | null>(null);
  const [loading, setLoading] = useState(true);
  const [processing, setProcessing] = useState(false);
  const [downloadingPdf, setDownloadingPdf] =
    useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [pendingAction, setPendingAction] = useState<OrderAction | null>(null);

  const load = useCallback(async () => {
    if (!params.id) return;

    setLoading(true);
    setError("");

    try {
      const [result, approval] = await Promise.all([
        purchaseOrderService.getById(params.id),
        procurementApprovalService.getOrderContext(params.id),
      ]);

      setOrder(result);
      setApprovalContext(approval);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Satın alma siparişi yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const receivedTotal = useMemo(
    () =>
      order?.items.reduce(
        (total, item) => total + item.receivedQuantity,
        0
      ) ?? 0,
    [order]
  );

  const orderedTotal = useMemo(
    () =>
      order?.items.reduce(
        (total, item) => total + item.quantity,
        0
      ) ?? 0,
    [order]
  );

  const deliveryRate =
    orderedTotal > 0
      ? (receivedTotal / orderedTotal) * 100
      : 0;

  async function downloadPurchaseOrderPdf() {
    if (!order?.id) {
      setError(
        "PDF oluşturmak için sipariş bilgisi bulunamadı."
      );
      return;
    }

    try {
      setDownloadingPdf(true);
      setError("");

      await reportService.downloadPurchaseOrderPdf(
        order.id
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Sipariş PDF'i indirilemedi."
      );
    } finally {
      setDownloadingPdf(false);
    }
  }


  async function runAction(action: OrderAction, reason: string) {
    if (!order) return;

    setProcessing(true);
    setError("");
    setSuccess("");

    try {
      const result =
        action === "submit"
          ? await purchaseOrderService.submit(order.id)
          : action === "approve"
            ? await purchaseOrderService.approve(order.id)
            : action === "reject"
              ? await purchaseOrderService.reject(order.id, reason)
              : await purchaseOrderService.cancel(order.id, reason);

      setPendingAction(null);
      setSuccess(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İşlem gerçekleştirilemedi."
      );
    } finally {
      setProcessing(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title={order?.orderNumber ?? "Satın Alma Siparişi"}
      description={
        order
          ? `${order.supplierTitle} · ${order.projectCode}`
          : "Sipariş bilgileri yükleniyor"
      }
    >
      <div className="mb-5 flex flex-wrap items-center gap-2 text-sm text-slate-500">
        <Link
          href="/satin-alma/siparis"
          className="hover:text-slate-900"
        >
          Satın Alma Siparişleri
        </Link>

        <span>›</span>

        <strong className="text-slate-800">
          {order?.orderNumber ?? "Sipariş"}
        </strong>
      </div>

      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {success && (
        <div className="mb-5 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          {success}
        </div>
      )}

      {loading ? (
        <Card>
          <CardContent className="py-12 text-center text-sm text-slate-500">
            Sipariş bilgileri yükleniyor...
          </CardContent>
        </Card>
      ) : !order ? (
        <EmptyState
          title="Satın alma siparişi bulunamadı"
          description="Kayıt silinmiş veya erişim yetkiniz olmayabilir."
        />
      ) : (
        <>
          <Card className="mb-6">
            <CardContent className="py-5">
              <div className="flex flex-col gap-5 xl:flex-row xl:items-center xl:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant={statusVariant(order.status)}>
                      {statusLabels[order.status]}
                    </Badge>

                    <Badge variant="info">
                      {order.currency}
                    </Badge>
                  </div>

                  <h2 className="mt-3 text-2xl font-semibold text-slate-900">
                    {order.orderNumber}
                  </h2>

                  <p className="mt-1 text-sm text-slate-500">
                    {order.supplierTitle} · {order.projectCode}
                  </p>
                </div>

                <div className="flex flex-wrap gap-3">
                  {order.status === 0 && (
                    <Button
                      loading={processing}
                      onClick={() => setPendingAction("submit")}
                    >
                      Onaya Gönder
                    </Button>
                  )}

                  {order.status === 1 &&
                    approvalContext?.canCurrentUserApprove && (
                    <>
                      <Button
                        loading={processing}
                        onClick={() => setPendingAction("approve")}
                      >
                        Onayla
                      </Button>

                      <Button
                        variant="danger"
                        loading={processing}
                        onClick={() => setPendingAction("reject")}
                      >
                        Reddet
                      </Button>
                    </>
                  )}

                  {order.status === 1 &&
                    !approvalContext?.canCurrentUserApprove && (
                      <Link
                        href="/satin-alma/butce-onay"
                        className="inline-flex h-10 items-center justify-center rounded-lg border border-amber-300 bg-amber-50 px-4 text-sm font-medium text-amber-800 hover:bg-amber-100"
                      >
                        Onay Merkezini Aç
                      </Link>
                    )}

                  {[0, 1, 6].includes(order.status) && (
                    <Button
                      variant="danger"
                      loading={processing}
                      onClick={() => setPendingAction("cancel")}
                    >
                      İptal Et
                    </Button>
                  )}

                  {[2, 3].includes(order.status) && (
                    <Link
                      href={`/depo-stok/mal-kabul/yeni?siparis=${order.id}`}
                      className="inline-flex h-10 items-center justify-center rounded-lg bg-emerald-700 px-4 text-sm font-medium text-white hover:bg-emerald-800"
                    >
                      Mal Kabul Oluştur
                    </Link>
                  )}

                  <Button
                    variant="secondary"
                    loading={downloadingPdf}
                    disabled={downloadingPdf}
                    onClick={downloadPurchaseOrderPdf}
                  >
                    Sipariş PDF İndir
                  </Button>

                  <Link
                    href={`/satin-alma/siparis/${order.id}/yazdir`}
                    target="_blank"
                    className="inline-flex h-10 items-center justify-center rounded-lg bg-brand-700 px-4 text-sm font-medium text-white hover:bg-brand-600"
                  >
                    Yazdır
                  </Link>

                  <Link
                    href={`/satin-alma/rfq/${order.rfqId}`}
                    className="inline-flex h-10 items-center justify-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
                  >
                    RFQ Aç
                  </Link>

                  <Link
                    href={`/satin-alma/${order.purchaseRequestId}`}
                    className="inline-flex h-10 items-center justify-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
                  >
                    Satın Alma Talebini Aç
                  </Link>
                </div>
              </div>
            </CardContent>
          </Card>

          <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <StatCard
              title="Sipariş Kalemi"
              value={order.items.length}
              icon="▤"
            />

            <StatCard
              title="Sipariş Toplamı"
              value={formatMoney(
                order.grandTotal,
                order.currency
              )}
              icon="₺"
            />

            <StatCard
              title="Teslim Alınan"
              value={formatNumber(receivedTotal)}
              icon="✓"
            />

            <StatCard
              title="Teslim Oranı"
              value={percent(deliveryRate)}
              icon="%"
            />
          </div>

          <Card className="mb-6">
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Bütçe ve Onay Akışı
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Siparişin TRY karşılığı, proje bütçesi ve yetki kademeleri
                </p>
              </div>
              {approvalContext?.budgetAllowsOrder ? (
                <Badge variant="success">Bütçe Uygun</Badge>
              ) : (
                <Badge variant="warning">Kontrol Gerekli</Badge>
              )}
            </CardHeader>
            <CardContent>
              {approvalContext ? (
                <>
                  {approvalContext.warnings.map((warning) => (
                    <div
                      key={warning}
                      className="mb-3 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
                    >
                      {warning}
                    </div>
                  ))}

                  <div className="mb-5 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                    <div className="rounded-lg border border-slate-200 p-4">
                      <div className="text-xs font-medium uppercase tracking-wide text-slate-500">
                        TRY Karşılığı
                      </div>
                      <div className="mt-2 font-semibold text-slate-900">
                        {formatMoney(approvalContext.orderAmountTry, "TRY")}
                      </div>
                    </div>
                    <div className="rounded-lg border border-slate-200 p-4">
                      <div className="text-xs font-medium uppercase tracking-wide text-slate-500">
                        Proje Bütçesi
                      </div>
                      <div className="mt-2 font-semibold text-slate-900">
                        {approvalContext.budget
                          ? formatMoney(approvalContext.budget.amountTry, "TRY")
                          : "Tanımlı değil"}
                      </div>
                    </div>
                    <div className="rounded-lg border border-slate-200 p-4">
                      <div className="text-xs font-medium uppercase tracking-wide text-slate-500">
                        Sipariş Sonrası Kalan
                      </div>
                      <div className="mt-2 font-semibold text-slate-900">
                        {approvalContext.budgetRemainingAfterOrderTry != null
                          ? formatMoney(
                              approvalContext.budgetRemainingAfterOrderTry,
                              "TRY",
                            )
                          : "—"}
                      </div>
                    </div>
                    <div className="rounded-lg border border-slate-200 p-4">
                      <div className="text-xs font-medium uppercase tracking-wide text-slate-500">
                        Sıradaki Adım
                      </div>
                      <div className="mt-2 font-semibold text-slate-900">
                        {approvalContext.currentStageName || "—"}
                      </div>
                    </div>
                  </div>

                  {approvalContext.steps.length > 0 ? (
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead>Sıra</TableHead>
                          <TableHead>Onay Kademesi</TableHead>
                          <TableHead>Gerekli Yetki</TableHead>
                          <TableHead>Durum</TableHead>
                          <TableHead>Karar Veren</TableHead>
                          <TableHead>Tarih</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {approvalContext.steps.map((step) => (
                          <TableRow key={`${step.sequence}-${step.code}`}>
                            <TableCell>{step.sequence}</TableCell>
                            <TableCell>
                              <strong>{step.name}</strong>
                            </TableCell>
                            <TableCell>{step.requiredAuthority}</TableCell>
                            <TableCell>
                              <Badge
                                variant={
                                  step.status === "Approved"
                                    ? "success"
                                    : step.status === "Rejected"
                                      ? "danger"
                                      : "warning"
                                }
                              >
                                {step.status === "Approved"
                                  ? "Onaylandı"
                                  : step.status === "Rejected"
                                    ? "Reddedildi"
                                    : "Bekliyor"}
                              </Badge>
                            </TableCell>
                            <TableCell>
                              {step.decidedByUsername || "—"}
                            </TableCell>
                            <TableCell>
                              {formatDateTime(step.decidedAtUtc)}
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  ) : (
                    <EmptyState
                      title="Onay planı henüz oluşmadı"
                      description="Şirket onay politikası tanımlandıktan sonra sipariş onaya gönderilebilir."
                    />
                  )}
                </>
              ) : (
                <p className="text-sm text-slate-500">
                  Bütçe ve onay bilgileri yüklenemedi.
                </p>
              )}
            </CardContent>
          </Card>

          <div className="mb-6 grid gap-6 xl:grid-cols-3">
            <Card className="xl:col-span-2">
              <CardHeader>
                <div>
                  <h2 className="text-lg font-semibold text-slate-900">
                    Sipariş Bilgileri
                  </h2>
                  <p className="mt-1 text-sm text-slate-500">
                    Belge, proje ve teslim bilgileri
                  </p>
                </div>
              </CardHeader>

              <CardContent>
                <dl className="grid gap-5 md:grid-cols-2">
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Sipariş Tarihi
                    </dt>
                    <dd className="mt-1 text-slate-900">
                      {formatDate(order.orderDate)}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Beklenen Teslim
                    </dt>
                    <dd className="mt-1 text-slate-900">
                      {formatDate(
                        order.expectedDeliveryDate
                      )}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Proje
                    </dt>
                    <dd className="mt-1 font-medium text-slate-900">
                      {order.projectCode} · {order.projectName}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Kaynak RFQ
                    </dt>
                    <dd className="mt-1 font-medium text-slate-900">
                      {order.rfqNumber}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Satın Alma Talebi
                    </dt>
                    <dd className="mt-1 font-medium text-slate-900">
                      {order.purchaseRequestNumber}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Ödeme Koşulu
                    </dt>
                    <dd className="mt-1 text-slate-900">
                      {order.paymentTerm || "—"}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Para Birimi
                    </dt>
                    <dd className="mt-1 text-slate-900">
                      {order.currency}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Kur
                    </dt>
                    <dd className="mt-1 text-slate-900">
                      {formatNumber(order.exchangeRate)}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Onay Tarihi
                    </dt>
                    <dd className="mt-1 text-slate-900">
                      {formatDateTime(order.approvedAtUtc)}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Teslim Adresi
                    </dt>
                    <dd className="mt-1 whitespace-pre-wrap text-slate-900">
                      {order.deliveryAddress || "—"}
                    </dd>
                  </div>
                </dl>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <div>
                  <h2 className="text-lg font-semibold text-slate-900">
                    Tedarikçi
                  </h2>
                  <p className="mt-1 text-sm text-slate-500">
                    Cari ve iletişim bilgileri
                  </p>
                </div>
              </CardHeader>

              <CardContent>
                <h3 className="text-lg font-semibold text-slate-950">
                  {order.supplierTitle}
                </h3>

                <p className="mt-1 text-sm text-slate-500">
                  {order.supplierCode}
                </p>

                <dl className="mt-5 space-y-4">
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Yetkili
                    </dt>
                    <dd className="mt-1 text-sm text-slate-900">
                      {order.supplierAuthorizedPerson || "—"}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Telefon
                    </dt>
                    <dd className="mt-1 text-sm text-slate-900">
                      {order.supplierPhone || "—"}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      E-posta
                    </dt>
                    <dd className="mt-1 break-all text-sm text-slate-900">
                      {order.supplierEmail || "—"}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Adres
                    </dt>
                    <dd className="mt-1 whitespace-pre-wrap text-sm text-slate-900">
                      {order.supplierAddress || "—"}
                    </dd>
                  </div>
                </dl>
              </CardContent>
            </Card>
          </div>

          {(order.description || order.notes) && (
            <Card className="mb-6">
              <CardHeader>
                <div>
                  <h2 className="text-lg font-semibold text-slate-900">
                    Açıklama ve Notlar
                  </h2>
                </div>
              </CardHeader>

              <CardContent>
                {order.description && (
                  <p className="whitespace-pre-wrap text-sm text-slate-700">
                    {order.description}
                  </p>
                )}

                {order.notes && (
                  <>
                    {order.description && (
                      <hr className="my-4 border-slate-200" />
                    )}

                    <p className="whitespace-pre-wrap text-sm text-slate-500">
                      {order.notes}
                    </p>
                  </>
                )}
              </CardContent>
            </Card>
          )}

          <Card className="mb-6">
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Sipariş Kalemleri
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  RFQ kazanan teklifinden aktarılan malzeme ve fiyatlar
                </p>
              </div>
            </CardHeader>

            <CardContent>
              {order.items.length === 0 ? (
                <EmptyState
                  title="Sipariş kalemi bulunamadı"
                  description="Bu siparişe bağlı malzeme satırı bulunmuyor."
                />
              ) : (
                <div className="overflow-x-auto">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>No</TableHead>
                        <TableHead className="min-w-64">
                          Malzeme
                        </TableHead>
                        <TableHead>Marka (istenen / verilen)</TableHead>
                        <TableHead className="text-right">
                          Miktar
                        </TableHead>
                        <TableHead>Birim</TableHead>
                        <TableHead className="text-right">
                          Birim Fiyat
                        </TableHead>
                        <TableHead className="text-right">
                          İskonto
                        </TableHead>
                        <TableHead className="text-right">
                          Net Birim
                        </TableHead>
                        <TableHead>Termin</TableHead>
                        <TableHead>Teslim Tarihi</TableHead>
                        <TableHead className="text-right">
                          Teslim Alınan
                        </TableHead>
                        <TableHead className="text-right">
                          Toplam
                        </TableHead>
                      </TableRow>
                    </TableHeader>

                    <TableBody>
                      {order.items.map((item) => (
                        <TableRow key={item.id}>
                          <TableCell>
                            {item.lineNumber}
                          </TableCell>

                          <TableCell>
                            <strong className="text-slate-900">
                              {item.materialDescription}
                            </strong>

                            {item.notes && (
                              <span className="mt-1 block text-xs text-slate-500">
                                {item.notes}
                              </span>
                            )}
                          </TableCell>

                          <TableCell>
                            {/* İKİ MARKA YAN YANA: üstte talep edenin
                                istediği, altta tedarikçinin verdiği.
                                Tek alan olsaydı "Schneider istendi,
                                ABB geldi" farkı kayıtta görünmezdi. */}
                            <span className="block text-xs text-slate-500">
                              İstenen: {requestedBrandLabel(item)}
                            </span>

                            <span className="mt-1 block text-slate-900">
                              {item.brand || "—"}
                              {item.model ? ` · ${item.model}` : ""}
                            </span>

                            {brandMismatch(item) && (
                              <span className="mt-1 block">
                                <Badge variant="warning">
                                  İstenen markadan farklı
                                </Badge>
                              </span>
                            )}
                          </TableCell>

                          <TableCell className="text-right font-medium">
                            {formatNumber(item.quantity)}
                          </TableCell>

                          <TableCell>{item.unit}</TableCell>

                          <TableCell className="text-right">
                            {formatMoney(
                              item.unitPrice,
                              order.currency
                            )}
                          </TableCell>

                          <TableCell className="text-right">
                            %{formatNumber(item.discountRate)}
                          </TableCell>

                          <TableCell className="text-right">
                            {formatMoney(
                              item.netUnitPrice,
                              order.currency
                            )}
                          </TableCell>

                          <TableCell>
                            {item.deliveryDays !== null &&
                            item.deliveryDays !== undefined
                              ? `${item.deliveryDays} gün`
                              : "—"}
                          </TableCell>

                          <TableCell>
                            {formatDate(
                              item.expectedDeliveryDate
                            )}
                          </TableCell>

                          <TableCell className="text-right">
                            {formatNumber(
                              item.receivedQuantity
                            )}
                          </TableCell>

                          <TableCell className="text-right font-semibold">
                            {formatMoney(
                              item.totalPrice,
                              order.currency
                            )}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Sipariş Özeti
                </h2>
              </div>
            </CardHeader>

            <CardContent>
              <div className="ml-auto grid max-w-lg gap-3">
                <div className="flex items-center justify-between border-b border-slate-200 pb-3">
                  <span className="text-sm text-slate-500">
                    Ara Toplam
                  </span>
                  <strong className="text-slate-900">
                    {formatMoney(
                      order.subtotal,
                      order.currency
                    )}
                  </strong>
                </div>

                <div className="flex items-center justify-between border-b border-slate-200 pb-3">
                  <span className="text-sm text-slate-500">
                    Toplam İskonto
                  </span>
                  <strong className="text-slate-900">
                    {formatMoney(
                      order.discountTotal,
                      order.currency
                    )}
                  </strong>
                </div>

                <div className="flex items-center justify-between pt-2">
                  <span className="text-base font-semibold text-slate-900">
                    Genel Toplam
                  </span>
                  <strong className="text-2xl text-slate-950">
                    {formatMoney(
                      order.grandTotal,
                      order.currency
                    )}
                  </strong>
                </div>
              </div>
            </CardContent>
          </Card>
        </>
      )}
      {pendingAction && (
        <ConfirmDialog
          /* key: her açılışta gerekçe alanı temiz başlasın. */
          key={pendingAction}
          open
          title={ACTION_DIALOGS[pendingAction].title}
          description={ACTION_DIALOGS[pendingAction].description}
          confirmLabel={ACTION_DIALOGS[pendingAction].confirmLabel}
          requireReason={ACTION_DIALOGS[pendingAction].reason}
          busy={processing}
          onCancel={() => setPendingAction(null)}
          onConfirm={(reason) => void runAction(pendingAction, reason)}
        />
      )}

    </ErpShell>
  );
}
