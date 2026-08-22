"use client";

import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog } from "@/components/ui";
import { useModuleActions } from "@/lib/auth/module-actions";

import {
  hrShiftService,
  HrShiftAssignmentItem,
  HrShiftItem,
} from "@/services/hr-shift.service";

import {
  companyService,
  CompanyListItem,
} from "@/services/company.service";

import {
  personnelService,
  PersonnelListItem,
} from "@/services/personnel.service";

import {
  projectService,
  ProjectListItem,
} from "@/services/project.service";
import { foldTurkish, matchesSearch } from "@/lib/search/fold";

type TabKey = "shifts" | "assignments";

type ShiftForm = {
  companyId: string;
  code: string;
  name: string;
  startTime: string;
  endTime: string;
  breakHours: string;
  dailyWorkingHours: string;
  isNightShift: boolean;
  description: string;
};

type AssignmentForm = {
  companyId: string;
  personnelId: string;
  shiftDefinitionId: string;
  projectId: string;
  startDate: string;
  endDate: string;
  teamName: string;
  description: string;
};

const initialShiftForm: ShiftForm = {
  companyId: "",
  code: "",
  name: "",
  startTime: "08:00",
  endTime: "17:00",
  breakHours: "1",
  dailyWorkingHours: "8",
  isNightShift: false,
  description: "",
};

const initialAssignmentForm: AssignmentForm = {
  companyId: "",
  personnelId: "",
  shiftDefinitionId: "",
  projectId: "",
  startDate: "",
  endDate: "",
  teamName: "",
  description: "",
};

function normalizeTime(value: string) {
  if (!value) {
    return "00:00:00";
  }

  return value.length === 5 ? `${value}:00` : value;
}

function inputTime(value?: string | null) {
  return value ? value.slice(0, 5) : "";
}

function formatDate(value?: string | null) {
  return value
    ? new Date(value).toLocaleDateString("tr-TR")
    : "—";
}

