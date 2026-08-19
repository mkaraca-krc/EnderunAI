"use client";

import Link from "next/link";
import {
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";

import ErpShell from "@/components/erp/erp-shell";
import { Button } from "@/components/ui";
import {
  purchaseRequestService,
  type PurchaseRequestListItem,
} from "@/services/purchase-request.service";

const statusLabels: Record<number, string> = {
  0: "Taslak",
  1: "Onay Bekliyor",
  2: "Onaylandı",
  3: "Teklif Sürecinde",
  4: "Siparişe Dönüştü",
  5: "Tamamlandı",
  6: "İptal",
  7: "Reddedildi",
  8: "Düzeltmeye İade",
};

const priorityLabels: Record<number, string> = {
  0: "Düşük",
  1: "Normal",
  2: "Yüksek",
  3: "Kritik",
};

function formatDate(value?: string | null) {
  if (!value) {
    return "—";
  }

  return new Date(value).toLocaleDateString(
    "tr-TR",
  );
}

function statusClasses(status: number) {
  switch (status) {
    case 0:
      return "bg-slate-100 text-slate-700";
    case 1:
      return "bg-amber-100 text-amber-800";
    case 2:
      return "bg-blue-100 text-blue-800";
    case 5:
      return "bg-emerald-100 text-emerald-800";
    case 6:
    case 7:
      return "bg-red-100 text-red-800";
    default:
      return "bg-violet-100 text-violet-800";
  }
}

const columns: DataTableColumn<PurchaseRequestListItem>[] = [
  {
    key: "talep",
    header: "Talep",
    value: (item) =>
      `${item.requestNumber} — ${item.description || "Açıklama yok"}`,
    render: (item) => (
      <>
        <Link
          href={`/depo-stok/malzeme-talepleri/${item.id}`}
          className="font-semibold text-slate-950 hover:underline"
        >
          {item.requestNumber}
        </Link>
        <div className="mt-1 text-xs text-slate-500">
          {item.description || "Açıklama yok"}
        </div>
      </>
    ),
  },
  {
    key: "proje",
    header: "Proje",
    value: (item) => `${item.projectName} (${item.projectCode})`,
    render: (item) => (
      <>
        <div className="font-medium text-slate-800">{item.projectName}</div>
        <div className="text-xs text-slate-500">{item.projectCode}</div>
      </>
    ),
  },
  { key: "talepEden", header: "Talep Eden", value: (item) => item.requestedByName },
  {
    key: "tarih",
    header: "Tarih",
    value: (item) =>
      `${formatDate(item.requestDate)} (ihtiyaç: ${formatDate(item.neededByDate)})`,
    render: (item) => (
      <>
        <div>{formatDate(item.requestDate)}</div>
        <div className="text-xs text-slate-500">
          İhtiyaç: {formatDate(item.neededByDate)}
        </div>
      </>
    ),
  },
  {
    key: "kalem",
    header: "Kalem",
    align: "center",
    value: (item) => item.itemCount,
  },
  {
    key: "miktar",
    header: "Miktar",
    numeric: true,
    value: (item) => item.totalQuantity,
  },
  {
    key: "oncelik",
    header: "Öncelik",
    value: (item) => priorityLabels[item.priority] ?? "—",
  },
  {
    key: "durum",
    header: "Durum",
    value: (item) => statusLabels[item.status] ?? "Bilinmiyor",
    render: (item) => (
      <span
        className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${statusClasses(
          item.status
        )}`}
      >
        {statusLabels[item.status] ?? "Bilinmiyor"}
      </span>
    ),
  },
  {
    key: "detay",
    header: "",
    align: "right",
    value: () => "",
    render: (item) => (
      <Link
        href={`/depo-stok/malzeme-talepleri/${item.id}`}
        className="inline-flex rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
      >
        Detay
      </Link>
    ),
  },
];

export default function MaterialRequestsPage() {
  const [items, setItems] = useState<
    PurchaseRequestListItem[]
  >([]);

  const [loading, setLoading] =
    useState(true);

  const [error, setError] =
    useState<string | null>(null);

  const [search, setSearch] =
    useState("");

  const [status, setStatus] =
    useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const result =
        await purchaseRequestService.getAll({
          requestType: 1,
          status:
            status === ""
              ? undefined
              : Number(status),
          search:
            search.trim() || undefined,
        });

      setItems(result);
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Şantiye malzeme talepleri alınamadı.",
      );
    } finally {
      setLoading(false);
    }
  }, [search, status]);

  useEffect(() => {
    void load();
  }, [load]);

  const summary = useMemo(() => {
    return {
      total: items.length,
      draft: items.filter(
        (item) => item.status === 0,
      ).length,
      waiting: items.filter(
        (item) => item.status === 1,
      ).length,
      approved: items.filter(
        (item) => item.status === 2,
      ).length,
      completed: items.filter(
        (item) => item.status === 5,
      ).length,
    };
  }, [items]);

  return (
    <ErpShell
      design="redwood"
      title="Şantiye Malzeme Talepleri"
      description="Projelerin depo ve şantiye malzeme taleplerini yönetin."
    >
      <div className="space-y-6">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <div>
            <Link
              href="/depo-stok"
              className="text-sm font-medium text-slate-600 hover:text-slate-950"
            >
              ← Depo &amp; Stok
            </Link>

            <p className="mt-2 text-sm text-slate-500">
              Talep ve onay
              süreçlerini proje bazında izleyin.
            </p>
          </div>

          <Link
            href="/depo-stok/malzeme-talepleri/yeni"
            className="inline-flex w-fit rounded-lg bg-brand-700 px-5 py-2.5 text-sm font-medium text-white hover:bg-brand-600"
          >
            + Yeni Malzeme Talebi
          </Link>
        </div>

        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
          <SummaryCard
            label="Toplam Talep"
            value={summary.total}
          />

          <SummaryCard
            label="Taslak"
            value={summary.draft}
          />

          <SummaryCard
            label="Onay Bekliyor"
            value={summary.waiting}
          />

          <SummaryCard
            label="Onaylandı"
            value={summary.approved}
          />

          <SummaryCard
            label="Tamamlandı"
            value={summary.completed}
          />
        </div>

        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <div className="grid gap-4 lg:grid-cols-[1fr_240px_auto]">
            <input
              value={search}
              onChange={(event) =>
                setSearch(event.target.value)
              }
              onKeyDown={(event) => {
                if (event.key === "Enter") {
                  void load();
                }
              }}
              placeholder="Talep no, proje veya talep eden ara..."
              className="rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-slate-300"
            />

            <select
              value={status}
              onChange={(event) =>
                setStatus(event.target.value)
              }
              className="rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-slate-300"
            >
              <option value="">
                Tüm durumlar
              </option>
              <option value="0">Taslak</option>
              <option value="1">
                Onay Bekliyor
              </option>
              <option value="2">
                Onaylandı
              </option>
              <option value="5">
                Tamamlandı
              </option>
              <option value="6">İptal</option>
              <option value="7">
                Reddedildi
              </option>
            </select>

            <Button variant="secondary" onClick={() => void load()}>Yenile</Button>
          </div>
        </section>

        {error ? (
          <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        ) : null}

        <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="overflow-x-auto">
            <DataTable
              rows={items}
              columns={columns}
              rowKey={(item) => item.id}
              loading={loading}
              title="Şantiye Malzeme Talepleri"
              emptyText="Şantiye malzeme talebi bulunamadı."
              /* FİLTRE DEĞİŞİNCE SAYFA 1'E DÖNER. Sayfalama F4'te eklendi
                 ama bu bağ kurulmamıştı: kullanıcı 7. sayfadayken filtreyi
                 daraltınca son sayfada kalıyordu. */
              resetKey={`${search}|${status}`}
            />
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
  value: number;
}) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="text-sm text-slate-500">
        {label}
      </div>
      <div className="mt-2 text-2xl font-semibold text-slate-950">
        {value}
      </div>
    </div>
  );
}
