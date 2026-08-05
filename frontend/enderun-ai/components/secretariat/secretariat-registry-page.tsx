"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  PhoneNoteStatus,
  ScheduleStatus,
  secretariatPlannerService,
  type PhoneNoteItem,
  type ScheduleItem,
} from "@/services/secretariat-planner.service";

export type SecretariatRegistryMode =
  | "phone-notes"
  | "meetings"
  | "appointments";

type Props = {
  mode: SecretariatRegistryMode;
};

const phoneStatus: Record<number, string> = {
  [PhoneNoteStatus.New]: "Yeni",
  [PhoneNoteStatus.Informed]: "İletildi",
  [PhoneNoteStatus.Returned]: "Geri Dönüldü",
  [PhoneNoteStatus.Closed]: "Kapandı",
  [PhoneNoteStatus.Cancelled]: "İptal",
};

const scheduleStatus: Record<number, string> = {
  [ScheduleStatus.Planned]: "Planlandı",
  [ScheduleStatus.Confirmed]: "Teyit Edildi",
  [ScheduleStatus.Completed]: "Tamamlandı",
  [ScheduleStatus.Cancelled]: "İptal",
};

const configs = {
  "phone-notes": {
    title: "Telefon Notları",
    description: "Gelen aramaları kaydedin, ilgili kişiye iletin ve dönüş durumunu izleyin.",
    newLabel: "Yeni Telefon Notu",
  },
  meetings: {
    title: "Toplantılar",
    description: "Şirket içi ve dışı toplantıları, katılımcıları ve sonuç durumunu yönetin.",
    newLabel: "Yeni Toplantı",
  },
  appointments: {
    title: "Randevular",
    description: "Yönetim ve ekip randevularını tek takvimde planlayın ve teyit edin.",
    newLabel: "Yeni Randevu",
  },
} as const;

function localNow() {
  const value = new Date(Date.now() - new Date().getTimezoneOffset() * 60_000);
  return value.toISOString().slice(0, 16);
}

function localPlusHour() {
  const value = new Date(
    Date.now() - new Date().getTimezoneOffset() * 60_000 + 60 * 60_000
  );
  return value.toISOString().slice(0, 16);
}

const initialPhoneForm = {
  companyId: "",
  projectId: "",
  callerName: "",
  phoneNumber: "",
  institutionName: "",
  subject: "",
  message: "",
  responsibleName: "",
  receivedAtUtc: localNow(),
  notes: "",
};

const initialScheduleForm = {
  companyId: "",
  projectId: "",
  title: "",
  contactName: "",
  companyName: "",
  location: "",
  startAtUtc: localNow(),
  endAtUtc: localPlusHour(),
  ownerName: "",
  participants: "",
  description: "",
  reminderAtUtc: "",
  notes: "",
};

function formatDateTime(value?: string | null) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("tr-TR", {
    dateStyle: "short",
    timeStyle: "short",
  }).format(new Date(value));
}

function toIso(value: string) {
  return value ? new Date(value).toISOString() : null;
}

