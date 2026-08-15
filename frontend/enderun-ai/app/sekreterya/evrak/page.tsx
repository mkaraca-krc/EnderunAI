"use client";

import {
  FormEvent,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog } from "@/components/ui";
import CorrespondenceDetailModal from "@/components/secretariat/correspondence-detail-modal";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  correspondenceService,
  CorrespondenceDirection,
  CorrespondenceStatus,
  type CorrespondenceItem,
} from "@/services/correspondence.service";

const directionLabels: Record<number, string> = {
  [CorrespondenceDirection.Incoming]: "Gelen Evrak",
  [CorrespondenceDirection.Outgoing]: "Giden Evrak",
};

const statusLabels: Record<number, string> = {
  [CorrespondenceStatus.Draft]: "Taslak",
  [CorrespondenceStatus.Registered]: "Kayıtlı",
  [CorrespondenceStatus.Assigned]: "Atandı",
  [CorrespondenceStatus.InProgress]: "İşlemde",
  [CorrespondenceStatus.Answered]: "Yanıtlandı",
  [CorrespondenceStatus.Completed]: "Tamamlandı",
  [CorrespondenceStatus.Archived]: "Arşivlendi",
  [CorrespondenceStatus.Cancelled]: "İptal",
};

function today() {
  return new Date().toISOString().slice(0, 10);
}

const initialForm = {
  companyId: "",
  projectId: "",
  direction: String(CorrespondenceDirection.Incoming),
  documentNumber: "",
  documentDate: today(),
  registrationDate: today(),
  subject: "",
  senderName: "",
  recipientName: "",
  institutionName: "",
  deliveryMethod: "",
  referenceNumber: "",
  description: "",
};

function formatDate(value?: string | null) {
  if (!value) {
    return "—";
  }

  return new Date(value).toLocaleDateString("tr-TR");
}