export default function WorkforceShiftPage() {
  /*
   * Aksiyon izinleri UÇLARDAN türetildi:
   *   yeni kayıt -> attendance-payroll.create
   *   güncelleme -> attendance-payroll.edit
   *   onay       -> attendance-payroll.approve
   *   silme      -> attendance-payroll.delete
   */
  const actions = useModuleActions("attendance-payroll");

  const [tab, setTab] = useState<TabKey>("shifts");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [shifts, setShifts] = useState<HrShiftItem[]>([]);
  const [assignments, setAssignments] =
    useState<HrShiftAssignmentItem[]>([]);

  const [shiftForm, setShiftForm] =
    useState<ShiftForm>(initialShiftForm);

  const [assignmentForm, setAssignmentForm] =
    useState<AssignmentForm>(initialAssignmentForm);

  const [editingShiftId, setEditingShiftId] =
    useState<string | null>(null);

  const [showShiftForm, setShowShiftForm] = useState(false);
  const [showAssignmentForm, setShowAssignmentForm] =
    useState(false);

  const [companyFilter, setCompanyFilter] = useState("");
  const [search, setSearch] = useState("");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  /** Silinmek üzere onay bekleyen vardiya ya da atama. */
  const [pending, setPending] = useState<
    | { kind: "shift"; item: HrShiftItem }
    | { kind: "assignment"; item: HrShiftAssignmentItem }
    | null
  >(null);

  const [actionId, setActionId] = useState<string | null>(null);

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const personnelById = useMemo(
    () => new Map(personnel.map((item) => [item.id, item])),
    [personnel]
  );

  const projectById = useMemo(
    () => new Map(projects.map((item) => [item.id, item])),
    [projects]
  );

  const shiftById = useMemo(
    () => new Map(shifts.map((item) => [item.id, item])),
    [shifts]
  );

  const visibleShifts = useMemo(() => {
    const term = foldTurkish(search);

    return shifts.filter((item) => {
      if (companyFilter && item.companyId !== companyFilter) {
        return false;
      }

      if (!term) {
        return true;
      }

      return matchesSearch(
        search,
        item.code,
        item.name,
        item.description,
      );
    });
  }, [shifts, companyFilter, search]);

  const visibleAssignments = useMemo(() => {
    const term = foldTurkish(search);

    return assignments.filter((item) => {
      if (companyFilter && item.companyId !== companyFilter) {
        return false;
      }

      if (!term) {
        return true;
      }

      const person = personnelById.get(item.personnelId);
      const shift = shiftById.get(item.shiftDefinitionId);
      const project = item.projectId
        ? projectById.get(item.projectId)
        : undefined;

      return matchesSearch(
        search,
        person?.fullName,
        person?.employeeNumber,
        shift?.name,
        shift?.code,
        project?.name,
        item.teamName,
      );
    });
  }, [
    assignments,
    companyFilter,
    personnelById,
    projectById,
    search,
    shiftById,
  ]);

  const filteredPersonnel = useMemo(() => {
    if (!assignmentForm.companyId) {
      return personnel;
    }

    return personnel.filter(
      (item) => item.companyId === assignmentForm.companyId
    );
  }, [assignmentForm.companyId, personnel]);

  const filteredShifts = useMemo(() => {
    if (!assignmentForm.companyId) {
      return shifts;
    }

    return shifts.filter(
      (item) => item.companyId === assignmentForm.companyId
    );
  }, [assignmentForm.companyId, shifts]);

  const nightShiftCount = shifts.filter(
    (item) => item.isNightShift
  ).length;

  const activeAssignmentCount = assignments.filter((item) => {
    const today = new Date().toISOString().slice(0, 10);

    return (
      item.startDate.slice(0, 10) <= today &&
      (!item.endDate || item.endDate.slice(0, 10) >= today)
    );
  }).length;

  async function loadAll() {
    setLoading(true);
    setError("");

    try {
      const [
        companyResult,
        personnelResult,
        projectResult,
        shiftResult,
        assignmentResult,
      ] = await Promise.all([
        companyService.getAll(),
        personnelService.getAll(),
        projectService.getAll(),
        hrShiftService.getShifts(),
        hrShiftService.getAssignments(),
      ]);

      setCompanies(companyResult);
      setPersonnel(personnelResult);
      setProjects(projectResult);
      setShifts(shiftResult);
      setAssignments(assignmentResult);

      if (companyResult.length === 1) {
        const companyId = companyResult[0].id;

        setShiftForm((current) => ({
          ...current,
          companyId,
        }));

        setAssignmentForm((current) => ({
          ...current,
          companyId,
        }));
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Vardiya ekranı yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    loadAll();
  }, []);

  function openCreateShift() {
    setEditingShiftId(null);
    setShiftForm({
      ...initialShiftForm,
      companyId:
        companies.length === 1 ? companies[0].id : "",
    });
    setShowShiftForm(true);
  }

  function openEditShift(item: HrShiftItem) {
    setEditingShiftId(item.id);

    setShiftForm({
      companyId: item.companyId,
      code: item.code,
      name: item.name,
      startTime: inputTime(item.startTime),
      endTime: inputTime(item.endTime),
      breakHours: String(item.breakHours),
      dailyWorkingHours: String(item.dailyWorkingHours),
      isNightShift: item.isNightShift,
      description: item.description ?? "",
    });

    setShowShiftForm(true);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  function openCreateAssignment() {
    setAssignmentForm({
      ...initialAssignmentForm,
      companyId:
        companies.length === 1 ? companies[0].id : "",
    });

    setShowAssignmentForm(true);
  }

  async function saveShift(event: FormEvent) {
    event.preventDefault();

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      const breakHours = Number(shiftForm.breakHours);
      const dailyWorkingHours = Number(
        shiftForm.dailyWorkingHours
      );

      if (!shiftForm.companyId) {
        throw new Error("Şirket seçilmelidir.");
      }

      if (!shiftForm.code.trim()) {
        throw new Error("Vardiya kodu zorunludur.");
      }

      if (!shiftForm.name.trim()) {
        throw new Error("Vardiya adı zorunludur.");
      }

      if (!shiftForm.startTime || !shiftForm.endTime) {
        throw new Error(
          "Başlangıç ve bitiş saatleri zorunludur."
        );
      }

      if (!Number.isFinite(breakHours) || breakHours < 0) {
        throw new Error("Mola süresi geçersizdir.");
      }

      if (
        !Number.isFinite(dailyWorkingHours) ||
        dailyWorkingHours <= 0
      ) {
        throw new Error(
          "Günlük çalışma saati sıfırdan büyük olmalıdır."
        );
      }

      const payload = {
        code: shiftForm.code.trim(),
        name: shiftForm.name.trim(),
        startTime: normalizeTime(shiftForm.startTime),
        endTime: normalizeTime(shiftForm.endTime),
        breakHours,
        dailyWorkingHours,
        isNightShift: shiftForm.isNightShift,
        description: shiftForm.description.trim() || null,
      };

      if (editingShiftId) {
        await hrShiftService.updateShift(
          editingShiftId,
          payload
        );

        setSuccess("Vardiya güncellendi.");
      } else {
        await hrShiftService.createShift({
          companyId: shiftForm.companyId,
          ...payload,
        });

        setSuccess("Yeni vardiya oluşturuldu.");
      }

      setShowShiftForm(false);
      setEditingShiftId(null);
      await loadAll();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Vardiya kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function saveAssignment(event: FormEvent) {
    event.preventDefault();

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      if (!assignmentForm.companyId) {
        throw new Error("Şirket seçilmelidir.");
      }

      if (!assignmentForm.personnelId) {
        throw new Error("Personel seçilmelidir.");
      }

      if (!assignmentForm.shiftDefinitionId) {
        throw new Error("Vardiya seçilmelidir.");
      }

      if (!assignmentForm.startDate) {
        throw new Error("Başlangıç tarihi zorunludur.");
      }

      if (
        assignmentForm.endDate &&
        assignmentForm.endDate < assignmentForm.startDate
      ) {
        throw new Error(
          "Bitiş tarihi başlangıç tarihinden önce olamaz."
        );
      }

      await hrShiftService.createAssignment({
        companyId: assignmentForm.companyId,
        personnelId: assignmentForm.personnelId,
        shiftDefinitionId:
          assignmentForm.shiftDefinitionId,
        projectId: assignmentForm.projectId || null,
        startDate: assignmentForm.startDate,
        endDate: assignmentForm.endDate || null,
        teamName: assignmentForm.teamName.trim() || null,
        description:
          assignmentForm.description.trim() || null,
      });

      setSuccess("Vardiya ataması oluşturuldu.");
      setShowAssignmentForm(false);
      await loadAll();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Vardiya ataması kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function deleteShift(item: HrShiftItem) {
    setPending(null);
    setActionId(item.id);

    try {
      await hrShiftService.deleteShift(item.id);
      setSuccess("Vardiya silindi.");
      await loadAll();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Vardiya silinemedi."
      );
    } finally {
      setActionId(null);
    }
  }

  async function deleteAssignment(
    item: HrShiftAssignmentItem
  ) {
    setPending(null);
    setActionId(item.id);

    try {
      await hrShiftService.deleteAssignment(item.id);
      setSuccess("Vardiya ataması silindi.");
      await loadAll();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Vardiya ataması silinemedi."
      );
    } finally {
      setActionId(null);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Vardiya ve Puantaj Merkezi"
      description="Vardiya tanımları, personel vardiya atamaları ve çalışma planları"
    >
      {error && (
        <div className="mb-5 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          {error}
        </div>
      )}

      {success && (
        <div className="mb-5 rounded-xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-700">
          {success}
        </div>
      )}

      <div className="mb-5 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {[
          ["Vardiya Tanımı", shifts.length],
          ["Gece Vardiyası", nightShiftCount],
          ["Toplam Atama", assignments.length],
          ["Aktif Atama", activeAssignmentCount],
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

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => setTab("shifts")}
              className={`rounded-lg px-4 py-2 text-sm font-semibold ${
                tab === "shifts"
                  ? "bg-blue-700 text-white"
                  : "border border-slate-300 bg-white text-slate-700"
              }`}
            >
              Vardiya Tanımları
            </button>

            <button
              type="button"
              onClick={() => setTab("assignments")}
              className={`rounded-lg px-4 py-2 text-sm font-semibold ${
                tab === "assignments"
                  ? "bg-blue-700 text-white"
                  : "border border-slate-300 bg-white text-slate-700"
              }`}
            >
              Vardiya Atamaları
            </button>
          </div>

          <div className="flex gap-2">
            <Button variant="secondary" onClick={loadAll}>Yenile</Button>

            {actions.can("create") && (
              <button
                type="button"
                onClick={
                  tab === "shifts"
                    ? openCreateShift
                    : openCreateAssignment
                }
                className="rounded-lg bg-blue-700 px-4 py-2 text-sm font-semibold text-white"
              >
                {tab === "shifts"
                  ? "+ Yeni Vardiya"
                  : "+ Yeni Atama"}
              </button>
            )}
          </div>
        </div>
      </section>

      {showShiftForm && (
        <section className="mb-5 rounded-xl border border-blue-200 bg-white p-5 shadow-sm">
          <form onSubmit={saveShift}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <select
                value={shiftForm.companyId}
                disabled={Boolean(editingShiftId)}
                onChange={(event) =>
                  setShiftForm((current) => ({
                    ...current,
                    companyId: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
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

              <input
                value={shiftForm.code}
                onChange={(event) =>
                  setShiftForm((current) => ({
                    ...current,
                    code: event.target.value,
                  }))
                }
                placeholder="Vardiya kodu"
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                value={shiftForm.name}
                onChange={(event) =>
                  setShiftForm((current) => ({
                    ...current,
                    name: event.target.value,
                  }))
                }
                placeholder="Vardiya adı"
                className="rounded-lg border border-slate-300 p-3"
              />

              <label className="flex items-center gap-2 rounded-lg border border-slate-300 p-3">
                <input
                  type="checkbox"
                  checked={shiftForm.isNightShift}
                  onChange={(event) =>
                    setShiftForm((current) => ({
                      ...current,
                      isNightShift: event.target.checked,
                    }))
                  }
                />
                <span className="text-sm font-semibold">
                  Gece vardiyası
                </span>
              </label>

              <input
                type="time"
                value={shiftForm.startTime}
                onChange={(event) =>
                  setShiftForm((current) => ({
                    ...current,
                    startTime: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                type="time"
                value={shiftForm.endTime}
                onChange={(event) =>
                  setShiftForm((current) => ({
                    ...current,
                    endTime: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                type="number"
                min="0"
                step="0.25"
                value={shiftForm.breakHours}
                onChange={(event) =>
                  setShiftForm((current) => ({
                    ...current,
                    breakHours: event.target.value,
                  }))
                }
                placeholder="Mola süresi"
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                type="number"
                min="0.5"
                step="0.25"
                value={shiftForm.dailyWorkingHours}
                onChange={(event) =>
                  setShiftForm((current) => ({
                    ...current,
                    dailyWorkingHours: event.target.value,
                  }))
                }
                placeholder="Günlük çalışma"
                className="rounded-lg border border-slate-300 p-3"
              />

              <textarea
                rows={3}
                value={shiftForm.description}
                onChange={(event) =>
                  setShiftForm((current) => ({
                    ...current,
                    description: event.target.value,
                  }))
                }
                placeholder="Açıklama"
                className="rounded-lg border border-slate-300 p-3 md:col-span-2 xl:col-span-4"
              />
            </div>

            <div className="mt-5 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setShowShiftForm(false)}
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

      {showAssignmentForm && (
        <section className="mb-5 rounded-xl border border-blue-200 bg-white p-5 shadow-sm">
          <form onSubmit={saveAssignment}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <select
                value={assignmentForm.companyId}
                onChange={(event) =>
                  setAssignmentForm((current) => ({
                    ...current,
                    companyId: event.target.value,
                    personnelId: "",
                    shiftDefinitionId: "",
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
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

              <select
                value={assignmentForm.personnelId}
                onChange={(event) =>
                  setAssignmentForm((current) => ({
                    ...current,
                    personnelId: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Personel seçin</option>

                {filteredPersonnel.map((person) => (
                  <option
                    value={person.id}
                    key={person.id}
                  >
                    {person.employeeNumber} - {person.fullName}
                  </option>
                ))}
              </select>

              <select
                value={assignmentForm.shiftDefinitionId}
                onChange={(event) =>
                  setAssignmentForm((current) => ({
                    ...current,
                    shiftDefinitionId: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Vardiya seçin</option>

                {filteredShifts.map((shift) => (
                  <option value={shift.id} key={shift.id}>
                    {shift.code} - {shift.name}
                  </option>
                ))}
              </select>

              <select
                value={assignmentForm.projectId}
                onChange={(event) =>
                  setAssignmentForm((current) => ({
                    ...current,
                    projectId: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Proje seçmeden devam et</option>

                {projects.map((project) => (
                  <option value={project.id} key={project.id}>
                    {project.code} - {project.name}
                  </option>
                ))}
              </select>

              <input
                type="date"
                value={assignmentForm.startDate}
                onChange={(event) =>
                  setAssignmentForm((current) => ({
                    ...current,
                    startDate: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                type="date"
                min={assignmentForm.startDate || undefined}
                value={assignmentForm.endDate}
                onChange={(event) =>
                  setAssignmentForm((current) => ({
                    ...current,
                    endDate: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                value={assignmentForm.teamName}
                onChange={(event) =>
                  setAssignmentForm((current) => ({
                    ...current,
                    teamName: event.target.value,
                  }))
                }
                placeholder="Takım adı"
                className="rounded-lg border border-slate-300 p-3"
              />

              <textarea
                rows={3}
                value={assignmentForm.description}
                onChange={(event) =>
                  setAssignmentForm((current) => ({
                    ...current,
                    description: event.target.value,
                  }))
                }
                placeholder="Atama açıklaması"
                className="rounded-lg border border-slate-300 p-3 md:col-span-2 xl:col-span-3"
              />
            </div>

            <div className="mt-5 flex justify-end gap-2">
              <button
                type="button"
                onClick={() =>
                  setShowAssignmentForm(false)
                }
                className="rounded-lg border border-slate-300 px-4 py-2"
              >
                Vazgeç
              </button>

              <button
                type="submit"
                disabled={saving}
                className="rounded-lg bg-blue-700 px-5 py-2 text-white"
              >
                {saving ? "Kaydediliyor…" : "Atama Oluştur"}
              </button>
            </div>
          </form>
        </section>
      )}

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="grid gap-3 md:grid-cols-3">
          <input
            value={search}
            onChange={(event) =>
              setSearch(event.target.value)
            }
            placeholder="Kod, vardiya, personel veya takım ara"
            className="rounded-lg border border-slate-300 p-3"
          />

          <select
            value={companyFilter}
            onChange={(event) =>
              setCompanyFilter(event.target.value)
            }
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">Tüm şirketler</option>

            {companies.map((company) => (
              <option
                value={company.id}
                key={company.id}
              >
                {company.name}
              </option>
            ))}
          </select>

          <button
            type="button"
            onClick={() => {
              setSearch("");
              setCompanyFilter("");
            }}
            className="rounded-lg border border-slate-300 p-3 font-semibold"
          >
            Filtreleri Temizle
          </button>
        </div>
      </section>

      {tab === "shifts" ? (
        <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[850px]">
              <thead className="bg-slate-50 text-left text-xs text-slate-500">
                <tr>
                  <th className="p-4">Kod</th>
                  <th className="p-4">Vardiya</th>
                  <th className="p-4">Başlangıç</th>
                  <th className="p-4">Bitiş</th>
                  <th className="p-4">Mola</th>
                  <th className="p-4">Çalışma</th>
                  <th className="p-4">Tür</th>
                  <th className="p-4 text-right">İşlemler</th>
                </tr>
              </thead>

              <tbody>
                {visibleShifts.map((item) => (
                  <tr
                    key={item.id}
                    className="border-t text-sm"
                  >
                    <td className="p-4 font-semibold">
                      {item.code}
                    </td>
                    <td className="p-4">
                      <strong>{item.name}</strong>
                      {item.description && (
                        <small className="block text-slate-500">
                          {item.description}
                        </small>
                      )}
                    </td>
                    <td className="p-4">
                      {inputTime(item.startTime)}
                    </td>
                    <td className="p-4">
                      {inputTime(item.endTime)}
                    </td>
                    <td className="p-4">
                      {item.breakHours} saat
                    </td>
                    <td className="p-4">
                      {item.dailyWorkingHours} saat
                    </td>
                    <td className="p-4">
                      {item.isNightShift
                        ? "Gece"
                        : "Gündüz"}
                    </td>
                    <td className="p-4">
                      <div className="flex justify-end gap-2">
                        {actions.can("edit") && (
                          <button
                            type="button"
                            onClick={() => openEditShift(item)}
                            className="rounded border px-3 py-1.5 text-xs"
                          >
                            Düzenle
                          </button>
                        )}

                        {actions.can("delete") && (
                          <button
                            type="button"
                            disabled={actionId === item.id}
                            onClick={() => setPending({ kind: "shift", item })}
                            className="rounded bg-red-50 px-3 py-1.5 text-xs text-red-700"
                          >
                            Sil
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}

                {!loading && visibleShifts.length === 0 && (
                  <tr>
                    <td
                      colSpan={8}
                      className="p-12 text-center text-slate-500"
                    >
                      Vardiya kaydı bulunamadı.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </section>
      ) : (
        <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[1050px]">
              <thead className="bg-slate-50 text-left text-xs text-slate-500">
                <tr>
                  <th className="p-4">Personel</th>
                  <th className="p-4">Vardiya</th>
                  <th className="p-4">Proje</th>
                  <th className="p-4">Başlangıç</th>
                  <th className="p-4">Bitiş</th>
                  <th className="p-4">Takım</th>
                  <th className="p-4">Açıklama</th>
                  <th className="p-4 text-right">İşlem</th>
                </tr>
              </thead>

              <tbody>
                {visibleAssignments.map((item) => {
                  const person = personnelById.get(
                    item.personnelId
                  );

                  const shift = shiftById.get(
                    item.shiftDefinitionId
                  );

                  const project = item.projectId
                    ? projectById.get(item.projectId)
                    : undefined;

                  return (
                    <tr
                      key={item.id}
                      className="border-t text-sm"
                    >
                      <td className="p-4">
                        <strong>
                          {person?.fullName ?? "—"}
                        </strong>
                        <small className="block text-slate-500">
                          {person?.employeeNumber ?? "—"}
                        </small>
                      </td>

                      <td className="p-4">
                        {shift
                          ? `${shift.code} - ${shift.name}`
                          : "—"}
                      </td>

                      <td className="p-4">
                        {project?.name ?? "Projesiz"}
                      </td>

                      <td className="p-4">
                        {formatDate(item.startDate)}
                      </td>

                      <td className="p-4">
                        {formatDate(item.endDate)}
                      </td>

                      <td className="p-4">
                        {item.teamName ?? "—"}
                      </td>

                      <td className="p-4">
                        {item.description ?? "—"}
                      </td>

                      <td className="p-4 text-right">
                        {actions.can("delete") && (
                          <button
                            type="button"
                            disabled={actionId === item.id}
                            onClick={() =>
                              setPending({
                                kind: "assignment",
                                item,
                              })
                            }
                            className="rounded bg-red-50 px-3 py-1.5 text-xs text-red-700"
                          >
                            Sil
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}

                {!loading &&
                  visibleAssignments.length === 0 && (
                    <tr>
                      <td
                        colSpan={8}
                        className="p-12 text-center text-slate-500"
                      >
                        Vardiya ataması bulunamadı.
                      </td>
                    </tr>
                  )}
              </tbody>
            </table>
          </div>
        </section>
      )}
      {pending && (
        <ConfirmDialog
          open
          title={
            pending.kind === "shift" ? "Vardiyayı Sil" : "Vardiya Atamasını Sil"
          }
          description={
            pending.kind === "shift"
              ? `"${pending.item.name}" vardiyası silinecek. Bu vardiyaya bağlı atamalar da geçersiz kalır.`
              : "Vardiya ataması silinecek; personel bu vardiyada görünmez olur."
          }
          confirmLabel="Sil"
          busy={actionId === pending.item.id}
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={() =>
            pending.kind === "shift"
              ? void deleteShift(pending.item)
              : void deleteAssignment(pending.item)
          }
        />
      )}
    </ErpShell>
  );
}
