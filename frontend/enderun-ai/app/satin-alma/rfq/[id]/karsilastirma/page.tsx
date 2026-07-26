"use client";

import Link from "next/link";
import {
  useParams,
  useRouter,
} from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
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
  type RfqComparisonSupplier,
} from "@/services/rfq.service";

import {
  purchaseOrderService,
  type CreatePurchaseOrderFromRfqResponse,
} from "@/services/purchase-order.service";

function formatMoney(value: number, currency = "TRY") {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}

function formatNumber(value: number) {
  return new Intl.NumberFormat("tr-TR", {
    maximumFractionDigits: 4,
  }).format(value);
}

function calculateAverage(
  suppliers: RfqComparisonSupplier[]
) {
  const quoted = suppliers.filter(
    (supplier) => supplier.hasQuotation
  );

  if (quoted.length === 0) return 0;

  return (
    quoted.reduce(
      (total, supplier) => total + supplier.grandTotal,
      0
    ) / quoted.length
  );
}

function calculateSaving(
  suppliers: RfqComparisonSupplier[]
) {
  const quoted = suppliers
    .filter((supplier) => supplier.hasQuotation)
    .sort((a, b) => a.grandTotal - b.grandTotal);

  if (quoted.length < 2) return 0;

  return quoted[1].grandTotal - quoted[0].grandTotal;
}

function calculateSavingRate(
  suppliers: RfqComparisonSupplier[]
) {
  const quoted = suppliers
    .filter((supplier) => supplier.hasQuotation)
    .sort((a, b) => a.grandTotal - b.grandTotal);

  if (quoted.length < 2 || quoted[1].grandTotal === 0) {
    return 0;
  }

  return (
    ((quoted[1].grandTotal - quoted[0].grandTotal) /
      quoted[1].grandTotal) *
    100
  );
}

