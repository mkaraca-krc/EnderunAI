"use client";

import Link from "next/link";
import {
  FormEvent,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { amount, money } from "@/lib/format/turkish";

import {
  inventoryService,
  type InventoryItemListItem,
} from "@/services/inventory.service";

import {
  purchaseRequestService,
  type PurchaseRequestListItem,
} from "@/services/purchase-request.service";

import {
  dashboardInventoryMovementService,
  type DashboardInventoryMovement,
} from "@/services/dashboard-inventory-movement.service";

import {
  inventoryMovementService,
  type SelectOption,
} from "@/services/inventory-movement.service";

function formatNumber(value: number): string {
  return amount(value);
}

function formatDate(value: string): string {
  return new Date(value).toLocaleDateString("tr-TR");
}

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString("tr-TR", {
    dateStyle: "short",
    timeStyle: "short",
  });
}

function typeLabel(type: number): string {
  if (type === 1) return "Sarf";
  if (type === 2) return "Demirbaş";
  return "Stok";
}

function movementLabel(type: number): string {
  const labels: Record<number, string> = {
    0: "Giriş",
    1: "Çıkış",
    2: "Transfer Çıkış",
    3: "Transfer Giriş",
  };

  return labels[type] ?? `Hareket ${type}`;
}

function movementClass(type: number): string {
  if (type === 0 || type === 3) {
    return "bg-emerald-100 text-emerald-800";
  }

  if (type === 1 || type === 2) {
    return "bg-amber-100 text-amber-800";
  }

  return "bg-slate-100 text-slate-700";
}

function requestStatusLabel(status: number): string {
  const labels: Record<number, string> = {
    0: "Taslak",
    1: "Onay Bekliyor",
    2: "Onaylandı",
    3: "Teklif Sürecinde",
    4: "Siparişe Dönüştü",
    5: "Tamamlandı",
    6: "İptal",
    7: "Reddedildi",
  };

  return labels[status] ?? "Bilinmiyor";
}

function requestStatusClass(status: number): string {
  if (status === 2 || status === 5) {
    return "bg-emerald-100 text-emerald-800";
  }

  if (status === 1) {
    return "bg-amber-100 text-amber-800";
  }

  if (status === 6 || status === 7) {
    return "bg-red-100 text-red-800";
  }

  return "bg-slate-100 text-slate-700";
}

