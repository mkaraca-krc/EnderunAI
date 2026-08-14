"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { dateTime, money, percent } from "@/lib/format/turkish";
import GanttChart from "@/components/schedule/gantt-chart";
import { usePermissions } from "@/lib/use-permissions";
import {
  DELAY_PENALTY_KIND,
  DELAY_PENALTY_KIND_LABELS,
  DEPENDENCY_TYPE_HINTS,
  DEPENDENCY_TYPE_LABELS,
  PROGRESS_SOURCE,
  RESOURCE_KIND,
  WORK_WEEK,
  projectScheduleService,
  type BaselineRevision,
  type DelayPenaltyView,
  type ProjectSchedule,
  type ResourceConflict,
  type ResourceSuggestions,
  type ScheduleActivity,
} from "@/services/project-schedule.service";

function rate(value?: number | null) {
  return value == null
    ? "—"
    : percent(value, 2);
}

function formatDate(iso?: string | null) {
  return iso ? iso.slice(0, 10).split("-").reverse().join(".") : "—";
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

type ActivityForm = {
  id?: string;
  name: string;
  plannedStartDate: string;
  plannedEndDate: string;
  parentActivityId: string;
  manualProgressRate: string;
  notes: string;
};

const emptyActivity: ActivityForm = {
  name: "",
  plannedStartDate: today(),
  plannedEndDate: today(),
  parentActivityId: "",
  manualProgressRate: "",
  notes: "",
};

/**
 * İş Programı (Gantt).
 *
 * Çubuklar icmal KISIMLARINDAN doğar; iş programının ayrı bir iş kalemi
 * listesi yoktur. Gerçekleşen yüzde saha günlük raporundan gelir ve
 * yanında KAYNAĞI yazar — ölçülmüş bir oranla elle girilmiş bir oran
 * aynı görünürse ikisine de güvenilmez.
 */
export default function WorkSchedulePage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;

  // Düzenleme yetkisi olmayan kullanıcıya (saha) işe yaramayan düğme
  // göstermek, 403'e tıklatmaktan başka bir şey yapmaz. Gerçek kontrol
  // uçlarda; bu yalnızca arayüz nezaketi.
  const { has } = usePermissions();
  const canManage = has("schedule.manage");

  const [schedule, setSchedule] = useState<ProjectSchedule | null>(null);
  const [hasSchedule, setHasSchedule] = useState(true);
  const [sectionCount, setSectionCount] = useState(0);
  const [absenceMessage, setAbsenceMessage] = useState("");

  const [penalty, setPenalty] = useState<DelayPenaltyView | null>(null);
  const [revisions, setRevisions] = useState<BaselineRevision[]>([]);
  const [conflicts, setConflicts] = useState<ResourceConflict[]>([]);

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [suggestions, setSuggestions] = useState<{
    activityId: string;
    data: ResourceSuggestions;
  } | null>(null);

  const [activityForm, setActivityForm] = useState<ActivityForm | null>(null);
  const [showDeadlineForm, setShowDeadlineForm] = useState(false);

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [baselineOpen, setBaselineOpen] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const result = await projectScheduleService.get(projectId);

      setHasSchedule(result.hasSchedule);
      setSectionCount(result.sectionCount);
      setAbsenceMessage(result.message ?? "");
      setSchedule(result.schedule ?? null);

      if (result.schedule) {
        const [history, conflictList] = await Promise.all([
          projectScheduleService.baselineHistory(result.schedule.id),
          projectScheduleService.conflicts(result.schedule.id),
        ]);

        setRevisions(history);
        setConflicts(conflictList.items);
      }
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  // Ceza tutarı ayrı bir yetki kapısında (hakediş görüntüleme). Yetki
  // yoksa bölüm hiç görünmez — hata mesajı göstermek yanlış olurdu.
  useEffect(() => {
    void (async () => {
      try {
        const result = await projectScheduleService.delayPenalty(projectId);
        setPenalty(result);
      } catch {
        setPenalty(null);
      }
    })();
  }, [projectId]);

  // Öneriler seçili aktiviteyle birlikte saklanıyor: seçim değişince
  // eski listenin yeni aktiviteye aitmiş gibi görünmemesi için.
  useEffect(() => {
    if (!selectedId) return;

    let cancelled = false;

    void (async () => {
      try {
        const result = await projectScheduleService.resourceSuggestions(selectedId);
        if (!cancelled) setSuggestions({ activityId: selectedId, data: result });
      } catch {
        // Öneri alınamazsa seçim kutuları boş kalır; ekran yine çalışır.
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [selectedId]);

  const selected = useMemo(
    () => schedule?.activities.find((x) => x.id === selectedId) ?? null,
    [schedule, selectedId]
  );

  const topLevel = useMemo(
    () => schedule?.activities.filter((x) => !x.parentActivityId) ?? [],
    [schedule]
  );

  async function run(action: () => Promise<string>) {
    setBusy(true);
    setError("");
    setNotice("");

    try {
      setNotice(await action());
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  async function createSchedule() {
    await run(async () => {
      const result = await projectScheduleService.create(projectId, {
        seedFromSections: true,
        workWeek: WORK_WEEK.MondayToSaturday,
      });

      return result.message;
    });
  }

  async function saveActivity(form: ActivityForm) {
    if (!schedule) return;

    const body = {
      name: form.name.trim(),
      plannedStartDate: form.plannedStartDate,
      plannedEndDate: form.plannedEndDate,
      parentActivityId: form.parentActivityId || null,
      manualProgressRate: form.manualProgressRate
        ? Number(form.manualProgressRate)
        : null,
      notes: form.notes.trim() || null,
    };

    await run(async () => {
      if (form.id) {
        const current = schedule.activities.find((x) => x.id === form.id);

        const result = await projectScheduleService.updateActivity(form.id, {
          ...body,
          // İcmal bağları formdan değiştirilmiyor; korunuyor.
          projectHakedisSectionId: current?.sectionId ?? null,
          projectBoqItemId: current?.boqItemId ?? null,
        });

        setActivityForm(null);
        return result.message;
      }

      const result = await projectScheduleService.createActivity(schedule.id, body);
      setActivityForm(null);
      return result.message;
    });
  }

  /**
   * Baseline kaydetme.
   *
   * İLK KAYITTA gerekçe istenmez — referans yoktan var ediliyor.
   * SONRAKİ REVİZYONLARDA zorunlu: referans tarih değişince tüm
   * gecikme ölçüsü değişiyor ve bunun nedeni aylar sonra sorulacak.
   *
   * Eskiden window.prompt ile soruluyordu; metinde "(zorunlu)" yazsa
   * da tarayıcı boş metni kabul ediyordu, kod da bunu ancak sonradan
   * yakalayıp hata basıyordu. ConfirmDialog'da onay düğmesi gerekçe
   * yazılmadan zaten açılmıyor.
   */
  async function saveBaseline(reason: string | null) {
    if (!schedule) return;

    setBaselineOpen(false);

    await run(async () => {
      const result = await projectScheduleService.saveBaseline(schedule.id, reason);
      return result.message;
    });
  }

  if (loading) {
    return (
      <ErpShell design="redwood" title="İş Programı" description="Gantt, kritik yol ve gecikme takibi">
        <div className="erp-panel erp-loading">Yükleniyor...</div>
      </ErpShell>
    );
  }

  if (!hasSchedule) {
    return (
      <ErpShell design="redwood" title="İş Programı" description="Gantt, kritik yol ve gecikme takibi">
        {error && <div className="erp-alert error">{error}</div>}

        <div className="erp-page-toolbar">
          <Link className="erp-secondary-button" href={`/projeler/${projectId}`}>
            Proje Merkezi
          </Link>
          <Link
            className="erp-secondary-button"
            href={`/projeler/${projectId}/kisimlar`}
          >
            İcmal Kısımları
          </Link>
        </div>

        <section className="erp-panel">
          <div className="erp-empty-state">
            <strong>Bu proje için iş programı yok</strong>
            <p>{absenceMessage}</p>

            {!canManage ? (
              <p>
                İş programını açma yetkisi Genel Müdür ve Teknik
                Koordinatör{"’"}dedir.
              </p>
            ) : sectionCount > 0 ? (
              <button
                type="button"
                className="erp-primary-button"
                disabled={busy}
                onClick={createSchedule}
              >
                İş Programı Aç ({sectionCount} kısımdan çubuk oluştur)
              </button>
            ) : (
              <Link
                className="erp-primary-button"
                href={`/projeler/${projectId}/kisimlar`}
              >
                Önce İcmal Kısımlarını Tanımla
              </Link>
            )}
          </div>
        </section>
      </ErpShell>
    );
  }

  if (!schedule) return null;

  const delayed = schedule.delayWorkDays > 0;
  const deadlineFloat = schedule.deadlineFloatWorkDays;

  return (
    <ErpShell
      design="redwood"
      title={`İş Programı · ${schedule.projectCode}`}
      description={`${schedule.projectName} — Gantt, kritik yol ve gecikme takibi`}
    >
      {error && <div className="erp-alert error">{error}</div>}
      {notice && <div className="erp-alert success">{notice}</div>}

      {schedule.warnings.map((warning) => (
        <div key={warning} className="erp-alert warning">
          {warning}
        </div>
      ))}

      <div className="erp-page-toolbar">
        <Link className="erp-secondary-button" href={`/projeler/${projectId}`}>
          Proje Merkezi
        </Link>

        {canManage && (
          <>
            <button
              type="button"
              className="erp-secondary-button"
              disabled={busy}
              onClick={() =>
                run(async () => {
                  const result = await projectScheduleService.seedFromSections(
                    schedule.id
                  );
                  return result.message;
                })
              }
            >
              Kısımlardan Doldur
            </button>

            <button
              type="button"
              className="erp-secondary-button"
              disabled={busy}
              onClick={() => setActivityForm({ ...emptyActivity })}
            >
              Aktivite Ekle
            </button>

            <button
              type="button"
              className="erp-primary-button"
              disabled={busy}
              onClick={() =>
                schedule.baselineRevisionNumber > 0
                  ? setBaselineOpen(true)
                  : void saveBaseline(null)
              }
            >
              {schedule.baselineRevisionNumber === 0
                ? "Baseline Kaydet"
                : "Baseline'ı Yenile"}
            </button>
          </>
        )}

        {penalty && canManage && (
          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => setShowDeadlineForm((current) => !current)}
          >
            Termin & Gecikme Cezası
          </button>
        )}
      </div>

      <div className="erp-quick-grid">
        <div className="erp-panel">
          <small style={{ display: "block" }}>Gerçekleşen</small>
          <strong style={{ fontSize: 22 }}>{rate(schedule.progressRate)}</strong>
          <small style={{ display: "block" }}>
            {schedule.hasContractSummary
              ? `işveren kabulü ${rate(schedule.employerRate)}`
              : "icmal yok — elle girilen değerler"}
          </small>
        </div>

        <div className="erp-panel">
          <small style={{ display: "block" }}>Plan Bitişi</small>
          <strong style={{ fontSize: 18 }}>
            {formatDate(schedule.projectFinish)}
          </strong>
          <small style={{ display: "block" }}>
            başlangıç {formatDate(schedule.projectStart)}
          </small>
        </div>

        <div
          className="erp-panel"
          style={
            delayed
              ? { borderColor: "var(--color-semantic-danger)" }
              : undefined
          }
        >
          <small style={{ display: "block" }}>Tahmini Bitiş</small>
          <strong
            className={delayed ? "rw-value-danger" : undefined}
            style={{ fontSize: 18 }}
          >
            {formatDate(schedule.forecastFinish)}
          </strong>
          <small style={{ display: "block" }}>
            {delayed
              ? `${schedule.delayWorkDays} iş günü gecikme`
              : "plana göre gidiyor"}
          </small>
        </div>

        <div className="erp-panel">
          <small style={{ display: "block" }}>Termin</small>
          <strong style={{ fontSize: 18 }}>{formatDate(schedule.deadline)}</strong>
          <small style={{ display: "block" }}>
            {deadlineFloat == null
              ? "termin girilmemiş"
              : deadlineFloat < 0
                ? `${Math.abs(deadlineFloat)} iş günü aşıyor`
                : `${deadlineFloat} iş günü bolluk`}
            {schedule.hasContractDeadline ? " · sözleşmeden" : " · plandan"}
          </small>
        </div>
      </div>

      {penalty && showDeadlineForm && (
        <DeadlineForm
          projectId={projectId}
          penalty={penalty}
          busy={busy}
          onSaved={async (message) => {
            setNotice(message);
            setShowDeadlineForm(false);
            const refreshed = await projectScheduleService.delayPenalty(projectId);
            setPenalty(refreshed);
            await load();
          }}
          onError={setError}
        />
      )}

      {penalty?.penalty.applicable && (
        <div className="erp-alert warning">
          <strong>Tahmini gecikme cezası: </strong>
          {money(penalty.penalty.amount)}
          {penalty.penalty.capApplied && " (tavana dayandı)"} —{" "}
          {penalty.delayCalendarDays} takvim günü × günlük{" "}
          {money(penalty.penalty.dailyAmount)}. {penalty.disclaimer}
        </div>
      )}

      <section className="erp-table-card" style={{ marginTop: 16 }}>
        <div className="erp-table-header">
          <h2>Gantt Şeması</h2>
          <small>
            {schedule.workWeekName} · baseline{" "}
            {schedule.baselineRevisionNumber === 0
              ? "kaydedilmedi"
              : `${schedule.baselineRevisionNumber}. revizyon`}
          </small>
        </div>

        <div style={{ padding: 16 }}>
          <GanttChart
            activities={schedule.activities}
            dependencies={schedule.dependencies}
            workWeek={schedule.workWeek}
            holidays={schedule.holidays}
            deadline={schedule.deadline}
            asOf={schedule.asOf}
            selectedId={selectedId}
            onSelect={setSelectedId}
          />
        </div>
      </section>

      {activityForm && (
        <ActivityEditor
          form={activityForm}
          topLevel={topLevel}
          busy={busy}
          onChange={setActivityForm}
          onCancel={() => setActivityForm(null)}
          onSubmit={() => saveActivity(activityForm)}
        />
      )}

      {selected && (
        <ActivityPanel
          activity={selected}
          schedule={schedule}
          canManage={canManage}
          suggestions={
            suggestions?.activityId === selected.id ? suggestions.data : null
          }
          busy={busy}
          onEdit={() =>
            setActivityForm({
              id: selected.id,
              name: selected.name,
              plannedStartDate: selected.plannedStart.slice(0, 10),
              plannedEndDate: selected.plannedEnd.slice(0, 10),
              parentActivityId: selected.parentActivityId ?? "",
              manualProgressRate:
                selected.manualProgressRate == null
                  ? ""
                  : String(selected.manualProgressRate),
              notes: selected.notes ?? "",
            })
          }
          onRun={run}
          onClose={() => setSelectedId(null)}
        />
      )}

      <SectionProgressTable activities={schedule.activities} />

      <DependencyList
        schedule={schedule}
        busy={busy}
        canManage={canManage}
        onRun={run}
      />

      {conflicts.length > 0 && <ConflictList conflicts={conflicts} />}

      {revisions.length > 0 && <BaselineHistory revisions={revisions} />}

      <ConfirmDialog
        open={baselineOpen}
        title="Baseline'ı Yeniden Kaydet"
        description={
          `Bu ${schedule.baselineRevisionNumber}. revizyon olacak. ` +
          "Referans tarih değiştiğinde tüm gecikme ölçüsü de değişir; " +
          "eski baseline geçmişte kalır ama karşılaştırma bundan sonra " +
          "yeni tarihe göre yapılır."
        }
        confirmLabel="Baseline'ı Kaydet"
        requireReason
        busy={busy}
        error={error}
        onCancel={() => setBaselineOpen(false)}
        onConfirm={(reason) => void saveBaseline(reason)}
      />
    </ErpShell>
  );
}

/* -------------------------------------------------------------- */

function ActivityEditor({
  form,
  topLevel,
  busy,
  onChange,
  onCancel,
  onSubmit,
}: {
  form: ActivityForm;
  topLevel: ScheduleActivity[];
  busy: boolean;
  onChange: (form: ActivityForm) => void;
  onCancel: () => void;
  onSubmit: () => void;
}) {
  return (
    <section className="erp-panel erp-mt">
      <div className="erp-panel-header">
        <div>
          <h2>{form.id ? "Aktiviteyi Düzenle" : "Yeni Aktivite"}</h2>
          <p>
            Alt aktivite eklemek için üst çubuğu seçin. İcmal kısmına bağlı
            çubuklarda ilerleme saha raporundan gelir, elle girilemez.
          </p>
        </div>
      </div>

      <div className="erp-form-grid" style={{ padding: "0 16px 16px" }}>
        <label>
          <span>Aktivite Adı</span>
          <input
            value={form.name}
            onChange={(event) => onChange({ ...form, name: event.target.value })}
            placeholder="Kablo tavası montajı"
          />
        </label>

        <label>
          <span>Üst Aktivite</span>
          <select
            value={form.parentActivityId}
            onChange={(event) =>
              onChange({ ...form, parentActivityId: event.target.value })
            }
          >
            <option value="">— Ana çubuk —</option>
            {topLevel
              .filter((x) => x.id !== form.id)
              .map((activity) => (
                <option key={activity.id} value={activity.id}>
                  {activity.name}
                </option>
              ))}
          </select>
        </label>

        <label>
          <span>Planlanan Başlangıç</span>
          <input
            type="date"
            value={form.plannedStartDate}
            onChange={(event) =>
              onChange({ ...form, plannedStartDate: event.target.value })
            }
          />
        </label>

        <label>
          <span>Planlanan Bitiş</span>
          <input
            type="date"
            value={form.plannedEndDate}
            onChange={(event) =>
              onChange({ ...form, plannedEndDate: event.target.value })
            }
          />
        </label>

        <label>
          <span>İlerleme % (icmale bağlı değilse)</span>
          <input
            type="number"
            min={0}
            max={100}
            value={form.manualProgressRate}
            onChange={(event) =>
              onChange({ ...form, manualProgressRate: event.target.value })
            }
          />
        </label>

        <label className="span-2">
          <span>Not</span>
          <input
            value={form.notes}
            onChange={(event) => onChange({ ...form, notes: event.target.value })}
          />
        </label>
      </div>

      <div style={{ display: "flex", gap: 8, padding: "0 16px 16px" }}>
        <button
          type="button"
          className="erp-primary-button"
          disabled={busy || !form.name.trim()}
          onClick={onSubmit}
        >
          Kaydet
        </button>
        <button type="button" className="erp-secondary-button" onClick={onCancel}>
          Vazgeç
        </button>
      </div>
    </section>
  );
}

function ActivityPanel({
  activity,
  schedule,
  suggestions,
  busy,
  canManage,
  onEdit,
  onRun,
  onClose,
}: {
  activity: ScheduleActivity;
  schedule: ProjectSchedule;
  suggestions: ResourceSuggestions | null;
  busy: boolean;
  canManage: boolean;
  onEdit: () => void;
  onRun: (action: () => Promise<string>) => Promise<void>;
  onClose: () => void;
}) {
  const [resourceKind, setResourceKind] = useState<number>(RESOURCE_KIND.Personnel);
  const [resourceId, setResourceId] = useState("");
  const [role, setRole] = useState("");

  const [successorId, setSuccessorId] = useState("");
  const [dependencyType, setDependencyType] = useState(0);
  const [lag, setLag] = useState("0");

  return (
    <section className="erp-panel erp-mt">
      <div className="erp-panel-header">
        <div>
          <h2>{activity.name}</h2>
          <p>
            {activity.sectionName
              ? `İcmal kısmı: ${activity.sectionName}`
              : activity.boqItemCode
                ? `İcmal satırı: ${activity.boqItemCode} — ${activity.boqItemDescription}`
                : "İcmale bağlı değil"}
          </p>
        </div>
        <button type="button" className="erp-secondary-button" onClick={onClose}>
          Kapat
        </button>
      </div>

      <div className="erp-detail-grid" style={{ padding: "0 16px 12px" }}>
        <div>
          <span>Plan</span>
          <strong>
            {formatDate(activity.plannedStart)} – {formatDate(activity.plannedEnd)}
          </strong>
        </div>
        <div>
          <span>Baseline</span>
          <strong>
            {activity.baselineStart
              ? `${formatDate(activity.baselineStart)} – ${formatDate(activity.baselineEnd)}`
              : "kaydedilmedi"}
          </strong>
        </div>
        <div>
          <span>Süre / Bolluk</span>
          <strong>
            {activity.durationWorkDays} iş günü ·{" "}
            {activity.isCritical
              ? "KRİTİK YOL"
              : `${activity.totalFloatWorkDays} gün bolluk`}
          </strong>
        </div>
        <div>
          <span>Gerçekleşen</span>
          <strong>
            {rate(activity.progressRate)}
            {activity.progressSource === PROGRESS_SOURCE.None && " (ölçülemiyor)"}
          </strong>
          <small style={{ display: "block", color: "var(--erp-muted)" }}>
            {activity.progressSourceName}
            {activity.employerRate != null &&
              ` · işveren kabulü ${rate(activity.employerRate)}`}
          </small>
        </div>
        <div>
          <span>Beklenen (plana göre)</span>
          <strong className={activity.isBehind ? "rw-value-danger" : undefined}>
            {rate(activity.expectedRate)}
            {activity.isBehind && " — geride"}
          </strong>
        </div>
        <div>
          <span>Tahmini Bitiş</span>
          <strong>
            {formatDate(activity.forecastFinish)}
            {activity.slipWorkDays > 0 && ` (+${activity.slipWorkDays} gün)`}
          </strong>
          {activity.forecastNote && (
            <small style={{ display: "block", color: "var(--erp-muted)" }}>
              {activity.forecastNote}
            </small>
          )}
        </div>
        {activity.shiftedWorkDays > 0 && (
          <div className="span-2">
            <span>Bağımlılık kaydırması</span>
            <strong>
              Girilen tarih bağımlılık nedeniyle {activity.shiftedWorkDays} iş günü
              ileri alındı.
            </strong>
          </div>
        )}
        {activity.baselineSlipWorkDays != null &&
          activity.baselineSlipWorkDays !== 0 && (
            <div className="span-2">
              <span>Baseline sapması</span>
              <strong>
                Plan, kilitli referanstan {activity.baselineSlipWorkDays} iş günü
                kaymış.
              </strong>
            </div>
          )}
      </div>

      {canManage && (
        <div
          style={{ display: "flex", gap: 8, padding: "0 16px 16px", flexWrap: "wrap" }}
        >
          <button type="button" className="erp-secondary-button" onClick={onEdit}>
            Tarihleri Düzenle
          </button>
          <button
            type="button"
            className="erp-secondary-button"
            disabled={busy}
            onClick={() =>
              onRun(async () => {
                const result = await projectScheduleService.deleteActivity(
                  activity.id
                );
                return result.message;
              })
            }
          >
            Aktiviteyi Sil
          </button>
        </div>
      )}

      {/* Kaynaklar */}
      <div style={{ padding: "0 16px 16px" }}>
        <h3 style={{ fontSize: 14, marginBottom: 8 }}>Kaynaklar</h3>

        {activity.resources.length === 0 ? (
          <p style={{ fontSize: 13, color: "var(--erp-muted)" }}>
            Bu aktiviteye kimse atanmamış.
          </p>
        ) : (
          <ul style={{ listStyle: "none", padding: 0, margin: "0 0 10px" }}>
            {activity.resources.map((resource) => (
              <li
                key={resource.id}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 8,
                  padding: "4px 0",
                  fontSize: 13,
                }}
              >
                <span className="erp-status blue">{resource.kindName}</span>
                <strong>{resource.name}</strong>
                {resource.role && <small>{resource.role}</small>}
                {canManage && (
                  <button
                    type="button"
                    className="erp-secondary-button"
                    disabled={busy}
                    onClick={() =>
                      onRun(async () => {
                        const result = await projectScheduleService.removeResource(
                          resource.id
                        );
                        return result.message;
                      })
                    }
                  >
                    Kaldır
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}

        <div
          style={{
            display: canManage ? "flex" : "none",
            gap: 8,
            flexWrap: "wrap",
            alignItems: "center",
          }}
        >
          <select
            value={resourceKind}
            onChange={(event) => {
              setResourceKind(Number(event.target.value));
              setResourceId("");
            }}
          >
            <option value={RESOURCE_KIND.Personnel}>Personel</option>
            <option value={RESOURCE_KIND.Subcontractor}>Taşeron</option>
          </select>

          <select
            value={resourceId}
            onChange={(event) => setResourceId(event.target.value)}
          >
            <option value="">Seçiniz</option>
            {resourceKind === RESOURCE_KIND.Personnel
              ? suggestions?.personnel.map((person) => (
                  <option key={person.id} value={person.id}>
                    {person.name}
                    {person.onThisProject ? " · bu projede" : ""}
                  </option>
                ))
              : suggestions?.subcontractors.map((contract) => (
                  <option key={contract.id} value={contract.id}>
                    {contract.name}
                    {contract.coversSection ? " · bu kısmın taşeronu" : ""}
                  </option>
                ))}
          </select>

          <input
            value={role}
            placeholder="Rol (ör. ekip şefi)"
            onChange={(event) => setRole(event.target.value)}
          />

          <button
            type="button"
            className="erp-primary-button"
            disabled={busy || !resourceId}
            onClick={() =>
              onRun(async () => {
                const result = await projectScheduleService.assignResource(
                  activity.id,
                  {
                    kind: resourceKind,
                    personnelId:
                      resourceKind === RESOURCE_KIND.Personnel ? resourceId : null,
                    subcontractorContractId:
                      resourceKind === RESOURCE_KIND.Subcontractor
                        ? resourceId
                        : null,
                    role: role.trim() || null,
                  }
                );

                setResourceId("");
                setRole("");
                return result.message;
              })
            }
          >
            Ata
          </button>
        </div>
      </div>

      {/* Bağımlılık ekleme */}
      <div style={{ padding: "0 16px 16px", display: canManage ? "block" : "none" }}>
        <h3 style={{ fontSize: 14, marginBottom: 8 }}>Bu aktiviteden sonra gelen</h3>

        <div style={{ display: "flex", gap: 8, flexWrap: "wrap", alignItems: "center" }}>
          <select
            value={successorId}
            onChange={(event) => setSuccessorId(event.target.value)}
          >
            <option value="">Ardıl aktivite seçin</option>
            {schedule.activities
              .filter((x) => x.id !== activity.id)
              .map((x) => (
                <option key={x.id} value={x.id}>
                  {x.name}
                </option>
              ))}
          </select>

          <select
            value={dependencyType}
            onChange={(event) => setDependencyType(Number(event.target.value))}
          >
            {Object.entries(DEPENDENCY_TYPE_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>

          <input
            type="number"
            value={lag}
            onChange={(event) => setLag(event.target.value)}
            style={{ width: 90 }}
            title="Gecikme payı (iş günü). Negatif = örtüşme."
          />

          <button
            type="button"
            className="erp-primary-button"
            disabled={busy || !successorId}
            onClick={() =>
              onRun(async () => {
                const result = await projectScheduleService.createDependency(
                  schedule.id,
                  {
                    predecessorActivityId: activity.id,
                    successorActivityId: successorId,
                    type: dependencyType,
                    lagWorkDays: Number(lag) || 0,
                  }
                );

                setSuccessorId("");
                return result.message;
              })
            }
          >
            Bağla
          </button>
        </div>

        <small style={{ display: "block", marginTop: 6, color: "var(--erp-muted)" }}>
          {DEPENDENCY_TYPE_HINTS[dependencyType]} Gecikme payı iş günüdür; negatif
          değer örtüşme demektir.
        </small>
      </div>
    </section>
  );
}

function SectionProgressTable({ activities }: { activities: ScheduleActivity[] }) {
  const rows = activities.filter((x) => !x.parentActivityId);

  return (
    <section className="erp-table-card" style={{ marginTop: 16 }}>
      <div className="erp-table-header">
        <h2>Kısım Bazlı İlerleme</h2>
        <small>{rows.length} ana çubuk</small>
      </div>

      <div className="erp-table-wrap">
        <table className="erp-table">
          <thead>
            <tr>
              <th>Kısım / Aktivite</th>
              <th>Plan</th>
              <th>Gerçekleşen</th>
              <th>Beklenen</th>
              <th>Kaynak</th>
              <th>Bolluk</th>
              <th>Tahmini Bitiş</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((activity) => (
              <tr key={activity.id}>
                <td>
                  <strong>{activity.name}</strong>
                  {activity.isCritical && (
                    <span className="erp-status red" style={{ marginLeft: 6 }}>
                      Kritik yol
                    </span>
                  )}
                </td>
                <td>
                  {formatDate(activity.plannedStart)} –{" "}
                  {formatDate(activity.plannedEnd)}
                </td>
                <td>
                  <strong>{rate(activity.progressRate)}</strong>
                </td>
                <td
                  className={activity.isBehind ? "rw-value-danger" : undefined}
                >
                  {rate(activity.expectedRate)}
                </td>
                <td style={{ fontSize: 12 }}>{activity.progressSourceName}</td>
                <td>
                  {activity.isCritical ? "0" : activity.totalFloatWorkDays} gün
                </td>
                <td>
                  {formatDate(activity.forecastFinish)}
                  {activity.slipWorkDays > 0 && (
                    <small className="rw-value-danger" style={{ display: "block" }}>
                      +{activity.slipWorkDays} iş günü
                    </small>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function DependencyList({
  schedule,
  busy,
  canManage,
  onRun,
}: {
  schedule: ProjectSchedule;
  busy: boolean;
  canManage: boolean;
  onRun: (action: () => Promise<string>) => Promise<void>;
}) {
  if (schedule.dependencies.length === 0) return null;

  return (
    <section className="erp-table-card" style={{ marginTop: 16 }}>
      <div className="erp-table-header">
        <h2>Bağımlılıklar</h2>
        <small>{schedule.dependencies.length} bağ</small>
      </div>

      <div className="erp-table-wrap">
        <table className="erp-table">
          <thead>
            <tr>
              <th>Öncül</th>
              <th>Ardıl</th>
              <th>Tür</th>
              <th>Gecikme Payı</th>
              {canManage && <th>İşlem</th>}
            </tr>
          </thead>
          <tbody>
            {schedule.dependencies.map((dependency) => (
              <tr key={dependency.id}>
                <td>{dependency.predecessorName}</td>
                <td>{dependency.successorName}</td>
                <td>{dependency.typeName}</td>
                <td>
                  {dependency.lagWorkDays === 0
                    ? "—"
                    : `${dependency.lagWorkDays} iş günü`}
                </td>
                {canManage && (
                  <td>
                    <button
                      type="button"
                      className="erp-secondary-button"
                      disabled={busy}
                      onClick={() =>
                        onRun(async () => {
                          const result =
                            await projectScheduleService.deleteDependency(
                              dependency.id
                            );
                          return result.message;
                        })
                      }
                    >
                      Kaldır
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function ConflictList({ conflicts }: { conflicts: ResourceConflict[] }) {
  return (
    <section className="erp-table-card" style={{ marginTop: 16 }}>
      <div className="erp-table-header">
        <h2>Kaynak Çakışmaları</h2>
        <small>
          {conflicts.filter((x) => x.bothCritical).length} kritik ·{" "}
          {conflicts.length} toplam
        </small>
      </div>

      <div style={{ padding: "8px 16px 0", fontSize: 13, color: "var(--erp-muted)" }}>
        Çakışma bir hata değil, uyarıdır: bir ekip gerçekten iki işi birden
        yürütebilir. İki aktivite de kritik yoldaysa uyarı ağırlaşır.
      </div>

      <div className="erp-table-wrap">
        <table className="erp-table">
          <thead>
            <tr>
              <th>Kaynak</th>
              <th>Aktivite 1</th>
              <th>Aktivite 2</th>
              <th>Çakışma</th>
              <th>Durum</th>
            </tr>
          </thead>
          <tbody>
            {conflicts.map((conflict) => (
              <tr
                key={`${conflict.resourceId}-${conflict.firstActivityId}-${conflict.secondActivityId}`}
              >
                <td>
                  <strong>{conflict.resourceName}</strong>
                </td>
                <td>{conflict.firstActivityName}</td>
                <td>{conflict.secondActivityName}</td>
                <td>
                  {formatDate(conflict.overlapStart)} –{" "}
                  {formatDate(conflict.overlapFinish)}
                  <small style={{ display: "block" }}>
                    {conflict.overlapWorkDays} iş günü
                  </small>
                </td>
                <td>
                  <span
                    className={
                      conflict.bothCritical ? "erp-status red" : "erp-status orange"
                    }
                  >
                    {conflict.severity}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function BaselineHistory({ revisions }: { revisions: BaselineRevision[] }) {
  return (
    <section className="erp-table-card" style={{ marginTop: 16 }}>
      <div className="erp-table-header">
        <h2>Baseline Revizyonları</h2>
        <small>{revisions.length} kayıt</small>
      </div>

      <div style={{ padding: "8px 16px 0", fontSize: 13, color: "var(--erp-muted)" }}>
        Sık revizyon, planın gerçeğe uydurulduğunun işaretidir; bu yüzden her
        değişiklik gerekçesiyle kaydediliyor.
      </div>

      <div className="erp-table-wrap">
        <table className="erp-table">
          <thead>
            <tr>
              <th>Rev.</th>
              <th>Tarih</th>
              <th>Aktivite</th>
              <th>Plan Aralığı</th>
              <th>Gerekçe</th>
            </tr>
          </thead>
          <tbody>
            {revisions.map((revision) => (
              <tr key={revision.id}>
                <td>
                  <strong>{revision.revisionNumber}</strong>
                </td>
                <td>{dateTime(revision.setAtUtc)}</td>
                <td>{revision.activityCount}</td>
                <td>
                  {formatDate(revision.plannedStartDate)} –{" "}
                  {formatDate(revision.plannedEndDate)}
                </td>
                <td>{revision.reason || "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function DeadlineForm({
  projectId,
  penalty,
  busy,
  onSaved,
  onError,
}: {
  projectId: string;
  penalty: DelayPenaltyView;
  busy: boolean;
  onSaved: (message: string) => Promise<void>;
  onError: (message: string) => void;
}) {
  const [deadline, setDeadline] = useState(
    penalty.contractDeadlineDate?.slice(0, 10) ?? ""
  );
  const [kind, setKind] = useState(penalty.delayPenaltyKind);
  const [value, setValue] = useState(String(penalty.delayPenaltyValue ?? 0));
  const [capRate, setCapRate] = useState(
    penalty.delayPenaltyCapRate == null ? "" : String(penalty.delayPenaltyCapRate)
  );
  const [saving, setSaving] = useState(false);

  return (
    <section className="erp-panel erp-mt">
      <div className="erp-panel-header">
        <div>
          <h2>Termin ve Gecikme Cezası</h2>
          <p>
            Sözleşme termini planlanan bitişten ayrıdır: plan düzenlendikçe
            kayar, termin kaymaz.
          </p>
        </div>
      </div>

      <div className="erp-form-grid" style={{ padding: "0 16px 16px" }}>
        <label>
          <span>Sözleşme Termini</span>
          <input
            type="date"
            value={deadline}
            onChange={(event) => setDeadline(event.target.value)}
          />
          <small>Boş bırakılırsa projenin planlanan bitişi termin sayılır.</small>
        </label>

        <label>
          <span>Ceza Biçimi</span>
          <select
            value={kind}
            onChange={(event) => setKind(Number(event.target.value))}
          >
            {Object.entries(DELAY_PENALTY_KIND_LABELS).map(([key, label]) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </label>

        {kind !== DELAY_PENALTY_KIND.None && (
          <>
            <label>
              <span>
                {kind === DELAY_PENALTY_KIND.RateOfContractPerDay
                  ? "Günlük Oran (%)"
                  : "Günlük Tutar (TL)"}
              </span>
              <input
                type="number"
                step="0.0001"
                value={value}
                onChange={(event) => setValue(event.target.value)}
              />
              {kind === DELAY_PENALTY_KIND.RateOfContractPerDay && (
                <small>Binde 1 için 0,1 yazın.</small>
              )}
            </label>

            <label>
              <span>Ceza Tavanı (bedelin %&apos;si)</span>
              <input
                type="number"
                step="0.01"
                value={capRate}
                onChange={(event) => setCapRate(event.target.value)}
                placeholder="10"
              />
              <small>Boş bırakılırsa tavan uygulanmaz.</small>
            </label>
          </>
        )}
      </div>

      <div style={{ padding: "0 16px 16px" }}>
        <button
          type="button"
          className="erp-primary-button"
          disabled={busy || saving}
          onClick={() => {
            void (async () => {
              setSaving(true);

              try {
                const result = await projectScheduleService.updateDeadline(
                  projectId,
                  {
                    contractDeadlineDate: deadline || null,
                    delayPenaltyKind: kind,
                    delayPenaltyValue: Number(value) || 0,
                    delayPenaltyCapRate: capRate ? Number(capRate) : null,
                  }
                );

                await onSaved(result.message);
              } catch (err) {
                onError(messageOf(err));
              } finally {
                setSaving(false);
              }
            })();
          }}
        >
          Kaydet
        </button>
      </div>
    </section>
  );
}
