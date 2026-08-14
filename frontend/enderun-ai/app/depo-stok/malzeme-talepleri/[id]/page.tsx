"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import {
  useCallback,
  useEffect,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { quantity } from "@/lib/format/turkish";

import {
  purchaseRequestService,
  type PurchaseRequestDetail,
} from "@/services/purchase-request.service";
import { requestedBrandLabel } from "@/lib/purchasing/requested-brand";

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
  return quantity(value ?? 0);
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


/**
 * Malzeme talebi kararları. Eskiden iptal, prompt + confirm ikilisiyle
 * alınıyordu; gerekçe boş bırakılabiliyor ve kayda boş geçiyordu.
 * Onaya gönderme ile onaylama ise hiç onay sormuyordu.
 */
type RequestAction = "submit" | "approve" | "cancel";

const ACTION_DIALOGS: Record<
  RequestAction,
  { title: string; description: string; confirmLabel: string; showReason?: boolean }
> = {
  submit: {
    title: "Talep onaya gönderilsin mi?",
    description: "Talep onaycıya düşer; onaylanana kadar kalemler değiştirilemez.",
    confirmLabel: "Onaya Gönder",
  },
  approve: {
    title: "Talep onaylansın mı?",
    description: "Onaylanan talep için depodan çıkış ya da satın alma başlatılabilir.",
    confirmLabel: "Onayla",
  },
  cancel: {
    title: "Talep iptal edilsin mi?",
    description: "İptal edilen talep yeniden açılamaz; gerekçe kayda geçer.",
    confirmLabel: "İptal Et",
    showReason: true,
  },
};

export default function MaterialRequestDetailPage() {
  const params = useParams<{ id: string }>();
  const requestId = params.id;

  const [request, setRequest] =
    useState<PurchaseRequestDetail | null>(null);

  const [loading, setLoading] = useState(true);
  const [processing, setProcessing] = useState(false);

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [pendingAction, setPendingAction] = useState<RequestAction | null>(null);

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



  const loadPage = useCallback(async () => {
    if (!requestId) return;

    try {
      setLoading(true);
      setError("");

      const requestData =
        await purchaseRequestService.getById(requestId);

      setRequest(requestData);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Malzeme talebi yüklenemedi.",
      );
    } finally {
      setLoading(false);
    }
  }, [requestId]);

  useEffect(() => {
    void loadPage();
  }, [loadPage]);


  async function refreshOperationalData() {
    await loadRequest();
  }


  async function runRequestAction(action: RequestAction, reason: string) {
    if (!request) return;

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

      setPendingAction(null);
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








  return (
    <ErpShell
      design="redwood"
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
                      onClick={() => setPendingAction("submit")}
                      className="button-primary"
                    >
                      Onaya Gönder
                    </button>
                  ) : null}

                  {request.status === 1 ? (
                    <button
                      type="button"
                      disabled={processing}
                      onClick={() => setPendingAction("approve")}
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
                      onClick={() => setPendingAction("cancel")}
                      className="button-danger"
                    >
                      Talebi İptal Et
                    </button>
                  ) : null}
                </div>
              </div>
            </section>


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

            </div>

            <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
              <div className="border-b border-slate-200 px-5 py-4">
                <h2 className="text-lg font-semibold text-slate-950">
                  Talep Kalemleri
                </h2>

                <p className="mt-1 text-sm text-slate-500">
                  Talepte yazan malzemeler ve miktarları.
                </p>
              </div>

              <div className="overflow-x-auto">
                <table className="w-full min-w-[640px] text-sm">
                  <thead className="bg-slate-50">
                    <tr>
                      <th className="px-4 py-3 text-left font-medium text-slate-600">
                        #
                      </th>
                      <th className="px-4 py-3 text-left font-medium text-slate-600">
                        Malzeme
                      </th>
                      <th className="px-4 py-3 text-right font-medium text-slate-600">
                        Miktar
                      </th>
                      <th className="px-4 py-3 text-left font-medium text-slate-600">
                        İhtiyaç Tarihi
                      </th>
                      <th className="px-4 py-3 text-left font-medium text-slate-600">
                        Not
                      </th>
                    </tr>
                  </thead>

                  <tbody className="divide-y divide-slate-100">
                    {request.items.length === 0 ? (
                      <tr>
                        <td
                          colSpan={5}
                          className="px-4 py-8 text-center text-slate-500"
                        >
                          Talepte kalem bulunmuyor.
                        </td>
                      </tr>
                    ) : (
                      request.items.map((item) => (
                        <tr key={item.id}>
                          <td className="px-4 py-3 text-slate-600">
                            {item.lineNumber}
                          </td>

                          <td className="px-4 py-3">
                            <div className="font-medium text-slate-900">
                              {item.inventoryItemName ||
                                item.materialDescription}
                            </div>

                            {item.inventoryItemCode ? (
                              <div className="text-xs text-slate-500">
                                {item.inventoryItemCode}
                              </div>
                            ) : null}

                            {/* İstenen marka — üç durumun metni tek
                                yerden (lib/purchasing/requested-brand)
                                gelir, ekran kuralı tekrar yazmaz. */}
                            <div className="mt-1 text-xs text-slate-600">
                              Marka: {requestedBrandLabel(item)}
                            </div>
                          </td>

                          <td className="px-4 py-3 text-right tabular-nums text-slate-900">
                            {formatNumber(item.quantity)} {item.unit}
                          </td>

                          <td className="px-4 py-3 text-slate-600">
                            {formatDate(item.requestedDeliveryDate)}
                          </td>

                          <td className="px-4 py-3 text-slate-600">
                            {item.notes || "—"}
                          </td>
                        </tr>
                      ))
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
      {pendingAction && (
        <ConfirmDialog
          key={pendingAction}
          open
          title={ACTION_DIALOGS[pendingAction].title}
          description={ACTION_DIALOGS[pendingAction].description}
          confirmLabel={ACTION_DIALOGS[pendingAction].confirmLabel}
          showReason={ACTION_DIALOGS[pendingAction].showReason}
          busy={processing}
          onCancel={() => setPendingAction(null)}
          onConfirm={(reason) => void runRequestAction(pendingAction, reason)}
        />
      )}

    </ErpShell>
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

