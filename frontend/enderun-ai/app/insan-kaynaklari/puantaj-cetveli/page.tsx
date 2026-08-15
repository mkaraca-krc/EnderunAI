"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { Button, EmptyState, Select } from "@/components/ui";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  projectSiteService,
  type ProjectSiteListItem,
} from "@/services/project-site.service";
import {
  ATTENDANCE_STATUS,
  ATTENDANCE_STATUS_LABELS,
  ATTENDANCE_STATUS_SHORT,
  attendanceSheetService,
  type AttendanceCell,
  type AttendanceSheet,
  type AttendanceSheetEntry,
} from "@/services/attendance-sheet.service";

const MONTHS = [
  "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
  "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
];

/** Durum → hücre rengi. Anlamsal: çalışıldı nötr, eksik kırmızı. */
const STATUS_STYLE: Record<number, string> = {
  0: "bg-red-100 text-red-800",
  1: "bg-white text-slate-700",
  2: "bg-brand-100 text-brand-800",
  3: "bg-amber-100 text-amber-900",
  4: "bg-emerald-100 text-emerald-800",
  5: "bg-slate-100 text-slate-400",
  6: "bg-red-50 text-red-700",
  7: "bg-amber-50 text-amber-800",
  8: "bg-brand-50 text-brand-700",
  9: "bg-slate-50 text-slate-600",
};

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

/** Bir hücrenin geçerli durumu: kayıt varsa o, yoksa öneri. */
function currentStatus(cell: AttendanceCell) {
  return cell.status ?? cell.suggestedStatus;
}

/**
 * Girilen mesai saati hangi kovaya gider.
 *
 * KÖPRÜNÜN KURALIYLA AYNI: genel tatil hafta tatilinden önce bakılır.
 * Gün başına tek kova olduğu için tek giriş kutusu yetiyor ve aynı
 * saat iki kovaya birden yazılamıyor.
 */
function overtimeKindOf(
  cell: AttendanceCell
): "publicHolidayHours" | "sundayHours" | "overtimeHours" {
  if (cell.isHoliday) return "publicHolidayHours";
  if (!cell.isWorkDay) return "sundayHours";

  return "overtimeHours";
}

/** Hücrenin toplam mesai saati; gün başına tek kova dolu olur. */
function currentOvertime(cell: AttendanceCell) {
  return (
    (cell.overtimeHours ?? 0) +
    (cell.sundayHours ?? 0) +
    (cell.publicHolidayHours ?? 0)
  );
}

const KIND_LABEL: Record<string, string> = {
  overtimeHours: "fazla çalışma ×1,5",
  sundayHours: "hafta tatili ×2",
  publicHolidayHours: "genel tatil ×2",
};

/**
 * Mesai kutusu ne zaman kilitli.
 *
 * YALNIZ İKİ SEBEP: gün onaylanmış (Ayı Onayla) ya da o günün mesaisi
 * onaylı bir fazla mesai talebinden geliyor. GÜN TÜRÜ ASLA KİLİTLEMEZ:
 * hafta tatili ve genel tatilde çalışmanın kendisi zaten mesaidir ve
 * en yüksek çarpana (×2) gider — kapatılsaydı en pahalı mesai hiç
 * girilemezdi.
 */
function isOvertimeLocked(cell: AttendanceCell) {
  return cell.isApproved || cell.overtimeLocked;
}

/**
 * Aylık puantaj cetveli.
 *
 * Izgara personel × gün. Cetvel resmî tatil takviminden DOLU gelir;
 * kullanıcı yalnızca istisnaları düzeltir — 79 kişi × 26 günü elle
 * doldurmak, puantajın bugüne kadar hiç tutulmamasının nedeniydi.
 *
 * Kaydetme ve onay TEK İSTEKTE gidiyor: eski ekran personel başına bir
 * istek atıyor ve yarısı geçip yarısı düşebiliyordu.
 */
