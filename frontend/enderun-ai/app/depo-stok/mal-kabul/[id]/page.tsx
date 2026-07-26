"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import {
  goodsReceiptService,
  type GoodsReceiptDetail,
  type GoodsReceiptItem,
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
  const router = useRouter();

  const [receipt, setReceipt] =
    useState<GoodsReceiptDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

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
    void loadReceipt();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  const totals = useMemo(() => {
    const items = receipt?.items ?? [];

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
    };
  }, [receipt]);

  if (loading) {
    return (
      <div className="p-6">
        <div className="rounded-xl border border-slate-200 bg-white p-12 text-center text-sm text-slate-500 shadow-sm">
          Mal Kabul kaydı yükleniyor...
        </div>
      </div>
    );
  }

  if (error || !receipt) {
    return (
      <div className="space-y-4 p-6">
        <Link
          href="/depo-stok/mal-kabul"
          className="text-sm font-medium text-slate-600 hover:text-slate-950"
        >
          ← Mal Kabul listesi
        </Link>

        <div className="rounded-xl border border-red-200 bg-red-50 p-5 text-sm text-red-700">
          {error || "Mal Kabul kaydı bulunamadı."}
        </div>

        <button
          type="button"
          onClick={() => void loadReceipt()}
          className="rounded-lg bg-slate-950 px-4 py-2 text-sm font-medium text-white hover:bg-slate-800"
        >
          Tekrar Dene
        </button>
      </div>
    );
  }

  return (
    <div className="space-y-6 p-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
        <div>
          <Link
            href="/depo-stok/mal-kabul"
            className="text-sm font-medium text-slate-600 hover:text-slate-950"
          >
            ← Mal Kabul listesi
          </Link>

          <div className="mt-3 flex flex-wrap items-center gap-3">
            <h1 className="text-2xl font-semibold text-slate-950">
              {receipt.receiptNumber}
            </h1>

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
            href={`/satin-alma/siparisler/${receipt.purchaseOrderId}`}
            className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
          >
            Siparişi Aç
          </Link>

          <button
            type="button"
            onClick={() => router.refresh()}
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
            <span
              title="Stok kaydı işlemi sonraki aşamada aktif edilecek."
              className="cursor-not-allowed rounded-lg bg-slate-300 px-4 py-2 text-sm font-medium text-slate-600"
            >
              Stok Kaydı Yap
            </span>
          ) : null}
        </div>
      </div>

      {receipt.status === 0 ? (
        <div className="rounded-xl border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800">
          Bu kayıt taslak durumundadır. Kabul, red, hasarlı miktar ve
          stok giriş işlemleri bir sonraki aşamada aktif edilecektir.
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
            ) : receipt.status === 0 ? (
              <p className="mt-1 text-sm text-amber-700">
                Muhasebe fişi Mal Kabul post edildiğinde otomatik oluşacaktır.
              </p>
            ) : (
              <p className="mt-1 text-sm text-red-700">
                Mal Kabul post edilmiş ancak bağlı muhasebe fişi bulunamadı.
              </p>
            )}
          </div>

          <span
            className={`inline-flex w-fit rounded-full px-3 py-1 text-xs font-medium ${
              receipt.accountingVoucherId
                ? "bg-emerald-100 text-emerald-800"
                : receipt.status === 0
                  ? "bg-amber-100 text-amber-800"
                  : "bg-red-100 text-red-800"
            }`}
          >
            {receipt.accountingVoucherId
              ? "Muhasebeleştirildi"
              : receipt.status === 0
                ? "Bekliyor"
                : "Fiş Bulunamadı"}
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
                className="inline-flex rounded-lg bg-slate-950 px-4 py-2 text-sm font-medium text-white hover:bg-slate-800"
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
