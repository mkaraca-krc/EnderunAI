"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";
import { Button, ConfirmDialog } from "@/components/ui";
import { dateTime } from "@/lib/format/turkish";
import { useModuleActions } from "@/lib/auth/module-actions";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  visitorService,
  VisitorStatus,
  type VisitorItem,
} from "@/services/visitor.service";

const statusLabels: Record<number, string> = {
  [VisitorStatus.Expected]: "Bekleniyor",
  [VisitorStatus.CheckedIn]: "Giriş Yaptı",
  [VisitorStatus.CheckedOut]: "Çıkış Yaptı",
  [VisitorStatus.Cancelled]: "İptal",
  [VisitorStatus.Rejected]: "Reddedildi",
};

function localDateTimeValue() {
  const now = new Date();
  const offset = now.getTimezoneOffset();
  const local = new Date(now.getTime() - offset * 60_000);

  return local.toISOString().slice(0, 16);
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return "—";
  }

  return dateTime(value);
}

const initialForm = {
  companyId: "",
  projectId: "",
  fullName: "",
  identityNumber: "",
  phoneNumber: "",
  email: "",
  companyName: "",
  vehiclePlate: "",
  visitorCardNumber: "",
  personToVisit: "",
  departmentName: "",
  visitPurpose: "",
  plannedVisitAtUtc: localDateTimeValue(),
  approvedByName: "",
  description: "",
};

