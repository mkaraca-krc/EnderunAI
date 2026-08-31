"use client";

import {
  FormEvent,
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
import { useModuleActions } from "@/lib/auth/module-actions";
import { Button, ConfirmDialog } from "@/components/ui";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

import {
  workTaskService,
  WorkTaskPriority,
  WorkTaskStatus,
  type WorkTask,
  type WorkTaskDashboard,
} from "@/services/work-task.service";

const priorityLabels: Record<number, string> = {
  [WorkTaskPriority.Low]: "Düşük",
  [WorkTaskPriority.Normal]: "Normal",
  [WorkTaskPriority.High]: "Yüksek",
  [WorkTaskPriority.Critical]: "Kritik",
};

const statusLabels: Record<number, string> = {
  [WorkTaskStatus.Draft]: "Taslak",
  [WorkTaskStatus.Open]: "Açık",
  [WorkTaskStatus.InProgress]: "Devam Ediyor",
  [WorkTaskStatus.Waiting]: "Bekliyor",
  [WorkTaskStatus.Completed]: "Tamamlandı",
  [WorkTaskStatus.Cancelled]: "İptal",
};

const priorityClasses: Record<number, string> = {
  [WorkTaskPriority.Low]: "gray",
  [WorkTaskPriority.Normal]: "blue",
  [WorkTaskPriority.High]: "yellow",
  [WorkTaskPriority.Critical]: "red",
};

const statusClasses: Record<number, string> = {
  [WorkTaskStatus.Draft]: "gray",
  [WorkTaskStatus.Open]: "blue",
  [WorkTaskStatus.InProgress]: "yellow",
  [WorkTaskStatus.Waiting]: "yellow",
  [WorkTaskStatus.Completed]: "green",
  [WorkTaskStatus.Cancelled]: "red",
};

const initialForm = {
  companyId: "",
  projectId: "",
  title: "",
  description: "",
  priority: String(WorkTaskPriority.Normal),
  startDate: "",
  dueDate: "",
  tags: "",
};

function formatDate(value?: string | null) {
  if (!value) {
    return "—";
  }

  return new Date(value).toLocaleDateString(
    "tr-TR"
  );
}

export default function WorkTasksPage() {
  /**
   * Düğme -> uç -> izin (WorkTasksController):
   *   POST tasks               -> tasks.manage
   *   POST tasks/{id}/start    -> tasks.manage
   *   POST tasks/{id}/complete -> tasks.manage
   *   POST tasks/{id}/cancel   -> tasks.manage
   *
   * BU MODÜLDE YETKİ AYRIMI YOK: oluşturma, başlatma, tamamlama ve
   * İPTAL aynı anahtarda. "Yıkıcı aksiyon delete yetkisi ister"
   * kuralını burada uygulayamam — tasks.delete diye bir anahtar yok
   * ve uç tek anahtar zorluyor. Arayüzde uydursaydım tasks.manage'i
   * olan kullanıcı iptal düğmesini göremez ama uca yine erişirdi.
   * Ayrım isteniyorsa ÖNCE uç bölünmeli (bkz. TEMIZLIK-TARAMASI.md).
   */
  const actions = useModuleActions("tasks");
  const [companies, setCompanies] = useState<
    CompanyListItem[]
  >([]);

  const [projects, setProjects] = useState<
    ProjectListItem[]
  >([]);

  const [items, setItems] = useState<WorkTask[]>([]);

  const [dashboard, setDashboard] =
    useState<WorkTaskDashboard | null>(null);

  const [form, setForm] = useState(initialForm);
  const [showForm, setShowForm] = useState(false);

  const [companyFilter, setCompanyFilter] =
    useState("");

  const [projectFilter, setProjectFilter] =
    useState("");

  const [statusFilter, setStatusFilter] =
    useState("");

  const [priorityFilter, setPriorityFilter] =
    useState("");

  const [overdueOnly, setOverdueOnly] =
    useState(false);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  /** Onay bekleyen görev işlemi. */
  const [pending, setPending] = useState<{
    kind: "complete" | "cancel";
    id: string;
  } | null>(null);

  const [processingId, setProcessingId] =
    useState("");

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const filteredFormProjects = useMemo(
    () =>
      projects.filter(
        (project) =>
          !form.companyId ||
          project.companyId === form.companyId
      ),
    [projects, form.companyId]
  );

  const filteredProjects = useMemo(
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
        taskRows,
        dashboardData,
      ] = await Promise.all([
        companyService.getAll(),
        projectService.getAll(),
        workTaskService.getAll({
          companyId:
            companyFilter || undefined,
          projectId:
            projectFilter || undefined,
          status:
            statusFilter === ""
              ? undefined
              : Number(statusFilter),
          priority:
            priorityFilter === ""
              ? undefined
              : Number(priorityFilter),
          overdueOnly,
        }),
        workTaskService.getDashboard(),
      ]);

      setCompanies(companyRows);
      setProjects(projectRows);
      setItems(taskRows.items);
      setDashboard(dashboardData);

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
          : "İş emirleri yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [
    companyFilter,
    projectFilter,
    statusFilter,
    priorityFilter,
    overdueOnly,
    form.companyId,
  ]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (
      projectFilter &&
      !filteredProjects.some(
        (project) =>
          project.id === projectFilter
      )
    ) {
      setProjectFilter("");
    }
  }, [filteredProjects, projectFilter]);

  useEffect(() => {
    if (
      form.projectId &&
      !filteredFormProjects.some(
        (project) =>
          project.id === form.projectId
      )
    ) {
      setForm((current) => ({
        ...current,
        projectId: "",
      }));
    }
  }, [
    filteredFormProjects,
    form.projectId,
  ]);

  async function createTask(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      await workTaskService.create({
        companyId: form.companyId,
        projectId: form.projectId || null,
        title: form.title.trim(),
        description:
          form.description.trim() || null,
        priority: Number(
          form.priority
        ) as WorkTaskPriority,
        assignedToUserId: null,
        startDate: form.startDate || null,
        dueDate: form.dueDate || null,
        sourceModule: "MANUAL",
        sourceEntityId: null,
        sourceEventCode: null,
        tags: form.tags.trim() || null,
      });

      setSuccess(
        "İş emri başarıyla oluşturuldu."
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
          : "İş emri oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  async function startTask(id: string) {
    setProcessingId(id);
    setError("");
    setSuccess("");

    try {
      await workTaskService.start(id);
      setSuccess("İş emri başlatıldı.");
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İş emri başlatılamadı."
      );
    } finally {
      setProcessingId("");
    }
  }

  /**
   * Görevi tamamla — not isteğe bağlı.
   *
   * Eskiden window.prompt sonucu `?? ""` ile karşılanıyordu: kullanıcı
   * "Vazgeç"e bassa bile null boş metne dönüşüyor ve GÖREV YİNE
   * TAMAMLANIYORDU. Diyalogdan çıkış yolu yoktu.
   */
  async function completeTask(id: string, note: string) {
    setPending(null);
    setProcessingId(id);
    setError("");
    setSuccess("");

    try {
      await workTaskService.complete(
        id,
        note.trim() || null
      );

      setSuccess("İş emri tamamlandı.");
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İş emri tamamlanamadı."
      );
    } finally {
      setProcessingId("");
    }
  }

  async function cancelTask(id: string, reason: string) {
    setPending(null);
    setProcessingId(id);
    setError("");
    setSuccess("");

    try {
      await workTaskService.cancel(
        id,
        reason.trim()
      );

      setSuccess("İş emri iptal edildi.");
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "İş emri iptal edilemedi."
      );
    } finally {
      setProcessingId("");
    }
  }


  /* Sütunlar `projects`, `actions` ve `processingId` üzerine kapanıyor;
     belleğe alınmıyor (bayat kapanış riski — F4b desen kararı). */
  const columns: DataTableColumn<WorkTask>[] = [
    {
      key: "gorev",
      header: "İş Emri",
      value: (item) =>
        `${item.taskNumber} — ${item.title}${item.isOverdue ? " (Gecikti)" : ""}`,
      render: (item) => (
        <>
          <strong>{item.taskNumber}</strong>
          <small>{item.title}</small>
          {item.isOverdue && <span className="erp-status red">Gecikti</span>}
        </>
      ),
    },
    {
      key: "proje",
      header: "Proje",
      value: (item) => {
        const project = projects.find((row) => row.id === item.projectId);
        return project ? `${project.code} — ${project.name}` : "—";
      },
      render: (item) => {
        const project = projects.find((row) => row.id === item.projectId);

        return project ? (
          <>
            <strong>{project.code}</strong>
            <small>{project.name}</small>
          </>
        ) : (
          "—"
        );
      },
    },
    {
      key: "oncelik",
      header: "Öncelik",
      value: (item) => priorityLabels[item.priority] ?? item.priorityName,
      render: (item) => (
        <span className={`erp-status ${priorityClasses[item.priority] ?? "gray"}`}>
          {priorityLabels[item.priority] ?? item.priorityName}
        </span>
      ),
    },
    {
      key: "durum",
      header: "Durum",
      value: (item) => statusLabels[item.status] ?? item.statusName,
      render: (item) => (
        <span className={`erp-status ${statusClasses[item.status] ?? "gray"}`}>
          {statusLabels[item.status] ?? item.statusName}
        </span>
      ),
    },
    {
      key: "baslangic",
      header: "Başlangıç",
      value: (item) => formatDate(item.startDate),
    },
    {
      key: "sonTarih",
      header: "Son Tarih",
      value: (item) => formatDate(item.dueDate),
    },
    {
      key: "kaynak",
      header: "Kaynak",
      value: (item) => item.sourceModule || "MANUAL",
    },
    {
      key: "islem",
      header: "İşlem",
      value: () => "",
      render: (item) => {
        const closed =
          item.status === WorkTaskStatus.Completed ||
          item.status === WorkTaskStatus.Cancelled;

        return (
          <div className="flex flex-wrap gap-2">
            {(item.status === WorkTaskStatus.Open ||
              item.status === WorkTaskStatus.Waiting) &&
              actions.can("manage") && (
                <button
                  type="button"
                  disabled={processingId === item.id}
                  onClick={() => void startTask(item.id)}
                >
                  Başlat
                </button>
              )}

            {!closed && actions.can("manage") && (
              <button
                type="button"
                disabled={processingId === item.id}
                onClick={() => setPending({ kind: "complete", id: item.id })}
              >
                Tamamla
              </button>
            )}

            {!closed && actions.can("manage") && (
              <button
                type="button"
                disabled={processingId === item.id}
                onClick={() => setPending({ kind: "cancel", id: item.id })}
              >
                İptal
              </button>
            )}
          </div>
        );
      },
    },
  ];


  return (
    <ErpShell
      design="redwood"
      title="İş Emirleri"
      description="Şirket, proje ve ERP süreçlerine bağlı iş emirlerini açın ve yönetin"
    >
      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      {success && (
        <div className="erp-alert success">
          {success}
        </div>
      )}

      <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-6">
        {[
          ["Açık", dashboard?.totalOpen ?? 0],
          [
            "Bana Atanan",
            dashboard?.assignedToMe ?? 0,
          ],
          [
            "Bugün Bitecek",
            dashboard?.dueToday ?? 0,
          ],
          ["Geciken", dashboard?.overdue ?? 0],
          ["Kritik", dashboard?.critical ?? 0],
          [
            "Bugün Tamamlanan",
            dashboard?.completedToday ?? 0,
          ],
        ].map(([label, value]) => (
          <div
            key={String(label)}
            className="rounded-xl border bg-white p-4"
          >
            <small>{label}</small>
            <strong className="mt-2 block text-2xl">
              {loading ? "…" : value}
            </strong>
          </div>
        ))}
      </div>

      <div className="erp-page-toolbar">
        {/* Görev ataması ve durum değişikliği ekip içinde yapılıyor. */}
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>

        <div>
          <strong>
            {loading ? "…" : items.length} iş emri
          </strong>
          <span> listelendi</span>
        </div>

        {actions.can("manage") && (
          <button
            type="button"
            className="erp-primary-button"
            onClick={() =>
              setShowForm((value) => !value)
            }
          >
            {showForm
              ? "Formu Kapat"
              : "+ Yeni İş Emri"}
          </button>
        )}
      </div>

      {showForm && (
        <form
          className="erp-form-card"
          onSubmit={createTask}
        >
          <div className="erp-form-header">
            <h2>Yeni İş Emri</h2>
            <p>
              Elle iş emri açın ve projeye
              bağlayın.
            </p>
          </div>

          <div className="erp-form-grid">
            <label>
              <span>Şirket *</span>
              <select
                required
                value={form.companyId}
                onChange={(event) =>
                  setForm({
                    ...form,
                    companyId:
                      event.target.value,
                    projectId: "",
                  })
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
                    {company.code} —{" "}
                    {company.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Proje</span>
              <select
                value={form.projectId}
                onChange={(event) =>
                  setForm({
                    ...form,
                    projectId:
                      event.target.value,
                  })
                }
              >
                <option value="">
                  Proje seçilmedi
                </option>

                {filteredFormProjects.map(
                  (project) => (
                    <option
                      key={project.id}
                      value={project.id}
                    >
                      {project.code} —{" "}
                      {project.name}
                    </option>
                  )
                )}
              </select>
            </label>

            <label className="span-2">
              <span>Başlık *</span>
              <input
                required
                maxLength={250}
                value={form.title}
                onChange={(event) =>
                  setForm({
                    ...form,
                    title: event.target.value,
                  })
                }
              />
            </label>

            <label className="span-2">
              <span>Açıklama</span>
              <textarea
                rows={4}
                value={form.description}
                onChange={(event) =>
                  setForm({
                    ...form,
                    description:
                      event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Öncelik</span>
              <select
                value={form.priority}
                onChange={(event) =>
                  setForm({
                    ...form,
                    priority:
                      event.target.value,
                  })
                }
              >
                {Object.entries(
                  priorityLabels
                ).map(([value, label]) => (
                  <option
                    key={value}
                    value={value}
                  >
                    {label}
                  </option>
                ))}
              </select>
            </label>

            <label>
              <span>Etiketler</span>
              <input
                placeholder="satın alma, acil"
                value={form.tags}
                onChange={(event) =>
                  setForm({
                    ...form,
                    tags: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Başlangıç</span>
              <input
                type="date"
                value={form.startDate}
                onChange={(event) =>
                  setForm({
                    ...form,
                    startDate:
                      event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Son Tarih</span>
              <input
                type="date"
                min={form.startDate || undefined}
                value={form.dueDate}
                onChange={(event) =>
                  setForm({
                    ...form,
                    dueDate:
                      event.target.value,
                  })
                }
              />
            </label>
          </div>

          <div className="erp-form-actions">
            <button
              type="button"
              className="erp-secondary-button"
              onClick={() =>
                setShowForm(false)
              }
            >
              Vazgeç
            </button>

            {actions.can("manage") && (
              <button
                type="submit"
                className="erp-primary-button"
                disabled={saving}
              >
                {saving
                  ? "Kaydediliyor..."
                  : "İş Emrini Kaydet"}
              </button>
            )}
          </div>
        </form>
      )}

      <div className="erp-form-card">
        <div className="erp-form-grid">
          <label>
            <span>Şirket</span>
            <select
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
                  {company.code} —{" "}
                  {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Proje</span>
            <select
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

              {filteredProjects.map(
                (project) => (
                  <option
                    key={project.id}
                    value={project.id}
                  >
                    {project.code} —{" "}
                    {project.name}
                  </option>
                )
              )}
            </select>
          </label>

          <label>
            <span>Durum</span>
            <select
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
          </label>

          <label>
            <span>Öncelik</span>
            <select
              value={priorityFilter}
              onChange={(event) =>
                setPriorityFilter(
                  event.target.value
                )
              }
            >
              <option value="">
                Tüm öncelikler
              </option>

              {Object.entries(
                priorityLabels
              ).map(([value, label]) => (
                <option
                  key={value}
                  value={value}
                >
                  {label}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Gecikme</span>
            <select
              value={
                overdueOnly ? "true" : ""
              }
              onChange={(event) =>
                setOverdueOnly(
                  event.target.value ===
                    "true"
                )
              }
            >
              <option value="">
                Tüm iş emirleri
              </option>
              <option value="true">
                Yalnızca gecikenler
              </option>
            </select>
          </label>
        </div>
      </div>

      <div className="erp-table-card">
        <DataTable
            rows={items}
            columns={columns}
            rowKey={(item) => item.id}
            loading={loading}
            title="İş Emirleri"
            emptyText="İş emri bulunamadı. Yeni bir iş emri açın veya filtreleri değiştirin."
            resetKey={`${projectFilter}|${statusFilter}|${priorityFilter}`}
          />
      </div>
      {pending && (
        <ConfirmDialog
          key={`${pending.kind}-${pending.id}`}
          open
          title={
            pending.kind === "complete" ? "İş Emrini Tamamla" : "İş Emrini İptal Et"
          }
          description={
            pending.kind === "complete"
              ? "İş emri tamamlandı olarak işaretlenecek. Tamamlama notu isteğe bağlı ama kayda geçer."
              : "İş emri iptal edilecek. İptal nedeni zorunlu; iş emrini açan kişi bunu görecek."
          }
          confirmLabel={
            pending.kind === "complete" ? "İş Emrini Tamamla" : "İş Emrini İptal Et"
          }
          requireReason={pending.kind === "cancel"}
          showReason
          reasonLabel={
            pending.kind === "complete"
              ? "Tamamlama notu (isteğe bağlı)"
              : "İptal nedeni (zorunlu)"
          }
          busy={processingId === pending.id}
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={(text) =>
            pending.kind === "complete"
              ? void completeTask(pending.id, text)
              : void cancelTask(pending.id, text)
          }
        />
      )}
    </ErpShell>
  );
}