export default function InventoryOperationsPage() {
  /**
   * Düğme -> uç -> izin:
   *   PUT inventory/items/{id} -> inventory.edit
   *
   * Asgari stok hücresi `updateMinimumStock` çağırıyor; o da kalemi
   * okuyup `updateItem` ile geri yazıyor, yani uç inventory.edit.
   */
  const actions = useModuleActions("inventory");

  const [items, setItems] = useState<InventoryItemListItem[]>([]);
  const [requests, setRequests] = useState<PurchaseRequestListItem[]>([]);
  const [movements, setMovements] =
    useState<DashboardInventoryMovement[]>([]);

  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [category, setCategory] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [criticalOnly, setCriticalOnly] = useState(false);

  const [categories, setCategories] = useState<string[]>([]);
  const [warehouses, setWarehouses] = useState<SelectOption[]>([]);

  /** Liste 20 satırda sessizce kesiliyordu; artık sayfalanıyor. */
  const [visibleCount, setVisibleCount] = useState(25);

  const [loading, setLoading] = useState(true);
  const [loadingItems, setLoadingItems] = useState(false);
  const [error, setError] = useState("");

  const loadDashboard = useCallback(async () => {
    try {
      setLoading(true);
      setError("");

      const [inventoryData, requestData, movementData, categoryData, warehouseData] =
        await Promise.all([
          /*
           * YÖNETİM EKRANI ARŞİVİ DE GÖRÜR. Uç varsayılan olarak
           * arşivlenmiş kartları gizliyor (seçiciler görmesin diye);
           * burada açıkça isteniyor ki kart geri açılabilsin.
           * Ekran zaten `item.isActive` ile ayırıyor.
           */
          inventoryService.getItems({ includeInactive: true }),
          purchaseRequestService.getAll({
            requestType: 1,
          }),
          dashboardInventoryMovementService.getAll(),
          inventoryService.getCategories().catch(() => []),
          inventoryMovementService.getWarehouses().catch(() => []),
        ]);

      setItems(inventoryData);
      setRequests(requestData);
      setMovements(movementData);
      setCategories(categoryData);
      setWarehouses(warehouseData);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Depo operasyon verileri yüklenemedi.",
      );
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadDashboard();
  }, [loadDashboard]);

  const loadItems = useCallback(
    async (
      term: string,
      selectedCategory: string,
      selectedWarehouseId: string,
      onlyCritical: boolean,
    ) => {
      try {
        setLoadingItems(true);
        setError("");

        const data = await inventoryService.getItems({
          search: term.trim() || undefined,
          category: selectedCategory || undefined,
          warehouseId: selectedWarehouseId || undefined,
          criticalOnly: onlyCritical || undefined,
          includeInactive: true,
        });

        setItems(data);
        setVisibleCount(25);
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Malzeme listesi yüklenemedi.",
        );
      } finally {
        setLoadingItems(false);
      }
    },
    [],
  );

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const term = search.trim();
    setAppliedSearch(term);

    void loadItems(term, category, warehouseId, criticalOnly);
  }

  function clearSearch() {
    setSearch("");
    setAppliedSearch("");
    setCategory("");
    setWarehouseId("");
    setCriticalOnly(false);
    void loadItems("", "", "", false);
  }

  /** Süzgeç değişince listeyi hemen tazele — ayrı "Ara" tıklaması gerekmesin. */
  function applyFilter(patch: {
    category?: string;
    warehouseId?: string;
    criticalOnly?: boolean;
  }) {
    const next = {
      category: patch.category ?? category,
      warehouseId: patch.warehouseId ?? warehouseId,
      criticalOnly: patch.criticalOnly ?? criticalOnly,
    };

    setCategory(next.category);
    setWarehouseId(next.warehouseId);
    setCriticalOnly(next.criticalOnly);

    void loadItems(
      appliedSearch,
      next.category,
      next.warehouseId,
      next.criticalOnly,
    );
  }

  const summary = useMemo(() => {
    const criticalItems = items.filter(
      (item) =>
        item.isActive &&
        item.totalStock <= item.minimumStock,
    );

    const totalStock = items.reduce(
      (sum, item) => sum + item.totalStock,
      0,
    );

    const openRequests = requests.filter(
      (request) =>
        ![5, 6, 7].includes(request.status),
    );

    const waitingApproval = requests.filter(
      (request) => request.status === 1,
    );

    const approvedRequests = requests.filter(
      (request) => request.status === 2,
    );

    const today = new Date().toISOString().slice(0, 10);

    const todayMovements = movements.filter(
      (movement) =>
        new Date(movement.movementDate)
          .toISOString()
          .slice(0, 10) === today,
    );

    const todayIssues = todayMovements
      .filter(
        (movement) =>
          movement.type === 1 ||
          movement.type === 2,
      )
      .reduce(
        (sum, movement) =>
          sum + movement.quantity,
        0,
      );

    return {
      totalItems: items.length,
      activeItems: items.filter(
        (item) => item.isActive,
      ).length,
      criticalItems,
      totalStock,
      openRequests,
      waitingApproval,
      approvedRequests,
      todayMovements,
      todayIssues,
    };
  }, [items, movements, requests]);

  const recentMovements = useMemo(
    () =>
      [...movements]
        .sort(
          (a, b) =>
            new Date(b.movementDate).getTime() -
            new Date(a.movementDate).getTime(),
        )
        .slice(0, 8),
    [movements],
  );

  const recentRequests = useMemo(
    () =>
      [...requests]
        .filter(
          (request) =>
            ![5, 6, 7].includes(request.status),
        )
        .sort(
          (a, b) =>
            new Date(b.requestDate).getTime() -
            new Date(a.requestDate).getTime(),
        )
        .slice(0, 6),
    [requests],
  );

  const criticalItems = useMemo(
    () =>
      [...summary.criticalItems]
        .sort(
          (a, b) =>
            a.totalStock -
            b.totalStock,
        )
        .slice(0, 8),
    [summary.criticalItems],
  );

  return (
    <ErpShell
      design="redwood"
      title="Depo & Stok"
      description="Malzeme taleplerini, stok seviyelerini ve depo hareketlerini tek merkezden yönetin."
    >
      <div className="space-y-6">
        {error ? (
          <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        ) : null}

        <section className="rounded-2xl border border-slate-200 bg-brand-950 p-6 text-white shadow-sm">
          <div className="flex flex-col gap-6 xl:flex-row xl:items-center xl:justify-between">
            <div>
              <p className="text-sm font-medium text-slate-400">
                Operasyon Merkezi
              </p>

              <h2 className="mt-2 text-2xl font-semibold">
                Depo operasyonlarını yönetin
              </h2>

              <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-300">
                Şantiye talepleri,
                mal kabul, depo çıkışı ve stok
                hareketlerini aynı merkezden takip edin.
              </p>
            </div>

            <div className="flex flex-wrap gap-3">
              <Link
                href="/depo-stok/malzeme-talepleri/yeni"
                className="inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5 text-sm font-semibold text-slate-950 hover:bg-slate-100"
              >
                + Yeni Malzeme Talebi
              </Link>

              <Link
                href="/depo-stok/mal-kabul"
                className="inline-flex items-center justify-center rounded-lg border border-slate-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-brand-600"
              >
                Mal Kabul
              </Link>

              <button
                type="button"
                onClick={() => void loadDashboard()}
                disabled={loading}
                className="inline-flex items-center justify-center rounded-lg border border-slate-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50"
              >
                {loading ? "Yenileniyor..." : "Verileri Yenile"}
              </button>
            </div>
          </div>
        </section>

        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            label="Açık Malzeme Talebi"
            value={
              loading
                ? "…"
                : summary.openRequests.length
            }
            description={`${summary.waitingApproval.length} onay bekliyor`}
            href="/depo-stok/malzeme-talepleri"
          />

          <StatCard
            label="Kritik Stok"
            value={
              loading
                ? "…"
                : summary.criticalItems.length
            }
            description="Minimum seviyenin altında"
            warning={summary.criticalItems.length > 0}
          />

          <StatCard
            label="Bugünkü Çıkış"
            value={
              loading
                ? "…"
                : formatNumber(summary.todayIssues)
            }
            description={`${summary.todayMovements.length} hareket kaydı`}
            href="/depo-stok/hareketler"
          />
        </div>

        <section>
          <div className="mb-4 flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold text-slate-950">
                Hızlı İşlemler
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Günlük depo işlemlerine hızlı erişim.
              </p>
            </div>
          </div>

          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
            <QuickAction
              title="Malzeme Talepleri"
              description="Talep ve onay akışı"
              href="/depo-stok/malzeme-talepleri"
              icon="○"
            />

            <QuickAction
              title="Mal Kabul"
              description="Sipariş teslimatı ve stok girişi"
              href="/depo-stok/mal-kabul"
              icon="↓"
            />

            <QuickAction
              title="Depo Çıkışı"
              description="Manuel stok çıkış işlemi"
              href="/depo-stok/cikis"
              icon="↑"
            />

            <QuickAction
              title="Depo Transferi"
              description="Depolar arasında malzeme aktarımı"
              href="/depo-stok/transfer"
              icon="⇄"
            />

            <QuickAction
              title="Yeni Malzeme"
              description="Yeni stok veya demirbaş kartı"
              href="/depo-stok/yeni"
              icon="+"
            />
          </div>
        </section>

        <div className="grid gap-6 xl:grid-cols-2">
          <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
            <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
              <div>
                <h2 className="text-lg font-semibold text-slate-950">
                  Açık Malzeme Talepleri
                </h2>

                <p className="mt-1 text-sm text-slate-500">
                  İşlem bekleyen son şantiye talepleri.
                </p>
              </div>

              <Link
                href="/depo-stok/malzeme-talepleri"
                className="text-sm font-medium text-slate-600 hover:text-slate-950"
              >
                Tümünü Gör
              </Link>
            </div>

            <div className="divide-y divide-slate-100">
              {loading ? (
                <LoadingRow text="Talepler yükleniyor..." />
              ) : recentRequests.length === 0 ? (
                <EmptyRow text="Açık malzeme talebi bulunmuyor." />
              ) : (
                recentRequests.map((request) => (
                  <Link
                    key={request.id}
                    href={`/depo-stok/malzeme-talepleri/${request.id}`}
                    className="flex items-center justify-between gap-4 px-5 py-4 hover:bg-slate-50"
                  >
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <strong className="text-sm text-slate-950">
                          {request.requestNumber}
                        </strong>

                        <span
                          className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${requestStatusClass(
                            request.status,
                          )}`}
                        >
                          {requestStatusLabel(
                            request.status,
                          )}
                        </span>
                      </div>

                      <p className="mt-1 truncate text-sm text-slate-600">
                        {request.projectCode} ·{" "}
                        {request.projectName}
                      </p>

                      <p className="mt-1 text-xs text-slate-500">
                        {request.requestedByName} ·{" "}
                        {formatDate(
                          request.requestDate,
                        )}
                      </p>
                    </div>

                    <div className="shrink-0 text-right">
                      <strong className="block text-sm text-slate-900">
                        {request.itemCount} kalem
                      </strong>

                      <span className="mt-1 block text-xs text-slate-500">
                        {formatNumber(
                          request.totalQuantity,
                        )}{" "}
                        miktar
                      </span>
                    </div>
                  </Link>
                ))
              )}
            </div>
          </section>

          <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
            <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
              <div>
                <h2 className="text-lg font-semibold text-slate-950">
                  Kritik Malzemeler
                </h2>

                <p className="mt-1 text-sm text-slate-500">
                  Kullanılabilir stok seviyesi minimumun altında olan kartlar.
                </p>
              </div>

              <span className="rounded-full bg-amber-100 px-2.5 py-1 text-xs font-medium text-amber-800">
                {summary.criticalItems.length} kayıt
              </span>
            </div>

            <div className="divide-y divide-slate-100">
              {loading ? (
                <LoadingRow text="Kritik stoklar yükleniyor..." />
              ) : criticalItems.length === 0 ? (
                <EmptyRow text="Kritik seviyede malzeme bulunmuyor." />
              ) : (
                criticalItems.map((item) => (
                  <div
                    key={item.id}
                    className="flex items-center justify-between gap-4 px-5 py-4"
                  >
                    <div className="min-w-0">
                      <strong className="block truncate text-sm text-slate-950">
                        {item.name}
                      </strong>

                      <span className="mt-1 block text-xs text-slate-500">
                        {item.code} ·{" "}
                        {item.category || typeLabel(item.type)}
                      </span>
                    </div>

                    <div className="shrink-0 text-right">
                      <strong className="block text-sm text-red-700">
                        {formatNumber(
                          item.totalStock,
                        )}{" "}
                        {item.unit}
                      </strong>

                      <span className="mt-1 block text-xs text-slate-500">
                        Minimum:{" "}
                        {formatNumber(
                          item.minimumStock,
                        )}
                      </span>
                    </div>
                  </div>
                ))
              )}
            </div>
          </section>
        </div>

        <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="flex flex-col gap-3 border-b border-slate-200 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <h2 className="text-lg font-semibold text-slate-950">
                Son Stok Hareketleri
              </h2>

              <p className="mt-1 text-sm text-slate-500">
                En son gerçekleşen depo giriş, çıkış ve transfer işlemleri.
              </p>
            </div>

            <Link
              href="/depo-stok/hareketler"
              className="text-sm font-medium text-slate-600 hover:text-slate-950"
            >
              Tüm Hareketleri Gör
            </Link>
          </div>

          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-slate-200 text-sm">
              <thead className="bg-slate-50">
                <tr>
                  <TableHead>Tarih</TableHead>
                  <TableHead>Hareket</TableHead>
                  <TableHead>Malzeme</TableHead>
                  <TableHead>Depo</TableHead>
                  <TableHead>Proje</TableHead>
                  <TableHead right>Miktar</TableHead>
                  <TableHead>Referans</TableHead>
                </tr>
              </thead>

              <tbody className="divide-y divide-slate-100">
                {loading ? (
                  <tr>
                    <td
                      colSpan={7}
                      className="px-4 py-10 text-center text-slate-500"
                    >
                      Stok hareketleri yükleniyor...
                    </td>
                  </tr>
                ) : recentMovements.length === 0 ? (
                  <tr>
                    <td
                      colSpan={7}
                      className="px-4 py-10 text-center text-slate-500"
                    >
                      Henüz stok hareketi bulunmuyor.
                    </td>
                  </tr>
                ) : (
                  recentMovements.map((movement) => (
                    <tr
                      key={movement.id}
                      className="hover:bg-slate-50"
                    >
                      <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                        {formatDateTime(
                          movement.movementDate,
                        )}
                      </td>

                      <td className="px-4 py-3">
                        <span
                          className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${movementClass(
                            movement.type,
                          )}`}
                        >
                          {movementLabel(
                            movement.type,
                          )}
                        </span>
                      </td>

                      <td className="px-4 py-3">
                        <strong className="block text-slate-900">
                          {movement.itemName}
                        </strong>

                        <span className="mt-1 block text-xs text-slate-500">
                          {movement.itemCode}
                        </span>
                      </td>

                      <td className="px-4 py-3 text-slate-700">
                        {movement.warehouseName}
                      </td>

                      <td className="px-4 py-3 text-slate-700">
                        {movement.projectName || "—"}
                      </td>

                      <td className="whitespace-nowrap px-4 py-3 text-right font-medium text-slate-900">
                        {formatNumber(
                          movement.quantity,
                        )}
                      </td>

                      <td className="px-4 py-3 text-slate-600">
                        {movement.referenceNumber}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="flex flex-col gap-3 border-b border-slate-200 px-5 py-4 lg:flex-row lg:items-center lg:justify-between">
            <div>
              <h2 className="text-lg font-semibold text-slate-950">
                Malzeme Kartları
              </h2>

              <p className="mt-1 text-sm text-slate-500">
                Stok kartlarını kod, malzeme, marka veya model ile arayın.
              </p>
            </div>

            <div className="flex gap-2">
              <Link
                href="/depo-stok/yeni"
                className="inline-flex items-center justify-center rounded-lg bg-brand-700 px-4 py-2 text-sm font-medium text-white hover:bg-brand-600"
              >
                + Yeni Malzeme
              </Link>
            </div>
          </div>

          <div className="border-b border-slate-200 p-4">
            <form
              onSubmit={submitSearch}
              className="flex flex-col gap-3 sm:flex-row"
            >
              <input
                value={search}
                onChange={(event) =>
                  setSearch(event.target.value)
                }
                placeholder="Kod, malzeme, marka veya model ara"
                className="min-w-0 flex-1 rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-slate-300"
              />

              <button
                type="submit"
                disabled={loadingItems}
                className="rounded-lg border border-slate-300 bg-white px-5 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
              >
                {loadingItems ? "Aranıyor..." : "Ara"}
              </button>

              {appliedSearch || category || warehouseId || criticalOnly ? (
                <button
                  type="button"
                  onClick={clearSearch}
                  className="rounded-lg border border-slate-300 bg-white px-5 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
                >
                  Temizle
                </button>
              ) : null}
            </form>

            <div className="mt-3 flex flex-col gap-3 sm:flex-row sm:items-center">
              <select
                value={category}
                onChange={(event) =>
                  applyFilter({ category: event.target.value })
                }
                className="rounded-lg border border-slate-300 px-3 py-2 text-sm"
              >
                <option value="">Tüm kategoriler</option>
                {categories.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>

              <select
                value={warehouseId}
                onChange={(event) =>
                  applyFilter({ warehouseId: event.target.value })
                }
                className="rounded-lg border border-slate-300 px-3 py-2 text-sm"
              >
                <option value="">Tüm depolar</option>
                {warehouses.map((warehouse) => (
                  <option key={warehouse.id} value={warehouse.id}>
                    {warehouse.code ? `${warehouse.code} — ` : ""}
                    {warehouse.name}
                  </option>
                ))}
              </select>

              <label className="flex items-center gap-2 text-sm text-slate-700">
                <input
                  type="checkbox"
                  checked={criticalOnly}
                  onChange={(event) =>
                    applyFilter({ criticalOnly: event.target.checked })
                  }
                />
                Yalnızca kritik seviyedekiler
              </label>
            </div>
          </div>

          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-slate-200 text-sm">
              <thead className="bg-slate-50">
                <tr>
                  <TableHead>Kod</TableHead>
                  <TableHead>Malzeme</TableHead>
                  <TableHead>Kategori</TableHead>
                  <TableHead>Tip</TableHead>
                  <TableHead right>
                    Toplam Stok
                  </TableHead>
                  <TableHead right>
                    Kullanılabilir
                  </TableHead>
                  <TableHead right>
                    Min. Stok
                  </TableHead>
                  <TableHead right>
                    Stok Değeri
                  </TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead>{""}</TableHead>
                </tr>
              </thead>

              <tbody className="divide-y divide-slate-100">
                {loadingItems ? (
                  <tr>
                    <td
                      colSpan={10}
                      className="px-4 py-10 text-center text-slate-500"
                    >
                      Malzeme kartları yükleniyor...
                    </td>
                  </tr>
                ) : items.length === 0 ? (
                  <tr>
                    <td
                      colSpan={10}
                      className="px-4 py-10 text-center text-slate-500"
                    >
                      Kayıt bulunamadı.
                    </td>
                  </tr>
                ) : (
                  items.slice(0, visibleCount).map((item) => {
                    // Minimum stok tanımlanmamışsa (0) kritik sayılmaz;
                    // aksi halde stoğu biten her kalem kırmızı görünürdü.
                    const critical =
                      item.minimumStock > 0 &&
                      item.totalStock <= item.minimumStock;

                    return (
                      <tr
                        key={item.id}
                        className="hover:bg-slate-50"
                      >
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
                              .join(" · ") ||
                              "Marka/model belirtilmedi"}
                          </div>
                        </td>

                        <td className="px-4 py-3 text-slate-600">
                          {item.category || "—"}
                        </td>

                        <td className="px-4 py-3 text-slate-600">
                          {typeLabel(item.type)}
                        </td>

                        <td className="whitespace-nowrap px-4 py-3 text-right font-medium text-slate-900">
                          {formatNumber(
                            item.totalStock,
                          )}{" "}
                          {item.unit}
                        </td>

                        <td className="whitespace-nowrap px-4 py-3 text-right text-slate-700">
                          {formatNumber(
                            item.totalStock,
                          )}{" "}
                          {item.unit}
                        </td>

                        <td className="whitespace-nowrap px-4 py-3 text-right">
                          <MinimumStockCell
                            item={item}
                            onSaved={loadDashboard}
                            canEdit={actions.can("edit")}
                          />
                        </td>

                        <td className="whitespace-nowrap px-4 py-3 text-right text-slate-700">
                          {money(item.stockValue)}
                          <div className="text-xs text-slate-500">
                            birim {money(item.averageUnitCost)}
                          </div>
                        </td>

                        <td className="px-4 py-3">
                          <span
                            className={
                              critical
                                ? "inline-flex rounded-full bg-amber-100 px-2.5 py-1 text-xs font-medium text-amber-800"
                                : "inline-flex rounded-full bg-emerald-100 px-2.5 py-1 text-xs font-medium text-emerald-800"
                            }
                          >
                            {critical
                              ? "Kritik"
                              : "Normal"}
                          </span>
                        </td>

                        <td className="whitespace-nowrap px-4 py-3 text-right">
                          <Link
                            href={`/depo-stok/malzeme/${item.id}`}
                            className="inline-flex rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
                          >
                            Kartı Aç
                          </Link>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>

          {items.length > 0 ? (
            <div className="flex flex-col items-center gap-2 border-t border-slate-200 px-5 py-3 text-sm text-slate-500 sm:flex-row sm:justify-between">
              <span>
                {Math.min(visibleCount, items.length)} / {items.length} malzeme
                {" · "}
                Toplam stok değeri: {money(
                  items.reduce((sum, item) => sum + item.stockValue, 0),
                )}
              </span>

              {items.length > visibleCount ? (
                <button
                  type="button"
                  onClick={() => setVisibleCount((current) => current + 25)}
                  className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
                >
                  Daha fazla göster
                </button>
              ) : null}
            </div>
          ) : null}
        </section>
      </div>
    </ErpShell>
  );
}

function StatCard({
  label,
  value,
  description,
  warning = false,
  href,
}: {
  label: string;
  value: string | number;
  description: string;
  warning?: boolean;
  href?: string;
}) {
  const content = (
    <div
      className={
        warning
          ? "h-full rounded-xl border border-amber-200 bg-amber-50 p-5 shadow-sm transition hover:border-amber-300"
          : "h-full rounded-xl border border-slate-200 bg-white p-5 shadow-sm transition hover:border-slate-300"
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
            ? "mt-2 text-3xl font-semibold text-amber-900"
            : "mt-2 text-3xl font-semibold text-slate-950"
        }
      >
        {value}
      </p>

      <p
        className={
          warning
            ? "mt-2 text-xs text-amber-700"
            : "mt-2 text-xs text-slate-500"
        }
      >
        {description}
      </p>
    </div>
  );

  return href ? (
    <Link href={href}>{content}</Link>
  ) : (
    content
  );
}

function QuickAction({
  title,
  description,
  href,
  icon,
}: {
  title: string;
  description: string;
  href: string;
  icon: string;
}) {
  return (
    <Link
      href={href}
      className="group rounded-xl border border-slate-200 bg-white p-5 shadow-sm transition hover:border-slate-400 hover:shadow-md"
    >
      <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-slate-100 text-xl font-semibold text-slate-700 transition group-hover:bg-slate-950 group-hover:text-white">
        {icon}
      </div>

      <h3 className="mt-4 font-semibold text-slate-950">
        {title}
      </h3>

      <p className="mt-1 text-sm leading-5 text-slate-500">
        {description}
      </p>
    </Link>
  );
}

function LoadingRow({
  text,
}: {
  text: string;
}) {
  return (
    <div className="px-5 py-12 text-center text-sm text-slate-500">
      {text}
    </div>
  );
}

function EmptyRow({
  text,
}: {
  text: string;
}) {
  return (
    <div className="px-5 py-12 text-center text-sm text-slate-500">
      {text}
    </div>
  );
}

function MinimumStockCell({
  item,
  onSaved,
  canEdit,
}: {
  item: InventoryItemListItem;
  onSaved: () => void;
  /**
   * İzin PROP olarak geliyor: bu hücre SATIR BAŞINA render ediliyor.
   */
  canEdit: boolean;
}) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(item.minimumStock);
  const [saving, setSaving] = useState(false);

  if (!editing) {
    /*
     * YETKİSİZ KULLANICIDA DEĞER GİZLENMEZ. Bu düğme aynı zamanda
     * kritik stok seviyesini GÖSTERİYOR; gizlemek okuma yetkisi olan
     * kullanıcıdan veriyi de saklardı. Sadece düzenleme girişi kapanıyor
     * (piyasa ekranındaki tonaj hücresiyle aynı gerekçe).
     */
    if (!canEdit) {
      return (
        <span className="text-slate-700">
          {formatNumber(item.minimumStock)} {item.unit}
        </span>
      );
    }

    return (
      <button
        type="button"
        onClick={() => {
          setValue(item.minimumStock);
          setEditing(true);
        }}
        className="text-slate-700 underline decoration-dotted hover:text-slate-950"
        title="Kritik stok seviyesini düzenle"
      >
        {formatNumber(item.minimumStock)} {item.unit}
      </button>
    );
  }

  return (
    <div className="flex items-center justify-end gap-1">
      <input
        type="number"
        min="0"
        step="0.01"
        value={value}
        onChange={(event) => setValue(Number(event.target.value))}
        className="w-20 rounded border border-slate-300 px-2 py-1 text-right text-sm"
        autoFocus
      />
      <button
        type="button"
        disabled={saving}
        onClick={async () => {
          setSaving(true);
          try {
            await inventoryService.updateMinimumStock(item, value);
            onSaved();
            setEditing(false);
          } catch {
            // sessizce bırak, kullanıcı tekrar deneyebilir
          } finally {
            setSaving(false);
          }
        }}
        className="rounded bg-brand-700 px-2 py-1 text-xs text-white disabled:opacity-50"
      >
        ✓
      </button>
      <button
        type="button"
        onClick={() => setEditing(false)}
        className="rounded border border-slate-300 px-2 py-1 text-xs"
      >
        ✕
      </button>
    </div>
  );
}

function TableHead({
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