export default function AttendanceSheetPage() {
  const today = new Date();

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [year, setYear] = useState(today.getFullYear());
  const [month, setMonth] = useState(today.getMonth() + 1);

  const [sheet, setSheet] = useState<AttendanceSheet | null>(null);

  /** Kaydedilmemiş düzeltmeler: "personelId|tarih" → durum. */
  const [draft, setDraft] = useState<Record<string, number>>({});

  /** Kaydedilmemiş mesai saatleri: "personelId|tarih" → saat metni. */
  const [overtimeDraft, setOvertimeDraft] = useState<Record<string, string>>({});

  /** Mesai sütunu açık mı: kapalıyken ızgara okunaklı kalıyor. */
  const [overtimeMode, setOvertimeMode] = useState(false);

  // Kapsam: merkez / şantiye ekseni ya da belirli bir proje-şantiye.
  const [scope, setScope] = useState<"" | "office" | "site" | "project">("");
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [projectId, setProjectId] = useState("");
  const [sites, setSites] = useState<ProjectSiteListItem[]>([]);
  const [projectSiteId, setProjectSiteId] = useState("");

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  /**
   * Ay onayı — geri alınamaz ve bordroya girer.
   *
   * Eskiden onay `run()` içindeki window.confirm ile soruluyordu
   * ve VAZGEÇEN kullanıcıya "Onay iptal edildi." mesajı BAŞARI
   * bildirimi olarak gösteriliyordu — hiçbir şey olmamışken
   * yeşil bir kutu çıkıyordu.
   */
  const [approveOpen, setApproveOpen] = useState(false);
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");

    try {
      setSheet(
        await attendanceSheetService.get(companyId, year, month, {
          workLocation:
            scope === "office" ? 1 : scope === "site" ? 2 : undefined,
          projectId: scope === "project" ? projectId || undefined : undefined,
          projectSiteId:
            scope === "project" ? projectSiteId || undefined : undefined,
        })
      );

      setDraft({});
      setOvertimeDraft({});
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }, [companyId, year, month, scope, projectId, projectSiteId]);

  useEffect(() => {
    void (async () => {
      try {
        const rows = await companyService.getAll();
        setCompanies(rows);

        const first = rows.find((x) => x.isActive !== false) ?? rows[0];
        if (first) setCompanyId((current) => current || first.id);
      } catch (err) {
        setError(messageOf(err));
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  // Proje listesi yalnız proje kapsamı seçilince gerekiyor.
  useEffect(() => {
    if (!companyId) return;

    void (async () => {
      try {
        const rows = await projectService.getAll(companyId);
        setProjects(rows);
      } catch {
        // Proje listesi alınamazsa kapsam seçimi çalışmaz ama cetvel
        // açılmaya devam eder; hata mesajı cetveli gizlememeli.
        setProjects([]);
      }
    })();
  }, [companyId]);

  useEffect(() => {
    void (async () => {
      if (!projectId) {
        setSites([]);
        return;
      }

      try {
        setSites(await projectSiteService.getAll(projectId));
      } catch {
        setSites([]);
      }
    })();
  }, [projectId]);

  // Aynı hücrenin hem durumu hem mesaisi değişmiş olabilir; sayım
  // hücre başına.
  const dirtyKeys = useMemo(
    () => [
      ...new Set([...Object.keys(draft), ...Object.keys(overtimeDraft)]),
    ],
    [draft, overtimeDraft]
  );

  const dirtyCount = dirtyKeys.length;

  const days = useMemo(
    () => sheet?.rows[0]?.cells ?? [],
    [sheet]
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

  /** Hücreye tıklayınca durumu sıradakine çevirir. */
  function cycle(personnelId: string, cell: AttendanceCell) {
    if (cell.isApproved) return;

    // Tıklama sırası: en sık kullanılandan seyreğe.
    const order: number[] = [
      ATTENDANCE_STATUS.Worked,
      ATTENDANCE_STATUS.PaidLeave,
      ATTENDANCE_STATUS.Absent,
      ATTENDANCE_STATUS.SickReport,
      ATTENDANCE_STATUS.UnpaidLeave,
      ATTENDANCE_STATUS.HalfDay,
      ATTENDANCE_STATUS.WeeklyHoliday,
      ATTENDANCE_STATUS.PublicHoliday,
    ];

    const key = `${personnelId}|${cell.date}`;
    const current = draft[key] ?? currentStatus(cell);
    const index = order.indexOf(current);
    const next = order[(index + 1) % order.length];

    setDraft((state) => ({ ...state, [key]: next }));
  }

  async function saveDraft() {
    if (!sheet || dirtyCount === 0) return;

    const entries: AttendanceSheetEntry[] = dirtyKeys.map((key) => {
      const [personnelId, date] = key.split("|");
      const cell = sheet.rows
        .find((row) => row.personnelId === personnelId)
        ?.cells.find((x) => x.date === date);

      const status =
        draft[key] ?? (cell ? currentStatus(cell) : ATTENDANCE_STATUS.Worked);

      const worksFullDay = status === ATTENDANCE_STATUS.Worked ||
        status === ATTENDANCE_STATUS.RemoteWork;

      const hours = worksFullDay
        ? sheet.dailyWorkHours
        : status === ATTENDANCE_STATUS.HalfDay
          ? sheet.dailyWorkHours / 2
          : 0;

      // Mevcut değerler aynen geri gönderiliyor: sıfır göndermek,
      // günlük puantajdan ya da onaylı mesaiden gelen tatil
      // saatlerini silmek demekti.
      const overtime = {
        overtimeHours: cell?.overtimeHours ?? 0,
        sundayHours: cell?.sundayHours ?? 0,
        publicHolidayHours: cell?.publicHolidayHours ?? 0,
      };

      // Mesai girildiyse günün türüne göre TEK kovaya yazılır,
      // diğerleri sıfırlanır — aynı saat iki kovada duramaz.
      if (key in overtimeDraft && cell) {
        const value = Number(overtimeDraft[key]) || 0;

        overtime.overtimeHours = 0;
        overtime.sundayHours = 0;
        overtime.publicHolidayHours = 0;
        overtime[overtimeKindOf(cell)] = value;
      }

      return {
        personnelId,
        workDate: date.slice(0, 10),
        status,
        normalHours: hours,
        ...overtime,
        description: null,
      };
    });

    await run(async () => {
      const result = await attendanceSheetService.save(companyId, entries);

      return result.message;
    });
  }

  return (
    <ErpShell
      design="redwood"
      title="Puantaj Cetveli"
      description="Aylık personel × gün ızgarası; takvimden dolar, istisnalar düzeltilir"
    >
      {/* Cetvel saha girişleriyle ve takvim değişikliğiyle güncelleniyor. */}
      <div className="mb-4 flex justify-end">
        <Button variant="secondary" disabled={busy} onClick={() => void load()}>Yenile</Button>
      </div>

      {error && (
        <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          {error}
        </div>
      )}
      {notice && (
        <div className="mb-4 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          {notice}
        </div>
      )}

      <div className="mb-4 flex flex-wrap items-end gap-2">
        <div className="w-64">
          <Select
            label="Şirket"
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
            options={companies.map((company) => ({
              value: company.id,
              label: `${company.code} · ${company.name}`,
            }))}
          />
        </div>

        <div className="w-36">
          <Select
            label="Ay"
            value={String(month)}
            onChange={(event) => setMonth(Number(event.target.value))}
            options={MONTHS.map((name, index) => ({
              value: String(index + 1),
              label: name,
            }))}
          />
        </div>

        <div className="w-28">
          <Select
            label="Yıl"
            value={String(year)}
            onChange={(event) => setYear(Number(event.target.value))}
            options={[year - 1, year, year + 1].map((value) => ({
              value: String(value),
              label: String(value),
            }))}
          />
        </div>

        <div className="w-44">
          <Select
            label="Görev yeri"
            value={scope}
            onChange={(event) => {
              const next = event.target.value as typeof scope;

              setScope(next);

              if (next !== "project") {
                setProjectId("");
                setProjectSiteId("");
              }
            }}
            options={[
              { value: "", label: "Tümü" },
              { value: "office", label: "Merkez" },
              { value: "site", label: "Şantiyeler" },
              { value: "project", label: "Belirli proje" },
            ]}
          />
        </div>

        {scope === "project" && (
          <div className="w-56">
            <Select
              label="Proje"
              value={projectId}
              onChange={(event) => {
                setProjectId(event.target.value);
                setProjectSiteId("");
              }}
              options={[
                { value: "", label: "Proje seçin" },
                ...projects.map((project) => ({
                  value: project.id,
                  label: `${project.code} · ${project.name}`,
                })),
              ]}
            />
          </div>
        )}

        {scope === "project" && projectId && (
          <div className="w-52">
            <Select
              label="Şantiye"
              value={projectSiteId}
              onChange={(event) => setProjectSiteId(event.target.value)}
              options={[
                { value: "", label: "Projenin tamamı" },
                ...sites.map((site) => ({
                  value: site.id,
                  label: site.name,
                })),
              ]}
            />
          </div>
        )}

        <Link href="/insan-kaynaklari/tatil-takvimi">
          <Button variant="secondary">Tatil Takvimi</Button>
        </Link>
      </div>

      {scope === "project" && (
        <p className="mb-4 text-xs text-slate-500">
          Proje kapsamında kadrolu atananlar ve o döneme denk gelen
          onaylı çalışma görevlendirmesiyle GEÇİCİ gelenler birlikte
          listelenir; ikisi de gün maliyeti bu projeye yazıldığı için
          puantajı buradan girilmeli.
        </p>
      )}

      {sheet && !sheet.holidayCalendarVerified && (
        <div className="mb-4 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          <strong>Tatil takvimi doğrulanmadı.</strong> {sheet.message}{" "}
          <Link
            href="/insan-kaynaklari/tatil-takvimi"
            className="underline"
          >
            Takvime git
          </Link>
        </div>
      )}

      {sheet && (
        <div className="mb-4 flex flex-wrap items-center gap-2">
          <Button
            disabled={busy || !sheet.holidayCalendarVerified}
            onClick={() =>
              run(async () => {
                const result = await attendanceSheetService.generate({
                  companyId,
                  year,
                  month,
                });

                return result.message;
              })
            }
          >
            Takvimden Doldur
          </Button>

          <Button
            variant={overtimeMode ? "primary" : "secondary"}
            disabled={busy}
            onClick={() => setOvertimeMode((current) => !current)}
          >
            {overtimeMode ? "Mesai Girişini Kapat" : "Mesai Girişi"}
          </Button>

          <Button
            variant="secondary"
            disabled={busy || dirtyCount === 0}
            onClick={() => void saveDraft()}
          >
            {dirtyCount === 0
              ? "Kaydedilecek değişiklik yok"
              : `${dirtyCount} değişikliği kaydet`}
          </Button>

          <Button
            variant="secondary"
            disabled={busy || sheet.recordCount === 0}
            onClick={() => setApproveOpen(true)}
          >
            Ayı Onayla
          </Button>

          <span className="ml-auto text-xs text-slate-500">
            {sheet.personnelCount} personel · {sheet.recordCount} gün kaydı ·{" "}
            {sheet.approvedCount} onaylı · günlük {sheet.dailyWorkHours} saat
          </span>
        </div>
      )}

      {loading ? (
        <div className="rounded-xl border border-slate-200 bg-white p-6 text-sm text-slate-500">
          Yükleniyor...
        </div>
      ) : !sheet || sheet.rows.length === 0 ? (
        <EmptyState
          title="Cetvele girecek aktif personel yok"
          description="Şirketi değiştirin ya da personel kartlarını kontrol edin."
        />
      ) : (
        <>
          <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white">
            <table className="min-w-full border-collapse text-left text-xs">
              <thead className="bg-slate-50 text-slate-500">
                <tr>
                  <th className="sticky left-0 z-10 bg-slate-50 px-3 py-2 text-left">
                    Personel
                  </th>
                  {days.map((day) => {
                    const date = new Date(day.date);

                    return (
                      <th
                        key={day.date}
                        className={`px-1 py-2 text-center font-normal ${
                          day.isHoliday
                            ? "bg-emerald-50 text-emerald-800"
                            : !day.isWorkDay
                              ? "bg-slate-100 text-slate-400"
                              : ""
                        }`}
                        title={day.holidayName ?? undefined}
                      >
                        <span className="block font-semibold">
                          {date.getUTCDate()}
                        </span>
                        <span className="block">
                          {["Pz", "Pt", "Sa", "Ça", "Pe", "Cu", "Ct"][
                            date.getUTCDay()
                          ]}
                        </span>
                      </th>
                    );
                  })}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {sheet.rows.map((row) => (
                  <tr key={row.personnelId}>
                    <td className="sticky left-0 z-10 whitespace-nowrap bg-white px-3 py-2">
                      <span className="block font-medium text-slate-800">
                        {row.fullName}
                      </span>
                      <span className="block text-[11px] text-slate-500">
                        {row.workWeekName} · {row.workWeekSource}
                      </span>
                    </td>

                    {row.cells.map((cell) => {
                      const key = `${row.personnelId}|${cell.date}`;
                      const status = draft[key] ?? currentStatus(cell);
                      const dirty = key in draft;

                      const overtimeValue =
                        overtimeDraft[key] ??
                        (currentOvertime(cell) > 0
                          ? String(currentOvertime(cell))
                          : "");

                      const overtimeDirty = key in overtimeDraft;

                      return (
                        <td key={cell.date} className="p-0.5 text-center align-top">
                          <button
                            type="button"
                            disabled={cell.isApproved || busy}
                            onClick={() => cycle(row.personnelId, cell)}
                            title={`${ATTENDANCE_STATUS_LABELS[status]}${
                              cell.holidayName ? ` · ${cell.holidayName}` : ""
                            }${cell.isApproved ? " · onaylı" : ""}`}
                            className={`h-7 w-7 rounded border text-[11px] ${
                              STATUS_STYLE[status] ?? "bg-white"
                            } ${
                              dirty
                                ? "border-brand-600 ring-1 ring-brand-400"
                                : "border-slate-200"
                            } ${
                              cell.isApproved
                                ? "cursor-not-allowed opacity-70"
                                : "cursor-pointer hover:border-brand-500"
                            }`}
                          >
                            {ATTENDANCE_STATUS_SHORT[status] ?? "?"}
                          </button>

                          {overtimeMode && (
                            <input
                              type="number"
                              min="0"
                              step="0.5"
                              inputMode="decimal"
                              value={overtimeValue}
                              disabled={isOvertimeLocked(cell) || busy}
                              onChange={(event) =>
                                setOvertimeDraft((state) => ({
                                  ...state,
                                  [key]: event.target.value,
                                }))
                              }
                              title={
                                cell.overtimeLocked
                                  ? "Bu günün mesaisi onaylı fazla mesai " +
                                    "talebinden geliyor; düzeltme talep " +
                                    "ekranından yapılır."
                                  : cell.isApproved
                                    ? "Gün onaylı; mesai değiştirilemez."
                                    : `Mesai saati · ${KIND_LABEL[overtimeKindOf(cell)]}`
                              }
                              className={`mt-0.5 h-6 w-7 rounded border text-center text-[10px] ${
                                isOvertimeLocked(cell)
                                  ? "cursor-not-allowed border-slate-200 bg-slate-100 text-slate-500"
                                  : overtimeDirty
                                    ? "border-brand-600 ring-1 ring-brand-400"
                                    : // Çalışma günü olmayan günlerde mesai ×2:
                                      // sütun gri olduğu için kutu ayrıca
                                      // vurgulanıyor, kapalı sanılmasın.
                                      overtimeKindOf(cell) === "overtimeHours"
                                      ? "border-slate-300 bg-white"
                                      : "border-amber-300 bg-amber-50 text-amber-900"
                              }`}
                            />
                          )}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="mt-3 flex flex-wrap gap-3 text-xs text-slate-600">
            {Object.entries(ATTENDANCE_STATUS_LABELS).map(([value, label]) => (
              <span key={value} className="inline-flex items-center gap-1">
                <span
                  className={`inline-flex h-5 w-5 items-center justify-center rounded border border-slate-200 ${
                    STATUS_STYLE[Number(value)]
                  }`}
                >
                  {ATTENDANCE_STATUS_SHORT[Number(value)]}
                </span>
                {label}
              </span>
            ))}
          </div>

          <p className="mt-2 text-xs text-slate-500">
            Hücreye tıklayınca durum sıradakine geçer. Onaylı günler
            değiştirilemez.
          </p>

          {overtimeMode && (
            <p className="mt-1 text-xs text-slate-500">
              Alt kutuya o günün mesai saati girilir. Saat günün türüne
              göre ayrılır: normal günde fazla çalışma (×1,5), hafta
              tatilinde ×2, genel tatilde ×2 — ayrı bir onay yok,{" "}
              <strong>Ayı Onayla</strong> ile kesinleşir.{" "}
              <span className="rounded border border-amber-300 bg-amber-50 px-1 text-amber-900">
                Sarı
              </span>{" "}
              kutular çalışma günü olmayan günler: oraya girilen saat
              ×2&apos;ye gider. Gri kutular kilitlidir — gün onaylanmış
              ya da o güne onaylı fazla mesai talebi var; düzeltme talep
              ekranından yapılır.
            </p>
          )}
        </>
      )}
      <ConfirmDialog
        open={approveOpen}
        title="Ayın Tamamını Onayla"
        description={
          `${sheet?.recordCount ?? 0} gün kaydı onaylanacak. Onaylanan günler ` +
          "artık DEĞİŞTİRİLEMEZ ve bordroya girer. Bu işlem geri alınamaz."
        }
        confirmLabel="Ayı Onayla"
        busy={busy}
        error={error}
        onCancel={() => setApproveOpen(false)}
        onConfirm={() => {
          setApproveOpen(false);
          void run(async () => {
            const result = await attendanceSheetService.approve({
              companyId,
              year,
              month,
            });

            return result.message;
          });
        }}
      />
    </ErpShell>
  );
}
