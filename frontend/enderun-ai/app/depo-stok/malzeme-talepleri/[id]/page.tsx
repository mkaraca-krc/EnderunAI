"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import {
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";

import {
  purchaseRequestService,
  type IssueStockReservationResponse,
  type PurchaseRequestDetail,
  type PurchaseRequestStockStatus,
  type StockReservationListItem,
} from "@/services/purchase-request.service";

import {
  inventoryMovementService,
  type SelectOption,
} from "@/services/inventory-movement.service";

import { reportService } from "@/services/report.service";

const today = new Date().toISOString().slice(0, 10);

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Onay Bekliyor",
  2: "Onaylandı",
  3: "Teklif Sürecinde",
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

function formatNumber(value?: number | null) {
  return new Intl.NumberFormat("tr-TR", {
    maximumFractionDigits: 4,
  }).format(value ?? 0);
}

function formatDate(value?: string | null) {
  if (!value) return "—";

  return new Date(value).toLocaleDateString("tr-TR");
}

function requestStatusClass(status: number) {
  if (status === 2 || status === 5) {
    return "bg-emerald-100 text-emerald-800";
  }

  if (status === 1 || status === 3 || status === 4) {
    return "bg-amber-100 text-amber-800";
  }

  if (status === 6 || status === 7) {
    return "bg-red-100 text-red-800";
  }

  return "bg-slate-100 text-slate-700";
}

function priorityClass(priority: number) {
  if (priority === 3) {
    return "bg-red-100 text-red-800";
  }

  if (priority === 2) {
    return "bg-amber-100 text-amber-800";
  }

  if (priority === 1) {
    return "bg-blue-100 text-blue-800";
  }

  return "bg-slate-100 text-slate-700";
}

function reservationStatusClass(
  reservation: StockReservationListItem,
) {
  if (reservation.isExpired) {
    return "bg-red-100 text-red-800";
  }

  if (reservation.status === 0) {
    return "bg-blue-100 text-blue-800";
  }

  if (reservation.status === 1) {
    return "bg-amber-100 text-amber-800";
  }

  if (reservation.status === 2) {
    return "bg-emerald-100 text-emerald-800";
  }

  return "bg-slate-100 text-slate-700";
}

type IssueForm = {
  quantity: string;
  movementDate: string;
  description: string;
};

