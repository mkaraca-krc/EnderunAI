"use client";

import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog } from "@/components/ui";
import { decimal } from "@/lib/format/turkish";
import {
  hrOvertimeService,
  HrOvertimeItem,
} from "@/services/hr-overtime.service";
import {
  personnelService,
  PersonnelListItem,
} from "@/services/personnel.service";
import {
  companyService,
  CompanyListItem,
} from "@/services/company.service";

type OvertimeForm = {
  companyId: string;
  personnelId: string;
  workDate: string;
  requestedHours: string;
  approvedHours: string;
  isSundayWork: boolean;
  isPublicHolidayWork: boolean;
  reason: string;
  status: string;
  approvalNote: string;
};

const initialForm: OvertimeForm = {
  companyId: "",
  personnelId: "",
  workDate: "",
  requestedHours: "",
  approvedHours: "0",
  isSundayWork: false,
  isPublicHolidayWork: false,
  reason: "",
  status: "1",
  approvalNote: "",
};

const statuses = [
  { value: 0, label: "Taslak" },
  { value: 1, label: "Onay Bekliyor" },
  { value: 2, label: "Onaylandı" },
  { value: 3, label: "Reddedildi" },
  { value: 4, label: "İptal Edildi" },
];

function statusLabel(value: number) {
  return statuses.find((x) => x.value === value)?.label ?? "Bilinmiyor";
}

function statusClass(value: number) {
  if (value === 2) return "border-emerald-200 bg-emerald-50 text-emerald-700";
  if (value === 1) return "border-amber-200 bg-amber-50 text-amber-700";
  if (value === 3 || value === 4) {
    return "border-red-200 bg-red-50 text-red-700";
  }

  return "border-slate-200 bg-slate-50 text-slate-600";
}

function formatDate(value?: string | null) {
  return value
    ? new Date(value).toLocaleDateString("tr-TR")
    : "—";
}