export default function SecretariatRegistryPage({ mode }: Props) {
  const config = configs[mode];
  const isPhone = mode === "phone-notes";
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [phoneItems, setPhoneItems] = useState<PhoneNoteItem[]>([]);
  const [scheduleItems, setScheduleItems] = useState<ScheduleItem[]>([]);
  const [phoneForm, setPhoneForm] = useState(initialPhoneForm);
  const [scheduleForm, setScheduleForm] = useState(initialScheduleForm);
  const [showForm, setShowForm] = useState(false);
  const [companyFilter, setCompanyFilter] = useState("");
  const [projectFilter, setProjectFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [processingId, setProcessingId] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const selectedCompany = isPhone
    ? phoneForm.companyId
    : scheduleForm.companyId;
  const selectedProject = isPhone
    ? phoneForm.projectId
    : scheduleForm.projectId;

  const formProjects = useMemo(
    () =>
      projects.filter(
        (project) =>
          !selectedCompany || project.companyId === selectedCompany
      ),
    [projects, selectedCompany]
  );

  const filterProjects = useMemo(
    () =>
      projects.filter(
        (project) =>
          !companyFilter || project.companyId === companyFilter
      ),
    [projects, companyFilter]
  );

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const filters = {
        companyId: companyFilter || undefined,
        projectId: projectFilter || undefined,
        status: statusFilter === "" ? undefined : Number(statusFilter),
        search: search || undefined,
      };
      const [companyRows, projectRows, records] = await Promise.all([
        companyService.getAll(),
        projectService.getAll(),
        isPhone
          ? secretariatPlannerService.phoneNotes.getAll(filters)
          : mode === "meetings"
            ? secretariatPlannerService.meetings.getAll(filters)
            : secretariatPlannerService.appointments.getAll(filters),
      ]);
      setCompanies(companyRows);
      setProjects(projectRows);
      if (isPhone) {
        setPhoneItems(records as PhoneNoteItem[]);
      } else {
        setScheduleItems(records as ScheduleItem[]);
      }
      if (companyRows.length === 1) {
        if (isPhone && !phoneForm.companyId) {
          setPhoneForm((current) => ({
            ...current,
            companyId: companyRows[0].id,
          }));
        }
        if (!isPhone && !scheduleForm.companyId) {
          setScheduleForm((current) => ({
            ...current,
            companyId: companyRows[0].id,
          }));
        }
      }
    } catch (cause) {
      setError(
        cause instanceof Error ? cause.message : "Sekreterya kayıtları yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [
    companyFilter,
    isPhone,
    mode,
    phoneForm.companyId,
    projectFilter,
    scheduleForm.companyId,
    search,
    statusFilter,
  ]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (
      projectFilter &&
      !filterProjects.some((project) => project.id === projectFilter)
    ) {
      setProjectFilter("");
    }
  }, [filterProjects, projectFilter]);

  useEffect(() => {
    if (
      selectedProject &&
      !formProjects.some((project) => project.id === selectedProject)
    ) {
      if (isPhone) {
        setPhoneForm((current) => ({ ...current, projectId: "" }));
      } else {
        setScheduleForm((current) => ({ ...current, projectId: "" }));
      }
    }
  }, [formProjects, isPhone, selectedProject]);

  async function createRecord(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");
    try {
      if (isPhone) {
        await secretariatPlannerService.phoneNotes.create({
          companyId: phoneForm.companyId,
          projectId: phoneForm.projectId || null,
          callerName: phoneForm.callerName.trim(),
          phoneNumber: phoneForm.phoneNumber.trim() || null,
          institutionName: phoneForm.institutionName.trim() || null,
          subject: phoneForm.subject.trim(),
          message: phoneForm.message.trim(),
          responsibleName: phoneForm.responsibleName.trim(),
          receivedAtUtc: toIso(phoneForm.receivedAtUtc),
          notes: phoneForm.notes.trim() || null,
        });
        setPhoneForm({
          ...initialPhoneForm,
          companyId: phoneForm.companyId,
        });
      } else {
        const payload = {
          companyId: scheduleForm.companyId,
          projectId: scheduleForm.projectId || null,
          title: scheduleForm.title.trim(),
          contactName: scheduleForm.contactName.trim() || null,
          companyName: scheduleForm.companyName.trim() || null,
          location: scheduleForm.location.trim() || null,
          startAtUtc: toIso(scheduleForm.startAtUtc)!,
          endAtUtc: toIso(scheduleForm.endAtUtc),
          ownerName: scheduleForm.ownerName.trim() || null,
          participants: scheduleForm.participants.trim() || null,
          description: scheduleForm.description.trim() || null,
          reminderAtUtc: toIso(scheduleForm.reminderAtUtc),
          notes: scheduleForm.notes.trim() || null,
        };
        if (mode === "meetings") {
          await secretariatPlannerService.meetings.create(payload);
        } else {
          await secretariatPlannerService.appointments.create(payload);
        }
        setScheduleForm({
          ...initialScheduleForm,
          companyId: scheduleForm.companyId,
        });
      }
      setSuccess(`${config.title.slice(0, -1)} kaydı oluşturuldu.`);
      setShowForm(false);
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Kayıt oluşturulamadı.");
    } finally {
      setSaving(false);
    }
  }

  async function setPhoneStatus(id: string, status: PhoneNoteStatus) {
    setProcessingId(id);
    setError("");
    try {
      await secretariatPlannerService.phoneNotes.setStatus(id, status);
      setSuccess("Telefon notunun durumu güncellendi.");
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Durum güncellenemedi.");
    } finally {
      setProcessingId("");
    }
  }

  async function setScheduleStatus(id: string, status: ScheduleStatus) {
    setProcessingId(id);
    setError("");
    try {
      const api =
        mode === "meetings"
          ? secretariatPlannerService.meetings
          : secretariatPlannerService.appointments;
      await api.setStatus(id, status);
      setSuccess("Takvim kaydının durumu güncellendi.");
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Durum güncellenemedi.");
    } finally {
      setProcessingId("");
    }
  }

  async function deleteRecord(id: string) {
    if (!window.confirm("Bu kaydı silmek istediğinize emin misiniz?")) return;
    setProcessingId(id);
    setError("");
    try {
      if (isPhone) {
        await secretariatPlannerService.phoneNotes.delete(id);
      } else if (mode === "meetings") {
        await secretariatPlannerService.meetings.delete(id);
      } else {
        await secretariatPlannerService.appointments.delete(id);
      }
      setSuccess("Kayıt silindi.");
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Kayıt silinemedi.");
    } finally {
      setProcessingId("");
    }
  }

  const counts = isPhone
    ? {
        total: phoneItems.length,
        active: phoneItems.filter((item) =>
          [PhoneNoteStatus.New, PhoneNoteStatus.Informed].includes(item.status)
        ).length,
        done: phoneItems.filter((item) =>
          [PhoneNoteStatus.Returned, PhoneNoteStatus.Closed].includes(item.status)
        ).length,
      }
    : {
        total: scheduleItems.length,
        active: scheduleItems.filter((item) =>
          [ScheduleStatus.Planned, ScheduleStatus.Confirmed].includes(item.status)
        ).length,
        done: scheduleItems.filter(
          (item) => item.status === ScheduleStatus.Completed
        ).length,
      };

  return (
    <ErpShell title={config.title} description={config.description}>
      <div className="space-y-6">
        <section className="grid gap-4 md:grid-cols-3">
          {[
            ["Toplam Kayıt", counts.total, "Tüm kayıtlar"],
            [isPhone ? "Açık Not" : "Aktif Plan", counts.active, "İşlem bekliyor"],
            ["Tamamlanan", counts.done, "Sonuçlandırılan"],
          ].map(([label, value, note]) => (
            <article key={String(label)} className="rounded-2xl border bg-white p-5 shadow-sm">
              <p className="text-sm text-slate-500">{label}</p>
              <p className="mt-2 text-3xl font-semibold text-slate-900">{value}</p>
              <p className="mt-1 text-xs text-slate-400">{note}</p>
            </article>
          ))}
        </section>

        <section className="rounded-2xl border bg-white p-5 shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-lg font-semibold">Kayıt Listesi</h2>
              <p className="text-sm text-slate-500">Filtreleyin, durumunu güncelleyin veya yeni kayıt açın.</p>
            </div>
            <button
              type="button"
              onClick={() => setShowForm((value) => !value)}
              className="rounded-lg bg-brand-700 px-4 py-2 text-sm font-medium text-white"
            >
              {showForm ? "Formu Kapat" : config.newLabel}
            </button>
          </div>

          <div className="mt-5 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            <select
              value={companyFilter}
              onChange={(event) => {
                setCompanyFilter(event.target.value);
                setProjectFilter("");
              }}
              className="rounded-lg border px-3 py-2 text-sm"
            >
              <option value="">Tüm şirketler</option>
              {companies.map((company) => (
                <option key={company.id} value={company.id}>{company.name}</option>
              ))}
            </select>
            <select
              value={projectFilter}
              onChange={(event) => setProjectFilter(event.target.value)}
              className="rounded-lg border px-3 py-2 text-sm"
            >
              <option value="">Tüm projeler</option>
              {filterProjects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code} - {project.name}
                </option>
              ))}
            </select>
            <select
              value={statusFilter}
              onChange={(event) => setStatusFilter(event.target.value)}
              className="rounded-lg border px-3 py-2 text-sm"
            >
              <option value="">Tüm durumlar</option>
              {Object.entries(isPhone ? phoneStatus : scheduleStatus).map(
                ([value, label]) => (
                  <option key={value} value={value}>{label}</option>
                )
              )}
            </select>
            <input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Ara..."
              className="rounded-lg border px-3 py-2 text-sm"
            />
          </div>
        </section>

        {error && (
          <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</div>
        )}
        {success && (
          <div className="rounded-lg border border-green-200 bg-green-50 p-3 text-sm text-green-700">{success}</div>
        )}

        {showForm && (
          <form onSubmit={createRecord} className="rounded-2xl border bg-white p-5 shadow-sm">
            <h2 className="text-lg font-semibold">{config.newLabel}</h2>
            <div className="mt-4 grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <label className="space-y-1 text-sm">
                <span>Şirket</span>
                <select
                  required
                  value={selectedCompany}
                  onChange={(event) => {
                    if (isPhone) {
                      setPhoneForm((current) => ({
                        ...current,
                        companyId: event.target.value,
                        projectId: "",
                      }));
                    } else {
                      setScheduleForm((current) => ({
                        ...current,
                        companyId: event.target.value,
                        projectId: "",
                      }));
                    }
                  }}
                  className="w-full rounded-lg border px-3 py-2"
                >
                  <option value="">Şirket seçin</option>
                  {companies.map((company) => (
                    <option key={company.id} value={company.id}>{company.name}</option>
                  ))}
                </select>
              </label>
              <label className="space-y-1 text-sm">
                <span>Proje</span>
                <select
                  value={selectedProject}
                  onChange={(event) => {
                    if (isPhone) {
                      setPhoneForm((current) => ({ ...current, projectId: event.target.value }));
                    } else {
                      setScheduleForm((current) => ({ ...current, projectId: event.target.value }));
                    }
                  }}
                  className="w-full rounded-lg border px-3 py-2"
                >
                  <option value="">Proje bağlantısı yok</option>
                  {formProjects.map((project) => (
                    <option key={project.id} value={project.id}>
                      {project.code} - {project.name}
                    </option>
                  ))}
                </select>
              </label>

              {isPhone ? (
                <>
                  <Field label="Arayan Kişi" required value={phoneForm.callerName} onChange={(value) => setPhoneForm((current) => ({ ...current, callerName: value }))} />
                  <Field label="Telefon" value={phoneForm.phoneNumber} onChange={(value) => setPhoneForm((current) => ({ ...current, phoneNumber: value }))} />
                  <Field label="Kurum / Firma" value={phoneForm.institutionName} onChange={(value) => setPhoneForm((current) => ({ ...current, institutionName: value }))} />
                  <Field label="İletilecek Kişi" required value={phoneForm.responsibleName} onChange={(value) => setPhoneForm((current) => ({ ...current, responsibleName: value }))} />
                  <Field label="Konu" required value={phoneForm.subject} onChange={(value) => setPhoneForm((current) => ({ ...current, subject: value }))} />
                  <Field label="Arama Zamanı" type="datetime-local" required value={phoneForm.receivedAtUtc} onChange={(value) => setPhoneForm((current) => ({ ...current, receivedAtUtc: value }))} />
                  <TextArea label="Mesaj" required value={phoneForm.message} onChange={(value) => setPhoneForm((current) => ({ ...current, message: value }))} />
                  <TextArea label="Notlar" value={phoneForm.notes} onChange={(value) => setPhoneForm((current) => ({ ...current, notes: value }))} />
                </>
              ) : (
                <>
                  <Field label={mode === "meetings" ? "Toplantı Başlığı" : "Randevu Başlığı"} required value={scheduleForm.title} onChange={(value) => setScheduleForm((current) => ({ ...current, title: value }))} />
                  <Field label="İlgili Kişi" value={scheduleForm.contactName} onChange={(value) => setScheduleForm((current) => ({ ...current, contactName: value }))} />
                  <Field label="Kurum / Firma" value={scheduleForm.companyName} onChange={(value) => setScheduleForm((current) => ({ ...current, companyName: value }))} />
                  <Field label="Yer / Bağlantı" value={scheduleForm.location} onChange={(value) => setScheduleForm((current) => ({ ...current, location: value }))} />
                  <Field label="Başlangıç" type="datetime-local" required value={scheduleForm.startAtUtc} onChange={(value) => setScheduleForm((current) => ({ ...current, startAtUtc: value }))} />
                  <Field label="Bitiş" type="datetime-local" value={scheduleForm.endAtUtc} onChange={(value) => setScheduleForm((current) => ({ ...current, endAtUtc: value }))} />
                  <Field label="Sorumlu" value={scheduleForm.ownerName} onChange={(value) => setScheduleForm((current) => ({ ...current, ownerName: value }))} />
                  <Field label="Hatırlatma" type="datetime-local" value={scheduleForm.reminderAtUtc} onChange={(value) => setScheduleForm((current) => ({ ...current, reminderAtUtc: value }))} />
                  <TextArea label="Katılımcılar" value={scheduleForm.participants} onChange={(value) => setScheduleForm((current) => ({ ...current, participants: value }))} />
                  <TextArea label="Açıklama" value={scheduleForm.description} onChange={(value) => setScheduleForm((current) => ({ ...current, description: value }))} />
                  <TextArea label="Notlar" value={scheduleForm.notes} onChange={(value) => setScheduleForm((current) => ({ ...current, notes: value }))} />
                </>
              )}
            </div>
            <div className="mt-5 flex justify-end">
              <button
                disabled={saving}
                className="rounded-lg bg-brand-700 px-5 py-2 text-sm font-medium text-white disabled:opacity-50"
              >
                {saving ? "Kaydediliyor..." : "Kaydet"}
              </button>
            </div>
          </form>
        )}

        <section className="overflow-hidden rounded-2xl border bg-white shadow-sm">
          {loading ? (
            <p className="p-6 text-sm text-slate-500">Kayıtlar yükleniyor...</p>
          ) : isPhone ? (
            <PhoneTable
              items={phoneItems}
              processingId={processingId}
              onStatus={setPhoneStatus}
              onDelete={deleteRecord}
            />
          ) : (
            <ScheduleTable
              items={scheduleItems}
              processingId={processingId}
              onStatus={setScheduleStatus}
              onDelete={deleteRecord}
            />
          )}
        </section>
      </div>
    </ErpShell>
  );
}

