"use client";

import Link from "next/link";
import {
  useParams,
  useRouter,
} from "next/navigation";
import { useEffect, useMemo, useState } from "react";
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
  rfqService,
  type RfqComparison,
} from "@/services/rfq.service";

import {
  purchaseOrderService,
  type CreatePurchaseOrderFromRfqResponse,
} from "@/services/purchase-order.service";
import { brandMismatch } from "@/lib/purchasing/requested-brand";
import { useModuleActions } from "@/lib/auth/module-actions";

function formatMoney(value: number, currency = "TRY") {
  return currencyMoney(value, currency);
}

function formatNumber(value: number) {
  return quantity(value);
}

function formatPercent(value: number) {
  return percent(value);
}

function scoreLabel(score: number) {
  if (score >= 90) return "Çok Uygun";
  if (score >= 75) return "Uygun";
  if (score >= 60) return "Değerlendirilebilir";
  if (score > 0) return "Zayıf";
  return "Teklif Yok";
}

function scoreVariant(score: number) {
  if (score >= 90) return "success" as const;
  if (score >= 60) return "warning" as const;
  if (score > 0) return "danger" as const;
  return "default" as const;
}

export default function RfqComparisonPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();

  /*
   * Aksiyon izinleri UÇLARDAN:
   *   POST rfq/{id}/close        -> purchasing-rfq.edit
   *   POST rfq/{id}/award        -> purchasing-rfq.approve
   *   POST purchase-orders/from-rfq -> purchasing-orders.create
   */
  const actions = useModuleActions("purchasing-rfq");

  const [comparison, setComparison] =
    useState<RfqComparison | null>(null);
  const [selectedSupplierId, setSelectedSupplierId] =
    useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [closing, setClosing] = useState(false);
  const [awarding, setAwarding] = useState(false);
  const [awardedSupplierId, setAwardedSupplierId] =
    useState<string | null>(null);

  const [creatingOrder, setCreatingOrder] =
    useState(false);

  const [createdOrder, setCreatedOrder] =
    useState<CreatePurchaseOrderFromRfqResponse | null>(
      null,
    );

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [confirming, setConfirming] =
    useState<"kazanan" | "siparis" | "kapat" | null>(null);

  useEffect(() => {
    let active = true;

    async function initialize() {
      if (!params.id) return;

      setLoading(true);
      setError("");

      try {
        const result = await rfqService.getComparison(params.id);
        if (!active) return;

        setComparison(result);
        setSelectedSupplierId(
          result.recommendedSupplierId ?? result.lowestSupplierId ?? null
        );
      } catch (err) {
        if (!active) return;

        setError(
          err instanceof Error
            ? err.message
            : "RFQ karşılaştırması yüklenemedi."
        );
      } finally {
        if (active) setLoading(false);
      }
    }

    void initialize();
    return () => {
      active = false;
    };
  }, [params.id]);

  const quotedSuppliers = useMemo(
    () =>
      comparison?.suppliers
        .filter((supplier) => supplier.hasQuotation)
        .sort((a, b) => a.rank - b.rank) ?? [],
    [comparison]
  );

  const selectedSupplier =
    comparison?.suppliers.find(
      (supplier) =>
        supplier.rfqSupplierId === selectedSupplierId
    ) ?? null;

  async function awardSupplier() {
    if (!comparison || !selectedSupplier) return;

    setAwarding(true);
    setError("");
    setSuccess("");
    setCreatedOrder(null);

    try {
      const result = await rfqService.award(
        comparison.rfqId,
        selectedSupplier.rfqSupplierId
      );

      setAwardedSupplierId(result.rfqSupplierId);
      setConfirming(null);
      setSuccess(
        `${result.supplierTitle} kazanan tedarikçi olarak seçildi.`
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Kazanan tedarikçi seçilemedi."
      );
    } finally {
      setAwarding(false);
    }
  }

  async function createPurchaseOrder() {
    if (!comparison) return;

    if (!awardedSupplierId) {
      setError(
        "Önce kazanan tedarikçi seçilmelidir.",
      );
      return;
    }

    setCreatingOrder(true);
    setError("");
    setSuccess("");

    try {
      const result =
        await purchaseOrderService.createFromRfq(
          comparison.rfqId,
        );

      setCreatedOrder(result);
      setConfirming(null);

      setSuccess(
        `${result.orderNumber} numaralı satın alma siparişi oluşturuldu.`,
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Satın alma siparişi oluşturulamadı.",
      );
    } finally {
      setCreatingOrder(false);
    }
  }


  function openCreatedOrder() {
    if (!createdOrder?.id) return;

    router.push(
      `/satin-alma/siparis/${createdOrder.id}`,
    );

    router.refresh();
  }


  async function closeRfq() {
    if (!comparison) return;

    setClosing(true);
    setError("");
    setSuccess("");

    try {
      const result = await rfqService.close(comparison.rfqId);
      setConfirming(null);
      setSuccess(result.message);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "RFQ kapatılamadı."
      );
    } finally {
      setClosing(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title={
        comparison
          ? `${comparison.rfqNumber} Karşılaştırma`
          : "RFQ Karşılaştırma"
      }
      description="Tedarikçi tekliflerini fiyat, termin ve ödeme koşullarına göre karşılaştırın"
    >
      <div className="mb-5 flex flex-wrap items-center gap-2 text-sm text-slate-500">
        <Link
          href="/satin-alma/rfq"
          className="hover:text-slate-900"
        >
          RFQ Yönetimi
        </Link>

        <span>›</span>

        <Link
          href={`/satin-alma/rfq/${params.id}`}
          className="hover:text-slate-900"
        >
          {comparison?.rfqNumber ?? "RFQ"}
        </Link>

        <span>›</span>

        <strong className="text-slate-800">
          Karşılaştırma
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
            Teklifler karşılaştırılıyor...
          </CardContent>
        </Card>
      ) : !comparison ? (
        <EmptyState
          title="Karşılaştırma bulunamadı"
          description="RFQ bilgileri alınamadı."
        />
      ) : quotedSuppliers.length === 0 ? (
        <EmptyState
          title="Karşılaştırılacak teklif yok"
          description="En az bir tedarikçi teklifi kaydedildikten sonra karşılaştırma yapılabilir."
        />
      ) : (
        <>
          <Card className="mb-6">
            <CardContent className="py-5">
              <div className="flex flex-col gap-5 xl:flex-row xl:items-center xl:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant="info">
                      {comparison.rfqNumber}
                    </Badge>

                    <Badge variant="success">
                      {quotedSuppliers.length} Teklif
                    </Badge>
                  </div>

                  <h2 className="mt-3 text-2xl font-semibold text-slate-900">
                    Tedarikçi Teklif Karşılaştırması
                  </h2>

                  <p className="mt-1 text-sm text-slate-500">
                    En düşük teklif otomatik olarak belirlenmiştir.
                  </p>
                </div>

                <div className="flex flex-wrap gap-3">
                  <Link
                    href={`/satin-alma/rfq/${comparison.rfqId}`}
                    className="inline-flex h-10 items-center justify-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
                  >
                    RFQ Detayına Dön
                  </Link>

                  {actions.can("edit") && (
                    <Button
                      variant="danger"
                      loading={closing}
                      onClick={() => setConfirming("kapat")}
                    >
                      RFQ Kapat
                    </Button>
                  )}
                </div>
              </div>
            </CardContent>
          </Card>

          <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <StatCard
              title="En Düşük Teklif (TRY)"
              value={formatMoney(
                comparison.lowestNormalizedTotal,
                comparison.comparisonCurrency
              )}
              icon="↓"
            />

            <StatCard
              title="Teklif Ortalaması"
              value={formatMoney(
                comparison.averageNormalizedTotal,
                comparison.comparisonCurrency
              )}
              icon="∑"
            />

            <StatCard
              title="İkinci Teklife Göre Tasarruf"
              value={formatMoney(
                comparison.savingVsSecondLowest,
                comparison.comparisonCurrency
              )}
              icon="₺"
            />

            <StatCard
              title="Tasarruf Oranı"
              value={formatPercent(comparison.savingRate)}
              icon="%"
            />
          </div>

          <div className="mb-6 grid gap-6 xl:grid-cols-3">
            <div className="grid gap-4 md:grid-cols-2 xl:col-span-2">
              {quotedSuppliers.map((supplier) => {
                const isLowest =
                  comparison.lowestSupplierId ===
                  supplier.rfqSupplierId;

                const isSelected =
                  selectedSupplierId ===
                  supplier.rfqSupplierId;

                return (
                  <button
                    key={supplier.rfqSupplierId}
                    type="button"
                    onClick={() =>
                      setSelectedSupplierId(
                        supplier.rfqSupplierId
                      )
                    }
                    className={`rounded-xl border p-5 text-left transition ${
                      isSelected
                        ? "border-slate-900 ring-2 ring-slate-200"
                        : "border-slate-200 hover:border-slate-400"
                    }`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <span className="text-xs font-medium uppercase tracking-wide text-slate-500">
                          {supplier.rank}. Sıra
                        </span>

                        <h3 className="mt-1 text-lg font-semibold text-slate-900">
                          {supplier.supplierTitle}
                        </h3>
                      </div>

                      <div className="flex flex-col items-end gap-2">
                        {supplier.isRecommended ? (
                          <Badge variant="success">Önerilen</Badge>
                        ) : null}
                        {isLowest ? (
                          <Badge variant="info">En Düşük</Badge>
                        ) : null}
                      </div>
                    </div>

                    <p className="mt-5 text-2xl font-bold text-slate-950">
                      {formatMoney(
                        supplier.grandTotal,
                        supplier.currency
                      )}
                    </p>

                    <p className="mt-1 text-xs text-slate-500">
                      TRY karşılığı: {formatMoney(
                        supplier.normalizedGrandTotal,
                        comparison.comparisonCurrency
                      )} · Kur: {formatNumber(supplier.exchangeRate)}
                    </p>

                    <div className="mt-4 grid grid-cols-2 gap-3 text-sm">
                      <div>
                        <span className="block text-slate-500">
                          Termin
                        </span>
                        <strong className="mt-1 block text-slate-900">
                          {supplier.deliveryDays !== null &&
                          supplier.deliveryDays !== undefined
                            ? `${supplier.deliveryDays} gün`
                            : "Belirtilmedi"}
                        </strong>
                      </div>

                      <div>
                        <span className="block text-slate-500">
                          Ödeme
                        </span>
                        <strong className="mt-1 block text-slate-900">
                          {supplier.paymentTerm || "Belirtilmedi"}
                        </strong>
                      </div>
                    </div>

                    <div className="mt-4 flex items-center justify-between border-t border-slate-200 pt-4">
                      <span className="text-sm text-slate-500">
                        Değerlendirme
                      </span>

                      <Badge variant={scoreVariant(supplier.decisionScore)}>
                        {scoreLabel(supplier.decisionScore)} · {supplier.decisionScore}/100
                      </Badge>
                    </div>
                  </button>
                );
              })}
            </div>

            <Card>
              <CardHeader>
                <div>
                  <h2 className="text-lg font-semibold text-slate-900">
                    Karar Özeti
                  </h2>
                  <p className="mt-1 text-sm text-slate-500">
                    Seçili tedarikçinin değerlendirmesi
                  </p>
                </div>
              </CardHeader>

              <CardContent>
                {selectedSupplier ? (
                  <div>
                    <Badge
                      variant={
                        selectedSupplier.isRecommended
                          ? "success"
                          : "warning"
                      }
                    >
                      {selectedSupplier.isRecommended
                        ? "Önerilen Tedarikçi"
                        : "Alternatif Tedarikçi"}
                    </Badge>

                    <h3 className="mt-4 text-xl font-semibold text-slate-950">
                      {selectedSupplier.supplierTitle}
                    </h3>

                    <dl className="mt-5 space-y-4">
                      <div className="flex items-center justify-between border-b border-slate-200 pb-3">
                        <dt className="text-sm text-slate-500">
                          Teklif Toplamı
                        </dt>
                        <dd className="font-semibold text-slate-900">
                          {formatMoney(
                            selectedSupplier.grandTotal,
                            selectedSupplier.currency
                          )}
                        </dd>
                      </div>

                      <div className="flex items-center justify-between border-b border-slate-200 pb-3">
                        <dt className="text-sm text-slate-500">
                          TRY Karşılığı
                        </dt>
                        <dd className="font-semibold text-slate-900">
                          {formatMoney(
                            selectedSupplier.normalizedGrandTotal,
                            comparison.comparisonCurrency
                          )}
                        </dd>
                      </div>

                      <div className="flex items-center justify-between border-b border-slate-200 pb-3">
                        <dt className="text-sm text-slate-500">
                          Termin
                        </dt>
                        <dd className="font-semibold text-slate-900">
                          {selectedSupplier.deliveryDays !== null &&
                          selectedSupplier.deliveryDays !== undefined
                            ? `${selectedSupplier.deliveryDays} gün`
                            : "—"}
                        </dd>
                      </div>

                      <div className="flex items-center justify-between border-b border-slate-200 pb-3">
                        <dt className="text-sm text-slate-500">
                          Ödeme
                        </dt>
                        <dd className="max-w-44 text-right font-semibold text-slate-900">
                          {selectedSupplier.paymentTerm || "—"}
                        </dd>
                      </div>

                      <div className="flex items-center justify-between">
                        <dt className="text-sm text-slate-500">
                          Puan
                        </dt>
                        <dd>
                          <Badge
                            variant={scoreVariant(
                              selectedSupplier.decisionScore
                            )}
                          >
                            {selectedSupplier.decisionScore}/100
                          </Badge>
                        </dd>
                      </div>
                    </dl>

                    <div className="mt-5 grid grid-cols-2 gap-3 text-xs">
                      <div className="rounded-lg bg-slate-50 p-3">
                        <span className="text-slate-500">Fiyat</span>
                        <strong className="mt-1 block text-slate-900">
                          {selectedSupplier.priceScore}/100
                        </strong>
                      </div>
                      <div className="rounded-lg bg-slate-50 p-3">
                        <span className="text-slate-500">Termin</span>
                        <strong className="mt-1 block text-slate-900">
                          {selectedSupplier.deliveryTermScore}/100
                        </strong>
                      </div>
                      <div className="rounded-lg bg-slate-50 p-3">
                        <span className="text-slate-500">Geçmiş</span>
                        <strong className="mt-1 block text-slate-900">
                          {selectedSupplier.historicalPerformanceScore}/100
                        </strong>
                      </div>
                      <div className="rounded-lg bg-slate-50 p-3">
                        <span className="text-slate-500">Veri Güveni</span>
                        <strong className="mt-1 block text-slate-900">
                          {selectedSupplier.confidence}
                        </strong>
                      </div>
                    </div>

                    {awardedSupplierId ===
                    selectedSupplier.rfqSupplierId ? (
                      <div className="mt-6 space-y-4">
                        <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800">
                          <div className="font-semibold">
                            Kazanan tedarikçi seçildi
                          </div>

                          <p className="mt-1">
                            {selectedSupplier.supplierTitle}
                            {" "}teklifi satın alma siparişine
                            dönüştürülmeye hazır.
                          </p>
                        </div>

                        {createdOrder ? (
                          <div className="rounded-lg border border-blue-200 bg-blue-50 p-4">
                            <div className="text-xs font-medium uppercase tracking-wide text-blue-700">
                              Oluşturulan Sipariş
                            </div>

                            <div className="mt-2 text-lg font-semibold text-slate-950">
                              {createdOrder.orderNumber}
                            </div>

                            <div className="mt-2 text-sm text-slate-600">
                              {createdOrder.supplierTitle}
                            </div>

                            <div className="mt-1 text-sm font-semibold text-slate-900">
                              {formatMoney(
                                createdOrder.grandTotal,
                                createdOrder.currency,
                              )}
                            </div>

                            <Button
                              onClick={openCreatedOrder}
                              className="mt-4 w-full"
                            >
                              Sipariş Detayına Git
                            </Button>
                          </div>
                        ) : (
                          <Button
                            loading={creatingOrder}
                            disabled={creatingOrder}
                            onClick={() => setConfirming("siparis")}
                            className="w-full"
                          >
                            Satın Alma Siparişi Oluştur
                          </Button>
                        )}

                        <p className="text-xs text-slate-500">
                          Sipariş oluşturulduğunda teklif
                          kalemleri, fiyatlar, marka, model,
                          termin ve ödeme koşulları otomatik
                          aktarılır.
                        </p>
                      </div>
                    ) : (
                      <div className="mt-6 space-y-3">
                        {actions.can("approve") && (
                          <Button
                            loading={awarding}
                            disabled={!selectedSupplier.hasQuotation}
                            onClick={() => setConfirming("kazanan")}
                            className="w-full"
                          >
                            Kazanan Tedarikçi Seç
                          </Button>
                        )}

                        <p className="text-xs text-slate-500">
                          Seçimden sonra RFQ sonuçlandırılır ve
                          teklif girişi kapatılır.
                        </p>
                      </div>
                    )}
                  </div>
                ) : (
                  <p className="text-sm text-slate-500">
                    Değerlendirmek için bir tedarikçi seçin.
                  </p>
                )}
              </CardContent>
            </Card>
          </div>

          <Card>
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Kalem Bazlı Fiyat Karşılaştırması
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Tüm tekliflerin birim ve toplam fiyatları
                </p>
              </div>
            </CardHeader>

            <CardContent>
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="min-w-64">
                        Malzeme
                      </TableHead>

                      <TableHead className="text-right">
                        Talep Miktarı
                      </TableHead>

                      <TableHead>Birim</TableHead>

                      {quotedSuppliers.map((supplier) => (
                        <TableHead
                          key={supplier.rfqSupplierId}
                          className="min-w-52 text-right"
                        >
                          {supplier.supplierTitle}
                        </TableHead>
                      ))}
                    </TableRow>
                  </TableHeader>

                  <TableBody>
                    {quotedSuppliers[0]?.items.map(
                      (baseItem) => (
                        <TableRow key={baseItem.rfqItemId}>
                          <TableCell>
                            <strong className="text-slate-900">
                              {baseItem.materialDescription}
                            </strong>
                          </TableCell>

                          <TableCell className="text-right">
                            {formatNumber(
                              baseItem.requestedQuantity
                            )}
                          </TableCell>

                          <TableCell>
                            {baseItem.unit}
                          </TableCell>

                          {quotedSuppliers.map((supplier) => {
                            const supplierItem =
                              supplier.items.find(
                                (line) =>
                                  line.rfqItemId ===
                                  baseItem.rfqItemId
                              );

                            const itemTotals =
                              quotedSuppliers
                                .map(
                                  (row) =>
                                    row.items.find(
                                      (line) =>
                                        line.rfqItemId ===
                                        baseItem.rfqItemId
                                    )?.normalizedTotalPrice ?? 0
                                )
                                .filter((value) => value > 0);

                            const lowestLineTotal =
                              itemTotals.length > 0
                                ? Math.min(...itemTotals)
                                : 0;

                            const isLowestLine =
                              supplierItem &&
                              supplierItem.normalizedTotalPrice > 0 &&
                              supplierItem.normalizedTotalPrice ===
                                lowestLineTotal;

                            return (
                              <TableCell
                                key={supplier.rfqSupplierId}
                                className="text-right"
                              >
                                {supplierItem ? (
                                  <div>
                                    <div className="flex items-center justify-end gap-2">
                                      {isLowestLine && (
                                        <Badge variant="success">
                                          En Düşük
                                        </Badge>
                                      )}

                                      <strong className="text-slate-900">
                                        {formatMoney(
                                          supplierItem.totalPrice,
                                          supplier.currency
                                        )}
                                      </strong>
                                    </div>

                                    <span className="mt-1 block text-xs text-slate-500">
                                      Birim:{" "}
                                      {formatMoney(
                                        supplierItem.netUnitPrice,
                                        supplier.currency
                                      )}
                                    </span>

                                    <span className="mt-1 block text-xs text-slate-500">
                                      TRY: {formatMoney(
                                        supplierItem.normalizedTotalPrice,
                                        comparison.comparisonCurrency
                                      )}
                                    </span>

                                    {(supplierItem.brand ||
                                      supplierItem.model) && (
                                      <span className="mt-1 block text-xs text-slate-500">
                                        {supplierItem.brand || ""}
                                        {supplierItem.brand &&
                                        supplierItem.model
                                          ? " · "
                                          : ""}
                                        {supplierItem.model || ""}
                                      </span>
                                    )}

                                    {/* Karşılaştırmanın asıl sorusu:
                                        teklif edilen marka istenenle
                                        uyuyor mu. Muadil kabul edilen
                                        kalemde sapma zaten beklenendir,
                                        orada uyarı çıkmaz. */}
                                    {brandMismatch(supplierItem) && (
                                      <span className="mt-1 block text-xs font-medium text-amber-700">
                                        İstenen marka:{" "}
                                        {supplierItem.requestedBrand}
                                      </span>
                                    )}
                                  </div>
                                ) : (
                                  "—"
                                )}
                              </TableCell>
                            );
                          })}
                        </TableRow>
                      )
                    )}

                    <TableRow>
                      <TableCell colSpan={3}>
                        <strong className="text-slate-950">
                          Genel Toplam
                        </strong>
                      </TableCell>

                      {quotedSuppliers.map((supplier) => (
                        <TableCell
                          key={supplier.rfqSupplierId}
                          className="text-right"
                        >
                          <strong className="text-lg text-slate-950">
                            {formatMoney(
                              supplier.grandTotal,
                              supplier.currency
                            )}
                          </strong>
                          <span className="mt-1 block text-xs text-slate-500">
                            TRY: {formatMoney(
                              supplier.normalizedGrandTotal,
                              comparison.comparisonCurrency
                            )}
                          </span>
                        </TableCell>
                      ))}
                    </TableRow>
                  </TableBody>
                </Table>
              </div>
            </CardContent>
          </Card>
        </>
      )}
      {/*
        Üçü de geri dönüşü olan ya da olmayan kararlar; tarayıcı
        penceresi hangi tedarikçinin seçildiğini vurgulayamıyor ve
        işlem sürerken kilitleniyordu.
      */}
      <ConfirmDialog
        open={confirming === "kazanan"}
        title="Kazanan tedarikçi seçilsin mi?"
        description={
          selectedSupplier
            ? `${selectedSupplier.supplierTitle} kazanan olarak işaretlenir; diğer teklifler kapanır.`
            : ""
        }
        confirmLabel="Kazanan Seç"
        busy={awarding}
        onCancel={() => setConfirming(null)}
        onConfirm={() => void awardSupplier()}
      />

      <ConfirmDialog
        open={confirming === "siparis"}
        title="Kazanan tekliften sipariş oluşturulsun mu?"
        description="Teklif kalemleri satın alma siparişine aktarılır; sipariş taslak olarak açılır."
        confirmLabel="Sipariş Oluştur"
        busy={creatingOrder}
        onCancel={() => setConfirming(null)}
        onConfirm={() => void createPurchaseOrder()}
      />

      <ConfirmDialog
        open={confirming === "kapat"}
        title="RFQ kapatılsın mı?"
        description="Kapatıldıktan sonra yeni teklif girişi yapılamaz."
        confirmLabel="Kapat"
        busy={closing}
        onCancel={() => setConfirming(null)}
        onConfirm={() => void closeRfq()}
      />

    </ErpShell>
  );
}
