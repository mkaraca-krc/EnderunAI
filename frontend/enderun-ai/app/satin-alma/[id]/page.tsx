"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  Input,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import {
  PurchaseRequestDetail,
  purchaseRequestService,
} from "@/services/purchase-request.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import { rfqService } from "@/services/rfq.service";

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Onaya Gönderildi",
  2: "Onaylandı",
  3: "Teklif Aşamasında",
  4: "Siparişe Dönüştü",
  5: "Tamamlandı",
  6: "İptal",
  7: "Reddedildi",
};

const priorityLabels: Record<number, string> = {
  0: "Düşük",
  1: "Normal",
  2: "Yüksek",
  3: "Kritik",
};

function statusVariant(status: number) {
  if (status === 2 || status === 5) return "success" as const;
  if (status === 1 || status === 3 || status === 4) return "warning" as const;
  if (status === 6 || status === 7) return "danger" as const;
  return "default" as const;
}

function priorityVariant(priority: number) {
  if (priority === 3) return "danger" as const;
  if (priority === 2) return "warning" as const;
  if (priority === 1) return "info" as const;
  return "default" as const;
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

export default function PurchaseRequestDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();

  const [item, setItem] = useState<PurchaseRequestDetail | null>(null);
  const [suppliers, setSuppliers] = useState<CurrentAccountListItem[]>([]);
  const [showRfqForm, setShowRfqForm] = useState(false);
  const [selectedSupplierIds, setSelectedSupplierIds] = useState<string[]>([]);
  const [rfqTitle, setRfqTitle] = useState("");
  const [rfqDeadline, setRfqDeadline] = useState("");
  const [creatingRfq, setCreatingRfq] = useState(false);
  const [loading, setLoading] = useState(true);
  const [processing, setProcessing] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const load = useCallback(async () => {
    if (!params.id) return;

    setLoading(true);
    setError("");

    try {
      const detail = await purchaseRequestService.getById(params.id);
      setItem(detail);

      const accountRows = await currentAccountService.getAll(detail.companyId);

      setSuppliers(
        accountRows.filter(
          (account) =>
            account.status === 2 &&
            (account.roles & 2) === 2
        )
      );

      setRfqTitle(
        `${detail.requestNumber} Satın Alma Teklif Talebi`
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Satın alma talebi yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [params.id]);

  useEffect(() => {
    load();
  }, [load]);

  async function runAction(action: "submit" | "approve" | "cancel") {
    if (!item) return;

    let reason = "";

    if (action === "cancel") {
      reason = window.prompt("İptal gerekçesini yazın:") ?? "";
      if (!window.confirm("Bu satın alma talebi iptal edilsin mi?")) {
        return;
      }
    }

    setProcessing(true);
    setError("");
    setSuccess("");

    try {
      const result =
        action === "submit"
          ? await purchaseRequestService.submit(item.id)
          : action === "approve"
            ? await purchaseRequestService.approve(item.id)
            : await purchaseRequestService.cancel(item.id, reason);

      setSuccess(result.message);
      await load();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "İşlem gerçekleştirilemedi."
      );
    } finally {
      setProcessing(false);
    }
  }

  function toggleSupplier(supplierId: string) {
    setSelectedSupplierIds((current) =>
      current.includes(supplierId)
        ? current.filter((id) => id !== supplierId)
        : [...current, supplierId]
    );
  }

  async function createRfq() {
    if (!item) return;

    if (selectedSupplierIds.length === 0) {
      setError("En az bir tedarikçi seçmelisiniz.");
      return;
    }

    setCreatingRfq(true);
    setError("");
    setSuccess("");

    try {
      const result = await rfqService.createFromPurchaseRequest(
        item.id,
        {
          title: rfqTitle.trim(),
          responseDeadline: rfqDeadline || null,
          currency: "TRY",
          description:
            `${item.requestNumber} numaralı satın alma talebinden oluşturuldu.`,
          notes: null,
          supplierCurrentAccountIds: selectedSupplierIds,
        }
      );

      router.push(`/satin-alma/rfq/${result.id}`);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "RFQ oluşturulamadı."
      );
    } finally {
      setCreatingRfq(false);
    }
  }

  return (
    <ErpShell
      title={item?.requestNumber ?? "Satın Alma Talebi"}
      description={
        item
          ? `${item.projectCode} · ${item.projectName}`
          : "Talep bilgileri yükleniyor"
      }
    >
      <div className="mb-5 flex items-center gap-2 text-sm text-slate-500">
        <Link href="/satin-alma" className="hover:text-slate-900">
          Satın Alma
        </Link>
        <span>›</span>
        <strong className="text-slate-800">
          {item?.requestNumber ?? "Talep"}
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
            Talep yükleniyor...
          </CardContent>
        </Card>
      ) : !item ? (
        <EmptyState
          title="Satın alma talebi bulunamadı"
          description="Kayıt silinmiş veya erişiminiz olmayabilir."
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
                    <Badge variant={priorityVariant(item.priority)}>
                      {priorityLabels[item.priority]} Öncelik
                    </Badge>
                  </div>

                  <h2 className="mt-3 text-2xl font-semibold text-slate-900">
                    {item.requestNumber}
                  </h2>
                  <p className="mt-1 text-sm text-slate-500">
                    {item.projectCode} · {item.projectName}
                  </p>
                </div>

                <div className="flex flex-wrap gap-3">
                  {item.status === 0 && (
                    <Button
                      loading={processing}
                      onClick={() => runAction("submit")}
                    >
                      Onaya Gönder
                    </Button>
                  )}

                  {item.status === 1 && (
                    <Button
                      loading={processing}
                      onClick={() => runAction("approve")}
                    >
                      Onayla
                    </Button>
                  )}

                  {item.status === 2 && (
                    <Button
                      variant="secondary"
                      onClick={() =>
                        setShowRfqForm((value) => !value)
                      }
                    >
                      {showRfqForm
                        ? "RFQ Formunu Kapat"
                        : "RFQ Oluştur"}
                    </Button>
                  )}

                  {item.status === 3 && (
                    <Link
                      href="/satin-alma/rfq"
                      className="inline-flex h-10 items-center justify-center rounded-lg border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-50"
                    >
                      RFQ Listesini Aç
                    </Link>
                  )}

                  {![5, 6, 7].includes(item.status) && (
                    <Button
                      variant="danger"
                      loading={processing}
                      onClick={() => runAction("cancel")}
                    >
                      İptal Et
                    </Button>
                  )}
                </div>
              </div>
            </CardContent>
          </Card>

          {showRfqForm && item.status === 2 && (
            <Card className="mb-6">
              <CardHeader>
                <div>
                  <h2 className="text-lg font-semibold text-slate-900">
                    RFQ Oluştur
                  </h2>
                  <p className="mt-1 text-sm text-slate-500">
                    Teklif talebi gönderilecek tedarikçileri seçin
                  </p>
                </div>
              </CardHeader>

              <CardContent>
                <div className="grid gap-5 md:grid-cols-2">
                  <Input
                    label="RFQ Başlığı"
                    required
                    value={rfqTitle}
                    onChange={(event) =>
                      setRfqTitle(event.target.value)
                    }
                  />

                  <Input
                    label="Cevap Son Tarihi"
                    type="date"
                    value={rfqDeadline}
                    onChange={(event) =>
                      setRfqDeadline(event.target.value)
                    }
                  />
                </div>

                <div className="mt-6">
                  <div className="mb-3 flex items-center justify-between">
                    <h3 className="font-semibold text-slate-900">
                      Tedarikçiler
                    </h3>
                    <span className="text-sm text-slate-500">
                      {selectedSupplierIds.length} seçili
                    </span>
                  </div>

                  {suppliers.length === 0 ? (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-700">
                      Onaylı ve tedarikçi rolüne sahip cari kart bulunamadı.
                    </div>
                  ) : (
                    <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                      {suppliers.map((supplier) => {
                        const selected =
                          selectedSupplierIds.includes(supplier.id);

                        return (
                          <button
                            key={supplier.id}
                            type="button"
                            onClick={() =>
                              toggleSupplier(supplier.id)
                            }
                            className={`rounded-xl border p-4 text-left transition ${
                              selected
                                ? "border-brand-700 bg-brand-700 text-white"
                                : "border-slate-200 bg-white hover:border-slate-400"
                            }`}
                          >
                            <strong className="block">
                              {supplier.title}
                            </strong>
                            <span
                              className={`mt-1 block text-sm ${
                                selected
                                  ? "text-slate-300"
                                  : "text-slate-500"
                              }`}
                            >
                              {supplier.code}
                            </span>
                          </button>
                        );
                      })}
                    </div>
                  )}

                  <div className="mt-6 flex justify-end gap-3">
                    <Button
                      type="button"
                      variant="secondary"
                      onClick={() => setShowRfqForm(false)}
                    >
                      Vazgeç
                    </Button>

                    <Button
                      type="button"
                      loading={creatingRfq}
                      disabled={
                        suppliers.length === 0 ||
                        selectedSupplierIds.length === 0 ||
                        !rfqTitle.trim()
                      }
                      onClick={createRfq}
                    >
                      RFQ Oluştur
                    </Button>
                  </div>
                </div>
              </CardContent>
            </Card>
          )}

          <div className="mb-6 grid gap-6 xl:grid-cols-3">
            <Card className="xl:col-span-2">
              <CardHeader>
                <h2 className="text-lg font-semibold text-slate-900">
                  Talep Bilgileri
                </h2>
              </CardHeader>
              <CardContent>
                <div className="grid gap-5 md:grid-cols-2">
                  <div>
                    <span className="text-sm text-slate-500">Şirket</span>
                    <strong className="mt-1 block text-slate-900">
                      {item.companyName}
                    </strong>
                  </div>
                  <div>
                    <span className="text-sm text-slate-500">Proje</span>
                    <strong className="mt-1 block text-slate-900">
                      {item.projectName}
                    </strong>
                  </div>
                  <div>
                    <span className="text-sm text-slate-500">Talep Tarihi</span>
                    <strong className="mt-1 block text-slate-900">
                      {formatDate(item.requestDate)}
                    </strong>
                  </div>
                  <div>
                    <span className="text-sm text-slate-500">
                      İhtiyaç Tarihi
                    </span>
                    <strong className="mt-1 block text-slate-900">
                      {formatDate(item.neededByDate)}
                    </strong>
                  </div>
                  <div>
                    <span className="text-sm text-slate-500">Talep Eden</span>
                    <strong className="mt-1 block text-slate-900">
                      {item.requestedByName}
                    </strong>
                  </div>
                  <div>
                    <span className="text-sm text-slate-500">Kalem Sayısı</span>
                    <strong className="mt-1 block text-slate-900">
                      {item.items.length}
                    </strong>
                  </div>
                  <div className="md:col-span-2">
                    <span className="text-sm text-slate-500">Açıklama</span>
                    <strong className="mt-1 block font-medium text-slate-900">
                      {item.description || "—"}
                    </strong>
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <h2 className="text-lg font-semibold text-slate-900">
                  Süreç Özeti
                </h2>
              </CardHeader>
              <CardContent>
                <div className="space-y-4 text-sm">
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-slate-500">Durum</span>
                    <Badge variant={statusVariant(item.status)}>
                      {statusLabels[item.status]}
                    </Badge>
                  </div>
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-slate-500">Onay Tarihi</span>
                    <strong className="text-slate-800">
                      {formatDate(item.approvedAtUtc)}
                    </strong>
                  </div>
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-slate-500">İptal Tarihi</span>
                    <strong className="text-slate-800">
                      {formatDate(item.cancelledAtUtc)}
                    </strong>
                  </div>

                  {item.cancellationReason && (
                    <div className="rounded-lg bg-red-50 p-3 text-red-700">
                      {item.cancellationReason}
                    </div>
                  )}
                </div>
              </CardContent>
            </Card>
          </div>

          <Card>
            <CardHeader>
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  Talep Kalemleri
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  İstenen malzeme ve hizmetler
                </p>
              </div>
            </CardHeader>

            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>No</TableHead>
                    <TableHead>Malzeme / Hizmet</TableHead>
                    <TableHead>Miktar</TableHead>
                    <TableHead>Birim</TableHead>
                    <TableHead>Teslim Tarihi</TableHead>
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
                      <TableCell>
                        {line.quantity.toLocaleString("tr-TR")}
                      </TableCell>
                      <TableCell>{line.unit}</TableCell>
                      <TableCell>
                        {formatDate(line.requestedDeliveryDate)}
                      </TableCell>
                      <TableCell>{line.notes || "—"}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </>
      )}
    </ErpShell>
  );
}
