"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { quantity } from "@/lib/format/turkish";
import {
  goodsReceiptService,
  type GoodsReceiptListItem,
} from "@/services/goods-receipt.service";

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Stok Kaydı Yapıldı",
  2: "İptal",
};

function formatNumber(value: number) {
  return quantity(value);
}

function formatDate(value: string) {
  return new Date(value).toLocaleDateString("tr-TR");
}

function statusClass(status: number) {
  if (status === 1) {
    return "bg-emerald-100 text-emerald-800";
  }

  if (status === 2) {
    return "bg-red-100 text-red-800";
  }

  return "bg-amber-100 text-amber-800";
}

export default function GoodsReceiptListPage() {
  const [items, setItems] = useState<GoodsReceiptListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState("");
  const [search, setSearch] = useState("");
  const [error, setError] = useState("");

  async function load() {
    try {
      setLoading(true);
      setError("");

      const data = await goodsReceiptService.getAll({
        status:
          statusFilter === ""
            ? undefined
            : Number(statusFilter),
      });

      setItems(data);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Mal Kabul kayıtları yüklenemedi.",
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // İlk açılışta bir defa çalışır.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const filteredItems = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");

    if (!term) {
      return items;
    }

    return items.filter((item) =>
      [
        item.receiptNumber,
        item.purchaseOrderNumber,
        item.supplierTitle,
        item.warehouseCode,
        item.warehouseName,
        item.dispatchNoteNumber,
        item.receivedByName,
      ]
        .filter(Boolean)
        .some((value) =>
          String(value)
            .toLocaleLowerCase("tr-TR")
            .includes(term),
        ),
    );
  }, [items, search]);

  const summary = useMemo(
    () => ({
      total: items.length,
      draft: items.filter((item) => item.status === 0).length,
      posted: items.filter((item) => item.status === 1).length,
      accepted: items.reduce(
        (sum, item) => sum + item.acceptedQuantity,
        0,
      ),
    }),
    [items],
  );

  return (
    <ErpShell
      design="redwood"
      title="Mal Kabul"
      description="Satın alma siparişlerinden oluşturulan teslimat ve depo girişleri"
    >
      <div className="space-y-6">
      <div className="flex justify-end">
        <Link className="erp-primary-button" href="/satin-alma/siparis">
          Satın Alma Siparişleri
        </Link>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <SummaryCard
          label="Toplam Mal Kabul"
          value={summary.total}
        />
        <SummaryCard
          label="Taslak"
          value={summary.draft}
        />
        <SummaryCard
          label="Stok Kaydı Yapılan"
          value={summary.posted}
        />
        <SummaryCard
          label="Kabul Edilen Miktar"
          value={formatNumber(summary.accepted)}
        />
      </div>

      <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
        <div className="border-b border-slate-200 p-4">
          <form
            onSubmit={(event) => {
              event.preventDefault();
              void load();
            }}
            className="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px_auto]"
          >
            <input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Mal Kabul no, sipariş, tedarikçi veya depo ara"
              className="rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none ring-slate-300 focus:ring-2"
            />

            <select
              value={statusFilter}
              onChange={(event) =>
                setStatusFilter(event.target.value)
              }
              className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm outline-none ring-slate-300 focus:ring-2"
            >
              <option value="">Tüm durumlar</option>
              <option value="0">Taslak</option>
              <option value="1">Stok Kaydı Yapıldı</option>
              <option value="2">İptal</option>
            </select>

            <button
              type="submit"
              className="rounded-lg border border-slate-300 bg-white px-5 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
            >
              Listeyi Yenile
            </button>
          </form>
        </div>

        {error ? (
          <div className="m-4 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
            {error}
          </div>
        ) : null}

        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-slate-200 text-sm">
            <thead className="bg-slate-50">
              <tr className="text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                <th className="px-4 py-3">Mal Kabul</th>
                <th className="px-4 py-3">Sipariş</th>
                <th className="px-4 py-3">Tedarikçi</th>
                <th className="px-4 py-3">Depo</th>
                <th className="px-4 py-3">İrsaliye</th>
                <th className="px-4 py-3 text-right">Teslim</th>
                <th className="px-4 py-3 text-right">Kabul</th>
                <th className="px-4 py-3">Durum</th>
                <th className="px-4 py-3 text-right">İşlem</th>
              </tr>
            </thead>

            <tbody className="divide-y divide-slate-100">
              {loading ? (
                <tr>
                  <td
                    colSpan={9}
                    className="px-4 py-12 text-center text-slate-500"
                  >
                    Mal Kabul kayıtları yükleniyor...
                  </td>
                </tr>
              ) : filteredItems.length === 0 ? (
                <tr>
                  <td
                    colSpan={9}
                    className="px-4 py-12 text-center text-slate-500"
                  >
                    Mal Kabul kaydı bulunamadı.
                  </td>
                </tr>
              ) : (
                filteredItems.map((item) => (
                  <tr
                    key={item.id}
                    className="hover:bg-slate-50"
                  >
                    <td className="whitespace-nowrap px-4 py-3">
                      <div className="font-semibold text-slate-950">
                        {item.receiptNumber}
                      </div>
                      <div className="mt-1 text-xs text-slate-500">
                        {formatDate(item.receiptDate)}
                      </div>
                    </td>

                    <td className="whitespace-nowrap px-4 py-3 font-medium text-slate-800">
                      {item.purchaseOrderNumber}
                    </td>

                    <td className="min-w-56 px-4 py-3 text-slate-700">
                      {item.supplierTitle}
                    </td>

                    <td className="px-4 py-3">
                      <div className="font-medium text-slate-800">
                        {item.warehouseName}
                      </div>
                      <div className="text-xs text-slate-500">
                        {item.warehouseCode}
                      </div>
                    </td>

                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                      {item.dispatchNoteNumber || "—"}
                    </td>

                    <td className="whitespace-nowrap px-4 py-3 text-right text-slate-700">
                      {formatNumber(item.deliveredQuantity)}
                    </td>

                    <td className="whitespace-nowrap px-4 py-3 text-right font-medium text-slate-900">
                      {formatNumber(item.acceptedQuantity)}
                    </td>

                    <td className="px-4 py-3">
                      <span
                        className={`inline-flex whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-medium ${statusClass(
                          item.status,
                        )}`}
                      >
                        {statusLabels[item.status] ??
                          `Durum ${item.status}`}
                      </span>
                    </td>

                    <td className="whitespace-nowrap px-4 py-3 text-right">
                      <Link
                        href={`/depo-stok/mal-kabul/${item.id}`}
                        className="inline-flex rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
                      >
                        Aç
                      </Link>
                    </td>
                  </tr>
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
