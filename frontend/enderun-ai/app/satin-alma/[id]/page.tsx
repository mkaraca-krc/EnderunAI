"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { ConfirmDialog } from "@/components/ui";
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
  PURCHASE_REQUEST_STATUS_LABELS,
  PurchaseRequestDetail,
  purchaseRequestService,
} from "@/services/purchase-request.service";
import {
  currentAccountService,
  type CurrentAccountListItem,
} from "@/services/current-account.service";
import { rfqService } from "@/services/rfq.service";
import { quantity } from "@/lib/format/turkish";
import {
  requestedBrandBadgeVariant,
  requestedBrandLabel,
} from "@/lib/purchasing/requested-brand";

// Durum adları servisle aynı kaynaktan; iki ekranın aynı duruma
// farklı isim vermesi yanıltırdı.
const statusLabels = PURCHASE_REQUEST_STATUS_LABELS;

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
  // Düzeltmeye iade: kaybedilmiş değil, iş talep sahibinde.
  if (status === 8) return "warning" as const;
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

/**
 * Satın alma talebi kararları.
 *
 * Eskiden prompt + confirm ikilisiyle yürüyordu: gerekçe ayrı bir
 * pencerede soruluyor, boş bırakılabiliyor ve hata ancak iki pencere
 * kapandıktan sonra yazılıyordu. İptalde gerekçe isteğe bağlı ama
 * kayda değer olduğu için alan görünür, zorunlu değil.
 */
type RequestAction = "submit" | "approve" | "reject" | "return" | "cancel";

const ACTION_DIALOGS: Record<
  RequestAction,
  {
    title: string;
    description: string;
    confirmLabel: string;
    requireReason?: boolean;
    showReason?: boolean;
  }
> = {
  submit: {
    title: "Talep onaya gönderilsin mi?",
    description: "Talep onaycıya düşer; onaylanana kadar düzenlenemez.",
    confirmLabel: "Onaya Gönder",
  },
  approve: {
    title: "Talep onaylansın mı?",
    description: "Onaylanan talep için teklif toplanabilir ve sipariş açılabilir.",
    confirmLabel: "Onayla",
  },
  reject: {
    title: "Talep reddedilsin mi?",
    description:
      "Bu karar geri alınamaz. Gerekçe talep sahibine gider; neyi yanlış yaptığını buradan öğrenir.",
    confirmLabel: "Reddet",
    requireReason: true,
  },
  return: {
    title: "Talep düzeltmeye iade edilsin mi?",
    description:
      "Talep sahibine geri döner. Neyin düzeltilmesi gerektiğini yazmazsanız talep ne yapılacağı belli olmadan bekler.",
    confirmLabel: "Düzeltmeye İade Et",
    requireReason: true,
  },
  cancel: {
    title: "Talep iptal edilsin mi?",
    description: "İptal edilen talep yeniden açılamaz.",
    confirmLabel: "İptal Et",
    showReason: true,
  },
};