export default function VisitorsPage() {
  /*
   * Aksiyon izinleri UÇLARDAN (SecretariatController):
   *   POST/PUT/DELETE cargo|visitors -> secretariat.manage
   *
   * Sekreteryada create/edit/delete ayrımı YOK; hepsi tek "manage"
   * anahtarında. Uç öyle kurulmuş, ekran onu izliyor.
   */
  const actions = useModuleActions("secretariat");

  const [companies, setCompanies] =
    useState<CompanyListItem[]>([]);

  const [projects, setProjects] =
    useState<ProjectListItem[]>([]);

  const [items, setItems] =
    useState<VisitorItem[]>([]);

  const [form, setForm] =
    useState(initialForm);

  const [showForm, setShowForm] =
    useState(false);

  const [companyFilter, setCompanyFilter] =
    useState("");

  const [projectFilter, setProjectFilter] =
    useState("");

  const [statusFilter, setStatusFilter] =
    useState("");

  const [startDate, setStartDate] =
    useState(today());

  const [endDate, setEndDate] =
    useState(today());

  const [search, setSearch] =
    useState("");

  const [loading, setLoading] =
    useState(true);

  const [saving, setSaving] =
    useState(false);

  const [processingId, setProcessingId] =
    useState("");

  /** Onay bekleyen ziyaretçi işlemi. */
  const [pending, setPending] = useState<{
    kind: "check-in" | "check-out" | "delete";
    item: VisitorItem;
  } | null>(null);

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
        visitorRows,
      ] = await Promise.all([
        companyService.getAll(),
        projectService.getAll(),
        visitorService.getAll({
          companyId: companyFilter || undefined,
          projectId: projectFilter || undefined,
          status:
            statusFilter === ""
              ? undefined
              : Number(statusFilter),
          startDate: startDate || undefined,
          endDate: endDate || undefined,
          search: search || undefined,
        }),
      ]);

      setCompanies(companyRows);
      setProjects(projectRows);
      setItems(visitorRows);

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
          : "Ziyaretçi kayıtları yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [
    companyFilter,
    projectFilter,
    statusFilter,
    startDate,
    endDate,
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

  async function createVisitor(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      await visitorService.create({
        companyId: form.companyId,
        projectId: form.projectId || null,
        fullName: form.fullName.trim(),
        identityNumber:
          form.identityNumber.trim() || null,
        phoneNumber:
          form.phoneNumber.trim() || null,
        email:
          form.email.trim() || null,
        companyName:
          form.companyName.trim() || null,
        vehiclePlate:
          form.vehiclePlate.trim() || null,
        visitorCardNumber:
          form.visitorCardNumber.trim() || null,
        personToVisit:
          form.personToVisit.trim(),
        departmentName:
          form.departmentName.trim() || null,
        visitPurpose:
          form.visitPurpose.trim(),
        plannedVisitAtUtc:
          new Date(
            form.plannedVisitAtUtc
          ).toISOString(),
        approvedByName:
          form.approvedByName.trim() || null,
        description:
          form.description.trim() || null,
      });

      setSuccess(
        "Ziyaretçi kaydı başarıyla oluşturuldu."
      );

      setForm({
        ...initialForm,
        companyId: form.companyId,
        plannedVisitAtUtc:
          localDateTimeValue(),
      });

      setShowForm(false);
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Ziyaretçi kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function checkIn(item: VisitorItem, receivedByName: string) {
    setPending(null);
    setProcessingId(item.id);
    setError("");
    setSuccess("");

    try {
      await visitorService.checkIn(
        item.id,
        receivedByName.trim() || null
      );

      setSuccess(
        `${item.fullName} için giriş işlemi tamamlandı.`
      );

      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Ziyaretçi girişi yapılamadı."
      );
    } finally {
      setProcessingId("");
    }
  }

  async function checkOut(item: VisitorItem) {
    setPending(null);
    setProcessingId(item.id);
    setError("");
    setSuccess("");

    try {
      await visitorService.checkOut(item.id);

      setSuccess(
        `${item.fullName} için çıkış işlemi tamamlandı.`
      );

      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Ziyaretçi çıkışı yapılamadı."
      );
    } finally {
      setProcessingId("");
    }
  }

  async function deleteVisitor(id: string) {
    setPending(null);
    setProcessingId(id);
    setError("");
    setSuccess("");

    try {
      await visitorService.delete(id);

      setSuccess(
        "Ziyaretçi kaydı silindi."
      );

      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Ziyaretçi kaydı silinemedi."
      );
    } finally {
      setProcessingId("");
    }
  }


  /* Eylem sütunu duruma ve yetkiye bağlı; çıktıya girmez. */
  const columns = useMemo<DataTableColumn<VisitorItem>[]>(
    () => [
      {
        key: "ziyaretci",
        header: "Ziyaretçi",
        value: (item) =>
          [item.fullName, item.phoneNumber, item.vehiclePlate]
            .filter(Boolean)
            .join(" · "),
        render: (item) => (
          <>
            <div className="font-medium">{item.fullName}</div>
            <div className="text-xs text-slate-500">
              {item.phoneNumber || "Telefon yok"}
              {item.vehiclePlate ? ` · ${item.vehiclePlate}` : ""}
            </div>
          </>
        ),
      },
      { key: "firma", header: "Firma", value: (item) => item.companyName || "—" },
      {
        key: "kisi",
        header: "Ziyaret Edilecek",
        value: (item) =>
          [item.personToVisit, item.departmentName].filter(Boolean).join(" / "),
        render: (item) => (
          <>
            <div>{item.personToVisit}</div>
            <div className="text-xs text-slate-500">
              {item.departmentName || ""}
            </div>
          </>
        ),
      },
      { key: "amac", header: "Amaç", value: (item) => item.visitPurpose },
      {
        key: "planlanan",
        header: "Planlanan Tarih",
        value: (item) => formatDateTime(item.plannedVisitAtUtc),
      },
      {
        key: "giris",
        header: "Giriş",
        value: (item) => formatDateTime(item.checkInAtUtc),
      },
      {
        key: "cikis",
        header: "Çıkış",
        value: (item) => formatDateTime(item.checkOutAtUtc),
      },
      {
        key: "durum",
        header: "Durum",
        value: (item) => statusLabels[item.status] ?? item.statusName,
      },
      {
        key: "islemler",
        header: "İşlemler",
        align: "right",
        value: () => "",
        render: (item) => (
          <div className="flex justify-end gap-3">
            {item.status === VisitorStatus.Expected &&
              actions.can("manage") && (
                <button
                  type="button"
                  disabled={processingId === item.id}
                  onClick={() => setPending({ kind: "check-in", item })}
                  className="font-medium text-green-700 disabled:opacity-50"
                >
                  Giriş
                </button>
              )}

            {item.status === VisitorStatus.CheckedIn &&
              actions.can("manage") && (
                <button
                  type="button"
                  disabled={processingId === item.id}
                  onClick={() => setPending({ kind: "check-out", item })}
                  className="font-medium text-blue-700 disabled:opacity-50"
                >
                  Çıkış
                </button>
              )}

            {actions.can("manage") && (
              <button
                type="button"
                disabled={processingId === item.id}
                onClick={() => setPending({ kind: "delete", item })}
                className="font-medium text-red-600 disabled:opacity-50"
              >
                {processingId === item.id ? "İşleniyor..." : "Sil"}
              </button>
            )}
          </div>
        ),
      },
    ],
    [actions, processingId]
  );


  return (
    <ErpShell
      design="redwood"
      title="Sekreterya"
      description="Ziyaretçi giriş ve çıkış yönetimi"
    >
      <div className="space-y-6">
        {/* Ziyaretçi giriş/çıkışı güvenlik tarafından da işleniyor. */}
        <div className="flex justify-end">
          <Button variant="secondary" onClick={() => void load()}>Yenile</Button>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold">
              Ziyaretçiler
            </h1>

            <p className="mt-1 text-sm text-slate-500">
              Planlanan ziyaretleri ve giriş çıkışları yönetin
            </p>
          </div>

          {actions.can("manage") && (
            <button
              type="button"
              className="rounded-lg bg-brand-700 px-4 py-2 text-sm font-medium text-white"
              onClick={() =>
                setShowForm((value) => !value)
              }
            >
              {showForm
                ? "Formu Kapat"
                : "Yeni Ziyaretçi"}
            </button>
          )}
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
            onSubmit={createVisitor}
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
                <span>Ziyaret Tarihi ve Saati</span>

                <input
                  required
                  type="datetime-local"
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.plannedVisitAtUtc}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      plannedVisitAtUtc:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Ad Soyad</span>

                <input
                  required
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.fullName}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      fullName:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>TC Kimlik / Pasaport</span>

                <input
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.identityNumber}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      identityNumber:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Telefon</span>

                <input
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.phoneNumber}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      phoneNumber:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>E-posta</span>

                <input
                  type="email"
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.email}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      email:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Firma</span>

                <input
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.companyName}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      companyName:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Araç Plakası</span>

                <input
                  className="w-full rounded-lg border px-3 py-2 uppercase"
                  value={form.vehiclePlate}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      vehiclePlate:
                        event.target.value.toUpperCase(),
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Ziyaretçi Kart No</span>

                <input
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.visitorCardNumber}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      visitorCardNumber:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Ziyaret Edilecek Kişi</span>

                <input
                  required
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.personToVisit}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      personToVisit:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Bölüm</span>

                <input
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.departmentName}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      departmentName:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Onaylayan Kişi</span>

                <input
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.approvedByName}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      approvedByName:
                        event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm md:col-span-2 xl:col-span-3">
                <span>Ziyaret Amacı</span>

                <input
                  required
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.visitPurpose}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      visitPurpose:
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
                className="rounded-lg bg-brand-700 px-5 py-2 text-sm font-medium text-white disabled:opacity-60"
              >
                {saving
                  ? "Kaydediliyor..."
                  : "Ziyaretçiyi Kaydet"}
              </button>
            </div>
          </form>
        )}

        <section className="rounded-2xl border bg-white p-5 shadow-sm">
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
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
              type="date"
              className="rounded-lg border px-3 py-2 text-sm"
              value={startDate}
              onChange={(event) =>
                setStartDate(event.target.value)
              }
            />

            <input
              type="date"
              className="rounded-lg border px-3 py-2 text-sm"
              value={endDate}
              onChange={(event) =>
                setEndDate(event.target.value)
              }
            />

            <input
              className="rounded-lg border px-3 py-2 text-sm"
              placeholder="Ad, firma, plaka, kişi..."
              value={search}
              onChange={(event) =>
                setSearch(event.target.value)
              }
            />
          </div>
        </section>

        <section className="overflow-hidden rounded-2xl border bg-white shadow-sm">
          <div className="overflow-x-auto">
            <DataTable
              rows={items}
              columns={columns}
              rowKey={(item) => item.id}
              loading={loading}
              title="Ziyaretçi Kayıtları"
              emptyText="Ziyaretçi kaydı bulunamadı."
            />
          </div>
        </section>
      </div>
      {pending && (
        <ConfirmDialog
          key={`${pending.kind}-${pending.item.id}`}
          open
          title={
            pending.kind === "check-in"
              ? "Ziyaretçi Girişi"
              : pending.kind === "check-out"
                ? "Ziyaretçi Çıkışı"
                : "Ziyaretçi Kaydını Sil"
          }
          description={
            pending.kind === "check-in"
              ? `${pending.item.fullName} için giriş kaydedilecek. Karşılayan kişiyi yazarsanız kayda geçer.`
              : pending.kind === "check-out"
                ? `${pending.item.fullName} için çıkış saati kaydedilecek.`
                : `${pending.item.fullName} kaydı kalıcı olarak silinecek. Bu işlem geri alınamaz.`
          }
          confirmLabel={
            pending.kind === "check-in"
              ? "Girişi Kaydet"
              : pending.kind === "check-out"
                ? "Çıkışı Kaydet"
                : "Kaydı Sil"
          }
          showReason={pending.kind === "check-in"}
          reasonLabel="Karşılayan kişi (isteğe bağlı)"
          busy={processingId === pending.item.id}
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={(text) => {
            if (pending.kind === "check-in") {
              void checkIn(pending.item, text);
              return;
            }

            if (pending.kind === "check-out") {
              void checkOut(pending.item);
              return;
            }

            void deleteVisitor(pending.item.id);
          }}
        />
      )}
    </ErpShell>
  );
}
