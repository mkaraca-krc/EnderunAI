"use client";

import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";

import {
  hrLeaveService,
  HrLeaveListItem,
} from "@/services/hr-leave.service";

import {
  personnelService,
  PersonnelListItem,
} from "@/services/personnel.service";

import {
  companyService,
  CompanyListItem,
} from "@/services/company.service";

type LeaveForm = {
  companyId: string;
  personnelId: string;
  leaveType: string;
  startDate: string;
  endDate: string;
  totalDays: string;
  reason: string;
  documentPath: string;
  status: string;
  approvalNote: string;
};

const initialForm: LeaveForm = {
  companyId: "",
  personnelId: "",
  leaveType: "0",
  startDate: "",
  endDate: "",
  totalDays: "1",
  reason: "",
  documentPath: "",
  status: "1",
  approvalNote: "",
};

const leaveTypes = [
  { value: 0, label: "Yıllık İzin" },
  { value: 1, label: "Ücretsiz İzin" },
  { value: 2, label: "Hastalık İzni" },
  { value: 3, label: "Sağlık Raporu" },
  { value: 4, label: "Doğum İzni" },
  { value: 5, label: "Babalık İzni" },
  { value: 6, label: "Ölüm İzni" },
  { value: 7, label: "Evlilik İzni" },
  { value: 8, label: "Mazeret İzni" },
  { value: 99, label: "Diğer" },
];

const approvalStatuses = [
  { value: 0, label: "Taslak" },
  { value: 1, label: "Onay Bekliyor" },
  { value: 2, label: "Onaylandı" },
  { value: 3, label: "Reddedildi" },
  { value: 4, label: "İptal Edildi" },
];

function leaveTypeLabel(value: number) {
  return (
    leaveTypes.find((item) => item.value === value)?.label ??
    "Diğer"
  );
}

function statusLabel(value: number) {
  return (
    approvalStatuses.find((item) => item.value === value)
      ?.label ?? "Bilinmiyor"
  );
}

function statusClasses(status: number) {
  if (status === 2) {
    return "border-emerald-200 bg-emerald-50 text-emerald-700";
  }

  if (status === 3 || status === 4) {
    return "border-red-200 bg-red-50 text-red-700";
  }

  if (status === 1) {
    return "border-amber-200 bg-amber-50 text-amber-700";
  }

  return "border-slate-200 bg-slate-50 text-slate-600";
}

function formatDate(value?: string | null) {
  if (!value) {
    return "—";
  }

  return new Date(value).toLocaleDateString("tr-TR");
}

function calculateDays(startDate: string, endDate: string) {
  if (!startDate || !endDate) {
    return 1;
  }

  const start = new Date(`${startDate}T00:00:00`);
  const end = new Date(`${endDate}T00:00:00`);

  if (
    Number.isNaN(start.getTime()) ||
    Number.isNaN(end.getTime()) ||
    end < start
  ) {
    return 1;
  }

  const milliseconds =
    end.getTime() - start.getTime();

  return Math.floor(milliseconds / 86400000) + 1;
}

