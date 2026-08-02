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

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  inventoryMovementService,
  type SelectOption,
} from "@/services/inventory-movement.service";

import {
  stockReservationService,
  type StockReservationManagementListItem,
} from "@/services/stock-reservation.service";

const statusLabels: Record<number, string> = {
  0: "Aktif",
  1: "Kısmi Çıkış",
  2: "Tamamlandı",
  3: "Serbest Bırakıldı",
  4: "İptal",
};

function formatNumber(
  value?: number | null,
): string {
  return new Intl.NumberFormat("tr-TR", {
    maximumFractionDigits: 4,
  }).format(value ?? 0);
}

function formatDate(
  value?: string | null,
): string {
  if (!value) {
    return "—";
  }

  return new Date(value).toLocaleDateString(
    "tr-TR",
  );
}

function statusClass(
  item: StockReservationManagementListItem,
): string {
  if (item.isExpired) {
    return "bg-red-100 text-red-800";
  }

  if (item.status === 0) {
    return "bg-blue-100 text-blue-800";
  }

  if (item.status === 1) {
    return "bg-amber-100 text-amber-800";
  }

  if (item.status === 2) {
    return "bg-emerald-100 text-emerald-800";
  }

  if (item.status === 4) {
    return "bg-red-100 text-red-800";
  }

  return "bg-slate-100 text-slate-700";
}

