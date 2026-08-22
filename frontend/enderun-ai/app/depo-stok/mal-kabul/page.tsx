"use client";

import Link from "next/link";
import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";
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

/**
 * SUSPENSE SINIRI ŞART.
 *
 * Sayfa durumu (sayfa/boyut/süzgeç/arama) URL'de tutuluyor ve
 * `useSearchParams()` ile okunuyor. Next.js bu kancayı taşıyan bir
 * ekranı ön-render ederken Suspense sınırı olmadan derlemeyi
 * DURDURUYOR — deploy tam olarak burada kırıldı.
 *
 * Aynı desen `mal-kabul/yeni` ekranında da var; ikisi de aynı şekilde
 * sarmalanıyor.
 */
export default function GoodsReceiptListPage() {
  return (
    <Suspense
      fallback={
        <div className="p-6">
          <div className="rounded-xl border border-slate-200 bg-white p-12 text-center text-sm text-slate-500 shadow-sm">
            Mal kabul listesi hazırlanıyor...
          </div>
        </div>
      }
    >
      <GoodsReceiptListContent />
    </Suspense>
  );
}

function GoodsReceiptListContent() {
  const router = useRouter();
  const searchParams = useSearchParams();

  /*
   * SAYFA / SÜZGEÇ / ARAMA URL'DE.
   *
   * Kullanıcı 7. sayfadaki bir kaydı arkadaşına gönderebilmeli,
   * yenilediğinde aynı yerde kalmalı, geri tuşu çalışmalı. Durum
   * yalnız bellekte tutulsaydı bunların hiçbiri olmazdı.
   */
  const page = Math.max(Number(searchParams.get("sayfa") ?? 1), 1);
  const pageSize = Math.min(
    Math.max(Number(searchParams.get("boyut") ?? 50), 10),
    200,
  );
  const statusFilter = searchParams.get("durum") ?? "";
  const search = searchParams.get("ara") ?? "";

  const [items, setItems] = useState<GoodsReceiptListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [summary, setSummary] = useState({
    total: 0,
    draft: 0,
    posted: 0,
    cancelled: 0,
    acceptedQuantity: 0,
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  /** URL'i günceller; sayfa dışındaki her değişiklik 1. sayfaya döner. */
  const setQuery = useCallback(
    (degisiklik: Record<string, string | number | null>) => {
      const next = new URLSearchParams(searchParams.toString());

      for (const [anahtar, deger] of Object.entries(degisiklik)) {
        if (deger === null || deger === "") next.delete(anahtar);
        else next.set(anahtar, String(deger));
      }

      router.replace(`?${next.toString()}`, { scroll: false });
    },
    [router, searchParams],
  );

  /*
   * YARIŞ KORUMASI: hızlı yazan kullanıcıda istekler sırayla dönmez.
   * Geç dönen eski sayfa yenisini ezerse kullanıcı yanlış listeden
   * kayıt açar. Önceki istek iptal ediliyor.
   */
  const abortRef = useRef<AbortController | null>(null);
  const aramaZamanlayici = useRef<number | undefined>(undefined);

  const load = useCallback(async () => {
    abortRef.current?.abort();

    const controller = new AbortController();
    abortRef.current = controller;

    try {
      setLoading(true);
      setError("");

      const params = {
        status: statusFilter === "" ? undefined : Number(statusFilter),
        search: search || undefined,
        page,
        pageSize,
      };

      const [liste, ozet] = await Promise.all([
        goodsReceiptService.getAll(params, controller.signal),
        goodsReceiptService.getSummary(
          { search: search || undefined },
          controller.signal,
        ),
      ]);

      if (controller.signal.aborted) return;

      setItems(liste.items);
      setTotal(liste.total);
      setSummary(ozet);
    } catch (err) {
      if (controller.signal.aborted) return;

      setError(
        err instanceof Error
          ? err.message
          : "Mal Kabul kayıtları yüklenemedi.",
      );
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, [statusFilter, search, page, pageSize]);

  useEffect(() => {
    void load();
    // İlk açılışta bir defa çalışır.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /*
   * SÜZME VE ÖZET ARTIK SUNUCUDA.
   *
   * Burada istemci tarafı bir süzgeç ve `items.length` üzerinden
   * hesaplanan özet kartları vardı. Sayfalama gelince ikisi de
   * yanlışlanırdı: elde yalnız bir sayfa olduğu için 10.000 kayıtlık
   * listede "Toplam Mal Kabul: 50" yazardı ve arama yalnız o sayfada
   * çalışırdı — kullanıcı "kaydım yok" derdi.
   */

  /*
   * SÜTUNLAR VERİ OLARAK. `render` ekran için, `value` dosya/kâğıt
   * için: Excel'e giden metin ile ekranda görünen rozet aynı yerden
   * türüyor, ayrışamıyorlar.
   */
  const columns: DataTableColumn<GoodsReceiptListItem>[] = [
    {
      key: "receipt",
      header: "Mal Kabul",
      value: (row) => `${row.receiptNumber} · ${formatDate(row.receiptDate)}`,
      render: (row) => (
        <>
          <div className="font-semibold text-slate-950">{row.receiptNumber}</div>
          <div className="mt-1 text-xs text-slate-500">
            {formatDate(row.receiptDate)}
          </div>
        </>
      ),
    },
    { key: "order", header: "Sipariş", value: (row) => row.purchaseOrderNumber },
    { key: "supplier", header: "Tedarikçi", value: (row) => row.supplierTitle },
    {
      key: "warehouse",
      header: "Depo",
      value: (row) => `${row.warehouseName} (${row.warehouseCode})`,
      render: (row) => (
        <>
          <div className="font-medium text-slate-800">{row.warehouseName}</div>
          <div className="text-xs text-slate-500">{row.warehouseCode}</div>
        </>
      ),
    },
    {
      key: "dispatch",
      header: "İrsaliye",
      value: (row) => row.dispatchNoteNumber || "—",
    },
    {
      key: "delivered",
      header: "Teslim",
      align: "right",
      value: (row) => formatNumber(row.deliveredQuantity),
    },
    {
      key: "accepted",
      header: "Kabul",
      align: "right",
      value: (row) => formatNumber(row.acceptedQuantity),
    },
    {
      key: "status",
      header: "Durum",
      value: (row) => statusLabels[row.status] ?? `Durum ${row.status}`,
      render: (row) => (
        <span
          className={`inline-flex whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-medium ${statusClass(row.status)}`}
        >
          {statusLabels[row.status] ?? `Durum ${row.status}`}
        </span>
      ),
    },
    {
      key: "actions",
      header: "İşlem",
      align: "right",
      value: () => "",
      render: (row) => (
        <Link
          href={`/depo-stok/mal-kabul/${row.id}`}
          className="inline-flex rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          Aç
        </Link>
      ),
    },
  ];

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
          value={formatNumber(summary.acceptedQuantity)}
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
              defaultValue={search}
              onChange={(event) => {
                // 300 ms BEKLEME: her tuşta sunucuya sormak hem
                // gereksiz yük hem de yarış kaynağı. Değer URL'e
                // yazılıyor ki yenilemede ve paylaşılan bağlantıda
                // aynı arama açılsın.
                const deger = event.target.value;

                window.clearTimeout(aramaZamanlayici.current);
                aramaZamanlayici.current = window.setTimeout(
                  () => setQuery({ ara: deger, sayfa: 1 }),
                  300,
                );
              }}
              placeholder="Mal Kabul no, sipariş, tedarikçi veya depo ara"
              className="rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none ring-slate-300 focus:ring-2"
            />

            <select
              value={statusFilter}
              onChange={(event) =>
                // Süzgeç değişince 1. sayfaya dönülür; yoksa kullanıcı
                // 7. sayfadayken daraltınca boş ekranda kalır.
                setQuery({ durum: event.target.value, sayfa: 1 })
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

        <DataTable
          rows={items}
          columns={columns}
          rowKey={(row) => row.id}
          title="Mal Kabul"
          /*
            SUNUCU KİPİ: sayfa, boyut ve TOPLAM uçtan geliyor.
            İstemci kipinde tablo eldeki diziyi sayar; sayfalanmış bir
            listede o sayı yalnız bir sayfayı gösterir.
          */
          server={{
            total,
            page,
            pageSize,
            onChange: (nextPage, nextSize) =>
              setQuery({ sayfa: nextPage, boyut: nextSize }),
          }}
          loading={loading}
          emptyText="Mal kabul kaydı bulunamadı."
        />
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
