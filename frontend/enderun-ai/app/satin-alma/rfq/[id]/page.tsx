"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
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
  type RfqDetail,
} from "@/services/rfq.service";

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Gönderildi",
  2: "Teklif Geldi",
  3: "Kapandı",
  4: "Sonuçlandırıldı",
  5: "İptal",
};

const supplierStatusLabels: Record<number, string> = {
  0: "Seçildi",
  1: "Gönderildi",
  2: "Teklif Verdi",
  3: "Reddetti",
  4: "Kazandı",
};

function statusVariant(status: number) {
  if (status === 4) return "success" as const;
  if (status === 1 || status === 2) return "warning" as const;
  if (status === 5) return "danger" as const;
  return "default" as const;
}

function supplierStatusVariant(status: number) {
  if (status === 2 || status === 4) return "success" as const;
  if (status === 1) return "warning" as const;
  if (status === 3) return "danger" as const;
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

function formatMoney(
  value?: number | null,
  currency = "TRY"
) {
  if (value === null || value === undefined) return "—";

  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}

export default function RfqDetailPage() {
  const params = useParams<{ id: string }>();

  const [item, setItem] = useState<RfqDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [processing, setProcessing] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const load = useCallback(async () => {
    if (!params.id) return;

    setLoading(true);
    setError("");

    try {
      const result = await rfqService.getById(params.id);
      setItem(result);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "RFQ bilgileri yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    void load();
  }, [load]);

  async function sendRfq() {
    if (!item) return;

    if (
      !window.confirm(
        "RFQ seçili tedarikçilere gönderilmiş olarak işaretlensin mi?"
      )
    ) {
      return;
    }

    setProcessing(true);
    setError("");
    setSuccess("");

    try {
      const result = await rfqService.send(item.id);
      setSuccess(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "RFQ gönderilemedi."
      );
    } finally {
      setProcessing(false);
    }
  }

  async function closeRfq() {
    if (!item) return;

    if (
      !window.confirm(
        "RFQ kapatılsın mı? Kapatıldıktan sonra teklif girişi yapılamaz."
      )
    ) {
      return;
    }

    setProcessing(true);
    setError("");
    setSuccess("");

    try {
      const result = await rfqService.close(item.id);
      setSuccess(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "RFQ kapatılamadı."
      );
    } finally {
      setProcessing(false);
    }
  }

  const responseCount =
    item?.suppliers.filter(
      (supplier) => supplier.quotationId
    ).length ?? 0;

  const quotedTotal =
    item?.suppliers.reduce(
      (total, supplier) =>
        total + (supplier.quotationTotal ?? 0),
      0
    ) ?? 0;

  return (
    <ErpShell
      title={item?.rfqNumber ?? "RFQ Detayı"}
      description={
        item?.title ?? "Teklif talebi bilgileri yükleniyor"
      }
    >
      <div className="mb-5 flex flex-wrap items-center gap-2 text-sm text-slate-500">
        <Link
          href="/satin-alma/rfq"
          className="hover:text-slate-900"
        >
          RFQ Yönetimi
        </Link>

        <span>›</span>

        <strong className="text-slate-800">
          {item?.rfqNumber ?? "RFQ"}
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
            RFQ bilgileri yükleniyor...
          </CardContent>
        </Card>
      ) : !item ? (
        <EmptyState
          title="RFQ bulunamadı"
          description="Kayıt silinmiş veya erişim yetkiniz olmayabilir."
        />
      ) : (
        <>
          <Card className="mb-6">
            <CardContent className="py-5">
              <div className="flex flex-col gap-5 xl:flex-row xl:items-center xl:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant={statusVariant(item.status)}>
                      {statusLabels[item.status]}
                    </Badge>

                    <Badge variant="info">
                      {item.currency}
                    </Badge>
                  </div>

                  <h2 className="mt-3 text-2xl font-semibold text-slate-900">
                    {item.rfqNumber}
                  </h2>

                  <p className="mt-1 text-sm text-slate-500">
                    {item.title}
                  </p>
                </div>

                <div className="flex flex-wrap gap-3">
                  <Link
                    href={`/satin-alma/${item.purchaseRequestId}`}
                    className="inline-flex h-10 items-center justify-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
                  >
                    Kaynak Talebi Aç
                  </Link>

                  {item.status === 0 && (
                    <Button
                      loading={processing}
                      onClick={sendRfq}
                    >
                      RFQ Gönder
                    </Button>
                  )}

                  {responseCount > 0 && (
                    <Link
                      href={`/satin-alma/rfq/${item.id}/karsilastirma`}
                      className="inline-flex h-10 items-center justify-center rounded-lg bg-slate-900 px-4 text-sm font-medium text-white hover:bg-slate-800"
                    >
                      Teklifleri Karşılaştır
                    </Link>
                  )}

                  {[0, 1, 2].includes(item.status) && (
                    <Button
                      variant="danger"
                      loading={processing}
                      onClick={closeRfq}
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
              title="Malzeme Kalemi"
              value={item.items.length}
              icon="▤"
            />

            <StatCard
              title="Tedarikçi"
              value={item.suppliers.length}
              icon="◫"
            />

            <StatCard
              title="Gelen Teklif"
              value={`${responseCount} / ${item.suppliers.length}`}
              icon="✓"
            />

            <StatCard
              title="Teklif Toplamları"
              value={formatMoney(quotedTotal, item.currency)}
              icon="₺"
            />
          </div>

          <div className="mb-6 grid gap-6 xl:grid-cols-3">
            <Card className="xl:col-span-2">
              <CardHeader>
                <div>
                  <h2 className="text-lg font-semibold text-slate-900">
                    RFQ Bilgileri
                  </h2>
                  <p className="mt-1 text-sm text-slate-500">
                    Teklif talebi genel bilgileri
                  </p>
                </div>
              </CardHeader>

              <CardContent>
                <dl className="grid gap-5 md:grid-cols-2">
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Kaynak Satın Alma Talebi
                    </dt>
                    <dd className="mt-1 font-medium text-slate-900">
                      {item.purchaseRequestNumber}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      RFQ Tarihi
                    </dt>
                    <dd className="mt-1 text-slate-900">
                      {formatDate(item.issueDate)}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Cevap Son Tarihi
                    </dt>
                    <dd className="mt-1 text-slate-900">
                      {formatDate(item.responseDeadline)}
                    </dd>
                  </div>

                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Para Birimi
                    </dt>
                    <dd className="mt-1 text-slate-900">
                      {item.currency}
                    </dd>
                  </div>
                </dl>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <div>
                  <h2 className="text-lg font-semibold text-slate-900">
                    Açıklama
                  </h2>
                </div>
              </CardHeader>

              <CardContent>
                <p className="whitespace-pre-wrap text-sm text-slate-700">
                  {item.description || "Açıklama girilmemiş."}
                </p>

                {item.notes && (
                  <>
                    <hr className="my-4 border-slate-200" />
                    <p className="whitespace-pre-wrap text-sm text-slate-500">
                      {item.notes}
                    </p>
                  </>
                )}
              </CardContent>
            </Card>
          </div>

          <Card className="mb-6">
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Malzeme Kalemleri
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Tedarikçilerden fiyat istenen malzemeler
                </p>
              </div>
            </CardHeader>

            <CardContent>
              {item.items.length === 0 ? (
                <EmptyState
                  title="Malzeme bulunamadı"
                  description="Bu RFQ kaydında malzeme satırı bulunmuyor."
                />
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>No</TableHead>
                      <TableHead>Malzeme Açıklaması</TableHead>
                      <TableHead className="text-right">
                        Miktar
                      </TableHead>
                      <TableHead>Birim</TableHead>
                      <TableHead>Talep Tarihi</TableHead>
                      <TableHead>Not</TableHead>
                    </TableRow>
                  </TableHeader>

                  <TableBody>
                    {item.items.map((line) => (
                      <TableRow key={line.id}>
                        <TableCell>{line.lineNumber}</TableCell>

                        <TableCell>
                          <strong className="text-slate-900">
                            {line.materialDescription}
                          </strong>
                        </TableCell>

                        <TableCell className="text-right font-medium">
                          {line.quantity.toLocaleString("tr-TR")}
                        </TableCell>

                        <TableCell>{line.unit}</TableCell>

                        <TableCell>
                          {formatDate(line.requestedDeliveryDate)}
                        </TableCell>

                        <TableCell className="text-slate-500">
                          {line.notes || "—"}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Tedarikçiler ve Teklifler
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Teklif gönderim ve cevap durumları
                </p>
              </div>
            </CardHeader>

            <CardContent>
              {item.suppliers.length === 0 ? (
                <EmptyState
                  title="Tedarikçi bulunamadı"
                  description="Bu RFQ kaydına tedarikçi eklenmemiş."
                />
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Tedarikçi</TableHead>
                      <TableHead>Yetkili</TableHead>
                      <TableHead>Gönderim</TableHead>
                      <TableHead>Cevap</TableHead>
                      <TableHead>Termin</TableHead>
                      <TableHead>Ödeme</TableHead>
                      <TableHead className="text-right">
                        Toplam
                      </TableHead>
                      <TableHead>Durum</TableHead>
                      <TableHead className="text-right">
                        İşlem
                      </TableHead>
                    </TableRow>
                  </TableHeader>

                  <TableBody>
                    {item.suppliers.map((supplier) => (
                      <TableRow key={supplier.id}>
                        <TableCell>
                          <strong className="text-slate-900">
                            {supplier.supplierTitle}
                          </strong>
                          <span className="mt-1 block text-xs text-slate-500">
                            {supplier.supplierCode}
                          </span>
                        </TableCell>

                        <TableCell>
                          <span className="block">
                            {supplier.contactName || "—"}
                          </span>
                          <span className="mt-1 block text-xs text-slate-500">
                            {supplier.contactEmail || "—"}
                          </span>
                        </TableCell>

                        <TableCell>
                          {formatDateTime(supplier.sentAtUtc)}
                        </TableCell>

                        <TableCell>
                          {formatDateTime(supplier.respondedAtUtc)}
                        </TableCell>

                        <TableCell>
                          {supplier.deliveryDays !== null &&
                          supplier.deliveryDays !== undefined
                            ? `${supplier.deliveryDays} gün`
                            : "—"}
                        </TableCell>

                        <TableCell>
                          {supplier.paymentTerm || "—"}
                        </TableCell>

                        <TableCell className="text-right font-semibold">
                          {formatMoney(
                            supplier.quotationTotal,
                            item.currency
                          )}
                        </TableCell>

                        <TableCell>
                          <Badge
                            variant={supplierStatusVariant(
                              supplier.status
                            )}
                          >
                            {supplierStatusLabels[supplier.status]}
                          </Badge>
                        </TableCell>

                        <TableCell className="text-right">
                          {![3, 4, 5].includes(item.status) ? (
                            <Link
                              href={`/satin-alma/rfq/${item.id}/tedarikci/${supplier.id}`}
                              className="inline-flex h-9 items-center rounded-lg border border-slate-300 px-3 text-sm font-medium text-slate-700 hover:bg-slate-50"
                            >
                              {supplier.quotationId
                                ? "Teklifi Güncelle"
                                : "Teklif Gir"}
                            </Link>
                          ) : (
                            <span className="text-sm text-slate-400">
                              Kapalı
                            </span>
                          )}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>
        </>
      )}
    </ErpShell>
  );
}