export default function StockReservationsPage() {
  const [items, setItems] = useState<
    StockReservationManagementListItem[]
  >([]);

  const [companies, setCompanies] = useState<
    CompanyListItem[]
  >([]);

  const [projects, setProjects] = useState<
    ProjectListItem[]
  >([]);

  const [warehouses, setWarehouses] = useState<
    SelectOption[]
  >([]);

  const [search, setSearch] = useState("");
  const [companyId, setCompanyId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [warehouseId, setWarehouseId] =
    useState("");
  const [status, setStatus] = useState("");
  const [activeOnly, setActiveOnly] =
    useState(false);
  const [expiredOnly, setExpiredOnly] =
    useState(false);

  const [loading, setLoading] =
    useState(true);

  const [error, setError] =
    useState("");

  const filteredProjects = useMemo(
    () =>
      projects.filter(
        (project) =>
          !companyId ||
          project.companyId === companyId,
      ),
    [companyId, projects],
  );

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError("");

      const result =
        await stockReservationService.getAll({
          companyId: companyId || undefined,
          projectId: projectId || undefined,
          warehouseId:
            warehouseId || undefined,
          status:
            status === ""
              ? undefined
              : Number(status),
          search: search.trim() || undefined,
          activeOnly:
            activeOnly || undefined,
          expiredOnly:
            expiredOnly || undefined,
        });

      setItems(result);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Rezervasyonlar yüklenemedi.",
      );
    } finally {
      setLoading(false);
    }
  }, [
    activeOnly,
    companyId,
    expiredOnly,
    projectId,
    search,
    status,
    warehouseId,
  ]);

  useEffect(() => {
    void (async () => {
      try {
        const [
          companyRows,
          projectRows,
          warehouseRows,
        ] = await Promise.all([
          companyService.getAll(),
          projectService.getAll(),
          inventoryMovementService.getWarehouses(),
        ]);

        setCompanies(companyRows);
        setProjects(projectRows);
        setWarehouses(warehouseRows);

        if (companyRows.length === 1) {
          setCompanyId(companyRows[0].id);
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Filtre verileri yüklenemedi.",
        );
      }
    })();
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const summary = useMemo(() => {
    const active = items.filter(
      (item) =>
        [0, 1].includes(item.status) &&
        item.remainingQuantity > 0 &&
        !item.isExpired,
    );

    const partial = items.filter(
      (item) => item.status === 1,
    );

    const expired = items.filter(
      (item) => item.isExpired,
    );

    const remaining = active.reduce(
      (sum, item) =>
        sum + item.remainingQuantity,
      0,
    );

    return {
      total: items.length,
      active: active.length,
      partial: partial.length,
      expired: expired.length,
      remaining,
    };
  }, [items]);

  function submit(
    event: FormEvent<HTMLFormElement>,
  ) {
    event.preventDefault();
    void load();
  }

  function clearFilters() {
    setSearch("");
    setProjectId("");
    setWarehouseId("");
    setStatus("");
    setActiveOnly(false);
    setExpiredOnly(false);
  }

  return (
    <ErpShell
      title="Stok Rezervasyonları"
      description="Tüm proje, depo ve malzeme rezervasyonlarını tek merkezden yönetin."
    >
      <div className="space-y-6">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
          <div>
            <Link
              href="/depo-stok"
              className="text-sm font-medium text-slate-600 hover:text-slate-950"
            >
              ← Depo &amp; Stok
            </Link>

            <p className="mt-2 text-sm text-slate-500">
              Aktif, kısmi, tamamlanan,
              serbest bırakılan ve süresi dolan
              rezervasyonları görüntüleyin.
            </p>
          </div>

          <Link
            href="/depo-stok/malzeme-talepleri"
            className="inline-flex w-fit items-center justify-center rounded-lg bg-slate-950 px-4 py-2.5 text-sm font-medium text-white hover:bg-slate-800"
          >
            Malzeme Taleplerini Aç
          </Link>
        </div>

        {error ? (
          <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        ) : null}

        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
          <SummaryCard
            label="Toplam Rezervasyon"
            value={
              loading ? "…" : summary.total
            }
          />

          <SummaryCard
            label="Aktif"
            value={
              loading ? "…" : summary.active
            }
          />

          <SummaryCard
            label="Kısmi Çıkış"
            value={
              loading ? "…" : summary.partial
            }
          />

          <SummaryCard
            label="Süresi Dolan"
            value={
              loading ? "…" : summary.expired
            }
            warning={summary.expired > 0}
          />

          <SummaryCard
            label="Kalan Miktar"
            value={
              loading
                ? "…"
                : formatNumber(
                    summary.remaining,
                  )
            }
          />
        </div>

        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <form
            onSubmit={submit}
            className="space-y-4"
          >
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
              <Field label="Arama">
                <input
                  value={search}
                  onChange={(event) =>
                    setSearch(event.target.value)
                  }
                  placeholder="Rezervasyon, talep, proje veya malzeme"
                  className="input"
                />
              </Field>

              <Field label="Şirket">
                <select
                  value={companyId}
                  onChange={(event) => {
                    setCompanyId(
                      event.target.value,
                    );
                    setProjectId("");
                  }}
                  className="input"
                >
                  <option value="">
                    Tüm şirketler
                  </option>

                  {companies.map((company) => (
                    <option
                      key={company.id}
                      value={company.id}
                    >
                      {company.code} ·{" "}
                      {company.name}
                    </option>
                  ))}
                </select>
              </Field>

              <Field label="Proje">
                <select
                  value={projectId}
                  onChange={(event) =>
                    setProjectId(
                      event.target.value,
                    )
                  }
                  className="input"
                >
                  <option value="">
                    Tüm projeler
                  </option>

                  {filteredProjects.map(
                    (project) => (
                      <option
                        key={project.id}
                        value={project.id}
                      >
                        {project.code} ·{" "}
                        {project.name}
                      </option>
                    ),
                  )}
                </select>
              </Field>

              <Field label="Depo">
                <select
                  value={warehouseId}
                  onChange={(event) =>
                    setWarehouseId(
                      event.target.value,
                    )
                  }
                  className="input"
                >
                  <option value="">
                    Tüm depolar
                  </option>

                  {warehouses.map(
                    (warehouse) => (
                      <option
                        key={warehouse.id}
                        value={warehouse.id}
                      >
                        {warehouse.code
                          ? `${warehouse.code} · `
                          : ""}
                        {warehouse.name}
                      </option>
                    ),
                  )}
                </select>
              </Field>

              <Field label="Durum">
                <select
                  value={status}
                  onChange={(event) =>
                    setStatus(
                      event.target.value,
                    )
                  }
                  className="input"
                >
                  <option value="">
                    Tüm durumlar
                  </option>
                  <option value="0">
                    Aktif
                  </option>
                  <option value="1">
                    Kısmi Çıkış
                  </option>
                  <option value="2">
                    Tamamlandı
                  </option>
                  <option value="3">
                    Serbest Bırakıldı
                  </option>
                  <option value="4">
                    İptal
                  </option>
                </select>
              </Field>
            </div>

            <div className="flex flex-col gap-3 border-t border-slate-200 pt-4 lg:flex-row lg:items-center lg:justify-between">
              <div className="flex flex-wrap gap-4">
                <label className="inline-flex items-center gap-2 text-sm text-slate-700">
                  <input
                    type="checkbox"
                    checked={activeOnly}
                    onChange={(event) =>
                      setActiveOnly(
                        event.target.checked,
                      )
                    }
                  />
                  Yalnız aktif rezervasyonlar
                </label>

                <label className="inline-flex items-center gap-2 text-sm text-slate-700">
                  <input
                    type="checkbox"
                    checked={expiredOnly}
                    onChange={(event) =>
                      setExpiredOnly(
                        event.target.checked,
                      )
                    }
                  />
                  Yalnız süresi dolanlar
                </label>
              </div>

              <div className="flex flex-wrap gap-2">
                <button
                  type="button"
                  onClick={clearFilters}
                  className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
                >
                  Temizle
                </button>

                <button
                  type="submit"
                  disabled={loading}
                  className="rounded-lg bg-slate-950 px-5 py-2 text-sm font-medium text-white hover:bg-slate-800 disabled:opacity-50"
                >
                  {loading
                    ? "Yükleniyor..."
                    : "Filtrele"}
                </button>
              </div>
            </div>
          </form>
        </section>

        <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
            <div>
              <h2 className="text-lg font-semibold text-slate-950">
                Rezervasyon Listesi
              </h2>

              <p className="mt-1 text-sm text-slate-500">
                Kalan miktarı bulunan rezervasyonlar
                malzeme talebi üzerinden çıkışa
                dönüştürülebilir.
              </p>
            </div>

            <span className="rounded-full bg-slate-100 px-3 py-1 text-xs font-medium text-slate-700">
              {items.length} kayıt
            </span>
          </div>

          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-slate-200 text-sm">
              <thead className="bg-slate-50">
                <tr>
                  <TableHead>
                    Rezervasyon
                  </TableHead>
                  <TableHead>Talep</TableHead>
                  <TableHead>
                    Proje
                  </TableHead>
                  <TableHead>Depo</TableHead>
                  <TableHead>
                    Malzeme
                  </TableHead>
                  <TableHead right>
                    Talep
                  </TableHead>
                  <TableHead right>
                    Rezerve
                  </TableHead>
                  <TableHead right>
                    Çıkış
                  </TableHead>
                  <TableHead right>
                    Kalan
                  </TableHead>
                  <TableHead>
                    Durum
                  </TableHead>
                  <TableHead>
                    İşlem
                  </TableHead>
                </tr>
              </thead>

              <tbody className="divide-y divide-slate-100">
                {loading ? (
                  <tr>
                    <td
                      colSpan={11}
                      className="px-4 py-12 text-center text-slate-500"
                    >
                      Rezervasyonlar yükleniyor...
                    </td>
                  </tr>
                ) : items.length === 0 ? (
                  <tr>
                    <td
                      colSpan={11}
                      className="px-4 py-12 text-center text-slate-500"
                    >
                      Filtrelere uygun rezervasyon
                      bulunamadı.
                    </td>
                  </tr>
                ) : (
                  items.map((item) => (
                    <tr
                      key={item.id}
                      className="align-top hover:bg-slate-50"
                    >
                      <td className="px-4 py-4">
                        <strong className="whitespace-nowrap text-slate-950">
                          {item.reservationNumber}
                        </strong>

                        <span className="mt-1 block text-xs text-slate-500">
                          {formatDate(
                            item.reservationDate,
                          )}
                        </span>

                        {item.expirationDate ? (
                          <span
                            className={
                              item.isExpired
                                ? "mt-1 block text-xs font-medium text-red-700"
                                : "mt-1 block text-xs text-slate-500"
                            }
                          >
                            Son:{" "}
                            {formatDate(
                              item.expirationDate,
                            )}
                          </span>
                        ) : null}
                      </td>

                      <td className="px-4 py-4">
                        <Link
                          href={`/depo-stok/malzeme-talepleri/${item.purchaseRequestId}`}
                          className="font-semibold text-slate-900 hover:underline"
                        >
                          {item.requestNumber}
                        </Link>
                      </td>

                      <td className="px-4 py-4">
                        <strong className="block text-slate-900">
                          {item.projectName}
                        </strong>

                        <span className="mt-1 block text-xs text-slate-500">
                          {item.projectCode}
                        </span>
                      </td>

                      <td className="px-4 py-4">
                        <strong className="block text-slate-800">
                          {item.warehouseName}
                        </strong>

                        <span className="mt-1 block text-xs text-slate-500">
                          {item.warehouseCode}
                        </span>
                      </td>

                      <td className="px-4 py-4">
                        <strong className="block text-slate-900">
                          {item.inventoryItemName}
                        </strong>

                        <span className="mt-1 block text-xs text-slate-500">
                          {item.inventoryItemCode} ·{" "}
                          {item.unit}
                        </span>
                      </td>

                      <NumberCell
                        value={
                          item.requestedQuantity
                        }
                      />

                      <NumberCell
                        value={
                          item.reservedQuantity
                        }
                      />

                      <NumberCell
                        value={
                          item.consumedQuantity
                        }
                      />

                      <NumberCell
                        value={
                          item.remainingQuantity
                        }
                        warning={
                          item.remainingQuantity >
                            0 &&
                          item.isExpired
                        }
                      />

                      <td className="px-4 py-4">
                        <span
                          className={`inline-flex whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-medium ${statusClass(
                            item,
                          )}`}
                        >
                          {item.isExpired
                            ? "Süresi Doldu"
                            : statusLabels[
                                item.status
                              ] ??
                              item.statusName}
                        </span>
                      </td>

                      <td className="px-4 py-4">
                        <Link
                          href={`/depo-stok/malzeme-talepleri/${item.purchaseRequestId}`}
                          className="inline-flex whitespace-nowrap rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs font-medium text-slate-700 hover:bg-slate-50"
                        >
                          Talebi Aç
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
  value: string | number;
  warning?: boolean;
}) {
  return (
    <div
      className={
        warning
          ? "rounded-xl border border-red-200 bg-red-50 p-5 shadow-sm"
          : "rounded-xl border border-slate-200 bg-white p-5 shadow-sm"
      }
    >
      <p
        className={
          warning
            ? "text-sm font-medium text-red-700"
            : "text-sm font-medium text-slate-500"
        }
      >
        {label}
      </p>

      <p
        className={
          warning
            ? "mt-2 text-3xl font-semibold text-red-900"
            : "mt-2 text-3xl font-semibold text-slate-950"
        }
      >
        {value}
      </p>
    </div>
  );
}

function Field({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <label className="block space-y-2">
      <span className="text-sm font-medium text-slate-700">
        {label}
      </span>
      {children}
    </label>
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

function NumberCell({
  value,
  warning = false,
}: {
  value: number;
  warning?: boolean;
}) {
  return (
    <td
      className={`whitespace-nowrap px-4 py-4 text-right font-medium ${
        warning
          ? "text-red-700"
          : "text-slate-800"
      }`}
    >
      {formatNumber(value)}
    </td>
  );
}
