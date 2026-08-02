"use client";

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
  cargoService,
  CargoDirection,
  CargoStatus,
  type CargoItem,
} from "@/services/cargo.service";

const directionLabels: Record<number, string> = {
  [CargoDirection.Incoming]: "Gelen Kargo",
  [CargoDirection.Outgoing]: "Giden Kargo",
};

const statusLabels: Record<number, string> = {
  [CargoStatus.Registered]: "Kayıtlı",
  [CargoStatus.InTransit]: "Yolda",
  [CargoStatus.Delivered]: "Teslim Edildi",
  [CargoStatus.Returned]: "İade Edildi",
  [CargoStatus.Cancelled]: "İptal",
};

function today() {
  return new Date().toISOString().slice(0, 10);
}

function formatDate(value?: string | null) {
  if (!value) {
    return "—";
  }

  return new Date(value).toLocaleDateString("tr-TR");
}

const initialForm = {
  companyId: "",
  projectId: "",
  direction: String(CargoDirection.Incoming),
  trackingNumber: "",
  cargoCompany: "",
  senderName: "",
  recipientName: "",
  institutionName: "",
  cargoDate: today(),
  expectedDeliveryDate: "",
  description: "",
};

export default function CargoPage() {
  const [companies, setCompanies] =
    useState<CompanyListItem[]>([]);

  const [projects, setProjects] =
    useState<ProjectListItem[]>([]);

  const [items, setItems] =
    useState<CargoItem[]>([]);

  const [form, setForm] =
    useState(initialForm);

  const [showForm, setShowForm] =
    useState(false);

  const [companyFilter, setCompanyFilter] =
    useState("");

  const [projectFilter, setProjectFilter] =
    useState("");

  const [directionFilter, setDirectionFilter] =
    useState("");

  const [statusFilter, setStatusFilter] =
    useState("");

  const [search, setSearch] =
    useState("");

  const [loading, setLoading] =
    useState(true);

  const [saving, setSaving] =
    useState(false);

  const [processingId, setProcessingId] =
    useState("");

  const [error, setError] =
    useState("");

  const [success, setSuccess] =
    useState("");

  const formProjects = useMemo(
    () =>
      projects.filter(
        (project) =>
          !form.companyId ||
          project.companyId === form.companyId
      ),
    [projects, form.companyId]
  );

  const filterProjects = useMemo(
    () =>
      projects.filter(
        (project) =>
          !companyFilter ||
          project.companyId === companyFilter
      ),
    [projects, companyFilter]
  );

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [
        companyRows,
        projectRows,
        cargoRows,
      ] = await Promise.all([
        companyService.getAll(),
        projectService.getAll(),
        cargoService.getAll({
          companyId: companyFilter || undefined,
          projectId: projectFilter || undefined,
          direction:
            directionFilter === ""
              ? undefined
              : Number(directionFilter),
          status:
            statusFilter === ""
              ? undefined
              : Number(statusFilter),
          search: search || undefined,
        }),
      ]);

      setCompanies(companyRows);
      setProjects(projectRows);
      setItems(cargoRows);

      if (
        !form.companyId &&
        companyRows.length === 1
      ) {
        setForm((current) => ({
          ...current,
          companyId: companyRows[0].id,
        }));
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Kargo kayıtları yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [
    companyFilter,
    projectFilter,
    directionFilter,
    statusFilter,
    search,
    form.companyId,
  ]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (
      projectFilter &&
      !filterProjects.some(
        (project) =>
          project.id === projectFilter
      )
    ) {
      setProjectFilter("");
    }
  }, [filterProjects, projectFilter]);

  useEffect(() => {
    if (
      form.projectId &&
      !formProjects.some(
        (project) =>
          project.id === form.projectId
      )
    ) {
      setForm((current) => ({
        ...current,
        projectId: "",
      }));
    }
  }, [formProjects, form.projectId]);

  async function createCargo(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      await cargoService.create({
        companyId: form.companyId,
        projectId: form.projectId || null,
        direction:
          Number(form.direction) as CargoDirection,
        trackingNumber:
          form.trackingNumber.trim(),
        cargoCompany:
          form.cargoCompany.trim(),
        senderName:
          form.senderName.trim() || null,
        recipientName:
          form.recipientName.trim() || null,
        institutionName:
          form.institutionName.trim() || null,
        cargoDate: form.cargoDate,
        expectedDeliveryDate:
          form.expectedDeliveryDate || null,
        description:
          form.description.trim() || null,
      });

      setSuccess(
        "Kargo kaydı başarıyla oluşturuldu."
      );

      setForm({
        ...initialForm,
        companyId: form.companyId,
      });

      setShowForm(false);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Kargo kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function changeStatus(
    item: CargoItem,
    status: CargoStatus
  ) {
    let deliveredToName: string | null =
      item.deliveredToName || null;

    if (status === CargoStatus.Delivered) {
      const value = window.prompt(
        "Kargoyu teslim alan kişinin adı:",
        item.deliveredToName || ""
      );

      if (value === null) {
        return;
      }

      deliveredToName =
        value.trim() || null;
    }

    setProcessingId(item.id);
    setError("");
    setSuccess("");

    try {
      await cargoService.update(item.id, {
        projectId: item.projectId || null,
        cargoCompany: item.cargoCompany,
        senderName: item.senderName || null,
        recipientName:
          item.recipientName || null,
        institutionName:
          item.institutionName || null,
        cargoDate:
          item.cargoDate.slice(0, 10),
        expectedDeliveryDate:
          item.expectedDeliveryDate
            ? item.expectedDeliveryDate.slice(0, 10)
            : null,
        deliveredAtUtc:
          status === CargoStatus.Delivered
            ? new Date().toISOString()
            : item.deliveredAtUtc || null,
        deliveredToName,
        description: null,
        status,
      });

      setSuccess(
        `Kargo durumu "${statusLabels[status]}" olarak güncellendi.`
      );

      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Kargo durumu güncellenemedi."
      );
    } finally {
      setProcessingId("");
    }
  }

  async function deleteCargo(id: string) {
    const confirmed = window.confirm(
      "Bu kargo kaydını silmek istediğinize emin misiniz?"
    );

    if (!confirmed) {
      return;
    }

    setProcessingId(id);
    setError("");
    setSuccess("");

    try {
      await cargoService.delete(id);

      setSuccess("Kargo kaydı silindi.");

      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Kargo kaydı silinemedi."
      );
    } finally {
      setProcessingId("");
    }
  }

  return (
    <ErpShell
      title="Sekreterya"
      description="Kargo ve teslimat süreçleri"
    >
      <div className="space-y-6">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold">
              Kargo Takibi
            </h1>

            <p className="mt-1 text-sm text-slate-500">
              Gelen ve giden kargoları kayıt altına alın
            </p>
          </div>

          <button
            type="button"
            className="rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white"
            onClick={() =>
              setShowForm((value) => !value)
            }
          >
            {showForm
              ? "Formu Kapat"
              : "Yeni Kargo Kaydı"}
          </button>
        </div>

        {error && (
          <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
            {error}
          </div>
        )}

        {success && (
          <div className="rounded-lg border border-green-200 bg-green-50 p-3 text-sm text-green-700">
            {success}
          </div>
        )}

        {showForm && (
          <form
            onSubmit={createCargo}
            className="rounded-2xl border bg-white p-5 shadow-sm"
          >
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <label className="space-y-1 text-sm">
                <span>Şirket</span>

                <select
                  required
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.companyId}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      companyId:
                        event.target.value,
                      projectId: "",
                    }))
                  }
                >
                  <option value="">
                    Şirket seçin
                  </option>

                  {companies.map((company) => (
                    <option
                      key={company.id}
                      value={company.id}
                    >
                      {company.name}
                    </option>
                  ))}
                </select>
              </label>

              <label className="space-y-1 text-sm">
                <span>Proje</span>

                <select
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.projectId}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      projectId:
                        event.target.value,
                    }))
                  }
                >
                  <option value="">
                    Proje bağlantısı yok
                  </option>

                  {formProjects.map((project) => (
                    <option
                      key={project.id}
                      value={project.id}
                    >
                      {project.code} - {project.name}
                    </option>
                  ))}
                </select>
              </label>

              <label className="space-y-1 text-sm">
                <span>Kargo Yönü</span>

                <select
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.direction}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      direction:
                        event.target.value,
                    }))
                  }
                >
                  <option value="0">
                    Gelen Kargo
                  </option>

                  <option value="1">
                    Giden Kargo
                  </option>
                </select>
              </label>

              <label className="space-y-1 text-sm">
                <span>Takip Numarası</span>

                <input
                  required
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.trackingNumber}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      trackingNumber:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Kargo Firması</span>

                <input
                  required
                  className="w-full rounded-lg border px-3 py-2"
                  placeholder="Yurtiçi, MNG, Aras..."
                  value={form.cargoCompany}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      cargoCompany:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Kargo Tarihi</span>

                <input
                  required
                  type="date"
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.cargoDate}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      cargoDate:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Beklenen Teslim Tarihi</span>

                <input
                  type="date"
                  className="w-full rounded-lg border px-3 py-2"
                  value={
                    form.expectedDeliveryDate
                  }
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      expectedDeliveryDate:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Kurum / Firma</span>

                <input
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.institutionName}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      institutionName:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Gönderen</span>

                <input
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.senderName}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      senderName:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Alıcı</span>

                <input
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.recipientName}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      recipientName:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm md:col-span-2 xl:col-span-3">
                <span>Açıklama</span>

                <textarea
                  className="min-h-24 w-full rounded-lg border px-3 py-2"
                  value={form.description}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      description:
                        event.target.value,
                    }))
                  }
                />
              </label>
            </div>

            <div className="mt-5 flex justify-end">
              <button
                disabled={saving}
                className="rounded-lg bg-slate-900 px-5 py-2 text-sm font-medium text-white disabled:opacity-60"
              >
                {saving
                  ? "Kaydediliyor..."
                  : "Kargo Kaydını Oluştur"}
              </button>
            </div>
          </form>
        )}

        <section className="rounded-2xl border bg-white p-5 shadow-sm">
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
            <select
              className="rounded-lg border px-3 py-2 text-sm"
              value={companyFilter}
              onChange={(event) => {
                setCompanyFilter(
                  event.target.value
                );
                setProjectFilter("");
              }}
            >
              <option value="">
                Tüm şirketler
              </option>

              {companies.map((company) => (
                <option
                  key={company.id}
                  value={company.id}
                >
                  {company.name}
                </option>
              ))}
            </select>

            <select
              className="rounded-lg border px-3 py-2 text-sm"
              value={projectFilter}
              onChange={(event) =>
                setProjectFilter(
                  event.target.value
                )
              }
            >
              <option value="">
                Tüm projeler
              </option>

              {filterProjects.map((project) => (
                <option
                  key={project.id}
                  value={project.id}
                >
                  {project.code} - {project.name}
                </option>
              ))}
            </select>

            <select
              className="rounded-lg border px-3 py-2 text-sm"
              value={directionFilter}
              onChange={(event) =>
                setDirectionFilter(
                  event.target.value
                )
              }
            >
              <option value="">
                Gelen ve giden
              </option>

              <option value="0">
                Gelen Kargo
              </option>

              <option value="1">
                Giden Kargo
              </option>
            </select>

            <select
              className="rounded-lg border px-3 py-2 text-sm"
              value={statusFilter}
              onChange={(event) =>
                setStatusFilter(
                  event.target.value
                )
              }
            >
              <option value="">
                Tüm durumlar
              </option>

              {Object.entries(statusLabels).map(
                ([value, label]) => (
                  <option
                    key={value}
                    value={value}
                  >
                    {label}
                  </option>
                )
              )}
            </select>

            <input
              className="rounded-lg border px-3 py-2 text-sm"
              placeholder="Takip no, firma, gönderen..."
              value={search}
              onChange={(event) =>
                setSearch(event.target.value)
              }
            />
          </div>
        </section>

        <section className="overflow-hidden rounded-2xl border bg-white shadow-sm">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[1200px] text-left text-sm">
              <thead className="border-b bg-slate-50">
                <tr>
                  <th className="px-4 py-3">
                    Yön
                  </th>

                  <th className="px-4 py-3">
                    Takip No
                  </th>

                  <th className="px-4 py-3">
                    Kargo Firması
                  </th>

                  <th className="px-4 py-3">
                    Kurum
                  </th>

                  <th className="px-4 py-3">
                    Gönderen / Alıcı
                  </th>

                  <th className="px-4 py-3">
                    Tarih
                  </th>

                  <th className="px-4 py-3">
                    Beklenen Teslim
                  </th>

                  <th className="px-4 py-3">
                    Durum
                  </th>

                  <th className="px-4 py-3 text-right">
                    İşlemler
                  </th>
                </tr>
              </thead>

              <tbody>
                {loading ? (
                  <tr>
                    <td
                      className="px-4 py-8 text-center"
                      colSpan={9}
                    >
                      Yükleniyor...
                    </td>
                  </tr>
                ) : items.length === 0 ? (
                  <tr>
                    <td
                      className="px-4 py-8 text-center"
                      colSpan={9}
                    >
                      Kargo kaydı bulunamadı.
                    </td>
                  </tr>
                ) : (
                  items.map((item) => (
                    <tr
                      key={item.id}
                      className="border-b last:border-0"
                    >
                      <td className="px-4 py-3">
                        {
                          directionLabels[
                            item.direction
                          ]
                        }
                      </td>

                      <td className="px-4 py-3 font-medium">
                        {item.trackingNumber}
                      </td>

                      <td className="px-4 py-3">
                        {item.cargoCompany}
                      </td>

                      <td className="px-4 py-3">
                        {item.institutionName ||
                          "—"}
                      </td>

                      <td className="px-4 py-3">
                        {item.direction ===
                        CargoDirection.Incoming
                          ? item.senderName || "—"
                          : item.recipientName ||
                            "—"}
                      </td>

                      <td className="px-4 py-3">
                        {formatDate(
                          item.cargoDate
                        )}
                      </td>

                      <td className="px-4 py-3">
                        {formatDate(
                          item.expectedDeliveryDate
                        )}
                      </td>

                      <td className="px-4 py-3">
                        <select
                          disabled={
                            processingId === item.id
                          }
                          className="rounded-lg border px-2 py-1 text-sm"
                          value={item.status}
                          onChange={(event) =>
                            void changeStatus(
                              item,
                              Number(
                                event.target.value
                              ) as CargoStatus
                            )
                          }
                        >
                          {Object.entries(
                            statusLabels
                          ).map(
                            ([value, label]) => (
                              <option
                                key={value}
                                value={value}
                              >
                                {label}
                              </option>
                            )
                          )}
                        </select>

                        {item.deliveredToName && (
                          <div className="mt-1 text-xs text-slate-500">
                            Teslim alan:{" "}
                            {item.deliveredToName}
                          </div>
                        )}
                      </td>

                      <td className="px-4 py-3 text-right">
                        <button
                          type="button"
                          disabled={
                            processingId === item.id
                          }
                          onClick={() =>
                            void deleteCargo(item.id)
                          }
                          className="text-sm font-medium text-red-600 disabled:opacity-50"
                        >
                          {processingId === item.id
                            ? "İşleniyor..."
                            : "Sil"}
                        </button>
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