export default function HrLeaveManagementPage() {
  const [items, setItems] = useState<HrLeaveListItem[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>(
    []
  );
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>(
    []
  );

  const [form, setForm] =
    useState<LeaveForm>(initialForm);

  const [editingId, setEditingId] = useState<string | null>(
    null
  );

  const [showForm, setShowForm] = useState(false);

  const [companyFilter, setCompanyFilter] = useState("");
  const [personnelFilter, setPersonnelFilter] = useState("");
  const [leaveTypeFilter, setLeaveTypeFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [startDateFilter, setStartDateFilter] = useState("");
  const [endDateFilter, setEndDateFilter] = useState("");
  const [search, setSearch] = useState("");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [actionId, setActionId] = useState<string | null>(
    null
  );

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  async function loadLeaves() {
    setLoading(true);
    setError("");

    try {
      const result = await hrLeaveService.getAll({
        companyId: companyFilter || undefined,
        personnelId: personnelFilter || undefined,
        leaveType:
          leaveTypeFilter === ""
            ? undefined
            : Number(leaveTypeFilter),
        status:
          statusFilter === ""
            ? undefined
            : Number(statusFilter),
        startDate: startDateFilter || undefined,
        endDate: endDateFilter || undefined,
      });

      setItems(result);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İzin kayıtları yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  async function loadReferenceData() {
    try {
      const [companyResult, personnelResult] =
        await Promise.all([
          companyService.getAll(),
          personnelService.getAll(),
        ]);

      setCompanies(companyResult);
      setPersonnel(personnelResult);

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
          : "Şirket ve personel bilgileri yüklenemedi."
      );
    }
  }

  useEffect(() => {
    async function loadPage() {
      await Promise.all([
        loadReferenceData(),
        loadLeaves(),
      ]);
    }

    loadPage();
    // İlk sayfa yüklemesinde çalışır.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!form.startDate || !form.endDate) {
      return;
    }

    const totalDays = calculateDays(
      form.startDate,
      form.endDate
    );

    setForm((current) => ({
      ...current,
      totalDays: String(totalDays),
    }));
  }, [form.startDate, form.endDate]);

  const personnelById = useMemo(() => {
    return new Map(
      personnel.map((person) => [person.id, person])
    );
  }, [personnel]);

  const filteredPersonnelForForm = useMemo(() => {
    if (!form.companyId) {
      return personnel;
    }

    return personnel.filter(
      (person) => person.companyId === form.companyId
    );
  }, [form.companyId, personnel]);

  const visibleItems = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");

    if (!term) {
      return items;
    }

    return items.filter((item) => {
      const person = personnelById.get(item.personnelId);

      const values = [
        person?.fullName,
        person?.employeeNumber,
        item.reason,
        leaveTypeLabel(item.leaveType),
        statusLabel(item.status),
      ]
        .filter(Boolean)
        .join(" ")
        .toLocaleLowerCase("tr-TR");

      return values.includes(term);
    });
  }, [items, personnelById, search]);

  const pendingCount = useMemo(
    () => items.filter((item) => item.status === 1).length,
    [items]
  );

  const approvedCount = useMemo(
    () => items.filter((item) => item.status === 2).length,
    [items]
  );

  const rejectedCount = useMemo(
    () =>
      items.filter(
        (item) => item.status === 3 || item.status === 4
      ).length,
    [items]
  );

  const approvedDays = useMemo(
    () =>
      items
        .filter((item) => item.status === 2)
        .reduce(
          (total, item) => total + Number(item.totalDays),
          0
        ),
    [items]
  );

  function updateForm<K extends keyof LeaveForm>(
    key: K,
    value: LeaveForm[K]
  ) {
    setForm((current) => ({
      ...current,
      [key]: value,
    }));
  }

  function openCreateForm() {
    setEditingId(null);
    setError("");
    setSuccess("");

    setForm({
      ...initialForm,
      companyId:
        companies.length === 1 ? companies[0].id : "",
    });

    setShowForm(true);
  }

  function openEditForm(item: HrLeaveListItem) {
    setEditingId(item.id);
    setError("");
    setSuccess("");

    setForm({
      companyId: item.companyId,
      personnelId: item.personnelId,
      leaveType: String(item.leaveType),
      startDate: item.startDate.slice(0, 10),
      endDate: item.endDate.slice(0, 10),
      totalDays: String(item.totalDays),
      reason: item.reason,
      documentPath: item.documentPath ?? "",
      status: String(item.status),
      approvalNote: item.approvalNote ?? "",
    });

    setShowForm(true);

    window.scrollTo({
      top: 0,
      behavior: "smooth",
    });
  }

  function closeForm() {
    setShowForm(false);
    setEditingId(null);
    setForm(initialForm);
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      if (!form.companyId) {
        throw new Error("Şirket seçilmelidir.");
      }

      if (!form.personnelId) {
        throw new Error("Personel seçilmelidir.");
      }

      if (!form.startDate || !form.endDate) {
        throw new Error(
          "Başlangıç ve bitiş tarihleri zorunludur."
        );
      }

      if (
        new Date(form.endDate) <
        new Date(form.startDate)
      ) {
        throw new Error(
          "Bitiş tarihi başlangıç tarihinden önce olamaz."
        );
      }

      if (!form.reason.trim()) {
        throw new Error("İzin nedeni zorunludur.");
      }

      const totalDays = Number(form.totalDays);

      if (!Number.isFinite(totalDays) || totalDays <= 0) {
        throw new Error(
          "Toplam izin günü sıfırdan büyük olmalıdır."
        );
      }

      if (editingId) {
        await hrLeaveService.update(editingId, {
          projectId: null,
          leaveType: Number(form.leaveType),
          startDate: form.startDate,
          endDate: form.endDate,
          totalDays,
          reason: form.reason.trim(),
          documentPath:
            form.documentPath.trim() || null,
          status: Number(form.status),
          approvalNote:
            form.approvalNote.trim() || null,
        });

        setSuccess(
          "İzin kaydı başarıyla güncellendi."
        );
      } else {
        await hrLeaveService.create({
          companyId: form.companyId,
          personnelId: form.personnelId,
          projectId: null,
          leaveType: Number(form.leaveType),
          startDate: form.startDate,
          endDate: form.endDate,
          totalDays,
          reason: form.reason.trim(),
          documentPath:
            form.documentPath.trim() || null,
        });

        setSuccess(
          "İzin talebi başarıyla oluşturuldu."
        );
      }

      closeForm();
      await loadLeaves();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İzin kaydı kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function handleApprove(item: HrLeaveListItem) {
    const confirmed = window.confirm(
      `${personnelById.get(item.personnelId)?.fullName ?? "Personel"} için izin talebi onaylansın mı?`
    );

    if (!confirmed) {
      return;
    }

    setActionId(item.id);
    setError("");
    setSuccess("");

    try {
      await hrLeaveService.approve(item.id);
      setSuccess("İzin talebi onaylandı.");
      await loadLeaves();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İzin talebi onaylanamadı."
      );
    } finally {
      setActionId(null);
    }
  }

  async function handleReject(item: HrLeaveListItem) {
    const note = window.prompt(
      "Reddetme gerekçesini yazın:"
    );

    if (note === null) {
      return;
    }

    if (!note.trim()) {
      setError("Reddetme gerekçesi zorunludur.");
      return;
    }

    setActionId(item.id);
    setError("");
    setSuccess("");

    try {
      await hrLeaveService.update(item.id, {
        projectId: item.projectId ?? null,
        leaveType: item.leaveType,
        startDate: item.startDate.slice(0, 10),
        endDate: item.endDate.slice(0, 10),
        totalDays: item.totalDays,
        reason: item.reason,
        documentPath: item.documentPath ?? null,
        status: 3,
        approvalNote: note.trim(),
      });

      setSuccess("İzin talebi reddedildi.");
      await loadLeaves();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İzin talebi reddedilemedi."
      );
    } finally {
      setActionId(null);
    }
  }

  async function handleDelete(item: HrLeaveListItem) {
    const person = personnelById.get(item.personnelId);

    const confirmed = window.confirm(
      `${person?.fullName ?? "Personel"} için izin kaydı silinsin mi?`
    );

    if (!confirmed) {
      return;
    }

    setActionId(item.id);
    setError("");
    setSuccess("");

    try {
      await hrLeaveService.delete(item.id);
      setSuccess("İzin kaydı silindi.");
      await loadLeaves();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İzin kaydı silinemedi."
      );
    } finally {
      setActionId(null);
    }
  }

  async function clearFilters() {
    setCompanyFilter("");
    setPersonnelFilter("");
    setLeaveTypeFilter("");
    setStatusFilter("");
    setStartDateFilter("");
    setEndDateFilter("");
    setSearch("");

    setLoading(true);
    setError("");

    try {
      setItems(await hrLeaveService.getAll());
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İzin kayıtları yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  return (
    <ErpShell
      title="İzin Yönetimi"
      description="Personel izin talepleri, onayları ve izin geçmişi"
    >
      {error && (
        <div className="mb-5 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {success && (
        <div className="mb-5 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          {success}
        </div>
      )}

      <div className="mb-5 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <article className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <span className="text-xs font-bold text-slate-500">
            TOPLAM İZİN
          </span>
          <strong className="mt-3 block text-3xl text-slate-800">
            {loading ? "…" : items.length}
          </strong>
          <small className="mt-1 block text-slate-500">
            Kayıtlı izin talebi
          </small>
        </article>

        <article className="rounded-xl border border-amber-200 bg-white p-5 shadow-sm">
          <span className="text-xs font-bold text-amber-700">
            ONAY BEKLİYOR
          </span>
          <strong className="mt-3 block text-3xl text-slate-800">
            {loading ? "…" : pendingCount}
          </strong>
          <small className="mt-1 block text-slate-500">
            Yönetici işlemi bekliyor
          </small>
        </article>

        <article className="rounded-xl border border-emerald-200 bg-white p-5 shadow-sm">
          <span className="text-xs font-bold text-emerald-700">
            ONAYLANAN
          </span>
          <strong className="mt-3 block text-3xl text-slate-800">
            {loading ? "…" : approvedCount}
          </strong>
          <small className="mt-1 block text-slate-500">
            Toplam {approvedDays} gün
          </small>
        </article>

        <article className="rounded-xl border border-red-200 bg-white p-5 shadow-sm">
          <span className="text-xs font-bold text-red-700">
            RED / İPTAL
          </span>
          <strong className="mt-3 block text-3xl text-slate-800">
            {loading ? "…" : rejectedCount}
          </strong>
          <small className="mt-1 block text-slate-500">
            Reddedilen veya iptal edilen
          </small>
        </article>
      </div>

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <span className="text-xs font-bold tracking-wide text-blue-700">
              İZİN İŞLEMLERİ
            </span>
            <h2 className="mt-1 text-xl font-bold text-slate-800">
              Personel izin kayıtları
            </h2>
          </div>

          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={loadLeaves}
              disabled={loading}
              className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-50 disabled:opacity-50"
            >
              {loading ? "Yükleniyor…" : "Yenile"}
            </button>

            <button
              type="button"
              onClick={openCreateForm}
              className="rounded-lg bg-blue-700 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-800"
            >
              + Yeni İzin Talebi
            </button>
          </div>
        </div>
      </section>

      {showForm && (
        <section className="mb-5 rounded-xl border border-blue-200 bg-white p-5 shadow-sm">
          <div className="mb-5 flex items-center justify-between gap-4">
            <div>
              <span className="text-xs font-bold text-blue-700">
                {editingId
                  ? "İZİN KAYDI DÜZENLE"
                  : "YENİ İZİN TALEBİ"}
              </span>

              <h3 className="mt-1 text-lg font-bold text-slate-800">
                {editingId
                  ? "İzin bilgilerini güncelleyin"
                  : "Personel izin talebi oluşturun"}
              </h3>
            </div>

            <button
              type="button"
              onClick={closeForm}
              className="rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-700 hover:bg-slate-50"
            >
              Kapat
            </button>
          </div>

          <form onSubmit={handleSubmit}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <label className="block">
                <span className="mb-2 block text-sm font-semibold text-slate-700">
                  Şirket *
                </span>

                <select
                  value={form.companyId}
                  disabled={Boolean(editingId)}
                  onChange={(event) => {
                    updateForm(
                      "companyId",
                      event.target.value
                    );
                    updateForm("personnelId", "");
                  }}
                  className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
                >
                  <option value="">Şirket seçin</option>

                  {companies.map((company) => (
                    <option
                      value={company.id}
                      key={company.id}
                    >
                      {company.name}
                    </option>
                  ))}
                </select>
              </label>

              <label className="block">
                <span className="mb-2 block text-sm font-semibold text-slate-700">
                  Personel *
                </span>

                <select
                  value={form.personnelId}
                  disabled={Boolean(editingId)}
                  onChange={(event) =>
                    updateForm(
                      "personnelId",
                      event.target.value
                    )
                  }
                  className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
                >
                  <option value="">Personel seçin</option>

                  {filteredPersonnelForForm.map((person) => (
                    <option
                      value={person.id}
                      key={person.id}
                    >
                      {person.employeeNumber} - {person.fullName}
                    </option>
                  ))}
                </select>
              </label>

              <label className="block">
                <span className="mb-2 block text-sm font-semibold text-slate-700">
                  İzin Türü *
                </span>

                <select
                  value={form.leaveType}
                  onChange={(event) =>
                    updateForm(
                      "leaveType",
                      event.target.value
                    )
                  }
                  className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
                >
                  {leaveTypes.map((type) => (
                    <option
                      value={type.value}
                      key={type.value}
                    >
                      {type.label}
                    </option>
                  ))}
                </select>
              </label>

              <label className="block">
                <span className="mb-2 block text-sm font-semibold text-slate-700">
                  Başlangıç Tarihi *
                </span>

                <input
                  type="date"
                  value={form.startDate}
                  onChange={(event) =>
                    updateForm(
                      "startDate",
                      event.target.value
                    )
                  }
                  className="w-full rounded-lg border border-slate-300 px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
                />
              </label>

              <label className="block">
                <span className="mb-2 block text-sm font-semibold text-slate-700">
                  Bitiş Tarihi *
                </span>

                <input
                  type="date"
                  min={form.startDate || undefined}
                  value={form.endDate}
                  onChange={(event) =>
                    updateForm(
                      "endDate",
                      event.target.value
                    )
                  }
                  className="w-full rounded-lg border border-slate-300 px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
                />
              </label>

              <label className="block">
                <span className="mb-2 block text-sm font-semibold text-slate-700">
                  Toplam Gün *
                </span>

                <input
                  type="number"
                  min="0.5"
                  step="0.5"
                  value={form.totalDays}
                  onChange={(event) =>
                    updateForm(
                      "totalDays",
                      event.target.value
                    )
                  }
                  className="w-full rounded-lg border border-slate-300 bg-slate-50 px-3 py-2.5 text-sm font-semibold text-slate-800 outline-none focus:border-blue-500"
                />
              </label>

              {editingId && (
                <label className="block">
                  <span className="mb-2 block text-sm font-semibold text-slate-700">
                    Durum
                  </span>

                  <select
                    value={form.status}
                    onChange={(event) =>
                      updateForm(
                        "status",
                        event.target.value
                      )
                    }
                    className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
                  >
                    {approvalStatuses.map((status) => (
                      <option
                        value={status.value}
                        key={status.value}
                      >
                        {status.label}
                      </option>
                    ))}
                  </select>
                </label>
              )}

              <label className="block">
                <span className="mb-2 block text-sm font-semibold text-slate-700">
                  Evrak / Belge Yolu
                </span>

                <input
                  value={form.documentPath}
                  onChange={(event) =>
                    updateForm(
                      "documentPath",
                      event.target.value
                    )
                  }
                  placeholder="/uploads/hr/izin-belgesi.pdf"
                  className="w-full rounded-lg border border-slate-300 px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
                />
              </label>

              <label className="block md:col-span-2 xl:col-span-3">
                <span className="mb-2 block text-sm font-semibold text-slate-700">
                  İzin Nedeni *
                </span>

                <textarea
                  rows={3}
                  value={form.reason}
                  onChange={(event) =>
                    updateForm(
                      "reason",
                      event.target.value
                    )
                  }
                  placeholder="İzin talebinin nedenini yazın"
                  className="w-full rounded-lg border border-slate-300 px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
                />
              </label>

              {editingId && (
                <label className="block md:col-span-2 xl:col-span-3">
                  <span className="mb-2 block text-sm font-semibold text-slate-700">
                    Onay / Red Açıklaması
                  </span>

                  <textarea
                    rows={2}
                    value={form.approvalNote}
                    onChange={(event) =>
                      updateForm(
                        "approvalNote",
                        event.target.value
                      )
                    }
                    className="w-full rounded-lg border border-slate-300 px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
                  />
                </label>
              )}
            </div>

            <div className="mt-5 flex flex-wrap justify-end gap-2">
              <button
                type="button"
                onClick={closeForm}
                className="rounded-lg border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold text-slate-700 hover:bg-slate-50"
              >
                Vazgeç
              </button>

              <button
                type="submit"
                disabled={saving}
                className="rounded-lg bg-blue-700 px-5 py-2.5 text-sm font-semibold text-white hover:bg-blue-800 disabled:opacity-50"
              >
                {saving
                  ? "Kaydediliyor…"
                  : editingId
                    ? "Değişiklikleri Kaydet"
                    : "İzin Talebini Oluştur"}
              </button>
            </div>
          </form>
        </section>
      )}

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="mb-4">
          <span className="text-xs font-bold text-blue-700">
            FİLTRELER
          </span>
          <h3 className="mt-1 text-lg font-bold text-slate-800">
            İzin kayıtlarını filtreleyin
          </h3>
        </div>

        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <input
            value={search}
            onChange={(event) =>
              setSearch(event.target.value)
            }
            placeholder="Personel, sicil veya açıklama ara"
            className="rounded-lg border border-slate-300 px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
          />

          <select
            value={companyFilter}
            onChange={(event) => {
              setCompanyFilter(event.target.value);
              setPersonnelFilter("");
            }}
            className="rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
          >
            <option value="">Tüm şirketler</option>

            {companies.map((company) => (
              <option value={company.id} key={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          <select
            value={personnelFilter}
            onChange={(event) =>
              setPersonnelFilter(event.target.value)
            }
            className="rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
          >
            <option value="">Tüm personeller</option>

            {personnel
              .filter(
                (person) =>
                  !companyFilter ||
                  person.companyId === companyFilter
              )
              .map((person) => (
                <option value={person.id} key={person.id}>
                  {person.fullName}
                </option>
              ))}
          </select>

          <select
            value={leaveTypeFilter}
            onChange={(event) =>
              setLeaveTypeFilter(event.target.value)
            }
            className="rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
          >
            <option value="">Tüm izin türleri</option>

            {leaveTypes.map((type) => (
              <option value={type.value} key={type.value}>
                {type.label}
              </option>
            ))}
          </select>

          <select
            value={statusFilter}
            onChange={(event) =>
              setStatusFilter(event.target.value)
            }
            className="rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
          >
            <option value="">Tüm durumlar</option>

            {approvalStatuses.map((status) => (
              <option
                value={status.value}
                key={status.value}
              >
                {status.label}
              </option>
            ))}
          </select>

          <input
            type="date"
            value={startDateFilter}
            onChange={(event) =>
              setStartDateFilter(event.target.value)
            }
            className="rounded-lg border border-slate-300 px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
          />

          <input
            type="date"
            value={endDateFilter}
            onChange={(event) =>
              setEndDateFilter(event.target.value)
            }
            className="rounded-lg border border-slate-300 px-3 py-2.5 text-sm text-slate-800 outline-none focus:border-blue-500"
          />

          <div className="flex gap-2">
            <button
              type="button"
              onClick={loadLeaves}
              className="flex-1 rounded-lg bg-slate-800 px-4 py-2.5 text-sm font-semibold text-white hover:bg-slate-900"
            >
              Filtrele
            </button>

            <button
              type="button"
              onClick={clearFilters}
              className="rounded-lg border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold text-slate-700 hover:bg-slate-50"
            >
              Temizle
            </button>
          </div>
        </div>
      </section>

      <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <div>
            <span className="text-xs font-bold text-blue-700">
              İZİN LİSTESİ
            </span>

            <h3 className="mt-1 font-bold text-slate-800">
              {visibleItems.length} kayıt
            </h3>
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="min-w-[1150px] w-full border-collapse">
            <thead>
              <tr className="bg-slate-50 text-left text-xs font-bold text-slate-500">
                <th className="px-4 py-3">Personel</th>
                <th className="px-4 py-3">İzin Türü</th>
                <th className="px-4 py-3">Başlangıç</th>
                <th className="px-4 py-3">Bitiş</th>
                <th className="px-4 py-3">Gün</th>
                <th className="px-4 py-3">Neden</th>
                <th className="px-4 py-3">Durum</th>
                <th className="px-4 py-3">Oluşturma</th>
                <th className="px-4 py-3 text-right">
                  İşlemler
                </th>
              </tr>
            </thead>

            <tbody>
              {loading && (
                <tr>
                  <td
                    colSpan={9}
                    className="px-4 py-12 text-center text-sm text-slate-500"
                  >
                    İzin kayıtları yükleniyor…
                  </td>
                </tr>
              )}

              {!loading && visibleItems.length === 0 && (
                <tr>
                  <td
                    colSpan={9}
                    className="px-4 py-12 text-center text-sm text-slate-500"
                  >
                    Filtrelere uygun izin kaydı bulunamadı.
                  </td>
                </tr>
              )}

              {!loading &&
                visibleItems.map((item) => {
                  const person = personnelById.get(
                    item.personnelId
                  );

                  const busy = actionId === item.id;

                  return (
                    <tr
                      key={item.id}
                      className="border-t border-slate-100 text-sm text-slate-700 hover:bg-slate-50"
                    >
                      <td className="px-4 py-4">
                        <strong className="block text-slate-800">
                          {person?.fullName ??
                            "Personel bulunamadı"}
                        </strong>

                        <small className="mt-1 block text-slate-500">
                          {person?.employeeNumber ?? "—"}
                        </small>
                      </td>

                      <td className="px-4 py-4">
                        {leaveTypeLabel(item.leaveType)}
                      </td>

                      <td className="px-4 py-4">
                        {formatDate(item.startDate)}
                      </td>

                      <td className="px-4 py-4">
                        {formatDate(item.endDate)}
                      </td>

                      <td className="px-4 py-4 font-semibold">
                        {item.totalDays}
                      </td>

                      <td className="max-w-[280px] px-4 py-4">
                        <span
                          className="block truncate"
                          title={item.reason}
                        >
                          {item.reason}
                        </span>

                        {item.approvalNote && (
                          <small className="mt-1 block text-slate-500">
                            Not: {item.approvalNote}
                          </small>
                        )}
                      </td>

                      <td className="px-4 py-4">
                        <span
                          className={`inline-flex rounded-full border px-2.5 py-1 text-xs font-bold ${statusClasses(
                            item.status
                          )}`}
                        >
                          {statusLabel(item.status)}
                        </span>
                      </td>

                      <td className="px-4 py-4">
                        {formatDate(item.createdAtUtc)}
                      </td>

                      <td className="px-4 py-4">
                        <div className="flex justify-end gap-2">
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => openEditForm(item)}
                            className="rounded-md border border-slate-300 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                          >
                            Düzenle
                          </button>

                          {item.status === 1 && (
                            <>
                              <button
                                type="button"
                                disabled={busy}
                                onClick={() =>
                                  handleApprove(item)
                                }
                                className="rounded-md bg-emerald-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-emerald-700 disabled:opacity-50"
                              >
                                Onayla
                              </button>

                              <button
                                type="button"
                                disabled={busy}
                                onClick={() =>
                                  handleReject(item)
                                }
                                className="rounded-md bg-amber-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-amber-700 disabled:opacity-50"
                              >
                                Reddet
                              </button>
                            </>
                          )}

                          <button
                            type="button"
                            disabled={busy}
                            onClick={() =>
                              handleDelete(item)
                            }
                            className="rounded-md border border-red-200 bg-red-50 px-3 py-1.5 text-xs font-semibold text-red-700 hover:bg-red-100 disabled:opacity-50"
                          >
                            {busy ? "…" : "Sil"}
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
            </tbody>
          </table>
        </div>
      </section>
    </ErpShell>
  );
}