function getSupplierScore(
  supplier: RfqComparisonSupplier,
  comparison: RfqComparison
) {
  if (!supplier.hasQuotation || supplier.grandTotal <= 0) {
    return 0;
  }

  let score = 50;

  if (
    comparison.lowestSupplierId === supplier.rfqSupplierId
  ) {
    score += 30;
  } else if (comparison.lowestTotal > 0) {
    const differenceRate =
      ((supplier.grandTotal - comparison.lowestTotal) /
        comparison.lowestTotal) *
      100;

    score += Math.max(0, 30 - differenceRate * 2);
  }

  if (
    supplier.deliveryDays !== null &&
    supplier.deliveryDays !== undefined
  ) {
    if (supplier.deliveryDays <= 3) score += 20;
    else if (supplier.deliveryDays <= 7) score += 15;
    else if (supplier.deliveryDays <= 14) score += 10;
    else score += 5;
  }

  return Math.round(Math.min(score, 100));
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

  const load = useCallback(async () => {
    if (!params.id) return;

    setLoading(true);
    setError("");

    try {
      const result = await rfqService.getComparison(params.id);
      setComparison(result);

      setSelectedSupplierId(
        result.lowestSupplierId ?? null
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "RFQ karşılaştırması yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    void load();
  }, [load]);

  const quotedSuppliers = useMemo(
    () =>
      comparison?.suppliers
        .filter((supplier) => supplier.hasQuotation)
        .sort((a, b) => a.grandTotal - b.grandTotal) ?? [],
    [comparison]
  );

  const averageTotal = useMemo(
    () =>
      comparison
        ? calculateAverage(comparison.suppliers)
        : 0,
    [comparison]
  );

  const savingAmount = useMemo(
    () =>
      comparison
        ? calculateSaving(comparison.suppliers)
        : 0,
    [comparison]
  );

  const savingRate = useMemo(
    () =>
      comparison
        ? calculateSavingRate(comparison.suppliers)
        : 0,
    [comparison]
  );

  const selectedSupplier =
    comparison?.suppliers.find(
      (supplier) =>
        supplier.rfqSupplierId === selectedSupplierId
    ) ?? null;

  async function awardSupplier() {
    if (!comparison || !selectedSupplier) return;

    const confirmed = window.confirm(
      `${selectedSupplier.supplierTitle} kazanan tedarikçi olarak seçilsin mi?`
    );

    if (!confirmed) return;

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

    if (
      !window.confirm(
        "Kazanan tekliften satın alma siparişi oluşturulsun mu?",
      )
    ) {
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

    if (
      !window.confirm(
        "RFQ kapatılsın mı? Kapatıldıktan sonra yeni teklif girişi yapılamaz."
      )
    ) {
      return;
    }

    setClosing(true);
    setError("");
    setSuccess("");

    try {
      const result = await rfqService.close(comparison.rfqId);
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

                  <Button
                    variant="danger"
                    loading={closing}
                    onClick={closeRfq}
                  >
                    RFQ Kapat
                  </Button>
                </div>
              </div>
            </CardContent>
          </Card>

          <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <StatCard
              title="En Düşük Teklif"
              value={formatMoney(
                comparison.lowestTotal,
                quotedSuppliers[0]?.currency ?? "TRY"
              )}
              icon="↓"
            />

            <StatCard
              title="Teklif Ortalaması"
              value={formatMoney(
                averageTotal,
                quotedSuppliers[0]?.currency ?? "TRY"
              )}
              icon="∑"
            />

            <StatCard
              title="İkinci Teklife Göre Tasarruf"
              value={formatMoney(
                savingAmount,
                quotedSuppliers[0]?.currency ?? "TRY"
              )}
              icon="₺"
            />

            <StatCard
              title="Tasarruf Oranı"
              value={`%${savingRate.toLocaleString("tr-TR", {
                maximumFractionDigits: 2,
              })}`}
              icon="%"
            />
          </div>

          <div className="mb-6 grid gap-6 xl:grid-cols-3">
            <div className="grid gap-4 md:grid-cols-2 xl:col-span-2">
              {quotedSuppliers.map((supplier, index) => {
                const score = getSupplierScore(
                  supplier,
                  comparison
                );

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
                          {index + 1}. Teklif
                        </span>

                        <h3 className="mt-1 text-lg font-semibold text-slate-900">
                          {supplier.supplierTitle}
                        </h3>
                      </div>

                      {isLowest && (
                        <Badge variant="success">
                          En Düşük
                        </Badge>
                      )}
                    </div>

                    <p className="mt-5 text-2xl font-bold text-slate-950">
                      {formatMoney(
                        supplier.grandTotal,
                        supplier.currency
                      )}
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

                      <Badge variant={scoreVariant(score)}>
                        {scoreLabel(score)} · {score}/100
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
                        comparison.lowestSupplierId ===
                        selectedSupplier.rfqSupplierId
                          ? "success"
                          : "warning"
                      }
                    >
                      {comparison.lowestSupplierId ===
                      selectedSupplier.rfqSupplierId
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
                              getSupplierScore(
                                selectedSupplier,
                                comparison
                              )
                            )}
                          >
                            {getSupplierScore(
                              selectedSupplier,
                              comparison
                            )}
                            /100
                          </Badge>
                        </dd>
                      </div>
                    </dl>

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
                            onClick={createPurchaseOrder}
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
                        <Button
                          loading={awarding}
                          disabled={!selectedSupplier.hasQuotation}
                          onClick={awardSupplier}
                          className="w-full"
                        >
                          Kazanan Tedarikçi Seç
                        </Button>

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
                                    )?.totalPrice ?? 0
                                )
                                .filter((value) => value > 0);

                            const lowestLineTotal =
                              itemTotals.length > 0
                                ? Math.min(...itemTotals)
                                : 0;

                            const isLowestLine =
                              supplierItem &&
                              supplierItem.totalPrice > 0 &&
                              supplierItem.totalPrice ===
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
    </ErpShell>
  );
}