export default function OvertimePage() {
  const [items, setItems] = useState<HrOvertimeItem[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [form, setForm] = useState<OvertimeForm>(initialForm);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);

  const [companyFilter, setCompanyFilter] = useState("");
  const [personnelFilter, setPersonnelFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [startDateFilter, setStartDateFilter] = useState("");
  const [endDateFilter, setEndDateFilter] = useState("");
  const [search, setSearch] = useState("");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  /** Onay bekleyen fazla mesai işlemi. */
  const [pending, setPending] = useState<{
    kind: "approve" | "delete";
    item: HrOvertimeItem;
  } | null>(null);

  const [actionId, setActionId] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const personnelById = useMemo(
    () => new Map(personnel.map((x) => [x.id, x])),
    [personnel]
  );

  const formPersonnel = useMemo(
    () =>
      form.companyId
        ? personnel.filter((x) => x.companyId === form.companyId)
        : personnel,
    [form.companyId, personnel]
  );

  const visibleItems = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");

    if (!term) return items;

    return items.filter((item) => {
      const person = personnelById.get(item.personnelId);

      return [
        person?.fullName,
        person?.employeeNumber,
        item.reason,
        statusLabel(item.status),
      ]
        .filter(Boolean)
        .join(" ")
        .toLocaleLowerCase("tr-TR")
        .includes(term);
    });
  }, [items, personnelById, search]);

  const pendingCount = items.filter((x) => x.status === 1).length;
  const approvedCount = items.filter((x) => x.status === 2).length;
  const requestedTotal = items.reduce(
    (sum, x) => sum + Number(x.requestedHours),
    0
  );
  const approvedTotal = items.reduce(
    (sum, x) => sum + Number(x.approvedHours),
    0
  );

  async function loadItems() {
    setLoading(true);
    setError("");

    try {
      setItems(
        await hrOvertimeService.getAll({
          companyId: companyFilter || undefined,
          personnelId: personnelFilter || undefined,
          status:
            statusFilter === "" ? undefined : Number(statusFilter),
          startDate: startDateFilter || undefined,
          endDate: endDateFilter || undefined,
        })
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Fazla mesai kayıtları yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    async function loadPage() {
      try {
        const [companyResult, personnelResult, overtimeResult] =
          await Promise.all([
            companyService.getAll(),
            personnelService.getAll(),
            hrOvertimeService.getAll(),
          ]);

        setCompanies(companyResult);
        setPersonnel(personnelResult);
        setItems(overtimeResult);

        if (companyResult.length === 1) {
          setForm((current) => ({
            ...current,
            companyId: companyResult[0].id,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Fazla mesai ekranı yüklenemedi."
        );
      } finally {
        setLoading(false);
      }
    }

    loadPage();
  }, []);

  function openCreate() {
    setEditingId(null);
    setForm({
      ...initialForm,
      companyId: companies.length === 1 ? companies[0].id : "",
    });
    setShowForm(true);
  }

  function openEdit(item: HrOvertimeItem) {
    setEditingId(item.id);
    setForm({
      companyId: item.companyId,
      personnelId: item.personnelId,
      workDate: item.workDate.slice(0, 10),
      requestedHours: String(item.requestedHours),
      approvedHours: String(item.approvedHours),
      isSundayWork: item.isSundayWork,
      isPublicHolidayWork: item.isPublicHolidayWork,
      reason: item.reason,
      status: String(item.status),
      approvalNote: item.approvalNote ?? "",
    });
    setShowForm(true);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      const requestedHours = Number(form.requestedHours);
      const approvedHours = Number(form.approvedHours);

      if (!form.companyId) throw new Error("Şirket seçilmelidir.");
      if (!form.personnelId) throw new Error("Personel seçilmelidir.");
      if (!form.workDate) throw new Error("Çalışma tarihi zorunludur.");

      if (!Number.isFinite(requestedHours) || requestedHours <= 0) {
        throw new Error("Talep edilen saat sıfırdan büyük olmalıdır.");
      }

      if (!form.reason.trim()) {
        throw new Error("Fazla mesai nedeni zorunludur.");
      }

      if (editingId) {
        await hrOvertimeService.update(editingId, {
          projectId: null,
          workDate: form.workDate,
          requestedHours,
          approvedHours,
          isSundayWork: form.isSundayWork,
          isPublicHolidayWork: form.isPublicHolidayWork,
          reason: form.reason.trim(),
          status: Number(form.status),
          approvalNote: form.approvalNote.trim() || null,
        });

        setSuccess("Fazla mesai kaydı güncellendi.");
      } else {
        await hrOvertimeService.create({
          companyId: form.companyId,
          personnelId: form.personnelId,
          projectId: null,
          workDate: form.workDate,
          requestedHours,
          isSundayWork: form.isSundayWork,
          isPublicHolidayWork: form.isPublicHolidayWork,
          reason: form.reason.trim(),
        });

        setSuccess("Fazla mesai talebi oluşturuldu.");
      }

      setShowForm(false);
      setEditingId(null);
      await loadItems();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Kayıt işlemi başarısız."
      );
    } finally {
      setSaving(false);
    }
  }

  async function approve(item: HrOvertimeItem) {
    setPending(null);
    setActionId(item.id);

    try {
      await hrOvertimeService.approve(item.id);
      setSuccess("Fazla mesai onaylandı.");
      await loadItems();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Onay işlemi başarısız."
      );
    } finally {
      setActionId(null);
    }
  }

  async function remove(item: HrOvertimeItem) {
    setPending(null);
    setActionId(item.id);

    try {
      await hrOvertimeService.delete(item.id);
      setSuccess("Fazla mesai kaydı silindi.");
      await loadItems();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Silme işlemi başarısız."
      );
    } finally {
      setActionId(null);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Fazla Mesai Yönetimi"
      description="Fazla mesai talepleri, onayları ve çalışma türleri"
    >
      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          {error}
        </div>
      )}

      {success && (
        <div className="mb-5 rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-700">
          {success}
        </div>
      )}

      <div className="mb-5 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {[
          ["Toplam Talep", items.length],
          ["Onay Bekleyen", pendingCount],
          ["Talep Edilen Saat", decimal(requestedTotal, 2)],
          ["Onaylanan Saat", decimal(approvedTotal, 2)],
        ].map(([title, value]) => (
          <article
            key={String(title)}
            className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"
          >
            <span className="text-xs font-bold text-slate-500">
              {title}
            </span>
            <strong className="mt-3 block text-3xl text-slate-800">
              {loading ? "…" : value}
            </strong>
          </article>
        ))}
      </div>

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-xl font-bold text-slate-800">
            Fazla Mesai Kayıtları
          </h2>

          <div className="flex gap-2">
            <Button variant="secondary" onClick={loadItems}>Yenile</Button>

            <button
              type="button"
              onClick={openCreate}
              className="rounded-lg bg-blue-700 px-4 py-2 text-sm font-semibold text-white"
            >
              + Yeni Fazla Mesai
            </button>
          </div>
        </div>
      </section>

      {showForm && (
        <section className="mb-5 rounded-xl border border-blue-200 bg-white p-5 shadow-sm">
          <form onSubmit={save}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <select
                value={form.companyId}
                disabled={Boolean(editingId)}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    companyId: e.target.value,
                    personnelId: "",
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Şirket seçin</option>
                {companies.map((x) => (
                  <option value={x.id} key={x.id}>
                    {x.name}
                  </option>
                ))}
              </select>

              <select
                value={form.personnelId}
                disabled={Boolean(editingId)}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    personnelId: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Personel seçin</option>
                {formPersonnel.map((x) => (
                  <option value={x.id} key={x.id}>
                    {x.employeeNumber} - {x.fullName}
                  </option>
                ))}
              </select>

              <input
                type="date"
                value={form.workDate}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    workDate: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                type="number"
                min="0.5"
                step="0.5"
                placeholder="Talep edilen saat"
                value={form.requestedHours}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    requestedHours: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              {editingId && (
                <>
                  <input
                    type="number"
                    min="0"
                    step="0.5"
                    placeholder="Onaylanan saat"
                    value={form.approvedHours}
                    onChange={(e) =>
                      setForm((x) => ({
                        ...x,
                        approvedHours: e.target.value,
                      }))
                    }
                    className="rounded-lg border border-slate-300 p-3"
                  />

                  <select
                    value={form.status}
                    onChange={(e) =>
                      setForm((x) => ({
                        ...x,
                        status: e.target.value,
                      }))
                    }
                    className="rounded-lg border border-slate-300 p-3"
                  >
                    {statuses.map((x) => (
                      <option value={x.value} key={x.value}>
                        {x.label}
                      </option>
                    ))}
                  </select>
                </>
              )}

              <div className="flex flex-wrap gap-4 rounded-lg border border-slate-200 p-3 md:col-span-2 xl:col-span-3">
                {[
                  ["isSundayWork", "Pazar çalışması"],
                  ["isPublicHolidayWork", "Resmî tatil"],
                ].map(([key, label]) => (
                  <label key={key} className="flex items-center gap-2">
                    <input
                      type="checkbox"
                      checked={Boolean(
                        form[key as keyof OvertimeForm]
                      )}
                      onChange={(e) =>
                        setForm((x) => ({
                          ...x,
                          [key]: e.target.checked,
                        }))
                      }
                    />
                    <span className="text-sm">{label}</span>
                  </label>
                ))}
              </div>

              <textarea
                rows={3}
                placeholder="Fazla mesai nedeni"
                value={form.reason}
                onChange={(e) =>
                  setForm((x) => ({
                    ...x,
                    reason: e.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3 md:col-span-2 xl:col-span-3"
              />

              {editingId && (
                <textarea
                  rows={2}
                  placeholder="Onay açıklaması"
                  value={form.approvalNote}
                  onChange={(e) =>
                    setForm((x) => ({
                      ...x,
                      approvalNote: e.target.value,
                    }))
                  }
                  className="rounded-lg border border-slate-300 p-3 md:col-span-2 xl:col-span-3"
                />
              )}
            </div>

            <div className="mt-5 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setShowForm(false)}
                className="rounded-lg border border-slate-300 px-4 py-2"
              >
                Vazgeç
              </button>

              <button
                type="submit"
                disabled={saving}
                className="rounded-lg bg-blue-700 px-5 py-2 text-white"
              >
                {saving ? "Kaydediliyor…" : "Kaydet"}
              </button>
            </div>
          </form>
        </section>
      )}

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Personel veya açıklama ara"
            className="rounded-lg border border-slate-300 p-3"
          />

          <select
            value={companyFilter}
            onChange={(e) => setCompanyFilter(e.target.value)}
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">Tüm şirketler</option>
            {companies.map((x) => (
              <option value={x.id} key={x.id}>
                {x.name}
              </option>
            ))}
          </select>

          <select
            value={personnelFilter}
            onChange={(e) => setPersonnelFilter(e.target.value)}
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">Tüm personeller</option>
            {personnel.map((x) => (
              <option value={x.id} key={x.id}>
                {x.fullName}
              </option>
            ))}
          </select>

          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">Tüm durumlar</option>
            {statuses.map((x) => (
              <option value={x.value} key={x.value}>
                {x.label}
              </option>
            ))}
          </select>

          <input
            type="date"
            value={startDateFilter}
            onChange={(e) => setStartDateFilter(e.target.value)}
            className="rounded-lg border border-slate-300 p-3"
          />

          <input
            type="date"
            value={endDateFilter}
            onChange={(e) => setEndDateFilter(e.target.value)}
            className="rounded-lg border border-slate-300 p-3"
          />

          <button
            type="button"
            onClick={loadItems}
            className="rounded-lg bg-brand-700 p-3 font-semibold text-white"
          >
            Filtrele
          </button>
        </div>
      </section>

      <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[1050px]">
            <thead className="bg-slate-50 text-left text-xs text-slate-500">
              <tr>
                <th className="p-4">Personel</th>
                <th className="p-4">Tarih</th>
                <th className="p-4">Talep</th>
                <th className="p-4">Onay</th>
                <th className="p-4">Çalışma Türü</th>
                <th className="p-4">Neden</th>
                <th className="p-4">Durum</th>
                <th className="p-4 text-right">İşlemler</th>
              </tr>
            </thead>

            <tbody>
              {visibleItems.map((item) => {
                const person = personnelById.get(item.personnelId);
                const busy = actionId === item.id;

                const types = [
                  item.isSundayWork ? "Pazar" : "",
                  item.isPublicHolidayWork ? "Resmî Tatil" : "",
                ].filter(Boolean);

                return (
                  <tr key={item.id} className="border-t text-sm">
                    <td className="p-4">
                      <strong>{person?.fullName ?? "—"}</strong>
                      <small className="block text-slate-500">
                        {person?.employeeNumber ?? "—"}
                      </small>
                    </td>
                    <td className="p-4">{formatDate(item.workDate)}</td>
                    <td className="p-4">{item.requestedHours} saat</td>
                    <td className="p-4">{item.approvedHours} saat</td>
                    <td className="p-4">
                      {types.length ? types.join(", ") : "Normal"}
                    </td>
                    <td className="max-w-[260px] p-4">
                      <span className="block truncate">{item.reason}</span>
                    </td>
                    <td className="p-4">
                      <span
                        className={`rounded-full border px-2 py-1 text-xs font-bold ${statusClass(
                          item.status
                        )}`}
                      >
                        {statusLabel(item.status)}
                      </span>
                    </td>
                    <td className="p-4">
                      <div className="flex justify-end gap-2">
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => openEdit(item)}
                          className="rounded border px-3 py-1.5 text-xs"
                        >
                          Düzenle
                        </button>

                        {item.status === 1 && (
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => setPending({ kind: "approve", item })}
                            className="rounded bg-emerald-600 px-3 py-1.5 text-xs text-white"
                          >
                            Onayla
                          </button>
                        )}

                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => setPending({ kind: "delete", item })}
                          className="rounded bg-red-50 px-3 py-1.5 text-xs text-red-700"
                        >
                          Sil
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}

              {!loading && visibleItems.length === 0 && (
                <tr>
                  <td colSpan={8} className="p-12 text-center text-slate-500">
                    Kayıt bulunamadı.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
      {pending && (
        <ConfirmDialog
          open
          title={
            pending.kind === "approve"
              ? "Fazla Mesaiyi Onayla"
              : "Fazla Mesai Kaydını Sil"
          }
          description={
            pending.kind === "approve"
              ? "Fazla mesai talebi onaylanacak ve puantaja işlenecek."
              : "Fazla mesai kaydı kalıcı olarak silinecek. Bu işlem geri alınamaz."
          }
          confirmLabel={pending.kind === "approve" ? "Onayla" : "Kaydı Sil"}
          busy={actionId === pending.item.id}
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={() =>
            pending.kind === "approve"
              ? void approve(pending.item)
              : void remove(pending.item)
          }
        />
      )}
    </ErpShell>
  );
}
