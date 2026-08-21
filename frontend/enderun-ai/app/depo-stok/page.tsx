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
  type InventoryCategory,
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

import {
  stockLevelService,
  type StockLevelRow,
} from "@/services/stock-level.service";

import { projectService, type ProjectListItem } from "@/services/project.service";
import { DataTable, type DataTableColumn } from "@/components/ui/data-table";

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
   * Asgari seviye bu ekranda düzenlenmiyor; tanımı
   * /depo-stok/stok-seviyeleri ekranında (POST api/stock-levels ->
   * inventory.edit).
   */
  const actions = useModuleActions("inventory");

  const [items, setItems] = useState<InventoryItemListItem[]>([]);
  const [requests, setRequests] = useState<PurchaseRequestListItem[]>([]);
  const [movements, setMovements] =
    useState<DashboardInventoryMovement[]>([]);

  /*
   * KRİTİK LİSTESİNİN KAYNAĞI DEPO SEVİYESİ (S8).
   *
   * Eskiden kart üzerindeki tek `minimumStock` ile toplam stok
   * karşılaştırılıyordu; şantiye deposunda duran mal merkezin eksiğini
   * kapatıyormuş gibi görünüyordu. Artık uyarı hangi DEPODA eksik
   * olduğunu söylüyor.
   */
  const [criticalLevels, setCriticalLevels] = useState<StockLevelRow[]>([]);

  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [category, setCategory] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [criticalOnly, setCriticalOnly] = useState(false);

  /*
   * PROJE SÜZGECİ (S9): "bu iş için hangi kartlar açıldı". Özel imalat
   * ve dekoratif ürünler projeye bağlı doğuyor; katalog kalemleri
   * bağsız kalıyor.
   */
  const [projectId, setProjectId] = useState("");
  const [projects, setProjects] = useState<ProjectListItem[]>([]);

  /*
   * Kategori artık kendi varlığı (S1): ad, tip ve özellik şablonu
   * taşıyor. Bu ekrandaki süzgeç yalnız ADI kullanıyor; kart açma
   * ekranı şablonun tamamını kullanacak (S2).
   */
  const [categories, setCategories] = useState<InventoryCategory[]>([]);
  const [warehouses, setWarehouses] = useState<SelectOption[]>([]);

  /** Liste 20 satırda sessizce kesiliyordu; artık sayfalanıyor. */

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

      setProjects(await projectService.getAll().catch(() => []));

      const levelData = await stockLevelService
        .list({ belowMinimumOnly: true })
        .catch(() => [] as StockLevelRow[]);

      setCriticalLevels(levelData);

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
      selectedProjectId: string,
    ) => {
      try {
        setLoadingItems(true);
        setError("");

        const data = await inventoryService.getItems({
          search: term.trim() || undefined,
          category: selectedCategory || undefined,
          warehouseId: selectedWarehouseId || undefined,
          criticalOnly: onlyCritical || undefined,
          projectId: selectedProjectId || undefined,
          includeInactive: true,
        });

        setItems(data);
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

    void loadItems(term, category, warehouseId, criticalOnly, projectId);
  }

  function clearSearch() {
    setSearch("");
    setAppliedSearch("");
    setCategory("");
    setWarehouseId("");
    setCriticalOnly(false);
    void loadItems("", "", "", false, "");
  }

  /** Süzgeç değişince listeyi hemen tazele — ayrı "Ara" tıklaması gerekmesin. */
  function applyFilter(patch: {
    category?: string;
    warehouseId?: string;
    criticalOnly?: boolean;
    projectId?: string;
  }) {
    const next = {
      category: patch.category ?? category,
      warehouseId: patch.warehouseId ?? warehouseId,
      criticalOnly: patch.criticalOnly ?? criticalOnly,
      projectId: patch.projectId ?? projectId,
    };

    setCategory(next.category);
    setWarehouseId(next.warehouseId);
    setCriticalOnly(next.criticalOnly);
    setProjectId(next.projectId);

    void loadItems(
      appliedSearch,
      next.category,
      next.warehouseId,
      next.criticalOnly,
      next.projectId,
    );
  }

  const summary = useMemo(() => {
    // Kritik kalem = herhangi bir deposunda asgarinin altında olan
    // AKTİF kart. Arşivlenmiş kartın seviyesi uyarı üretmemeli.
    const activeIds = new Set(
      items.filter((item) => item.isActive).map((item) => item.id),
    );

    const criticalItems = criticalLevels.filter((level) =>
      activeIds.has(level.inventoryItemId),
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
  }, [criticalLevels, items, movements, requests]);

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

  // Önce tamamen tükenenler, sonra asgariye en yakın olanlar.
  const criticalItems = useMemo(
    () =>
      [...summary.criticalItems]
        .sort((a, b) => {
          if (a.isDepleted !== b.isDepleted) return a.isDepleted ? -1 : 1;
          return a.currentQuantity - b.currentQuantity;
        })
        .slice(0, 8),
    [summary.criticalItems],
  );

  /** Kart listesindeki durum rozeti bu kümeden okunuyor. */
  const criticalItemIds = useMemo(
    () => new Set(criticalLevels.map((level) => level.inventoryItemId)),
    [criticalLevels],
  );

  /*
   * SÜTUNLAR VERİ OLARAK (F4f). Eylem ve rozet taşıyan sütunlarda
   * `value` ayrı veriliyor: dışa aktarmada "Kartı Aç" düğmesi değil
   * kalemin kendisi yazmalı.
   *
   * BELLEĞE ALINMIYOR (F4b'deki desen kararı): sütunlar `criticalItemIds`
   * ve `criticalLevels` üzerine kapanıyor; bağımlılıktan çıkarmak bayat
   * kapanış demek olurdu.
   */
  const movementColumns: DataTableColumn<DashboardInventoryMovement>[] = [
    {
      key: "tarih",
      header: "Tarih",
      value: (row) => formatDateTime(row.movementDate),
    },
    {
      key: "hareket",
      header: "Hareket",
      value: (row) => movementLabel(row.type),
      render: (row) => (
        <span
          className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${movementClass(
            row.type,
          )}`}
        >
          {movementLabel(row.type)}
        </span>
      ),
    },
    {
      key: "malzeme",
      header: "Malzeme",
      value: (row) => `${row.itemCode} — ${row.itemName}`,
      render: (row) => (
        <>
          <strong className="block text-slate-900">{row.itemName}</strong>
          <span className="mt-1 block text-xs text-slate-500">{row.itemCode}</span>
        </>
      ),
    },
    { key: "depo", header: "Depo", value: (row) => row.warehouseName },
    { key: "proje", header: "Proje", value: (row) => row.projectName || "—" },
    {
      key: "miktar",
      header: "Miktar",
      numeric: true,
      value: (row) => formatNumber(row.quantity),
    },
    { key: "referans", header: "Referans", value: (row) => row.referenceNumber },
  ];

  const itemColumns: DataTableColumn<InventoryItemListItem>[] = [
    { key: "kod", header: "Kod", value: (row) => row.code },
    {
      key: "malzeme",
      header: "Malzeme",
      value: (row) =>
        [row.name, row.brand, row.model].filter(Boolean).join(" · "),
      render: (row) => (
        <div className="flex items-center gap-3">
          {row.coverPhotoId && (
            /* eslint-disable-next-line @next/next/no-img-element */
            <img
              src={inventoryService.photoUrl(row.coverPhotoId)}
              alt=""
              className="h-10 w-10 shrink-0 rounded object-cover"
            />
          )}

          <div className="min-w-0">
            <div className="font-medium text-slate-900">{row.name}</div>

            <div className="text-xs text-slate-500">
              {[row.brand, row.model].filter(Boolean).join(" · ") ||
                "Marka/model belirtilmedi"}
            </div>

            {row.projectName && (
              <div className="text-xs text-amber-700">
                {row.projectName} projesine bağlı
              </div>
            )}
          </div>
        </div>
      ),
    },
    { key: "kategori", header: "Kategori", value: (row) => row.category || "—" },
    { key: "tip", header: "Tip", value: (row) => typeLabel(row.type) },
    {
      key: "stok",
      header: "Toplam Stok",
      numeric: true,
      value: (row) => `${formatNumber(row.totalStock)} ${row.unit}`,
    },
    {
      key: "asgari",
      header: "Asgari Takip",
      numeric: true,
      value: (row) => {
        const levels = criticalLevels.filter(
          (level) => level.inventoryItemId === row.id,
        );

        return levels.length === 0
          ? "—"
          : levels
              .map(
                (level) =>
                  `${level.warehouseCode || level.warehouseName}: ` +
                  `${formatNumber(level.currentQuantity)} / ` +
                  `${formatNumber(level.minimumQuantity)}`,
              )
              .join(" · ");
      },
      render: (row) => {
        const levels = criticalLevels.filter(
          (level) => level.inventoryItemId === row.id,
        );

        if (levels.length === 0)
          return <span className="text-xs text-slate-400">—</span>;

        return (
          <>
            {levels.map((level) => (
              <div key={level.id} className="text-xs">
                <span className="text-slate-500">
                  {level.warehouseCode || level.warehouseName}:
                </span>{" "}
                <span className="font-medium text-amber-700">
                  {formatNumber(level.currentQuantity)}
                </span>
                {" / "}
                {formatNumber(level.minimumQuantity)}
              </div>
            ))}
          </>
        );
      },
    },
    {
      key: "deger",
      header: "Stok Değeri",
      numeric: true,
      value: (row) => money(row.stockValue),
      render: (row) => (
        <>
          {money(row.stockValue)}
          <div className="text-xs text-slate-500">
            birim {money(row.averageUnitCost)}
          </div>
        </>
      ),
      // TÜM satırlar üzerinden: görünen sayfanın toplamı yanıltırdı.
      footer: (rows) => money(rows.reduce((sum, row) => sum + row.stockValue, 0)),
    },
    {
      key: "durum",
      header: "Durum",
      value: (row) => (criticalItemIds.has(row.id) ? "Kritik" : "Normal"),
      render: (row) => (
        <span
          className={
            criticalItemIds.has(row.id)
              ? "inline-flex rounded-full bg-amber-100 px-2.5 py-1 text-xs font-medium text-amber-800"
              : "inline-flex rounded-full bg-emerald-100 px-2.5 py-1 text-xs font-medium text-emerald-800"
          }
        >
          {criticalItemIds.has(row.id) ? "Kritik" : "Normal"}
        </span>
      ),
    },
    {
      key: "ac",
      header: "",
      value: () => "",
      render: (row) => (
        <Link
          href={`/depo-stok/malzeme/${row.id}`}
          className="inline-flex rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
        >
          Kartı Aç
        </Link>
      ),
    },
  ];

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
                  Deposundaki miktar asgari seviyeye inmiş kalemler.
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
                criticalItems.map((level) => (
                  <div
                    key={level.id}
                    className="flex items-center justify-between gap-4 px-5 py-4"
                  >
                    <div className="min-w-0">
                      <strong className="block truncate text-sm text-slate-950">
                        {level.itemName}
                      </strong>

                      <span className="mt-1 block text-xs text-slate-500">
                        {level.itemCode} · {level.warehouseName}
                      </span>
                    </div>

                    <div className="shrink-0 text-right">
                      <strong className="block text-sm text-red-700">
                        {formatNumber(level.currentQuantity)} {level.unit}
                      </strong>

                      <span className="mt-1 block text-xs text-slate-500">
                        Asgari: {formatNumber(level.minimumQuantity)}
                        {level.suggestedQuantity != null
                          ? ` · öneri ${formatNumber(level.suggestedQuantity)}`
                          : " · azami tanımsız"}
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

          {loading ? (
            <p className="px-5 py-10 text-center text-slate-500">
              Stok hareketleri yükleniyor...
            </p>
          ) : recentMovements.length === 0 ? (
            <p className="px-5 py-10 text-center text-slate-500">
              Henüz stok hareketi bulunmuyor.
            </p>
          ) : (
            <DataTable
              rows={recentMovements}
              columns={movementColumns}
              rowKey={(row) => row.id}
              title="Son Stok Hareketleri"
            />
          )}
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
                {categories.map((category) => (
                  <option key={category.id} value={category.name}>
                    {category.name}
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

              <select
                value={projectId}
                onChange={(event) =>
                  applyFilter({ projectId: event.target.value })
                }
                className="rounded-lg border border-slate-300 px-3 py-2 text-sm"
              >
                <option value="">Tüm projeler</option>
                {projects.map((project) => (
                  <option key={project.id} value={project.id}>
                    {project.name}
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

          {loadingItems ? (
            <p className="px-5 py-10 text-center text-slate-500">
              Malzeme kartları yükleniyor...
            </p>
          ) : items.length === 0 ? (
            <p className="px-5 py-10 text-center text-slate-500">
              Kayıt bulunamadı.
            </p>
          ) : (
            /*
             * ELLE YAZILMIŞ "Daha fazla göster" SAYFALAMASI KALDIRILDI
             * (F4f). Bileşen gerçek sayfalama, dışa aktarma ve yazdırma
             * getiriyor. Toplam stok değeri artık ALT TOPLAM sütununda:
             * istemci kipinde bileşen TÜM satırları geçiriyor, yani
             * "Toplam" etiketi görünen sayfayı değil listenin tamamını
             * topluyor — bu programın baştan beri kovaladığı hata.
             */
            <DataTable
              rows={items}
              columns={itemColumns}
              rowKey={(row) => row.id}
              title="Malzeme Kartları"
              resetKey={`${appliedSearch}|${category}|${warehouseId}|${projectId}|${criticalOnly}`}
            />
          )}
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

/*
 * `MinimumStockCell` KALDIRILDI (S8).
 *
 * Kart üzerindeki tek asgari değeri satır içinde düzenliyordu. Asgari
 * artık depo bazında (`warehouse_stock_levels`) ve bir kartın birden
 * çok deposu olabildiği için tek hücreye sığmıyor. Tanım ekranı:
 * /depo-stok/stok-seviyeleri
 */

/*
 * `TableHead` KALDIRILDI (F4f): tablo başlıkları artık `DataTable`
 * sütun tanımlarından geliyor.
 */