export default function CorrespondencePage() {
  const [companies, setCompanies] =
    useState<CompanyListItem[]>([]);

  const [projects, setProjects] =
    useState<ProjectListItem[]>([]);

  const [items, setItems] =
    useState<CorrespondenceItem[]>([]);

  const [form, setForm] = useState(initialForm);
  const [showForm, setShowForm] = useState(false);

  const [companyFilter, setCompanyFilter] = useState("");
  const [projectFilter, setProjectFilter] = useState("");
  const [directionFilter, setDirectionFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [search, setSearch] = useState("");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [processingId, setProcessingId] = useState("");
  const [error, setError] = useState("");

  /** Silinmek üzere onay bekleyen evrak. */
  const [pending, setPending] = useState<{
    id: string;
    direction: CorrespondenceDirection;
    subject: string;
  } | null>(null);
  const [success, setSuccess] = useState("");

  // Detay panelinde açık evrak. Yön de tutuluyor: uçlar evrakı
  // yön parametresiyle ayırıyor, id tek başına yetmiyor.
  const [detailTarget, setDetailTarget] = useState<{
    id: string;
    direction: CorrespondenceDirection;
  } | null>(null);

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
      const [companyRows, projectRows, documentRows] =
        await Promise.all([
          companyService.getAll(),
          projectService.getAll(),
          correspondenceService.getAll({
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
      setItems(documentRows);

      if (!form.companyId && companyRows.length === 1) {
        setForm((current) => ({
          ...current,
          companyId: companyRows[0].id,
        }));
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Evrak kayıtları yüklenemedi."
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
        (project) => project.id === projectFilter
      )
    ) {
      setProjectFilter("");
    }
  }, [filterProjects, projectFilter]);

  useEffect(() => {
    if (
      form.projectId &&
      !formProjects.some(
        (project) => project.id === form.projectId
      )
    ) {
      setForm((current) => ({
        ...current,
        projectId: "",
      }));
    }
  }, [formProjects, form.projectId]);

  async function createDocument(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      await correspondenceService.create({
        companyId: form.companyId,
        projectId: form.projectId || null,
        direction: Number(
          form.direction
        ) as CorrespondenceDirection,
        documentNumber: form.documentNumber.trim(),
        documentDate: form.documentDate,
        registrationDate: form.registrationDate,
        subject: form.subject.trim(),
        senderName: form.senderName.trim() || null,
        recipientName: form.recipientName.trim() || null,
        institutionName:
          form.institutionName.trim() || null,
        deliveryMethod:
          form.deliveryMethod.trim() || null,
        referenceNumber:
          form.referenceNumber.trim() || null,
        description: form.description.trim() || null,
      });

      setSuccess("Evrak başarıyla kaydedildi.");

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
          : "Evrak kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function deleteDocument(id: string, direction: CorrespondenceDirection) {
    setPending(null);
    setProcessingId(id);
    setError("");
    setSuccess("");

    try {
      await correspondenceService.delete(id, direction);
      setSuccess("Evrak kaydı silindi.");
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Evrak silinemedi."
      );
    } finally {
      setProcessingId("");
    }
  }

  return (
    <ErpShell design="redwood" title="Sekreterya">
      <div className="space-y-6">
        {/* Evrak kaydı başka kullanıcılarca giriliyor. */}
        <div className="flex justify-end">
          <Button variant="secondary" onClick={() => void load()}>Yenile</Button>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold">
              Gelen / Giden Evrak
            </h1>
            <p className="mt-1 text-sm text-slate-500">
              Sekreterya evrak kayıt defteri
            </p>
          </div>

          <button
            type="button"
            className="rounded-lg bg-brand-700 px-4 py-2 text-sm font-medium text-white"
            onClick={() => setShowForm((value) => !value)}
          >
            {showForm ? "Formu Kapat" : "Yeni Evrak"}
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
            onSubmit={createDocument}
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
                      companyId: event.target.value,
                      projectId: "",
                    }))
                  }
                >
                  <option value="">Şirket seçin</option>
                  {companies.map((company) => (
                    <option key={company.id} value={company.id}>
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
                      projectId: event.target.value,
                    }))
                  }
                >
                  <option value="">Proje bağlantısı yok</option>
                  {formProjects.map((project) => (
                    <option key={project.id} value={project.id}>
                      {project.code} - {project.name}
                    </option>
                  ))}
                </select>
              </label>

              <label className="space-y-1 text-sm">
                <span>Evrak Yönü</span>
                <select
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.direction}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      direction: event.target.value,
                    }))
                  }
                >
                  <option value="0">Gelen Evrak</option>
                  <option value="1">Giden Evrak</option>
                </select>
              </label>

              <label className="space-y-1 text-sm">
                <span>Evrak Numarası</span>
                <input
                  required
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.documentNumber}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      documentNumber: event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Evrak Tarihi</span>
                <input
                  required
                  type="date"
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.documentDate}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      documentDate: event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Kayıt Tarihi</span>
                <input
                  required
                  type="date"
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.registrationDate}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      registrationDate: event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm md:col-span-2">
                <span>Konu</span>
                <input
                  required
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.subject}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      subject: event.target.value,
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
                      institutionName: event.target.value,
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
                      senderName: event.target.value,
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
                      recipientName: event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Teslim Şekli</span>
                <input
                  className="w-full rounded-lg border px-3 py-2"
                  placeholder="E-posta, elden, kargo..."
                  value={form.deliveryMethod}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      deliveryMethod: event.target.value,
                    }))
                  }
                />
              </label>

              <label className="space-y-1 text-sm">
                <span>Referans Numarası</span>
                <input
                  className="w-full rounded-lg border px-3 py-2"
                  value={form.referenceNumber}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      referenceNumber: event.target.value,
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
                      description: event.target.value,
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
                {saving ? "Kaydediliyor..." : "Evrakı Kaydet"}
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
                setCompanyFilter(event.target.value);
                setProjectFilter("");
              }}
            >
              <option value="">Tüm şirketler</option>
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>

            <select
              className="rounded-lg border px-3 py-2 text-sm"
              value={projectFilter}
              onChange={(event) =>
                setProjectFilter(event.target.value)
              }
            >
              <option value="">Tüm projeler</option>
              {filterProjects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.code} - {project.name}
                </option>
              ))}
            </select>

            <select
              className="rounded-lg border px-3 py-2 text-sm"
              value={directionFilter}
              onChange={(event) =>
                setDirectionFilter(event.target.value)
              }
            >
              <option value="">Gelen ve giden</option>
              <option value="0">Gelen Evrak</option>
              <option value="1">Giden Evrak</option>
            </select>

            <select
              className="rounded-lg border px-3 py-2 text-sm"
              value={statusFilter}
              onChange={(event) =>
                setStatusFilter(event.target.value)
              }
            >
              <option value="">Tüm durumlar</option>
              {Object.entries(statusLabels).map(
                ([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                )
              )}
            </select>

            <input
              className="rounded-lg border px-3 py-2 text-sm"
              placeholder="Evrak no, konu, kurum..."
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
        </section>

        <section className="overflow-hidden rounded-2xl border bg-white shadow-sm">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[1000px] text-left text-sm">
              <thead className="border-b bg-slate-50">
                <tr>
                  <th className="px-4 py-3">Yön</th>
                  <th className="px-4 py-3">Evrak No</th>
                  <th className="px-4 py-3">Konu</th>
                  <th className="px-4 py-3">Kurum</th>
                  <th className="px-4 py-3">Gönderen / Alıcı</th>
                  <th className="px-4 py-3">Evrak Tarihi</th>
                  <th className="px-4 py-3">Durum</th>
                  <th className="px-4 py-3 text-right">İşlem</th>
                </tr>
              </thead>

              <tbody>
                {loading ? (
                  <tr>
                    <td className="px-4 py-8 text-center" colSpan={8}>
                      Yükleniyor...
                    </td>
                  </tr>
                ) : items.length === 0 ? (
                  <tr>
                    <td className="px-4 py-8 text-center" colSpan={8}>
                      Evrak kaydı bulunamadı.
                    </td>
                  </tr>
                ) : (
                  items.map((item) => (
                    <tr key={item.id} className="border-b last:border-0">
                      <td className="px-4 py-3">
                        {directionLabels[item.direction]}
                      </td>

                      <td className="px-4 py-3 font-medium">
                        {item.documentNumber}
                      </td>

                      <td className="max-w-xs px-4 py-3">
                        {item.subject}
                      </td>

                      <td className="px-4 py-3">
                        {item.institutionName || "—"}
                      </td>

                      <td className="px-4 py-3">
                        {item.direction ===
                        CorrespondenceDirection.Incoming
                          ? item.senderName || "—"
                          : item.recipientName || "—"}
                      </td>

                      <td className="px-4 py-3">
                        {formatDate(item.documentDate)}
                      </td>

                      <td className="px-4 py-3">
                        {statusLabels[item.status] ??
                          item.statusName}
                      </td>

                      <td className="px-4 py-3 text-right">
                        <div className="flex items-center justify-end gap-3">
                          <button
                            type="button"
                            onClick={() =>
                              setDetailTarget({
                                id: item.id,
                                direction: item.direction,
                              })
                            }
                            className="text-sm font-medium text-brand-700 underline"
                          >
                            Detay
                            {item.attachmentCount > 0 &&
                              ` (${item.attachmentCount} ek)`}
                          </button>

                          <button
                            type="button"
                            disabled={processingId === item.id}
                            onClick={() =>
                              setPending({
                                id: item.id,
                                direction: item.direction,
                                subject: item.subject,
                              })
                            }
                            className="text-sm font-medium text-red-600 disabled:opacity-50"
                          >
                            {processingId === item.id
                              ? "Siliniyor..."
                              : "Sil"}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </section>
      </div>

      <CorrespondenceDetailModal
        documentId={detailTarget?.id ?? null}
        direction={
          detailTarget?.direction ?? CorrespondenceDirection.Incoming
        }
        onClose={() => setDetailTarget(null)}
        onChanged={() => void load()}
      />
      {pending && (
        <ConfirmDialog
          open
          title="Evrak Kaydını Sil"
          description={`"${pending.subject}" kaydı kalıcı olarak silinecek. Bu işlem geri alınamaz.`}
          confirmLabel="Evrakı Sil"
          busy={processingId === pending.id}
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={() => void deleteDocument(pending.id, pending.direction)}
        />
      )}
    </ErpShell>
  );
}