export default function PurchaseRequestDetailPage() {
  /**
   * Düğme -> uç -> izin (PurchaseRequestsController, RfqController):
   *   POST purchase-requests/{id}/submit -> purchasing-requests.edit
   *   POST purchase-requests/{id}/approve -> purchasing-requests.approve
   *   POST purchase-requests/{id}/reject  -> purchasing-requests.approve
   *   POST purchase-requests/{id}/iade    -> purchasing-requests.approve
   *   POST purchase-requests/{id}/cancel  -> purchasing-requests.DELETE
   *   POST rfq/create-from-purchase-request/{id} -> purchasing-RFQ.create
   *
   * ONAYLA / DÜZELTMEYE İADE / REDDET ÜÇÜ AYNI YETKİDE: üçü de onay
   * makamının kararı, hiçbiri defter izi bırakmıyor. İptal ayrı ve
   * daha ağır (delete).
   *
   * RFQ OLUŞTURMA BU EKRANDA AMA BAŞKA MODÜLÜN İZNİNİ İSTİYOR:
   * purchasing-rfq.create. Talep modülüne bağlamak yanlış olurdu.
   */
  const actions = useModuleActions("purchasing-requests");
  const rfqActions = useModuleActions("purchasing-rfq");

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
  const [pendingAction, setPendingAction] = useState<RequestAction | null>(null);

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

  async function runAction(action: RequestAction, reason: string) {
    if (!item) return;

    setProcessing(true);
    setError("");
    setSuccess("");

    try {
      const result =
        action === "submit"
          ? await purchaseRequestService.submit(item.id)
          : action === "approve"
            ? await purchaseRequestService.approve(item.id)
            : action === "reject"
              ? await purchaseRequestService.reject(item.id, reason)
              : action === "return"
                ? await purchaseRequestService.returnForRevision(item.id, reason)
                : await purchaseRequestService.cancel(item.id, reason);

      setPendingAction(null);
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
      design="redwood"
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

                  {/* Gerekçe kararın yanında durmalı: talep sahibi
                      talebi açtığı anda neden geri geldiğini görmeli,
                      aşağıda bir yerde aramamalı. */}
                  {item.status === 8 && item.returnReason && (
                    <div className="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
                      <strong>Düzeltmeye iade edildi:</strong>{" "}
                      {item.returnReason}
                      <span className="mt-1 block text-xs">
                        Talebi düzenleyip &quot;Düzeltildi, Yeniden
                        Gönder&quot; ile tekrar onaya gönderebilirsiniz.
                      </span>
                    </div>
                  )}

                  {item.status === 7 && item.rejectionReason && (
                    <div className="mt-3 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
                      <strong>Reddedildi:</strong> {item.rejectionReason}
                    </div>
                  )}

                  {(item.revisionCount ?? 0) > 0 && (
                    <p className="mt-2 text-xs text-slate-500">
                      Bu talep {item.revisionCount} kez düzeltilip yeniden
                      gönderildi.
                    </p>
                  )}
                </div>

                <div className="flex flex-wrap gap-3">
                  {(item.status === 0 || item.status === 8) &&
                    actions.can("edit") && (
                    <Button
                      loading={processing}
                      onClick={() => setPendingAction("submit")}
                    >
                      {item.status === 8
                        ? "Düzeltildi, Yeniden Gönder"
                        : "Onaya Gönder"}
                    </Button>
                  )}

                  {item.status === 1 && actions.can("approve") && (
                    <>
                      <Button
                        loading={processing}
                        onClick={() => setPendingAction("approve")}
                      >
                        Onayla
                      </Button>

                      <Button
                        variant="secondary"
                        loading={processing}
                        onClick={() => setPendingAction("return")}
                      >
                        Düzeltmeye İade Et
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

                  {item.status === 2 && rfqActions.can("create") && (
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

                  {![5, 6, 7].includes(item.status) &&
                    actions.can("delete") && (
                    <Button
                      variant="danger"
                      loading={processing}
                      onClick={() => setPendingAction("cancel")}
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

                    {rfqActions.can("create") && (
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
                    )}
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

                        {/* İstenen marka: zorunlu / tercih / farketmez
                            ayrımı lib/purchasing/requested-brand'dan
                            gelir; ekran kuralı yeniden yorumlamaz. */}
                        <div className="mt-1">
                          <Badge
                            variant={requestedBrandBadgeVariant(line)}
                          >
                            {requestedBrandLabel(line)}
                          </Badge>
                        </div>
                      </TableCell>
                      <TableCell>
                        {quantity(line.quantity)}
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
      {pendingAction && (
        <ConfirmDialog
          key={pendingAction}
          open
          title={ACTION_DIALOGS[pendingAction].title}
          description={ACTION_DIALOGS[pendingAction].description}
          confirmLabel={ACTION_DIALOGS[pendingAction].confirmLabel}
          requireReason={ACTION_DIALOGS[pendingAction].requireReason}
          showReason={ACTION_DIALOGS[pendingAction].showReason}
          busy={processing}
          onCancel={() => setPendingAction(null)}
          onConfirm={(reason) => void runAction(pendingAction, reason)}
        />
      )}

    </ErpShell>
  );
}
