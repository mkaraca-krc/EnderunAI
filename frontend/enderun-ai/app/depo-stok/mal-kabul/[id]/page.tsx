"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  goodsReceiptService,
  purchaseReturnService,
  type GoodsReceiptDetail,
  type GoodsReceiptInventoryOption,
  type GoodsReceiptItem,
  type PurchaseReturnListItem,
  type UpdateGoodsReceiptItemRequest,
} from "@/services/goods-receipt.service";

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Stok Kaydı Yapıldı",
  2: "İptal",
};

function statusClass(status: number) {
  if (status === 1) {
    return "bg-emerald-100 text-emerald-800";
  }

  if (status === 2) {
    return "bg-red-100 text-red-800";
  }

  return "bg-amber-100 text-amber-800";
}

function formatNumber(value: number) {
  return new Intl.NumberFormat("tr-TR", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4,
  }).format(value);
}

function formatDate(value?: string | null) {
  if (!value) {
    return "—";
  }

  return new Date(value).toLocaleDateString("tr-TR");
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return "—";
  }

  return new Date(value).toLocaleString("tr-TR");
}

function formatMoney(value?: number | null) {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency: "TRY",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value ?? 0);
}

export default function GoodsReceiptDetailPage() {
  const params = useParams<{ id: string }>();

  const [purchaseReturns, setPurchaseReturns] = useState<
    PurchaseReturnListItem[]
  >([]);

  const [receipt, setReceipt] =
    useState<GoodsReceiptDetail | null>(null);
  const [draftItems, setDraftItems] = useState<
    UpdateGoodsReceiptItemRequest[]
  >([]);
  const [inventoryOptions, setInventoryOptions] = useState<
    GoodsReceiptInventoryOption[]
  >([]);
  const [loading, setLoading] = useState(true);
  const [processing, setProcessing] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const id = params?.id;

  async function loadReceipt() {
    if (!id) {
      setError("Mal Kabul kimliği bulunamadı.");
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      setError("");

      const data = await goodsReceiptService.getById(id);
      setReceipt(data);

      // Kesinleşmiş kabulde iade belgesi doğmuş olabilir; yetki yoksa
      // sessizce boş kalır.
      if (data.status === 1) {
        setPurchaseReturns(
          await purchaseReturnService
            .getAll({ goodsReceiptId: data.id })
            .catch(() => [])
        );
      }
      setDraftItems(
        data.items.map((item) => ({
          id: item.id,
          inventoryItemId: item.inventoryItemId,
          deliveredQuantity: item.deliveredQuantity,
          acceptedQuantity: item.acceptedQuantity,
          rejectedQuantity: item.rejectedQuantity,
          damagedQuantity: item.damagedQuantity,
          lotNumber: item.lotNumber,
          serialNumber: item.serialNumber,
          productionDate: item.productionDate,
          expiryDate: item.expiryDate,
          warrantyEndDate: item.warrantyEndDate,
          shelfLocation: item.shelfLocation,
          notes: item.notes,
          rejectionReason: item.rejectionReason,
        })),
      );

      if (data.status === 0) {
        try {
          const options =
            await goodsReceiptService.getInventoryOptions(data.id);
          setInventoryOptions(options);
        } catch {
          setInventoryOptions([]);
        }
      } else {
        setInventoryOptions([]);
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Mal Kabul kaydı yüklenemedi.",
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadReceipt();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  const totals = useMemo(() => {
    const items =
      receipt?.status === 0 ? draftItems : (receipt?.items ?? []);

    return {
      delivered: items.reduce(
        (sum, item) => sum + item.deliveredQuantity,
        0,
      ),
      accepted: items.reduce(
        (sum, item) => sum + item.acceptedQuantity,
        0,
      ),
      rejected: items.reduce(
        (sum, item) => sum + item.rejectedQuantity,
        0,
      ),
      damaged: items.reduce(
        (sum, item) => sum + item.damagedQuantity,
        0,
      ),
      lines: items.length,

      // Kısmi kabul tablosu: siparişten ne kadarı hâlâ AÇIK.
      // Reddedilen miktar teslim alınmış sayılmadığı için açık
      // bakiyeye geri döner ve tedarikçi eksiği yeniden gönderebilir.
      ordered: items.reduce((sum, item) => {
        const source = receipt?.items.find((x) => x.id === item.id);
        return sum + (source?.orderedQuantity ?? 0);
      }, 0),

      previously: items.reduce((sum, item) => {
        const source = receipt?.items.find((x) => x.id === item.id);
        return sum + (source?.previouslyReceivedQuantity ?? 0);
      }, 0),
    };
  }, [draftItems, receipt]);

  function updateDraftItem(
    itemId: string,
    patch: Partial<UpdateGoodsReceiptItemRequest>,
  ) {
    setDraftItems((items) =>
      items.map((item) =>
        item.id === itemId ? { ...item, ...patch } : item,
      ),
    );
  }

  function validateDraft(requireAcceptedStock: boolean) {
    if (!receipt) return "Mal kabul kaydı bulunamadı.";

    for (const draft of draftItems) {
      const item = receipt.items.find((value) => value.id === draft.id);
      if (!item) return "Mal kabul kalemleri kayıtla uyuşmuyor.";

      if (
        draft.deliveredQuantity < 0 ||
        draft.acceptedQuantity < 0 ||
        draft.rejectedQuantity < 0 ||
        draft.damagedQuantity < 0
      ) {
        return `${item.lineNumber}. kalemde miktarlar negatif olamaz.`;
      }

      const distributed =
        draft.acceptedQuantity +
        draft.rejectedQuantity +
        draft.damagedQuantity;
      if (Math.abs(distributed - draft.deliveredQuantity) > 0.00001) {
        return `${item.lineNumber}. kalemde kabul, red ve hasarlı toplamı teslim miktarına eşit olmalıdır.`;
      }

      if (
        draft.deliveredQuantity >
        item.orderedQuantity - item.previouslyReceivedQuantity
      ) {
        return `${item.lineNumber}. kalemde teslim miktarı siparişin kalan miktarını aşıyor.`;
      }

      if (
        requireAcceptedStock &&
        draft.acceptedQuantity > 0 &&
        !draft.inventoryItemId
      ) {
        return `${item.lineNumber}. kalemde kabul edilen miktar için stok kartı seçilmelidir.`;
      }

      // Red/hasar varsa GEREKÇE zorunlu. Uç da bunu reddediyor;
      // burada kullanıcı sunucuya gitmeden uyarılıyor. Gerekçesiz red
      // tedarikçiyle mutabakatta savunulamaz ve alış iadesi belgesi
      // "sebebi bilinmeyen" satırla doğardı.
      if (
        requireAcceptedStock &&
        draft.rejectedQuantity + draft.damagedQuantity > 0 &&
        !draft.rejectionReason?.trim()
      ) {
        return `${item.lineNumber}. kalemde reddedilen/hasarlı miktar için gerekçe zorunludur.`;
      }
    }

    if (
      requireAcceptedStock &&
      !draftItems.some((item) => item.acceptedQuantity > 0)
    ) {
      return "Stok kaydı için en az bir kalemde kabul edilen miktar olmalıdır.";
    }

    return "";
  }

  async function saveDraft() {
    if (!receipt) return;
    const validationError = validateDraft(false);
    if (validationError) {
      setError(validationError);
      return;
    }

    try {
      setProcessing(true);
      setError("");
      setSuccess("");
      const result = await goodsReceiptService.updateDraft(
        receipt.id,
        draftItems,
      );
      setSuccess(result.message);
      await loadReceipt();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Mal kabul taslağı kaydedilemedi.",
      );
    } finally {
      setProcessing(false);
    }
  }

  async function postReceipt() {
    if (!receipt) return;
    const validationError = validateDraft(true);
    if (validationError) {
      setError(validationError);
      return;
    }

    if (
      !window.confirm(
        "Kabul edilen miktarlar depo stoklarına işlensin mi? Bu işlem geri alınamaz.",
      )
    ) {
      return;
    }

    try {
      setProcessing(true);
      setError("");
      setSuccess("");
      await goodsReceiptService.updateDraft(receipt.id, draftItems);
      const result = await goodsReceiptService.post(receipt.id);
      setSuccess(result.message);
      await loadReceipt();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Mal kabul stoklara işlenemedi.",
      );
    } finally {
      setProcessing(false);
    }
  }

  async function cancelReceipt() {
    if (!receipt) return;
    const reason = window.prompt("İptal nedenini yazın:")?.trim() ?? "";
    if (!reason) {
      setError("İptal nedeni zorunludur.");
      return;
    }

    if (!window.confirm("Mal kabul taslağı iptal edilsin mi?")) return;

    try {
      setProcessing(true);
      setError("");
      setSuccess("");
      const result = await goodsReceiptService.cancel(receipt.id, reason);
      setSuccess(result.message);
      await loadReceipt();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Mal kabul taslağı iptal edilemedi.",
      );
    } finally {
      setProcessing(false);
    }
  }

  if (loading) {
    return (
      <ErpShell title="Mal Kabul" description="Kayıt yükleniyor">
        <div className="erp-loading">Mal Kabul kaydı yükleniyor...</div>
      </ErpShell>
    );
  }

  if (!receipt) {
    return (
      <ErpShell title="Mal Kabul" description="Kayıt bulunamadı">
        <div className="erp-alert error">
          {error || "Mal Kabul kaydı bulunamadı."}
        </div>

        <div className="erp-row-actions">
          <Link className="erp-secondary-button" href="/depo-stok/mal-kabul">
            ← Mal Kabul listesi
          </Link>

          <button
            type="button"
            className="erp-primary-button"
            onClick={() => void loadReceipt()}
          >
            Tekrar Dene
          </button>
        </div>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      title={`Mal Kabul — ${receipt.receiptNumber}`}
      description="Teslim alınan miktarlar, stok kartı eşleşmesi ve depo girişi"
    >
      <div className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
        <div>
          <Link className="erp-row-link" href="/depo-stok/mal-kabul">
            ← Mal Kabul listesi
          </Link>

          <div className="mt-3 flex flex-wrap items-center gap-3">

            <span
              className={`inline-flex rounded-full px-3 py-1 text-xs font-medium ${statusClass(
                receipt.status,
              )}`}
            >
              {statusLabels[receipt.status] ??
                `Durum ${receipt.status}`}
            </span>
          </div>

          <p className="mt-1 text-sm text-slate-600">
            Mal Kabul tarihi: {formatDate(receipt.receiptDate)}
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          <Link
            href={`/satin-alma/siparis/${receipt.purchaseOrderId}`}
            className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
          >
            Siparişi Aç
          </Link>

          <button
            type="button"
            onClick={() => void loadReceipt()}
            disabled={processing}
            className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
          >
            Yenile
          </button>

          {receipt.accountingVoucherId ? (
            <Link
              href={`/muhasebe/fisler/${receipt.accountingVoucherId}`}
              className="rounded-lg bg-emerald-700 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-800"
            >
              Muhasebe Fişini Aç
            </Link>
          ) : null}

          {receipt.status === 0 ? (
            <>
              <button
                type="button"
                onClick={() => void saveDraft()}
                disabled={processing}
                className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
              >
                Taslağı Kaydet
              </button>
              <button
                type="button"
                onClick={() => void postReceipt()}
                disabled={processing}
                className="rounded-lg bg-emerald-700 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-800 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {processing ? "İşleniyor..." : "Stok Kaydı Yap"}
              </button>
              <button
                type="button"
                onClick={() => void cancelReceipt()}
                disabled={processing}
                className="rounded-lg bg-red-700 px-4 py-2 text-sm font-medium text-white hover:bg-red-800 disabled:cursor-not-allowed disabled:opacity-50"
              >
                İptal Et
              </button>
            </>
          ) : null}
        </div>
      </div>

      {error ? (
        <div className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          {error}
        </div>
      ) : null}

      {success ? (
        <div className="rounded-xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800">
          {success}
        </div>
      ) : null}

      {receipt.status === 0 ? (
        <div className="rounded-xl border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800">
          Stok kartlarını bağlayın; teslim, kabul, red ve hasarlı miktarları
          doğrulayın. “Stok Kaydı Yap” işlemi kabul edilen miktarları depoya
          ekler ve sipariş teslim durumunu günceller.
        </div>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
        <SummaryCard
          label="Kalem Sayısı"
          value={totals.lines}
        />
        <SummaryCard
          label="Teslim Edilen"
          value={formatNumber(totals.delivered)}
        />
        <SummaryCard
          label="Kabul Edilen"
          value={formatNumber(totals.accepted)}
        />
        <SummaryCard
          label="Reddedilen"
          value={formatNumber(totals.rejected)}
        />
        <SummaryCard
          label="Hasarlı"
          value={formatNumber(totals.damaged)}
        />
      </div>

      {/* Kısmi kabulün ne anlama geldiği tek cümlede: kaç adet
          stoğa girdi, kaç adet iade edildi, kaç adet hâlâ bekleniyor.
          Bu üç sayı ayrı ayrı duruyordu ve birlikte okunmadıkça
          "eksik mi geldi yoksa reddettik mi" anlaşılmıyordu. */}
      {(totals.rejected > 0 ||
        totals.damaged > 0 ||
        totals.delivered <
          totals.ordered - totals.previously) && (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          <strong>Kısmi kabul:</strong>{" "}
          {formatNumber(totals.ordered)} sipariş ·{" "}
          {formatNumber(totals.previously + totals.delivered)} geldi ·{" "}
          <strong>{formatNumber(totals.accepted)} stoğa girecek</strong>
          {totals.rejected + totals.damaged > 0 && (
            <>
              {" · "}
              {formatNumber(totals.rejected + totals.damaged)} iade edilecek
            </>
          )}
          {totals.ordered -
            totals.previously -
            totals.delivered >
            0 && (
            <>
              {" · "}
              {formatNumber(
                totals.ordered - totals.previously - totals.delivered
              )}{" "}
              hiç gelmedi
            </>
          )}
          <span className="mt-1 block text-xs">
            Reddedilen miktar siparişte açık kalır; tedarikçi eksiği
            yeniden gönderebilir.
          </span>
        </div>
      )}

      {receipt.status === 1 && purchaseReturns.length > 0 && (
        <div className="rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm">
          <strong className="text-slate-900">Alış İadesi Belgesi</strong>
          <ul className="mt-2 space-y-1">
            {purchaseReturns.map((row) => (
              <li key={row.id}>
                <Link
                  href={`/depo-stok/iadeler/${row.id}`}
                  className="text-teal-700 underline"
                >
                  {row.returnNumber}
                </Link>{" "}
                · {row.statusName} · {formatNumber(row.totalQuantity)} adet ·{" "}
                {row.supplierName}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="grid gap-6 xl:grid-cols-3">
        <InfoSection title="Sipariş ve Proje">
          <InfoRow
            label="Sipariş No"
            value={receipt.purchaseOrderNumber}
          />
          <InfoRow
            label="Proje Kodu"
            value={receipt.projectCode}
          />
          <InfoRow
            label="Proje"
            value={receipt.projectName}
          />
        </InfoSection>

        <InfoSection title="Tedarikçi">
          <InfoRow
            label="Cari Kod"
            value={receipt.supplierCode}
          />
          <InfoRow
            label="Tedarikçi"
            value={receipt.supplierTitle}
          />
        </InfoSection>

        <InfoSection title="Depo">
          <InfoRow
            label="Depo Kodu"
            value={receipt.warehouseCode}
          />
          <InfoRow
            label="Depo"
            value={receipt.warehouseName}
          />
          <InfoRow
            label="Teslim Alan"
            value={receipt.receivedByName}
          />
        </InfoSection>
      </div>

      <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-950">
              Muhasebe
            </h2>

            {receipt.accountingVoucherId ? (
              <p className="mt-1 text-sm text-emerald-700">
                Bu Mal Kabul için otomatik muhasebe fişi oluşturuldu.
              </p>
            ) : (
              <p className="mt-1 text-sm text-slate-600">
                Bu Mal Kabul kaydına bağlı muhasebe fişi bulunmuyor.
              </p>
            )}
          </div>

          <span
            className={`inline-flex w-fit rounded-full px-3 py-1 text-xs font-medium ${
              receipt.accountingVoucherId
                ? "bg-emerald-100 text-emerald-800"
                : "bg-slate-100 text-slate-700"
            }`}
          >
            {receipt.accountingVoucherId
              ? "Muhasebeleştirildi"
              : "Bağlı Fiş Yok"}
          </span>
        </div>

        <div className="mt-5 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <InfoRow
            label="Fiş No"
            value={receipt.accountingVoucherNumber || "—"}
          />

          <InfoRow
            label="Fiş Durumu"
            value={
              receipt.accountingVoucherStatus === 0
                ? "Taslak"
                : receipt.accountingVoucherStatus === 1
                  ? "Kesinleşti"
                  : receipt.accountingVoucherStatus === 2
                    ? "İptal"
                    : "—"
            }
          />

          <InfoRow
            label="Muhasebe Tutarı"
            value={
              receipt.accountingVoucherTotal != null
                ? formatMoney(receipt.accountingVoucherTotal)
                : "—"
            }
          />

          <div className="flex items-end">
            {receipt.accountingVoucherId ? (
              <Link
                href={`/muhasebe/fisler/${receipt.accountingVoucherId}`}
                className="inline-flex rounded-lg bg-brand-700 px-4 py-2 text-sm font-medium text-white hover:bg-brand-600"
              >
                Muhasebe Fişini Aç
              </Link>
            ) : (
              <span className="text-sm text-slate-500">
                Bağlı fiş yok
              </span>
            )}
          </div>
        </div>
      </section>

      <div className="grid gap-6 xl:grid-cols-2">
        <InfoSection title="İrsaliye ve Fatura">
          <InfoRow
            label="İrsaliye No"
            value={receipt.dispatchNoteNumber || "—"}
          />
          <InfoRow
            label="İrsaliye Tarihi"
            value={formatDate(receipt.dispatchNoteDate)}
          />
          <InfoRow
            label="Fatura No"
            value={receipt.invoiceNumber || "—"}
          />
          <InfoRow
            label="Fatura Tarihi"
            value={formatDate(receipt.invoiceDate)}
          />
        </InfoSection>

        <InfoSection title="Sevkiyat">
          <InfoRow
            label="Araç Plakası"
            value={receipt.vehiclePlate || "—"}
          />
          <InfoRow
            label="Sürücü"
            value={receipt.driverName || "—"}
          />
          <InfoRow
            label="Stok Kayıt Tarihi"
            value={formatDateTime(receipt.postedAtUtc)}
          />
          <InfoRow
            label="İptal Tarihi"
            value={formatDateTime(receipt.cancelledAtUtc)}
          />
        </InfoSection>
      </div>

      {(receipt.description ||
        receipt.notes ||
        receipt.cancellationReason) && (
        <InfoSection title="Açıklamalar">
          {receipt.description ? (
            <TextBlock
              label="Açıklama"
              value={receipt.description}
            />
          ) : null}

          {receipt.notes ? (
            <TextBlock
              label="Notlar"
              value={receipt.notes}
            />
          ) : null}

          {receipt.cancellationReason ? (
            <TextBlock
              label="İptal Nedeni"
              value={receipt.cancellationReason}
            />
          ) : null}
        </InfoSection>
      )}

      <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
        <div className="border-b border-slate-200 p-5">
          <h2 className="text-lg font-semibold text-slate-950">
            Teslimat Kalemleri
          </h2>
          <p className="mt-1 text-sm text-slate-600">
            Sipariş miktarı ile mevcut teslimat miktarlarını karşılaştırın.
          </p>
        </div>

        <div className="overflow-x-auto">
          <table className="min-w-[1500px] divide-y divide-slate-200 text-sm">
            <thead className="bg-slate-50">
              <tr className="text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                <th className="px-4 py-3">Sıra</th>
                <th className="px-4 py-3">Malzeme</th>
                <th className="px-4 py-3">Stok Kartı</th>
                <th className="px-4 py-3 text-right">Sipariş</th>
                <th className="px-4 py-3 text-right">Önceki Teslim</th>
                <th className="px-4 py-3 text-right">Teslim Edilen</th>
                <th className="px-4 py-3 text-right">Kabul</th>
                <th className="px-4 py-3 text-right">Red</th>
                <th className="px-4 py-3 text-right">Hasarlı</th>
                <th className="px-4 py-3">Red / Hasar Gerekçesi</th>
                <th className="px-4 py-3">Lot / Seri</th>
                <th className="px-4 py-3">Raf</th>
              </tr>
            </thead>

            <tbody className="divide-y divide-slate-100">
              {receipt.items.length === 0 ? (
                <tr>
                  <td
                    colSpan={11}
                    className="px-4 py-12 text-center text-slate-500"
                  >
                    Mal Kabul kalemi bulunamadı.
                  </td>
                </tr>
              ) : receipt.status === 0 ? (
                receipt.items
                  .slice()
                  .sort((a, b) => a.lineNumber - b.lineNumber)
                  .map((item) => {
                    const draft = draftItems.find(
                      (value) => value.id === item.id,
                    );
                    return draft ? (
                      <DraftGoodsReceiptItemRow
                        key={item.id}
                        item={item}
                        draft={draft}
                        inventoryOptions={inventoryOptions}
                        onChange={(patch) =>
                          updateDraftItem(item.id, patch)
                        }
                      />
                    ) : null;
                  })
              ) : (
                receipt.items
                  .slice()
                  .sort(
                    (a, b) =>
                      a.lineNumber - b.lineNumber,
                  )
                  .map((item) => (
                    <GoodsReceiptItemRow
                      key={item.id}
                      item={item}
                    />
                  ))
              )}
            </tbody>
          </table>
        </div>
      </section>
      </div>
    </ErpShell>
  );
}

function DraftGoodsReceiptItemRow({
  item,
  draft,
  inventoryOptions,
  onChange,
}: {
  item: GoodsReceiptItem;
  draft: UpdateGoodsReceiptItemRequest;
  inventoryOptions: GoodsReceiptInventoryOption[];
  onChange: (patch: Partial<UpdateGoodsReceiptItemRequest>) => void;
}) {
  const difference =
    draft.deliveredQuantity -
    draft.acceptedQuantity -
    draft.rejectedQuantity -
    draft.damagedQuantity;
  const matchingOptions = inventoryOptions.filter(
    (option) =>
      option.unit.toLocaleLowerCase("tr-TR") ===
      item.unit.toLocaleLowerCase("tr-TR"),
  );
  const selectedIsMissing =
    Boolean(draft.inventoryItemId) &&
    !matchingOptions.some(
      (option) => option.id === draft.inventoryItemId,
    );

  return (
    <tr className="align-top bg-amber-50/30 hover:bg-amber-50/60">
      <td className="whitespace-nowrap px-4 py-4 font-medium text-slate-900">
        {item.lineNumber}
      </td>

      <td className="min-w-72 px-4 py-4">
        <div className="font-medium text-slate-950">
          {item.materialDescription}
        </div>
        <div className="mt-1 text-xs text-slate-500">
          {[item.brand, item.model].filter(Boolean).join(" · ") ||
            "Marka/model belirtilmedi"}
        </div>
        {Math.abs(difference) > 0.00001 ? (
          <div className="mt-2 rounded bg-red-50 px-2 py-1 text-xs text-red-700">
            Kabul, red ve hasarlı toplamı teslim miktarıyla uyuşmuyor.
          </div>
        ) : null}
      </td>

      <td className="min-w-64 px-4 py-4">
        <select
          value={draft.inventoryItemId ?? ""}
          onChange={(event) =>
            onChange({ inventoryItemId: event.target.value || null })
          }
          className="w-full rounded-lg border border-slate-300 bg-white px-2 py-2 text-xs text-slate-900 outline-none ring-slate-300 focus:ring-2"
        >
          <option value="">Stok kartı seçin</option>
          {selectedIsMissing ? (
            <option value={draft.inventoryItemId ?? ""}>
              {item.inventoryItemCode || "Mevcut stok kartı"} · {item.unit}
            </option>
          ) : null}
          {matchingOptions.map((option) => (
            <option key={option.id} value={option.id}>
              {option.code} · {option.name} · {option.unit}
            </option>
          ))}
        </select>
        {matchingOptions.length === 0 ? (
          <p className="mt-1 text-xs text-amber-700">
            {item.unit} biriminde aktif stok kartı bulunamadı.
          </p>
        ) : null}
      </td>

      <NumberCell value={item.orderedQuantity} unit={item.unit} />
      <NumberCell
        value={item.previouslyReceivedQuantity}
        unit={item.unit}
      />
      <EditableNumberCell
        value={draft.deliveredQuantity}
        unit={item.unit}
        onChange={(value) => onChange({ deliveredQuantity: value })}
      />
      <EditableNumberCell
        value={draft.acceptedQuantity}
        unit={item.unit}
        onChange={(value) => onChange({ acceptedQuantity: value })}
        emphasized
      />
      <EditableNumberCell
        value={draft.rejectedQuantity}
        unit={item.unit}
        onChange={(value) => onChange({ rejectedQuantity: value })}
      />
      <EditableNumberCell
        value={draft.damagedQuantity}
        unit={item.unit}
        onChange={(value) => onChange({ damagedQuantity: value })}
      />

      {/* Gerekçe alanı yalnız red/hasar girildiğinde AÇILIR ve o
          zaman zorunludur. Her kalemde sürekli görünmesi tamamı
          kabul edilen teslimatlarda gereksiz gürültü olurdu. */}
      <td className="min-w-56 space-y-2 px-4 py-4">
        {draft.rejectedQuantity + draft.damagedQuantity > 0 ? (
          <>
            <textarea
              value={draft.rejectionReason ?? ""}
              onChange={(event) =>
                onChange({ rejectionReason: event.target.value })
              }
              rows={2}
              placeholder="Red / hasar gerekçesi (zorunlu)"
              className={`w-full rounded-lg border bg-white px-2 py-1.5 text-xs outline-none ring-slate-300 focus:ring-2 ${
                draft.rejectionReason?.trim()
                  ? "border-slate-300"
                  : "border-amber-400 bg-amber-50"
              }`}
            />
            {!draft.rejectionReason?.trim() && (
              <p className="text-[11px] text-amber-700">
                Gerekçe girilmeden kabul kesinleştirilemez.
              </p>
            )}
          </>
        ) : (
          <span className="text-xs text-slate-400">—</span>
        )}
      </td>

      <td className="min-w-52 space-y-2 px-4 py-4">
        <input
          value={draft.lotNumber ?? ""}
          onChange={(event) => onChange({ lotNumber: event.target.value })}
          placeholder="Lot numarası"
          className="w-full rounded-lg border border-slate-300 bg-white px-2 py-1.5 text-xs outline-none ring-slate-300 focus:ring-2"
        />
        <input
          value={draft.serialNumber ?? ""}
          onChange={(event) =>
            onChange({ serialNumber: event.target.value })
          }
          placeholder="Seri numarası"
          className="w-full rounded-lg border border-slate-300 bg-white px-2 py-1.5 text-xs outline-none ring-slate-300 focus:ring-2"
        />
      </td>

      <td className="min-w-40 px-4 py-4">
        <input
          value={draft.shelfLocation ?? ""}
          onChange={(event) =>
            onChange({ shelfLocation: event.target.value })
          }
          placeholder="Raf konumu"
          className="w-full rounded-lg border border-slate-300 bg-white px-2 py-1.5 text-xs outline-none ring-slate-300 focus:ring-2"
        />
      </td>
    </tr>
  );
}

function EditableNumberCell({
  value,
  unit,
  onChange,
  emphasized = false,
}: {
  value: number;
  unit: string;
  onChange: (value: number) => void;
  emphasized?: boolean;
}) {
  return (
    <td className="min-w-28 px-3 py-4 text-right">
      <input
        type="number"
        min="0"
        step="0.0001"
        value={value}
        onChange={(event) => onChange(Number(event.target.value) || 0)}
        className={`w-24 rounded-lg border bg-white px-2 py-1.5 text-right text-sm outline-none ring-slate-300 focus:ring-2 ${
          emphasized
            ? "border-emerald-300 font-semibold text-emerald-800"
            : "border-slate-300 text-slate-800"
        }`}
      />
      <div className="mt-1 text-xs text-slate-500">{unit}</div>
    </td>
  );
}

function GoodsReceiptItemRow({
  item,
}: {
  item: GoodsReceiptItem;
}) {
  const difference =
    item.deliveredQuantity -
    item.acceptedQuantity -
    item.rejectedQuantity -
    item.damagedQuantity;

  return (
    <tr className="align-top hover:bg-slate-50">
      <td className="whitespace-nowrap px-4 py-4 font-medium text-slate-900">
        {item.lineNumber}
      </td>

      <td className="min-w-72 px-4 py-4">
        <div className="font-medium text-slate-950">
          {item.materialDescription}
        </div>

        <div className="mt-1 text-xs text-slate-500">
          {[item.brand, item.model]
            .filter(Boolean)
            .join(" · ") || "Marka/model belirtilmedi"}
        </div>

        {item.notes ? (
          <div className="mt-2 text-xs text-slate-500">
            {item.notes}
          </div>
        ) : null}

        {Math.abs(difference) > 0.0001 ? (
          <div className="mt-2 rounded bg-red-50 px-2 py-1 text-xs text-red-700">
            Miktar dağılımı teslim miktarıyla uyuşmuyor.
          </div>
        ) : null}
      </td>

      <td className="min-w-48 px-4 py-4">
        {item.inventoryItemId ? (
          <>
            <div className="font-medium text-slate-900">
              {item.inventoryItemCode || "—"}
            </div>
            <div className="mt-1 text-xs text-slate-500">
              {item.inventoryItemName || "Stok kartı"}
            </div>
          </>
        ) : (
          <span className="inline-flex rounded-full bg-amber-100 px-2.5 py-1 text-xs font-medium text-amber-800">
            Stok kartı bağlı değil
          </span>
        )}
      </td>

      <NumberCell
        value={item.orderedQuantity}
        unit={item.unit}
      />
      <NumberCell
        value={item.previouslyReceivedQuantity}
        unit={item.unit}
      />
      <NumberCell
        value={item.deliveredQuantity}
        unit={item.unit}
      />
      <NumberCell
        value={item.acceptedQuantity}
        unit={item.unit}
        emphasized
      />
      <NumberCell
        value={item.rejectedQuantity}
        unit={item.unit}
      />
      <NumberCell
        value={item.damagedQuantity}
        unit={item.unit}
      />

      <td className="min-w-56 px-4 py-4 text-xs text-slate-700">
        {item.rejectedQuantity + item.damagedQuantity > 0
          ? item.rejectionReason || "—"
          : "—"}
      </td>

      <td className="min-w-48 px-4 py-4 text-slate-700">
        <div>
          <span className="text-xs text-slate-500">
            Lot:
          </span>{" "}
          {item.lotNumber || "—"}
        </div>
        <div className="mt-1">
          <span className="text-xs text-slate-500">
            Seri:
          </span>{" "}
          {item.serialNumber || "—"}
        </div>
      </td>

      <td className="whitespace-nowrap px-4 py-4 text-slate-700">
        {item.shelfLocation || "—"}
      </td>
    </tr>
  );
}

function NumberCell({
  value,
  unit,
  emphasized = false,
}: {
  value: number;
  unit: string;
  emphasized?: boolean;
}) {
  return (
    <td
      className={`whitespace-nowrap px-4 py-4 text-right ${
        emphasized
          ? "font-semibold text-slate-950"
          : "text-slate-700"
      }`}
    >
      {formatNumber(value)}
      <div className="text-xs font-normal text-slate-500">
        {unit}
      </div>
    </td>
  );
}

function SummaryCard({
  label,
  value,
}: {
  label: string;
  value: string | number;
}) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
      <p className="text-sm font-medium text-slate-500">
        {label}
      </p>
      <p className="mt-2 text-2xl font-semibold text-slate-950">
        {value}
      </p>
    </div>
  );
}

function InfoSection({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
      <h2 className="border-b border-slate-100 pb-3 text-base font-semibold text-slate-950">
        {title}
      </h2>
      <div className="mt-4 space-y-3">
        {children}
      </div>
    </section>
  );
}

function InfoRow({
  label,
  value,
}: {
  label: string;
  value: React.ReactNode;
}) {
  return (
    <div className="grid grid-cols-[130px_minmax(0,1fr)] gap-3 text-sm">
      <span className="text-slate-500">{label}</span>
      <span className="font-medium text-slate-900">
        {value}
      </span>
    </div>
  );
}

function TextBlock({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div>
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
        {label}
      </p>
      <p className="mt-1 whitespace-pre-wrap text-sm leading-6 text-slate-800">
        {value}
      </p>
    </div>
  );
}
