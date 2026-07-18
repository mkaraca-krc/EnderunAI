"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import {
  inventoryService,
  type InventoryItemListItem,
} from "@/services/inventory.service";

function formatNumber(value: number): string {
  return new Intl.NumberFormat("tr-TR", {
    maximumFractionDigits: 2,
  }).format(value);
}

function typeLabel(type: number): string {
  if (type === 1) return "Sarf";
  if (type === 2) return "Demirbaş";
  return "Stok";
}

export default function InventoryPage() {
  const [items, setItems] = useState<InventoryItemListItem[]>([]);
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  async function loadItems(term = appliedSearch) {
    try {
      setLoading(true);
      setError("");
      const data = await inventoryService.getItems({
        search: term.trim() || undefined,
      });
      setItems(data);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Malzeme listesi yüklenemedi.",
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadItems("");
    // İlk yüklemede yalnızca bir kez çalışır.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const summary = useMemo(() => {
    const critical = items.filter(
      (item) => item.availableStock <= item.minimumStock,
    ).length;

    const totalStock = items.reduce(
      (sum, item) => sum + item.totalStock,
      0,
    );

    return {
      totalItems: items.length,
      critical,
      totalStock,
      activeItems: items.filter((item) => item.isActive).length,
    };
  }, [items]);

  function submitSearch(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const term = search.trim();
    setAppliedSearch(term);
    void loadItems(term);
  }

  return (
    <div className="space-y-6 p-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
        <div>
          <p className="text-sm font-medium text-slate-500">
            Operasyon Merkezi
          </p>
          <h1 className="text-2xl font-semibold text-slate-950">
            Depo &amp; Stok
          </h1>
          <p className="mt-1 text-sm text-slate-600">
            Malzeme kartlarını, stok miktarlarını ve kritik seviyeleri yönetin.
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          <Link
            href="/depo-stok/hareketler"
            className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
          >
            Stok hareketleri
          </Link>
          <Link
            href="/depo-stok/yeni"
            className="rounded-lg bg-slate-950 px-4 py-2 text-sm font-medium text-white hover:bg-slate-800"
          >
            Yeni malzeme
          </Link>
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <SummaryCard label="Toplam malzeme" value={summary.totalItems} />
        <SummaryCard label="Aktif kart" value={summary.activeItems} />
        <SummaryCard label="Kritik stok" value={summary.critical} />
        <SummaryCard
          label="Toplam stok miktarı"
          value={formatNumber(summary.totalStock)}
        />
      </div>

      <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
        <div className="border-b border-slate-200 p-4">
          <form
            onSubmit={submitSearch}
            className="flex flex-col gap-3 sm:flex-row"
          >
            <input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Kod, malzeme, marka veya model ara"
              className="min-w-0 flex-1 rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none ring-slate-300 focus:ring-2"
            />
            <button
              type="submit"
              className="rounded-lg border border-slate-300 bg-white px-5 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
            >
              Ara
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
                <th className="px-4 py-3">Kod</th>
                <th className="px-4 py-3">Malzeme</th>
                <th className="px-4 py-3">Kategori</th>
                <th className="px-4 py-3">Tip</th>
                <th className="px-4 py-3 text-right">Toplam stok</th>
                <th className="px-4 py-3 text-right">Kullanılabilir</th>
                <th className="px-4 py-3">Durum</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {loading ? (
                <tr>
                  <td
                    colSpan={7}
                    className="px-4 py-12 text-center text-slate-500"
                  >
                    Malzeme kartları yükleniyor...
                  </td>
                </tr>
              ) : items.length === 0 ? (
                <tr>
                  <td
                    colSpan={7}
                    className="px-4 py-12 text-center text-slate-500"
                  >
                    Kayıt bulunamadı.
                  </td>
                </tr>
              ) : (
                items.map((item) => {
                  const critical =
                    item.availableStock <= item.minimumStock;

                  return (
                    <tr key={item.id} className="hover:bg-slate-50">
                      <td className="whitespace-nowrap px-4 py-3 font-medium text-slate-950">
                        {item.code}
                      </td>
                      <td className="px-4 py-3">
                        <div className="font-medium text-slate-900">
                          {item.name}
                        </div>
                        <div className="text-xs text-slate-500">
                          {[item.brand, item.model]
                            .filter(Boolean)
                            .join(" · ") || "Marka/model belirtilmedi"}
                        </div>
                      </td>
                      <td className="px-4 py-3 text-slate-600">
                        {item.category || "—"}
                      </td>
                      <td className="px-4 py-3 text-slate-600">
                        {typeLabel(item.type)}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-right font-medium text-slate-900">
                        {formatNumber(item.totalStock)} {item.unit}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-right text-slate-700">
                        {formatNumber(item.availableStock)} {item.unit}
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={
                            critical
                              ? "inline-flex rounded-full bg-amber-100 px-2.5 py-1 text-xs font-medium text-amber-800"
                              : "inline-flex rounded-full bg-emerald-100 px-2.5 py-1 text-xs font-medium text-emerald-800"
                          }
                        >
                          {critical ? "Kritik" : "Normal"}
                        </span>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
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
      <p className="text-sm font-medium text-slate-500">{label}</p>
      <p className="mt-2 text-2xl font-semibold text-slate-950">{value}</p>
    </div>
  );
}