export default function MaterialRequestDetailPage() {
  const params = useParams<{ id: string }>();
  const requestId = params.id;

  const [request, setRequest] =
    useState<PurchaseRequestDetail | null>(null);

  const [warehouses, setWarehouses] =
    useState<SelectOption[]>([]);

  const [selectedWarehouseId, setSelectedWarehouseId] =
    useState("");

  const [stockStatus, setStockStatus] =
    useState<PurchaseRequestStockStatus | null>(null);

  const [reservations, setReservations] =
    useState<StockReservationListItem[]>([]);

  const [expirationDate, setExpirationDate] =
    useState("");

  const [reservationDescription, setReservationDescription] =
    useState("");

  const [issueForms, setIssueForms] = useState<
    Record<string, IssueForm>
  >({});

  const [loading, setLoading] = useState(true);
  const [loadingStock, setLoadingStock] = useState(false);
  const [processing, setProcessing] = useState(false);

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const [lastIssue, setLastIssue] =
    useState<IssueStockReservationResponse | null>(
      null,
    );

  const [downloadingIssuePdf, setDownloadingIssuePdf] =
    useState(false);

  const loadRequest = useCallback(async () => {
    if (!requestId) return;

    const data =
      await purchaseRequestService.getById(requestId);

    setRequest(data);

    if (data.requestType !== 1) {
      setError(
        "Bu kayıt şantiye malzeme talebi değildir.",
      );
    }
  }, [requestId]);

  const loadReservations = useCallback(async () => {
    if (!requestId) return;

    const data =
      await purchaseRequestService.getReservations(
        requestId,
      );

    setReservations(data);

    setIssueForms((current) => {
      const next = { ...current };

      data.forEach((reservation) => {
        if (!next[reservation.id]) {
          next[reservation.id] = {
            quantity: String(
              reservation.remainingQuantity,
            ),
            movementDate: today,
            description: "",
          };
        }
      });

      return next;
    });
  }, [requestId]);

  const loadStockStatus = useCallback(
    async (warehouseId?: string) => {
      if (!requestId) return;

      try {
        setLoadingStock(true);

        const data =
          await purchaseRequestService.getStockStatus(
            requestId,
            warehouseId || undefined,
          );

        setStockStatus(data);
      } catch (err) {
        setStockStatus(null);

        setError(
          err instanceof Error
            ? err.message
            : "Stok durumu alınamadı.",
        );
      } finally {
        setLoadingStock(false);
      }
    },
    [requestId],
  );

  const loadPage = useCallback(async () => {
    if (!requestId) return;

    try {
      setLoading(true);
      setError("");

      const [
        requestData,
        warehouseData,
        reservationData,
      ] = await Promise.all([
        purchaseRequestService.getById(requestId),
        inventoryMovementService.getWarehouses(),
        purchaseRequestService.getReservations(requestId),
      ]);

      setRequest(requestData);
      setWarehouses(warehouseData);
      setReservations(reservationData);

      setIssueForms(() => {
        const next: Record<string, IssueForm> = {};

        reservationData.forEach((reservation) => {
          next[reservation.id] = {
            quantity: String(
              reservation.remainingQuantity,
            ),
            movementDate: today,
            description: "",
          };
        });

        return next;
      });

      const initialWarehouseId =
        reservationData[0]?.warehouseId ||
        warehouseData[0]?.id ||
        "";

      setSelectedWarehouseId(initialWarehouseId);

      await loadStockStatus(
        initialWarehouseId || undefined,
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Malzeme talebi yüklenemedi.",
      );
    } finally {
      setLoading(false);
    }
  }, [loadStockStatus, requestId]);

  useEffect(() => {
    void loadPage();
  }, [loadPage]);

  const activeReservations = useMemo(
    () =>
      reservations.filter(
        (reservation) =>
          reservation.remainingQuantity > 0 &&
          ![2, 3, 4].includes(reservation.status),
      ),
    [reservations],
  );

  async function refreshOperationalData() {
    await Promise.all([
      loadRequest(),
      loadReservations(),
      loadStockStatus(
        selectedWarehouseId || undefined,
      ),
    ]);
  }

  async function changeWarehouse(
    warehouseId: string,
  ) {
    setSelectedWarehouseId(warehouseId);
    setError("");
    setSuccess("");

    await loadStockStatus(
      warehouseId || undefined,
    );
  }

  async function runRequestAction(
    action: "submit" | "approve" | "cancel",
  ) {
    if (!request) return;

    let reason = "";

    if (action === "cancel") {
      reason =
        window.prompt(
          "Talep iptal gerekçesini yazın:",
        ) ?? "";

      if (
        !window.confirm(
          "Bu malzeme talebi iptal edilsin mi?",
        )
      ) {
        return;
      }
    }

    try {
      setProcessing(true);
      setError("");
      setSuccess("");

      const result =
        action === "submit"
          ? await purchaseRequestService.submit(
              request.id,
            )
          : action === "approve"
            ? await purchaseRequestService.approve(
                request.id,
              )
            : await purchaseRequestService.cancel(
                request.id,
                reason,
              );

      setSuccess(result.message);
      await refreshOperationalData();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İşlem gerçekleştirilemedi.",
      );
    } finally {
      setProcessing(false);
    }
  }

  async function reserveStock() {
    if (!request) return;

    if (!selectedWarehouseId) {
      setError("Rezervasyon için depo seçilmelidir.");
      return;
    }

    if (
      !window.confirm(
        "Seçili depodaki uygun stoklar bu talep için rezerve edilsin mi?",
      )
    ) {
      return;
    }

    try {
      setProcessing(true);
      setError("");
      setSuccess("");

      const result =
        await purchaseRequestService.reserve(
          request.id,
          {
            warehouseId: selectedWarehouseId,
            expirationDate:
              expirationDate || null,
            description:
              reservationDescription.trim() || null,
          },
        );

      setSuccess(
        `${formatNumber(
          result.totalNewlyReservedQuantity,
        )} miktar rezerve edildi. ` +
          `${formatNumber(
            result.totalMissingQuantity,
          )} miktar eksik kaldı.`,
      );

      setReservationDescription("");
      await refreshOperationalData();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Rezervasyon yapılamadı.",
      );
    } finally {
      setProcessing(false);
    }
  }

  function updateIssueForm(
    reservationId: string,
    key: keyof IssueForm,
    value: string,
  ) {
    setIssueForms((current) => ({
      ...current,
      [reservationId]: {
        ...(current[reservationId] ?? {
          quantity: "",
          movementDate: today,
          description: "",
        }),
        [key]: value,
      },
    }));
  }

  async function issueReservation(
    reservation: StockReservationListItem,
  ) {
    if (!request) return;

    const form = issueForms[reservation.id];

    const quantity = Number(
      form?.quantity ?? 0,
    );

    if (
      !quantity ||
      quantity <= 0 ||
      quantity > reservation.remainingQuantity
    ) {
      setError(
        `Çıkış miktarı 0 ile ${formatNumber(
          reservation.remainingQuantity,
        )} arasında olmalıdır.`,
      );
      return;
    }

    if (!form?.movementDate) {
      setError("Çıkış tarihi girilmelidir.");
      return;
    }

    if (
      !window.confirm(
        `${formatNumber(quantity)} miktar stoktan düşülsün mü?`,
      )
    ) {
      return;
    }

    try {
      setProcessing(true);
      setError("");
      setSuccess("");

      const result =
        await purchaseRequestService.issueReservation(
          request.id,
          {
            stockReservationId: reservation.id,
            quantity,
            movementDate: form.movementDate,
            description:
              form.description.trim() || null,
          },
        );

      setLastIssue(result);

      const voucherText =
        result.accountingVoucherNumber
          ? ` Muhasebe fişi: ${result.accountingVoucherNumber}.`
          : "";

      setSuccess(
        `${reservation.inventoryItemName} için depo çıkışı tamamlandı.${voucherText}`,
      );

      await refreshOperationalData();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Depo çıkışı yapılamadı.",
      );
    } finally {
      setProcessing(false);
    }
  }

  async function downloadLastIssuePdf() {
    if (!lastIssue?.stockMovementId) {
      setError(
        "PDF oluşturmak için stok hareketi bulunamadı.",
      );
      return;
    }

    try {
      setDownloadingIssuePdf(true);
      setError("");

      await reportService.downloadStockIssuePdf(
        lastIssue.stockMovementId,
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Depo çıkış fişi indirilemedi.",
      );
    } finally {
      setDownloadingIssuePdf(false);
    }
  }


  async function releaseReservation(
    reservation: StockReservationListItem,
  ) {
    if (!request) return;

    const reason =
      window.prompt(
        "Rezervasyonu serbest bırakma gerekçesi:",
      ) ?? "";

    if (
      !window.confirm(
        "Kalan rezervasyon miktarı kullanılabilir stoğa geri aktarılsın mı?",
      )
    ) {
      return;
    }

    try {
      setProcessing(true);
      setError("");
      setSuccess("");

      await purchaseRequestService.releaseReservation(
        request.id,
        reservation.id,
        reason,
      );

      setSuccess(
        "Rezervasyon serbest bırakıldı.",
      );

      await refreshOperationalData();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Rezervasyon serbest bırakılamadı.",
      );
    } finally {
      setProcessing(false);
    }
  }

  async function cancelReservation(
    reservation: StockReservationListItem,
  ) {
    if (!request) return;

    const reason =
      window.prompt(
        "Rezervasyon iptal gerekçesi:",
      ) ?? "";

    if (
      !window.confirm(
        "Bu rezervasyon iptal edilsin mi?",
      )
    ) {
      return;
    }

    try {
      setProcessing(true);
      setError("");
      setSuccess("");

      await purchaseRequestService.cancelReservation(
        request.id,
        reservation.id,
        reason,
      );

      setSuccess("Rezervasyon iptal edildi.");
      await refreshOperationalData();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Rezervasyon iptal edilemedi.",
      );
    } finally {
      setProcessing(false);
    }
  }

  return (
    <ErpShell
      title={
        request?.requestNumber ??
        "Malzeme Talebi"
      }
      description={
        request
          ? `${request.projectCode} · ${request.projectName}`
          : "Şantiye malzeme talebi yükleniyor"
      }
    >
      <div className="space-y-6">
        <div className="flex flex-wrap items-center gap-2 text-sm text-slate-500">
          <Link
            href="/depo-stok"
            className="hover:text-slate-900"
          >
            Depo &amp; Stok
          </Link>

          <span>›</span>

          <Link
            href="/depo-stok/malzeme-talepleri"
            className="hover:text-slate-900"
          >
            Malzeme Talepleri
          </Link>

          <span>›</span>

          <strong className="text-slate-800">
            {request?.requestNumber ?? "Talep"}
          </strong>
        </div>

        {error ? (
          <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        ) : null}

        {success ? (
          <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-4 text-sm text-emerald-800">
            <div className="font-medium">
              {success}
            </div>

            {lastIssue ? (
              <div className="mt-4 flex flex-col gap-4 rounded-lg border border-emerald-200 bg-white/70 p-4 lg:flex-row lg:items-center lg:justify-between">
                <div className="grid gap-3 sm:grid-cols-2">
                  <div>
                    <div className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Muhasebe fişi
                    </div>

                    <div className="mt-1 font-semibold text-slate-900">
                      {lastIssue.accountingVoucherNumber ||
                        "Oluşturulmadı"}
                    </div>
                  </div>

                  <div>
                    <div className="text-xs font-medium uppercase tracking-wide text-slate-500">
                      Toplam maliyet
                    </div>

                    <div className="mt-1 font-semibold text-slate-900">
                      {new Intl.NumberFormat("tr-TR", {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      }).format(lastIssue.totalCost ?? 0)}{" "}
                      TL
                    </div>
                  </div>
                </div>

                <button
                  type="button"
                  onClick={downloadLastIssuePdf}
                  disabled={
                    downloadingIssuePdf ||
                    !lastIssue.stockMovementId
                  }
                  className="inline-flex min-h-10 items-center justify-center rounded-lg bg-emerald-700 px-4 py-2 font-medium text-white transition hover:bg-emerald-800 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {downloadingIssuePdf
                    ? "PDF hazırlanıyor..."
                    : "Depo Çıkış Fişini İndir"}
                </button>
              </div>
            ) : null}
          </div>
        ) : null}

        {loading ? (
          <section className="rounded-xl border border-slate-200 bg-white p-12 text-center text-sm text-slate-500 shadow-sm">
            Malzeme talebi yükleniyor...
          </section>
        ) : !request ? (
          <section className="rounded-xl border border-slate-200 bg-white p-12 text-center shadow-sm">
            <h2 className="font-semibold text-slate-900">
              Malzeme talebi bulunamadı
            </h2>
            <p className="mt-2 text-sm text-slate-500">
              Kayıt silinmiş veya erişim yetkiniz olmayabilir.
            </p>
          </section>
        ) : (
          <>
            <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
              <div className="flex flex-col gap-5 xl:flex-row xl:items-center xl:justify-between">
                <div>
                  <div className="flex flex-wrap gap-2">
                    <span
                      className={`inline-flex rounded-full px-3 py-1 text-xs font-medium ${requestStatusClass(
                        request.status,
                      )}`}
                    >
                      {statusLabels[request.status] ??
                        "Bilinmiyor"}
                    </span>

                    <span
                      className={`inline-flex rounded-full px-3 py-1 text-xs font-medium ${priorityClass(
                        request.priority,
                      )}`}
                    >
                      {priorityLabels[
                        request.priority
                      ] ?? "Bilinmiyor"}{" "}
                      Öncelik
                    </span>

                    <span className="inline-flex rounded-full bg-violet-100 px-3 py-1 text-xs font-medium text-violet-800">
                      Şantiye Malzeme Talebi
                    </span>
                  </div>

                  <h2 className="mt-3 text-2xl font-semibold text-slate-950">
                    {request.requestNumber}
                  </h2>

                  <p className="mt-1 text-sm text-slate-500">
                    {request.projectCode} ·{" "}
                    {request.projectName}
                  </p>
                </div>

                <div className="flex flex-wrap gap-3">
                  {request.status === 0 ? (
                    <button
                      type="button"
                      disabled={processing}
                      onClick={() =>
                        void runRequestAction(
                          "submit",
                        )
                      }
                      className="button-primary"
                    >
                      Onaya Gönder
                    </button>
                  ) : null}

                  {request.status === 1 ? (
                    <button
                      type="button"
                      disabled={processing}
                      onClick={() =>
                        void runRequestAction(
                          "approve",
                        )
                      }
                      className="button-primary"
                    >
                      Talebi Onayla
                    </button>
                  ) : null}

                  {![5, 6, 7].includes(
                    request.status,
                  ) ? (
                    <button
                      type="button"
                      disabled={processing}
                      onClick={() =>
                        void runRequestAction(
                          "cancel",
                        )
                      }
                      className="button-danger"
                    >
                      Talebi İptal Et
                    </button>
                  ) : null}
                </div>
              </div>
            </section>

            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <SummaryCard
                label="Toplam Talep"
                value={
                  stockStatus
                    ? formatNumber(
                        stockStatus.totalRequestedQuantity,
                      )
                    : formatNumber(
                        request.items.reduce(
                          (sum, item) =>
                            sum + item.quantity,
                          0,
                        ),
                      )
                }
              />

              <SummaryCard
                label="Toplam Rezerve"
                value={formatNumber(
                  stockStatus?.totalReservedQuantity,
                )}
              />

              <SummaryCard
                label="Toplam Çıkış"
                value={formatNumber(
                  stockStatus?.totalIssuedQuantity,
                )}
              />

              <SummaryCard
                label="Eksik Miktar"
                value={formatNumber(
                  stockStatus?.totalMissingQuantity,
                )}
                warning={
                  (stockStatus?.totalMissingQuantity ??
                    0) > 0
                }
              />
            </div>

            <div className="grid gap-6 xl:grid-cols-3">
              <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm xl:col-span-2">
                <h2 className="text-lg font-semibold text-slate-950">
                  Talep Bilgileri
                </h2>

                <div className="mt-5 grid gap-5 md:grid-cols-2">
                  <Info
                    label="Şirket"
                    value={request.companyName}
                  />

                  <Info
                    label="Proje"
                    value={`${request.projectCode} · ${request.projectName}`}
                  />

                  <Info
                    label="Talep Eden"
                    value={request.requestedByName}
                  />

                  <Info
                    label="Talep Tarihi"
                    value={formatDate(
                      request.requestDate,
                    )}
                  />

                  <Info
                    label="İhtiyaç Tarihi"
                    value={formatDate(
                      request.neededByDate,
                    )}
                  />

                  <Info
                    label="Kalem Sayısı"
                    value={String(
                      request.items.length,
                    )}
                  />

                  <div className="md:col-span-2">
                    <Info
                      label="Açıklama"
                      value={
                        request.description || "—"
                      }
                    />
                  </div>
                </div>
              </section>

              <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
                <h2 className="text-lg font-semibold text-slate-950">
                  Rezervasyon Ayarları
                </h2>

                <div className="mt-5 space-y-4">
                  <Field label="Depo" required>
                    <select
                      value={selectedWarehouseId}
                      onChange={(event) =>
                        void changeWarehouse(
                          event.target.value,
                        )
                      }
                      className="input"
                    >
                      <option value="">
                        Depo seçin
                      </option>

                      {warehouses.map((warehouse) => (
                        <option
                          key={warehouse.id}
                          value={warehouse.id}
                        >
                          {warehouse.code
                            ? `${warehouse.code} · `
                            : ""}
                          {warehouse.name}
                        </option>
                      ))}
                    </select>
                  </Field>

                  <Field label="Son Geçerlilik Tarihi">
                    <input
                      type="date"
                      value={expirationDate}
                      onChange={(event) =>
                        setExpirationDate(
                          event.target.value,
                        )
                      }
                      className="input"
                    />
                  </Field>

                  <Field label="Rezervasyon Notu">
                    <textarea
                      rows={3}
                      value={reservationDescription}
                      onChange={(event) =>
                        setReservationDescription(
                          event.target.value,
                        )
                      }
                      placeholder="Hazırlama, teslim veya kullanım notu"
                      className="input resize-y"
                    />
                  </Field>

                  <button
                    type="button"
                    disabled={
                      processing ||
                      !selectedWarehouseId ||
                      request.status !== 2 ||
                      stockStatus?.isFullyReserved
                    }
                    onClick={() =>
                      void reserveStock()
                    }
                    className="button-primary w-full"
                  >
                    {stockStatus?.isFullyReserved
                      ? "Talep Tamamen Rezerve"
                      : request.status !== 2
                        ? "Önce Talebi Onaylayın"
                        : "Uygun Stokları Rezerve Et"}
                  </button>

                  {stockStatus?.warehouseName ? (
                    <div className="rounded-lg bg-slate-50 p-3 text-xs text-slate-600">
                      Stok kontrol edilen depo:{" "}
                      <strong>
                        {stockStatus.warehouseName}
                      </strong>
                    </div>
                  ) : null}
                </div>
              </section>
            </div>

            <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
              <div className="border-b border-slate-200 px-5 py-4">
                <h2 className="text-lg font-semibold text-slate-950">
                  Talep ve Stok Durumu
                </h2>

                <p className="mt-1 text-sm text-slate-500">
                  Seçili depodaki kullanılabilir,
                  rezerve edilebilir ve eksik miktarlar.
                </p>
              </div>

              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-slate-200 text-sm">
                  <thead className="bg-slate-50">
                    <tr>
                      <TableHeader>No</TableHeader>
                      <TableHeader>
                        Malzeme
                      </TableHeader>
                      <TableHeader right>
                        Talep
                      </TableHeader>
                      <TableHeader right>
                        Rezerve
                      </TableHeader>
                      <TableHeader right>
                        Çıkış
                      </TableHeader>
                      <TableHeader right>
                        Depo Stok
                      </TableHeader>
                      <TableHeader right>
                        Kullanılabilir
                      </TableHeader>
                      <TableHeader right>
                        Rezerve Edilebilir
                      </TableHeader>
                      <TableHeader right>
                        Eksik
                      </TableHeader>
                      <TableHeader>
                        Durum
                      </TableHeader>
                    </tr>
                  </thead>

                  <tbody className="divide-y divide-slate-100">
                    {loadingStock ? (
                      <tr>
                        <td
                          colSpan={10}
                          className="px-4 py-10 text-center text-slate-500"
                        >
                          Stok durumu yükleniyor...
                        </td>
                      </tr>
                    ) : !stockStatus ? (
                      <tr>
                        <td
                          colSpan={10}
                          className="px-4 py-10 text-center text-slate-500"
                        >
                          Stok durumu alınamadı.
                        </td>
                      </tr>
                    ) : (
                      stockStatus.lines.map(
                        (line) => {
                          const complete =
                            line.missingQuantity <= 0 &&
                            line.unreservedQuantity <= 0;

                          const partial =
                            line.reservableQuantity > 0 &&
                            !complete;

                          return (
                            <tr
                              key={
                                line.purchaseRequestItemId
                              }
                              className="hover:bg-slate-50"
                            >
                              <td className="px-4 py-3 text-slate-600">
                                {line.lineNumber}
                              </td>

                              <td className="px-4 py-3">
                                <div className="font-medium text-slate-900">
                                  {
                                    line.inventoryItemName
                                  }
                                </div>

                                <div className="mt-1 text-xs text-slate-500">
                                  {
                                    line.inventoryItemCode
                                  }{" "}
                                  · {line.unit}
                                </div>
                              </td>

                              <NumberCell
                                value={
                                  line.requestedQuantity
                                }
                              />

                              <NumberCell
                                value={
                                  line.reservedQuantity
                                }
                              />

                              <NumberCell
                                value={
                                  line.issuedQuantity
                                }
                              />

                              <NumberCell
                                value={
                                  line.warehouseQuantity
                                }
                              />

                              <NumberCell
                                value={
                                  line.warehouseAvailableQuantity
                                }
                              />

                              <NumberCell
                                value={
                                  line.reservableQuantity
                                }
                              />

                              <NumberCell
                                value={
                                  line.missingQuantity
                                }
                                danger={
                                  line.missingQuantity >
                                  0
                                }
                              />

                              <td className="px-4 py-3">
                                <span
                                  className={
                                    complete
                                      ? "status-success"
                                      : partial
                                        ? "status-warning"
                                        : "status-danger"
                                  }
                                >
                                  {complete
                                    ? "Tam"
                                    : partial
                                      ? "Kısmi"
                                      : "Stok Yok"}
                                </span>
                              </td>
                            </tr>
                          );
                        },
                      )
                    )}
                  </tbody>
                </table>
              </div>
            </section>

            <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
              <div className="flex flex-col gap-2 border-b border-slate-200 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <h2 className="text-lg font-semibold text-slate-950">
                    Rezervasyonlar
                  </h2>

                  <p className="mt-1 text-sm text-slate-500">
                    Rezerve edilen malzemelerin
                    çıkış ve serbest bırakma işlemleri.
                  </p>
                </div>

                <span className="text-sm text-slate-500">
                  {activeReservations.length} aktif /{" "}
                  {reservations.length} toplam
                </span>
              </div>

              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-slate-200 text-sm">
                  <thead className="bg-slate-50">
                    <tr>
                      <TableHeader>
                        Rezervasyon
                      </TableHeader>
                      <TableHeader>
                        Malzeme
                      </TableHeader>
                      <TableHeader>
                        Depo
                      </TableHeader>
                      <TableHeader right>
                        Rezerve
                      </TableHeader>
                      <TableHeader right>
                        Çıkış
                      </TableHeader>
                      <TableHeader right>
                        Kalan
                      </TableHeader>
                      <TableHeader>
                        Durum
                      </TableHeader>
                      <TableHeader>
                        Çıkış İşlemi
                      </TableHeader>
                    </tr>
                  </thead>

                  <tbody className="divide-y divide-slate-100">
                    {reservations.length === 0 ? (
                      <tr>
                        <td
                          colSpan={8}
                          className="px-4 py-12 text-center text-slate-500"
                        >
                          Bu talep için henüz
                          rezervasyon oluşturulmadı.
                        </td>
                      </tr>
                    ) : (
                      reservations.map(
                        (reservation) => {
                          const issueForm =
                            issueForms[
                              reservation.id
                            ];

                          const actionable =
                            reservation.remainingQuantity >
                              0 &&
                            !reservation.isExpired &&
                            ![2, 3, 4].includes(
                              reservation.status,
                            );

                          return (
                            <tr
                              key={reservation.id}
                              className="align-top hover:bg-slate-50"
                            >
                              <td className="px-4 py-4">
                                <div className="font-semibold text-slate-900">
                                  {
                                    reservation.reservationNumber
                                  }
                                </div>

                                <div className="mt-1 text-xs text-slate-500">
                                  {formatDate(
                                    reservation.reservationDate,
                                  )}
                                </div>

                                {reservation.expirationDate ? (
                                  <div className="mt-1 text-xs text-slate-500">
                                    Son:{" "}
                                    {formatDate(
                                      reservation.expirationDate,
                                    )}
                                  </div>
                                ) : null}
                              </td>

                              <td className="px-4 py-4">
                                <div className="font-medium text-slate-900">
                                  {
                                    reservation.inventoryItemName
                                  }
                                </div>

                                <div className="mt-1 text-xs text-slate-500">
                                  {
                                    reservation.inventoryItemCode
                                  }
                                </div>
                              </td>

                              <td className="px-4 py-4 text-slate-700">
                                {
                                  reservation.warehouseName
                                }
                              </td>

                              <NumberCell
                                value={
                                  reservation.reservedQuantity
                                }
                              />

                              <NumberCell
                                value={
                                  reservation.consumedQuantity
                                }
                              />

                              <NumberCell
                                value={
                                  reservation.remainingQuantity
                                }
                              />

                              <td className="px-4 py-4">
                                <span
                                  className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${reservationStatusClass(
                                    reservation,
                                  )}`}
                                >
                                  {reservation.isExpired
                                    ? "Süresi Doldu"
                                    : reservation.statusName}
                                </span>
                              </td>

                              <td className="min-w-[330px] px-4 py-4">
                                {actionable ? (
                                  <div className="space-y-3">
                                    <div className="grid grid-cols-2 gap-2">
                                      <input
                                        type="number"
                                        min="0.0001"
                                        max={
                                          reservation.remainingQuantity
                                        }
                                        step="0.0001"
                                        value={
                                          issueForm?.quantity ??
                                          ""
                                        }
                                        onChange={(event) =>
                                          updateIssueForm(
                                            reservation.id,
                                            "quantity",
                                            event.target
                                              .value,
                                          )
                                        }
                                        placeholder="Miktar"
                                        className="input"
                                      />

                                      <input
                                        type="date"
                                        value={
                                          issueForm?.movementDate ??
                                          today
                                        }
                                        onChange={(event) =>
                                          updateIssueForm(
                                            reservation.id,
                                            "movementDate",
                                            event.target
                                              .value,
                                          )
                                        }
                                        className="input"
                                      />
                                    </div>

                                    <input
                                      value={
                                        issueForm?.description ??
                                        ""
                                      }
                                      onChange={(event) =>
                                        updateIssueForm(
                                          reservation.id,
                                          "description",
                                          event.target
                                            .value,
                                        )
                                      }
                                      placeholder="Çıkış açıklaması"
                                      className="input"
                                    />

                                    <div className="flex flex-wrap gap-2">
                                      <button
                                        type="button"
                                        disabled={
                                          processing
                                        }
                                        onClick={() =>
                                          void issueReservation(
                                            reservation,
                                          )
                                        }
                                        className="button-primary button-small"
                                      >
                                        Depo Çıkışı
                                      </button>

                                      <button
                                        type="button"
                                        disabled={
                                          processing
                                        }
                                        onClick={() =>
                                          void releaseReservation(
                                            reservation,
                                          )
                                        }
                                        className="button-secondary button-small"
                                      >
                                        Serbest Bırak
                                      </button>

                                      <button
                                        type="button"
                                        disabled={
                                          processing
                                        }
                                        onClick={() =>
                                          void cancelReservation(
                                            reservation,
                                          )
                                        }
                                        className="button-danger button-small"
                                      >
                                        İptal
                                      </button>
                                    </div>
                                  </div>
                                ) : (
                                  <span className="text-sm text-slate-500">
                                    İşlem yapılamaz
                                  </span>
                                )}
                              </td>
                            </tr>
                          );
                        },
                      )
                    )}
                  </tbody>
                </table>
              </div>
            </section>
          </>
        )}
      </div>

      <style jsx>{`
        :global(.input) {
          width: 100%;
          border: 1px solid rgb(203 213 225);
          border-radius: 0.5rem;
          padding: 0.625rem 0.75rem;
          font-size: 0.875rem;
          outline: none;
          background: white;
        }

        :global(.input:focus) {
          border-color: rgb(100 116 139);
          box-shadow: 0 0 0 2px rgb(226 232 240);
        }

        :global(.button-primary),
        :global(.button-secondary),
        :global(.button-danger) {
          display: inline-flex;
          min-height: 2.5rem;
          align-items: center;
          justify-content: center;
          border-radius: 0.5rem;
          padding: 0.625rem 1rem;
          font-size: 0.875rem;
          font-weight: 500;
          transition: 150ms;
        }

        :global(.button-primary) {
          background: rgb(15 23 42);
          color: white;
        }

        :global(.button-primary:hover) {
          background: rgb(30 41 59);
        }

        :global(.button-secondary) {
          border: 1px solid rgb(203 213 225);
          background: white;
          color: rgb(51 65 85);
        }

        :global(.button-secondary:hover) {
          background: rgb(248 250 252);
        }

        :global(.button-danger) {
          border: 1px solid rgb(254 202 202);
          background: rgb(254 242 242);
          color: rgb(185 28 28);
        }

        :global(.button-danger:hover) {
          background: rgb(254 226 226);
        }

        :global(.button-small) {
          min-height: 2rem;
          padding: 0.4rem 0.7rem;
          font-size: 0.75rem;
        }

        :global(.button-primary:disabled),
        :global(.button-secondary:disabled),
        :global(.button-danger:disabled) {
          cursor: not-allowed;
          opacity: 0.5;
        }

        :global(.status-success),
        :global(.status-warning),
        :global(.status-danger) {
          display: inline-flex;
          border-radius: 9999px;
          padding: 0.25rem 0.625rem;
          font-size: 0.75rem;
          font-weight: 500;
        }

        :global(.status-success) {
          background: rgb(209 250 229);
          color: rgb(6 95 70);
        }

        :global(.status-warning) {
          background: rgb(254 243 199);
          color: rgb(146 64 14);
        }

        :global(.status-danger) {
          background: rgb(254 226 226);
          color: rgb(153 27 27);
        }
      `}</style>
    </ErpShell>
  );
}

function SummaryCard({
  label,
  value,
  warning = false,
}: {
  label: string;
  value: string;
  warning?: boolean;
}) {
  return (
    <div
      className={
        warning
          ? "rounded-xl border border-amber-200 bg-amber-50 p-5 shadow-sm"
          : "rounded-xl border border-slate-200 bg-white p-5 shadow-sm"
      }
    >
      <p
        className={
          warning
            ? "text-sm font-medium text-amber-700"
            : "text-sm font-medium text-slate-500"
        }
      >
        {label}
      </p>

      <p
        className={
          warning
            ? "mt-2 text-2xl font-semibold text-amber-900"
            : "mt-2 text-2xl font-semibold text-slate-950"
        }
      >
        {value}
      </p>
    </div>
  );
}

function Info({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div>
      <span className="text-sm text-slate-500">
        {label}
      </span>

      <strong className="mt-1 block font-medium text-slate-900">
        {value}
      </strong>
    </div>
  );
}

function Field({
  label,
  required = false,
  children,
}: {
  label: string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <label className="block space-y-2">
      <span className="text-sm font-medium text-slate-700">
        {label}
        {required ? (
          <span className="text-red-600"> *</span>
        ) : null}
      </span>

      {children}
    </label>
  );
}

function TableHeader({
  children,
  right = false,
}: {
  children: React.ReactNode;
  right?: boolean;
}) {
  return (
    <th
      className={`whitespace-nowrap px-4 py-3 text-xs font-semibold uppercase tracking-wide text-slate-600 ${
        right ? "text-right" : "text-left"
      }`}
    >
      {children}
    </th>
  );
}

function NumberCell({
  value,
  danger = false,
}: {
  value: number;
  danger?: boolean;
}) {
  return (
    <td
      className={`whitespace-nowrap px-4 py-4 text-right font-medium ${
        danger
          ? "text-red-700"
          : "text-slate-800"
      }`}
    >
      {formatNumber(value)}
    </td>
  );
}