function Field({
  label,
  value,
  onChange,
  required,
  type = "text",
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  required?: boolean;
  type?: string;
}) {
  return (
    <label className="space-y-1 text-sm">
      <span>{label}</span>
      <input
        type={type}
        required={required}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="w-full rounded-lg border px-3 py-2"
      />
    </label>
  );
}

function TextArea({
  label,
  value,
  onChange,
  required,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  required?: boolean;
}) {
  return (
    <label className="space-y-1 text-sm">
      <span>{label}</span>
      <textarea
        required={required}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        rows={3}
        className="w-full rounded-lg border px-3 py-2"
      />
    </label>
  );
}

function PhoneTable({
  items,
  processingId,
  onStatus,
  onDelete,
}: {
  items: PhoneNoteItem[];
  processingId: string;
  onStatus: (id: string, status: PhoneNoteStatus) => void;
  onDelete: (id: string) => void;
}) {
  if (items.length === 0) {
    return <p className="p-6 text-sm text-slate-500">Telefon notu bulunamadı.</p>;
  }
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full text-sm">
        <thead className="bg-slate-50 text-left text-slate-500">
          <tr>
            <th className="px-4 py-3">Tarih</th>
            <th className="px-4 py-3">Arayan</th>
            <th className="px-4 py-3">Konu / Mesaj</th>
            <th className="px-4 py-3">İletilecek Kişi</th>
            <th className="px-4 py-3">Durum</th>
            <th className="px-4 py-3">İşlem</th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((item) => (
            <tr key={item.id}>
              <td className="whitespace-nowrap px-4 py-3">{formatDateTime(item.receivedAtUtc)}</td>
              <td className="px-4 py-3">
                <strong>{item.callerName}</strong>
                <p className="text-xs text-slate-500">{item.institutionName || item.phoneNumber || "—"}</p>
              </td>
              <td className="max-w-md px-4 py-3">
                <strong>{item.subject}</strong>
                <p className="mt-1 line-clamp-2 text-xs text-slate-500">{item.message}</p>
              </td>
              <td className="px-4 py-3">{item.responsibleName}</td>
              <td className="px-4 py-3">
                <span className="rounded-full bg-slate-100 px-2 py-1 text-xs">{phoneStatus[item.status]}</span>
              </td>
              <td className="whitespace-nowrap px-4 py-3">
                {item.status === PhoneNoteStatus.New && (
                  <button disabled={processingId === item.id} onClick={() => onStatus(item.id, PhoneNoteStatus.Informed)} className="mr-2 text-blue-700">İletildi</button>
                )}
                {item.status === PhoneNoteStatus.Informed && (
                  <button disabled={processingId === item.id} onClick={() => onStatus(item.id, PhoneNoteStatus.Returned)} className="mr-2 text-green-700">Dönüş Yapıldı</button>
                )}
                <button disabled={processingId === item.id} onClick={() => onDelete(item.id)} className="text-red-700">Sil</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ScheduleTable({
  items,
  processingId,
  onStatus,
  onDelete,
}: {
  items: ScheduleItem[];
  processingId: string;
  onStatus: (id: string, status: ScheduleStatus) => void;
  onDelete: (id: string) => void;
}) {
  if (items.length === 0) {
    return <p className="p-6 text-sm text-slate-500">Takvim kaydı bulunamadı.</p>;
  }
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full text-sm">
        <thead className="bg-slate-50 text-left text-slate-500">
          <tr>
            <th className="px-4 py-3">Tarih / Saat</th>
            <th className="px-4 py-3">Başlık</th>
            <th className="px-4 py-3">İlgili</th>
            <th className="px-4 py-3">Yer / Sorumlu</th>
            <th className="px-4 py-3">Durum</th>
            <th className="px-4 py-3">İşlem</th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((item) => (
            <tr key={item.id}>
              <td className="whitespace-nowrap px-4 py-3">
                <strong>{formatDateTime(item.startAtUtc)}</strong>
                <p className="text-xs text-slate-500">{item.endAtUtc ? `Bitiş: ${formatDateTime(item.endAtUtc)}` : "—"}</p>
              </td>
              <td className="max-w-sm px-4 py-3">
                <strong>{item.title}</strong>
                <p className="mt-1 line-clamp-2 text-xs text-slate-500">{item.description || item.participants || "—"}</p>
              </td>
              <td className="px-4 py-3">
                {item.contactName || "—"}
                <p className="text-xs text-slate-500">{item.companyName || ""}</p>
              </td>
              <td className="px-4 py-3">
                {item.location || "—"}
                <p className="text-xs text-slate-500">{item.ownerName || ""}</p>
              </td>
              <td className="px-4 py-3">
                <span className="rounded-full bg-slate-100 px-2 py-1 text-xs">{scheduleStatus[item.status]}</span>
              </td>
              <td className="whitespace-nowrap px-4 py-3">
                {item.status === ScheduleStatus.Planned && (
                  <button disabled={processingId === item.id} onClick={() => onStatus(item.id, ScheduleStatus.Confirmed)} className="mr-2 text-blue-700">Teyit</button>
                )}
                {[ScheduleStatus.Planned, ScheduleStatus.Confirmed].includes(item.status) && (
                  <button disabled={processingId === item.id} onClick={() => onStatus(item.id, ScheduleStatus.Completed)} className="mr-2 text-green-700">Tamamla</button>
                )}
                <button disabled={processingId === item.id} onClick={() => onDelete(item.id)} className="text-red-700">Sil</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
