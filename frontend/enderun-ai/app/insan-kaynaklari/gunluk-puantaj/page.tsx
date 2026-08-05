"use client";

import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";

import {
  AttendanceItem,
  CreateAttendanceRequest,
  hrAttendanceService,
} from "@/services/hr-attendance.service";

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

import {
  projectSiteService,
  ProjectSiteListItem,
} from "@/services/project-site.service";
import {
  progressPaymentService,
  type ProjectHakedisSection,
} from "@/services/progress-payment.service";

type AttendanceForm = {
  companyId: string;
  projectId: string;
  projectSiteId: string;
  personnelId: string;
  workDate: string;
  status: string;
  checkInTime: string;
  checkOutTime: string;
  normalHours: string;
  overtimeHours: string;
  nightShiftHours: string;
  sundayHours: string;
  publicHolidayHours: string;
  teamName: string;
  roleName: string;
  workItemCode: string;
  workItemName: string;
  /** İcmal kısmı — ekibin o gün çalıştığı imalat. Opsiyonel. */
  projectHakedisSectionId: string;
  locationName: string;
  description: string;
};

type BulkForm = {
  companyId: string;
  projectId: string;
  projectSiteId: string;
  workDate: string;
  status: string;
  checkInTime: string;
  checkOutTime: string;
  normalHours: string;
  overtimeHours: string;
  nightShiftHours: string;
  sundayHours: string;
  publicHolidayHours: string;
  teamName: string;
  roleName: string;
  workItemCode: string;
  workItemName: string;
  /** İcmal kısmı — ekibin o gün çalıştığı imalat. Opsiyonel. */
  projectHakedisSectionId: string;
  locationName: string;
  description: string;
};

const today = new Date().toISOString().slice(0, 10);

/**
 * Backend'deki AttendanceStatus ile birebir aynı olmak zorunda.
 * Daha önce bu liste kaymıştı: arayüz 0'ı "Çalıştı" gönderiyor, backend
 * 0'ı "Devamsız" okuyordu; çalışılan gün özette devamsızlık sayılıyordu.
 */
const statusOptions = [
  { value: 0, label: "Devamsız" },
  { value: 1, label: "Çalıştı" },
  { value: 2, label: "Ücretli İzin" },
  { value: 3, label: "Raporlu" },
  { value: 4, label: "Resmî Tatil" },
  { value: 5, label: "Hafta Tatili" },
  { value: 6, label: "Ücretsiz İzin" },
  { value: 7, label: "Mazeretli Devamsız" },
  { value: 8, label: "Yarım Gün" },
  { value: 9, label: "Uzaktan Çalışma" },
];

/** Saat girişi beklenmeyen durumlar (çalışma yok). */
const nonWorkingStatuses = [0, 2, 3, 4, 5, 6, 7];

const initialForm: AttendanceForm = {
  companyId: "",
  projectId: "",
  projectSiteId: "",
  personnelId: "",
  workDate: today,
  status: "1",
  checkInTime: "08:00",
  checkOutTime: "17:00",
  normalHours: "8",
  overtimeHours: "0",
  nightShiftHours: "0",
  sundayHours: "0",
  publicHolidayHours: "0",
  teamName: "",
  roleName: "",
  workItemCode: "",
  workItemName: "",
  projectHakedisSectionId: "",
  locationName: "",
  description: "",
};

const initialBulkForm: BulkForm = {
  companyId: "",
  projectId: "",
  projectSiteId: "",
  workDate: today,
  status: "1",
  checkInTime: "08:00",
  checkOutTime: "17:00",
  normalHours: "8",
  overtimeHours: "0",
  nightShiftHours: "0",
  sundayHours: "0",
  publicHolidayHours: "0",
  teamName: "",
  roleName: "",
  workItemCode: "",
  workItemName: "",
  projectHakedisSectionId: "",
  locationName: "",
  description: "",
};

function statusLabel(status: number) {
  return (
    statusOptions.find((item) => item.value === status)?.label ??
    "Bilinmiyor"
  );
}

function statusClass(status: number) {
  // Çalışılan günler
  if ([1, 8, 9].includes(status)) {
    return "border-emerald-200 bg-emerald-50 text-emerald-700";
  }

  // Çalışılmayan ama ücrete esas günler (izin/tatil)
  if ([2, 4, 5].includes(status)) {
    return "border-amber-200 bg-amber-50 text-amber-700";
  }

  // Devamsızlık — ücrete esas değil
  if ([0, 7].includes(status)) {
    return "border-red-200 bg-red-50 text-red-700";
  }

  return "border-slate-200 bg-slate-50 text-slate-700";
}

function timeValue(value?: string | null) {
  return value ? value.slice(0, 5) : "";
}

function apiTime(value: string) {
  return value ? `${value}:00` : null;
}

function formatDate(value?: string | null) {
  return value
    ? new Date(value).toLocaleDateString("tr-TR")
    : "—";
}

function numberValue(value: string) {
  const result = Number(value.replace(",", "."));
  return Number.isFinite(result) ? result : 0;
}

function escapeCsv(value: unknown) {
  const text = String(value ?? "").replace(/"/g, '""');
  return `"${text}"`;
}

export default function DailyAttendancePage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [items, setItems] = useState<AttendanceItem[]>([]);

  // İşçilik maliyetinin şantiyeye dağıtılabilmesi için seçilen projenin
  // şantiyeleri; proje değişince yeniden yüklenir.
  const [singleFormSites, setSingleFormSites] = useState<ProjectSiteListItem[]>([]);
  const [singleFormSections, setSingleFormSections] =
    useState<ProjectHakedisSection[]>([]);
  const [bulkFormSites, setBulkFormSites] = useState<ProjectSiteListItem[]>([]);

  const [form, setForm] = useState<AttendanceForm>(initialForm);
  const [bulkForm, setBulkForm] =
    useState<BulkForm>(initialBulkForm);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [showSingleForm, setShowSingleForm] = useState(false);
  const [showBulkForm, setShowBulkForm] = useState(false);

  const [selectedPersonnelIds, setSelectedPersonnelIds] =
    useState<string[]>([]);

  const [selectedRecordIds, setSelectedRecordIds] =
    useState<string[]>([]);

  const [companyFilter, setCompanyFilter] = useState("");
  const [projectFilter, setProjectFilter] = useState("");
  const [personnelFilter, setPersonnelFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [startDateFilter, setStartDateFilter] = useState(today);
  const [endDateFilter, setEndDateFilter] = useState(today);
  const [searchFilter, setSearchFilter] = useState("");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
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

  useEffect(() => {
    if (!form.projectId) {
      setSingleFormSites([]);
      return;
    }

    let cancelled = false;

    projectSiteService
      .getAll(form.projectId)
      .then((result) => {
        if (!cancelled) setSingleFormSites(result);
      })
      .catch(() => {
        if (!cancelled) setSingleFormSites([]);
      });

    return () => {
      cancelled = true;
    };
  }, [form.projectId]);

  // İcmal kısımları: seçilirse işçilik o kısma yazılır. Liste
  // alınamazsa puantaj yine kaydedilir — kısım opsiyonel.
  useEffect(() => {
    if (!form.projectId) {
      setSingleFormSections([]);
      return;
    }

    let cancelled = false;

    progressPaymentService
      .getProjectSections(form.projectId)
      .then((result) => {
        if (!cancelled) setSingleFormSections(result);
      })
      .catch(() => {
        if (!cancelled) setSingleFormSections([]);
      });

    return () => {
      cancelled = true;
    };
  }, [form.projectId]);

  useEffect(() => {
    if (!bulkForm.projectId) {
      setBulkFormSites([]);
      return;
    }

    let cancelled = false;

    projectSiteService
      .getAll(bulkForm.projectId)
      .then((result) => {
        if (!cancelled) setBulkFormSites(result);
      })
      .catch(() => {
        if (!cancelled) setBulkFormSites([]);
      });

    return () => {
      cancelled = true;
    };
  }, [bulkForm.projectId]);

  const singleFormProjects = useMemo(() => {
    if (!form.companyId) return projects;

    return projects.filter(
      (item) => item.companyId === form.companyId
    );
  }, [form.companyId, projects]);

  const bulkFormProjects = useMemo(() => {
    if (!bulkForm.companyId) return projects;

    return projects.filter(
      (item) => item.companyId === bulkForm.companyId
    );
  }, [bulkForm.companyId, projects]);

  const filterProjects = useMemo(() => {
    if (!companyFilter) return projects;

    return projects.filter(
      (item) => item.companyId === companyFilter
    );
  }, [companyFilter, projects]);

  const singleFormPersonnel = useMemo(() => {
    if (!form.companyId) return personnel;

    return personnel.filter(
      (item) => item.companyId === form.companyId
    );
  }, [form.companyId, personnel]);

  const bulkPersonnel = useMemo(() => {
    return personnel.filter((item) => {
      if (
        bulkForm.companyId &&
        item.companyId !== bulkForm.companyId
      ) {
        return false;
      }

      if (!bulkForm.projectId) {
        return true;
      }

      return item.activeAssignments?.some(
        (assignment) =>
          assignment.projectId === bulkForm.projectId &&
          assignment.isActive !== false
      );
    });
  }, [
    bulkForm.companyId,
    bulkForm.projectId,
    personnel,
  ]);

  const totalNormal = items.reduce(
    (sum, item) => sum + Number(item.normalHours),
    0
  );

  const totalOvertime = items.reduce(
    (sum, item) => sum + Number(item.overtimeHours),
    0
  );

  const totalNight = items.reduce(
    (sum, item) => sum + Number(item.nightShiftHours),
    0
  );

  const totalSunday = items.reduce(
    (sum, item) => sum + Number(item.sundayHours),
    0
  );

  const totalHoliday = items.reduce(
    (sum, item) => sum + Number(item.publicHolidayHours),
    0
  );

  const totalHours = items.reduce(
    (sum, item) => sum + Number(item.totalHours),
    0
  );

  const approvedCount = items.filter(
    (item) => item.isApproved
  ).length;

  const unapprovedItems = items.filter(
    (item) => !item.isApproved
  );

  async function loadAttendance() {
    if (
      startDateFilter &&
      endDateFilter &&
      startDateFilter > endDateFilter
    ) {
      setError(
        "Başlangıç tarihi bitiş tarihinden sonra olamaz."
      );
      return;
    }

    setLoading(true);
    setError("");
    setSuccess("");

    try {
      const result = await hrAttendanceService.getAll({
        companyId: companyFilter || undefined,
        projectId: projectFilter || undefined,
        personnelId: personnelFilter || undefined,
        status:
          statusFilter === ""
            ? undefined
            : Number(statusFilter),
        startDate: startDateFilter || undefined,
        endDate: endDateFilter || undefined,
        search: searchFilter.trim() || undefined,
      });

      setItems(result);
      setSelectedRecordIds((current) =>
        current.filter((id) =>
          result.some(
            (item) => item.id === id && !item.isApproved
          )
        )
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Puantaj kayıtları yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    async function loadPage() {
      try {
        const [
          companyResult,
          projectResult,
          personnelResult,
          attendanceResult,
        ] = await Promise.all([
          companyService.getAll(),
          projectService.getAll(),
          personnelService.getAll(),
          hrAttendanceService.getAll({
            startDate: today,
            endDate: today,
          }),
        ]);

        setCompanies(companyResult);
        setProjects(projectResult);
        setPersonnel(personnelResult);
        setItems(attendanceResult);

        if (companyResult.length === 1) {
          const companyId = companyResult[0].id;

          setForm((current) => ({
            ...current,
            companyId,
          }));

          setBulkForm((current) => ({
            ...current,
            companyId,
          }));

          setCompanyFilter(companyId);
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Günlük puantaj ekranı yüklenemedi."
        );
      } finally {
        setLoading(false);
      }
    }

    loadPage();
  }, []);

  useEffect(() => {
    if (
      projectFilter &&
      companyFilter &&
      !projects.some(
        (project) =>
          project.id === projectFilter &&
          project.companyId === companyFilter
      )
    ) {
      setProjectFilter("");
    }
  }, [companyFilter, projectFilter, projects]);

  function openCreate() {
    setEditingId(null);

    setForm({
      ...initialForm,
      companyId:
        companyFilter ||
        (companies.length === 1 ? companies[0].id : ""),
      projectId: projectFilter,
      projectSiteId: "",
      workDate: startDateFilter || today,
    });

    setShowSingleForm(true);
    setShowBulkForm(false);
  }

  function openEdit(item: AttendanceItem) {
    if (item.isApproved) {
      setError("Onaylanmış puantaj kaydı düzenlenemez.");
      return;
    }

    setEditingId(item.id);

    setForm({
      companyId: item.companyId,
      projectId: item.projectId ?? "",
      projectSiteId: item.projectSiteId ?? "",
      personnelId: item.personnelId,
      workDate: item.workDate.slice(0, 10),
      status: String(item.status),
      checkInTime: timeValue(item.checkInTime),
      checkOutTime: timeValue(item.checkOutTime),
      normalHours: String(item.normalHours),
      overtimeHours: String(item.overtimeHours),
      nightShiftHours: String(item.nightShiftHours),
      sundayHours: String(item.sundayHours),
      publicHolidayHours: String(item.publicHolidayHours),
      teamName: item.teamName ?? "",
      roleName: item.roleName ?? "",
      workItemCode: item.workItemCode ?? "",
      workItemName: item.workItemName ?? "",
      projectHakedisSectionId: item.projectHakedisSectionId ?? "",
      locationName: item.locationName ?? "",
      description: item.description ?? "",
    });

    setShowSingleForm(true);
    setShowBulkForm(false);

    window.scrollTo({
      top: 0,
      behavior: "smooth",
    });
  }

  function openBulk() {
    setBulkForm({
      ...initialBulkForm,
      companyId:
        companyFilter ||
        (companies.length === 1 ? companies[0].id : ""),
      projectId: projectFilter,
      projectSiteId: "",
      workDate: startDateFilter || today,
    });

    setSelectedPersonnelIds([]);
    setShowBulkForm(true);
    setShowSingleForm(false);
  }

  function applyStatusRulesToForm(status: string) {
    const numericStatus = Number(status);

    setForm((current) => ({
      ...current,
      status,
      ...(nonWorkingStatuses.includes(numericStatus)
        ? {
            checkInTime: "",
            checkOutTime: "",
            normalHours: "0",
            overtimeHours: "0",
            nightShiftHours: "0",
            sundayHours: "0",
            publicHolidayHours: "0",
          }
        : {}),
    }));
  }

  function applyStatusRulesToBulk(status: string) {
    const numericStatus = Number(status);

    setBulkForm((current) => ({
      ...current,
      status,
      ...(nonWorkingStatuses.includes(numericStatus)
        ? {
            checkInTime: "",
            checkOutTime: "",
            normalHours: "0",
            overtimeHours: "0",
            nightShiftHours: "0",
            sundayHours: "0",
            publicHolidayHours: "0",
          }
        : {}),
    }));
  }

  function validateHours(values: {
    normalHours: string;
    overtimeHours: string;
    nightShiftHours: string;
    sundayHours: string;
    publicHolidayHours: string;
  }) {
    const hours = [
      numberValue(values.normalHours),
      numberValue(values.overtimeHours),
      numberValue(values.nightShiftHours),
      numberValue(values.sundayHours),
      numberValue(values.publicHolidayHours),
    ];

    if (hours.some((value) => value < 0)) {
      throw new Error("Çalışma saatleri negatif olamaz.");
    }

    const total = hours.reduce((sum, value) => sum + value, 0);

    if (total > 24) {
      throw new Error(
        "Bir günlük toplam çalışma süresi 24 saati aşamaz."
      );
    }
  }

  function buildPayload(
    values: AttendanceForm | BulkForm,
    personnelId: string
  ): CreateAttendanceRequest {
    validateHours(values);

    return {
      companyId: values.companyId,
      projectId: values.projectId || null,
      projectSiteId: values.projectSiteId || null,
      personnelId,
      workDate: values.workDate,
      status: Number(values.status),
      checkInTime: apiTime(values.checkInTime),
      checkOutTime: apiTime(values.checkOutTime),
      normalHours: numberValue(values.normalHours),
      overtimeHours: numberValue(values.overtimeHours),
      nightShiftHours: numberValue(values.nightShiftHours),
      sundayHours: numberValue(values.sundayHours),
      publicHolidayHours: numberValue(
        values.publicHolidayHours
      ),
      teamName: values.teamName.trim() || null,
      roleName: values.roleName.trim() || null,
      workItemCode: values.workItemCode.trim() || null,
      workItemName: values.workItemName.trim() || null,
      projectHakedisSectionId: values.projectHakedisSectionId || null,
      locationName: values.locationName.trim() || null,
      description: values.description.trim() || null,
    };
  }

  async function saveSingle(event: FormEvent) {
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

      if (!form.workDate) {
        throw new Error("Çalışma tarihi zorunludur.");
      }

      const payload = buildPayload(
        form,
        form.personnelId
      );

      if (editingId) {
        await hrAttendanceService.update(editingId, {
          projectId: payload.projectId,
          projectSiteId: payload.projectSiteId,
          status: payload.status,
          checkInTime: payload.checkInTime,
          checkOutTime: payload.checkOutTime,
          normalHours: payload.normalHours,
          overtimeHours: payload.overtimeHours,
          nightShiftHours: payload.nightShiftHours,
          sundayHours: payload.sundayHours,
          publicHolidayHours: payload.publicHolidayHours,
          teamName: payload.teamName,
          roleName: payload.roleName,
          workItemCode: payload.workItemCode,
          workItemName: payload.workItemName,
          locationName: payload.locationName,
          description: payload.description,
        });

        setSuccess("Puantaj kaydı güncellendi.");
      } else {
        await hrAttendanceService.create(payload);
        setSuccess("Puantaj kaydı oluşturuldu.");
      }

      setShowSingleForm(false);
      setEditingId(null);

      await loadAttendance();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Puantaj kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function saveBulk(event: FormEvent) {
    event.preventDefault();

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      if (!bulkForm.companyId) {
        throw new Error("Şirket seçilmelidir.");
      }

      if (!bulkForm.workDate) {
        throw new Error("Çalışma tarihi zorunludur.");
      }

      if (selectedPersonnelIds.length === 0) {
        throw new Error("En az bir personel seçilmelidir.");
      }

      validateHours(bulkForm);

      const results = await Promise.allSettled(
        selectedPersonnelIds.map((personnelId) =>
          hrAttendanceService.create(
            buildPayload(bulkForm, personnelId)
          )
        )
      );

      const successful = results.filter(
        (result) => result.status === "fulfilled"
      ).length;

      const failed = results.length - successful;

      if (failed > 0) {
        setError(
          `${successful} kayıt oluşturuldu, ${failed} kayıt oluşturulamadı. Aynı tarih için mevcut kayıtlar olabilir.`
        );
      } else {
        setSuccess(
          `${successful} personelin puantajı başarıyla oluşturuldu.`
        );
      }

      setShowBulkForm(false);
      setSelectedPersonnelIds([]);

      await loadAttendance();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Toplu puantaj oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  async function approveOne(item: AttendanceItem) {
    if (
      !window.confirm(
        "Bu puantaj kaydı onaylansın mı? Onaydan sonra düzenlenemez."
      )
    ) {
      return;
    }

    setActionId(item.id);
    setError("");
    setSuccess("");

    try {
      await hrAttendanceService.approve(item.id);
      setSuccess("Puantaj kaydı onaylandı.");
      await loadAttendance();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Puantaj onaylanamadı."
      );
    } finally {
      setActionId(null);
    }
  }

  async function approveSelected() {
    if (selectedRecordIds.length === 0) {
      setError("Onaylanacak kayıt seçilmedi.");
      return;
    }

    if (
      !window.confirm(
        `${selectedRecordIds.length} puantaj kaydı onaylansın mı?`
      )
    ) {
      return;
    }

    setSaving(true);
    setError("");
    setSuccess("");

    try {
      const results = await Promise.allSettled(
        selectedRecordIds.map((id) =>
          hrAttendanceService.approve(id)
        )
      );

      const successful = results.filter(
        (result) => result.status === "fulfilled"
      ).length;

      const failed = results.length - successful;

      if (failed > 0) {
        setError(
          `${successful} kayıt onaylandı, ${failed} kayıt onaylanamadı.`
        );
      } else {
        setSuccess(
          `${successful} puantaj kaydı onaylandı.`
        );
      }

      setSelectedRecordIds([]);
      await loadAttendance();
    } finally {
      setSaving(false);
    }
  }

  async function remove(item: AttendanceItem) {
    if (item.isApproved) {
      setError("Onaylanmış puantaj kaydı silinemez.");
      return;
    }

    if (!window.confirm("Puantaj kaydı silinsin mi?")) {
      return;
    }

    setActionId(item.id);
    setError("");
    setSuccess("");

    try {
      await hrAttendanceService.delete(item.id);
      setSuccess("Puantaj kaydı silindi.");
      await loadAttendance();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Puantaj kaydı silinemedi."
      );
    } finally {
      setActionId(null);
    }
  }

  function exportCsv() {
    const headers = [
      "Tarih",
      "Personel No",
      "Personel",
      "Proje",
      "Durum",
      "Giriş",
      "Çıkış",
      "Normal Saat",
      "Fazla Mesai",
      "Gece",
      "Pazar",
      "Resmi Tatil",
      "Toplam Saat",
      "Takım",
      "Görev",
      "İş Kalemi Kodu",
      "İş Kalemi",
      "Lokasyon",
      "Onay",
      "Açıklama",
    ];

    const rows = items.map((item) => {
      const person = personnelById.get(item.personnelId);
      const project = item.projectId
        ? projectById.get(item.projectId)
        : undefined;

      return [
        formatDate(item.workDate),
        person?.employeeNumber ?? "",
        person?.fullName ?? "",
        project?.name ?? "",
        statusLabel(item.status),
        timeValue(item.checkInTime),
        timeValue(item.checkOutTime),
        item.normalHours,
        item.overtimeHours,
        item.nightShiftHours,
        item.sundayHours,
        item.publicHolidayHours,
        item.totalHours,
        item.teamName ?? "",
        item.roleName ?? "",
        item.workItemCode ?? "",
        item.workItemName ?? "",
        item.locationName ?? "",
        item.isApproved ? "Onaylandı" : "Bekliyor",
        item.description ?? "",
      ];
    });

    const csv = [
      headers.map(escapeCsv).join(";"),
      ...rows.map((row) =>
        row.map(escapeCsv).join(";")
      ),
    ].join("\n");

    const blob = new Blob(
      ["\uFEFF", csv],
      {
        type: "text/csv;charset=utf-8;",
      }
    );

    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");

    link.href = url;
    link.download =
      `enderun-puantaj-${startDateFilter}-${endDateFilter}.csv`;

    document.body.appendChild(link);
    link.click();
    link.remove();

    URL.revokeObjectURL(url);
  }

  const allBulkSelected =
    bulkPersonnel.length > 0 &&
    bulkPersonnel.every((item) =>
      selectedPersonnelIds.includes(item.id)
    );

  const allRecordsSelected =
    unapprovedItems.length > 0 &&
    unapprovedItems.every((item) =>
      selectedRecordIds.includes(item.id)
    );

  return (
    <ErpShell
      title="Günlük Puantaj"
      description="Şantiye personeli günlük çalışma, fazla mesai ve adam/saat yönetimi"
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

      <div className="mb-5 grid gap-4 md:grid-cols-2 xl:grid-cols-6">
        {[
          ["Kayıt", items.length],
          ["Onaylanan", approvedCount],
          ["Normal Saat", totalNormal],
          ["Fazla Mesai", totalOvertime],
          ["Gece + Pazar", totalNight + totalSunday],
          ["Toplam Adam/Saat", totalHours],
        ].map(([title, value]) => (
          <article
            key={String(title)}
            className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm"
          >
            <span className="text-xs font-bold text-slate-500">
              {title}
            </span>

            <strong className="mt-3 block text-2xl text-slate-800">
              {loading
                ? "…"
                : Number(value).toLocaleString("tr-TR", {
                    maximumFractionDigits: 2,
                  })}
            </strong>
          </article>
        ))}
      </div>

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-xl font-bold text-slate-800">
              Puantaj İşlemleri
            </h2>

            <p className="mt-1 text-sm text-slate-500">
              Resmî tatil saati:{" "}
              {totalHoliday.toLocaleString("tr-TR")}
            </p>
          </div>

          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={loadAttendance}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-semibold"
            >
              Yenile
            </button>

            <button
              type="button"
              onClick={exportCsv}
              disabled={items.length === 0}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-semibold"
            >
              CSV Aktar
            </button>

            <button
              type="button"
              onClick={() => window.print()}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-semibold"
            >
              Yazdır / PDF
            </button>

            <button
              type="button"
              onClick={approveSelected}
              disabled={
                selectedRecordIds.length === 0 || saving
              }
              className="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-semibold text-white disabled:opacity-50"
            >
              Seçilenleri Onayla
            </button>

            <button
              type="button"
              onClick={openBulk}
              className="rounded-lg bg-indigo-700 px-4 py-2 text-sm font-semibold text-white"
            >
              + Toplu Puantaj
            </button>

            <button
              type="button"
              onClick={openCreate}
              className="rounded-lg bg-blue-700 px-4 py-2 text-sm font-semibold text-white"
            >
              + Tekil Kayıt
            </button>
          </div>
        </div>
      </section>

      {showSingleForm && (
        <section className="mb-5 rounded-xl border border-blue-200 bg-white p-5 shadow-sm">
          <div className="mb-4">
            <h3 className="text-lg font-bold text-slate-800">
              {editingId
                ? "Puantaj Kaydını Düzenle"
                : "Yeni Puantaj Kaydı"}
            </h3>
          </div>

          <form onSubmit={saveSingle}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <select
                value={form.companyId}
                disabled={Boolean(editingId)}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    companyId: event.target.value,
                    personnelId: "",
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Şirket seçin</option>

                {companies.map((company) => (
                  <option value={company.id} key={company.id}>
                    {company.name}
                  </option>
                ))}
              </select>

              <select
                value={form.projectId}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    projectId: event.target.value,
                    projectSiteId: "",
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Projesiz kayıt</option>

                {singleFormProjects.map((project) => (
                  <option value={project.id} key={project.id}>
                    {project.code} - {project.name}
                  </option>
                ))}
              </select>

              <select
                value={form.projectSiteId}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    projectSiteId: event.target.value,
                  }))
                }
                disabled={!form.projectId || singleFormSites.length === 0}
                className="rounded-lg border border-slate-300 p-3 disabled:bg-slate-50"
              >
                <option value="">Şantiye seçilmedi</option>

                {singleFormSites.map((site) => (
                  <option value={site.id} key={site.id}>
                    {site.code} - {site.name}
                  </option>
                ))}
              </select>

              <select
                value={form.personnelId}
                disabled={Boolean(editingId)}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    personnelId: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Personel seçin</option>

                {singleFormPersonnel.map((person) => (
                  <option value={person.id} key={person.id}>
                    {person.employeeNumber} - {person.fullName}
                  </option>
                ))}
              </select>

              <input
                type="date"
                value={form.workDate}
                disabled={Boolean(editingId)}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    workDate: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <select
                value={form.status}
                onChange={(event) =>
                  applyStatusRulesToForm(event.target.value)
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                {statusOptions.map((status) => (
                  <option
                    value={status.value}
                    key={status.value}
                  >
                    {status.label}
                  </option>
                ))}
              </select>

              <input
                type="time"
                value={form.checkInTime}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    checkInTime: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                type="time"
                value={form.checkOutTime}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    checkOutTime: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              {[
                ["normalHours", "Normal saat"],
                ["overtimeHours", "Fazla mesai"],
                ["nightShiftHours", "Gece saati"],
                ["sundayHours", "Pazar saati"],
                ["publicHolidayHours", "Resmî tatil saati"],
              ].map(([key, placeholder]) => (
                <input
                  key={key}
                  type="number"
                  min="0"
                  max="24"
                  step="0.25"
                  value={
                    form[key as keyof AttendanceForm]
                  }
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      [key]: event.target.value,
                    }))
                  }
                  placeholder={placeholder}
                  className="rounded-lg border border-slate-300 p-3"
                />
              ))}

              <input
                value={form.teamName}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    teamName: event.target.value,
                  }))
                }
                placeholder="Takım"
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                value={form.roleName}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    roleName: event.target.value,
                  }))
                }
                placeholder="Görev / unvan"
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                value={form.workItemCode}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    workItemCode: event.target.value,
                  }))
                }
                placeholder="İş kalemi kodu"
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                value={form.workItemName}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    workItemName: event.target.value,
                  }))
                }
                placeholder="İş kalemi adı"
                className="rounded-lg border border-slate-300 p-3"
              />

              {singleFormSections.length > 0 && (
                <select
                  value={form.projectHakedisSectionId}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      projectHakedisSectionId: event.target.value,
                    }))
                  }
                  className="rounded-lg border border-slate-300 p-3"
                  title="İcmal kısmı — seçilirse işçilik maliyeti o kısma yazılır"
                >
                  <option value="">İcmal kısmı seçilmedi</option>
                  {singleFormSections.map((section) => (
                    <option key={section.id} value={section.id}>
                      {section.name}
                    </option>
                  ))}
                </select>
              )}

              <input
                value={form.locationName}
                onChange={(event) =>
                  setForm((current) => ({
                    ...current,
                    locationName: event.target.value,
                  }))
                }
                placeholder="Lokasyon"
                className="rounded-lg border border-slate-300 p-3"
              />

              <textarea
                rows={3}
                value={form.description}
                onChange={(event) =>
                  setForm((current) => ({
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
                onClick={() => setShowSingleForm(false)}
                className="rounded-lg border border-slate-300 px-4 py-2"
              >
                Vazgeç
              </button>

              <button
                type="submit"
                disabled={saving}
                className="rounded-lg bg-blue-700 px-5 py-2 text-white disabled:opacity-50"
              >
                {saving ? "Kaydediliyor…" : "Kaydet"}
              </button>
            </div>
          </form>
        </section>
      )}

      {showBulkForm && (
        <section className="mb-5 rounded-xl border border-indigo-200 bg-white p-5 shadow-sm">
          <div className="mb-4">
            <h3 className="text-lg font-bold text-slate-800">
              Toplu Puantaj Oluştur
            </h3>

            <p className="mt-1 text-sm text-slate-500">
              Seçilen tüm personellere aynı puantaj bilgileri uygulanır.
            </p>
          </div>

          <form onSubmit={saveBulk}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <select
                value={bulkForm.companyId}
                onChange={(event) => {
                  setBulkForm((current) => ({
                    ...current,
                    companyId: event.target.value,
                  }));

                  setSelectedPersonnelIds([]);
                }}
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Şirket seçin</option>

                {companies.map((company) => (
                  <option value={company.id} key={company.id}>
                    {company.name}
                  </option>
                ))}
              </select>

              <select
                value={bulkForm.projectId}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    projectId: event.target.value,
                    projectSiteId: "",
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                <option value="">Projesiz kayıt</option>

                {bulkFormProjects.map((project) => (
                  <option value={project.id} key={project.id}>
                    {project.code} - {project.name}
                  </option>
                ))}
              </select>

              <select
                value={bulkForm.projectSiteId}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    projectSiteId: event.target.value,
                  }))
                }
                disabled={!bulkForm.projectId || bulkFormSites.length === 0}
                className="rounded-lg border border-slate-300 p-3 disabled:bg-slate-50"
              >
                <option value="">Şantiye seçilmedi</option>

                {bulkFormSites.map((site) => (
                  <option value={site.id} key={site.id}>
                    {site.code} - {site.name}
                  </option>
                ))}
              </select>

              <input
                type="date"
                value={bulkForm.workDate}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    workDate: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <select
                value={bulkForm.status}
                onChange={(event) =>
                  applyStatusRulesToBulk(event.target.value)
                }
                className="rounded-lg border border-slate-300 p-3"
              >
                {statusOptions.map((status) => (
                  <option
                    value={status.value}
                    key={status.value}
                  >
                    {status.label}
                  </option>
                ))}
              </select>

              <input
                type="time"
                value={bulkForm.checkInTime}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    checkInTime: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                type="time"
                value={bulkForm.checkOutTime}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    checkOutTime: event.target.value,
                  }))
                }
                className="rounded-lg border border-slate-300 p-3"
              />

              {[
                ["normalHours", "Normal saat"],
                ["overtimeHours", "Fazla mesai"],
                ["nightShiftHours", "Gece saati"],
                ["sundayHours", "Pazar saati"],
                ["publicHolidayHours", "Resmî tatil saati"],
              ].map(([key, placeholder]) => (
                <input
                  key={key}
                  type="number"
                  min="0"
                  max="24"
                  step="0.25"
                  value={
                    bulkForm[key as keyof BulkForm]
                  }
                  onChange={(event) =>
                    setBulkForm((current) => ({
                      ...current,
                      [key]: event.target.value,
                    }))
                  }
                  placeholder={placeholder}
                  className="rounded-lg border border-slate-300 p-3"
                />
              ))}

              <input
                value={bulkForm.teamName}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    teamName: event.target.value,
                  }))
                }
                placeholder="Takım"
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                value={bulkForm.roleName}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    roleName: event.target.value,
                  }))
                }
                placeholder="Görev / unvan"
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                value={bulkForm.workItemCode}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    workItemCode: event.target.value,
                  }))
                }
                placeholder="İş kalemi kodu"
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                value={bulkForm.workItemName}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    workItemName: event.target.value,
                  }))
                }
                placeholder="İş kalemi adı"
                className="rounded-lg border border-slate-300 p-3"
              />

              <input
                value={bulkForm.locationName}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    locationName: event.target.value,
                  }))
                }
                placeholder="Lokasyon"
                className="rounded-lg border border-slate-300 p-3"
              />

              <textarea
                rows={2}
                value={bulkForm.description}
                onChange={(event) =>
                  setBulkForm((current) => ({
                    ...current,
                    description: event.target.value,
                  }))
                }
                placeholder="Açıklama"
                className="rounded-lg border border-slate-300 p-3 md:col-span-2 xl:col-span-4"
              />
            </div>

            <div className="mt-5 rounded-xl border border-slate-200">
              <div className="flex items-center justify-between border-b bg-slate-50 p-4">
                <label className="flex items-center gap-2 font-semibold">
                  <input
                    type="checkbox"
                    checked={allBulkSelected}
                    onChange={(event) =>
                      setSelectedPersonnelIds(
                        event.target.checked
                          ? bulkPersonnel.map((item) => item.id)
                          : []
                      )
                    }
                  />

                  Tüm personeli seç
                </label>

                <span className="text-sm text-slate-500">
                  {selectedPersonnelIds.length} personel seçildi
                </span>
              </div>

              <div className="grid max-h-72 gap-2 overflow-y-auto p-4 md:grid-cols-2 xl:grid-cols-3">
                {bulkPersonnel.map((person) => (
                  <label
                    key={person.id}
                    className="flex items-center gap-3 rounded-lg border border-slate-200 p-3"
                  >
                    <input
                      type="checkbox"
                      checked={selectedPersonnelIds.includes(
                        person.id
                      )}
                      onChange={(event) =>
                        setSelectedPersonnelIds((current) =>
                          event.target.checked
                            ? [...current, person.id]
                            : current.filter(
                                (id) => id !== person.id
                              )
                        )
                      }
                    />

                    <span>
                      <strong className="block text-sm">
                        {person.fullName}
                      </strong>

                      <small className="text-slate-500">
                        {person.employeeNumber}
                      </small>
                    </span>
                  </label>
                ))}
              </div>
            </div>

            <div className="mt-5 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setShowBulkForm(false)}
                className="rounded-lg border border-slate-300 px-4 py-2"
              >
                Vazgeç
              </button>

              <button
                type="submit"
                disabled={
                  saving || selectedPersonnelIds.length === 0
                }
                className="rounded-lg bg-indigo-700 px-5 py-2 text-white disabled:opacity-50"
              >
                {saving
                  ? "Oluşturuluyor…"
                  : `${selectedPersonnelIds.length} Kayıt Oluştur`}
              </button>
            </div>
          </form>
        </section>
      )}

      <section className="mb-5 rounded-xl border border-slate-200 bg-white p-5 shadow-sm print:hidden">
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <select
            value={companyFilter}
            onChange={(event) => {
              setCompanyFilter(event.target.value);
              setPersonnelFilter("");
            }}
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">Tüm şirketler</option>

            {companies.map((company) => (
              <option value={company.id} key={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          <select
            value={projectFilter}
            onChange={(event) =>
              setProjectFilter(event.target.value)
            }
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">Tüm projeler</option>

            {filterProjects.map((project) => (
              <option value={project.id} key={project.id}>
                {project.code} - {project.name}
              </option>
            ))}
          </select>

          <select
            value={personnelFilter}
            onChange={(event) =>
              setPersonnelFilter(event.target.value)
            }
            className="rounded-lg border border-slate-300 p-3"
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
                  {person.employeeNumber} - {person.fullName}
                </option>
              ))}
          </select>

          <select
            value={statusFilter}
            onChange={(event) =>
              setStatusFilter(event.target.value)
            }
            className="rounded-lg border border-slate-300 p-3"
          >
            <option value="">Tüm durumlar</option>

            {statusOptions.map((status) => (
              <option value={status.value} key={status.value}>
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
            className="rounded-lg border border-slate-300 p-3"
          />

          <input
            type="date"
            value={endDateFilter}
            min={startDateFilter || undefined}
            onChange={(event) =>
              setEndDateFilter(event.target.value)
            }
            className="rounded-lg border border-slate-300 p-3"
          />

          <input
            value={searchFilter}
            onChange={(event) =>
              setSearchFilter(event.target.value)
            }
            placeholder="Takım, görev, iş kalemi, lokasyon ara"
            className="rounded-lg border border-slate-300 p-3"
          />

          <button
            type="button"
            onClick={loadAttendance}
            className="rounded-lg bg-brand-700 p-3 font-semibold text-white"
          >
            Filtrele
          </button>
        </div>
      </section>

      <section className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[1650px]">
            <thead className="bg-slate-50 text-left text-xs text-slate-500">
              <tr>
                <th className="p-4 print:hidden">
                  <input
                    type="checkbox"
                    checked={allRecordsSelected}
                    onChange={(event) =>
                      setSelectedRecordIds(
                        event.target.checked
                          ? unapprovedItems.map(
                              (item) => item.id
                            )
                          : []
                      )
                    }
                  />
                </th>

                <th className="p-4">Tarih</th>
                <th className="p-4">Personel</th>
                <th className="p-4">Proje</th>
                <th className="p-4">Durum</th>
                <th className="p-4">Giriş / Çıkış</th>
                <th className="p-4">Normal</th>
                <th className="p-4">FM</th>
                <th className="p-4">Gece</th>
                <th className="p-4">Pazar</th>
                <th className="p-4">Tatil</th>
                <th className="p-4">Toplam</th>
                <th className="p-4">İş / Lokasyon</th>
                <th className="p-4">Onay</th>
                <th className="p-4 text-right print:hidden">
                  İşlemler
                </th>
              </tr>
            </thead>

            <tbody>
              {items.map((item) => {
                const person = personnelById.get(
                  item.personnelId
                );

                const project = item.projectId
                  ? projectById.get(item.projectId)
                  : undefined;

                const busy = actionId === item.id;

                return (
                  <tr
                    key={item.id}
                    className="border-t text-sm"
                  >
                    <td className="p-4 print:hidden">
                      {!item.isApproved && (
                        <input
                          type="checkbox"
                          checked={selectedRecordIds.includes(
                            item.id
                          )}
                          onChange={(event) =>
                            setSelectedRecordIds((current) =>
                              event.target.checked
                                ? [...current, item.id]
                                : current.filter(
                                    (id) => id !== item.id
                                  )
                            )
                          }
                        />
                      )}
                    </td>

                    <td className="p-4">
                      {formatDate(item.workDate)}
                    </td>

                    <td className="p-4">
                      <strong>
                        {person?.fullName ?? "—"}
                      </strong>

                      <small className="block text-slate-500">
                        {person?.employeeNumber ?? "—"}
                      </small>
                    </td>

                    <td className="p-4">
                      {project?.name ?? "Projesiz"}
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
                      {timeValue(item.checkInTime) || "—"}
                      {" / "}
                      {timeValue(item.checkOutTime) || "—"}
                    </td>

                    <td className="p-4">
                      {item.normalHours}
                    </td>

                    <td className="p-4">
                      {item.overtimeHours}
                    </td>

                    <td className="p-4">
                      {item.nightShiftHours}
                    </td>

                    <td className="p-4">
                      {item.sundayHours}
                    </td>

                    <td className="p-4">
                      {item.publicHolidayHours}
                    </td>

                    <td className="p-4 font-bold">
                      {item.totalHours}
                    </td>

                    <td className="max-w-[260px] p-4">
                      <strong className="block truncate">
                        {item.workItemCode
                          ? `${item.workItemCode} - `
                          : ""}
                        {item.workItemName ?? "—"}
                      </strong>

                      <small className="block truncate text-slate-500">
                        {item.teamName ??
                          item.locationName ??
                          item.roleName ??
                          "—"}
                      </small>
                    </td>

                    <td className="p-4">
                      {item.isApproved ? (
                        <span className="font-semibold text-emerald-700">
                          Onaylandı
                        </span>
                      ) : (
                        <span className="text-amber-700">
                          Bekliyor
                        </span>
                      )}
                    </td>

                    <td className="p-4 print:hidden">
                      <div className="flex justify-end gap-2">
                        {!item.isApproved && (
                          <>
                            <button
                              type="button"
                              disabled={busy}
                              onClick={() => openEdit(item)}
                              className="rounded border px-3 py-1.5 text-xs"
                            >
                              Düzenle
                            </button>

                            <button
                              type="button"
                              disabled={busy}
                              onClick={() => approveOne(item)}
                              className="rounded bg-emerald-600 px-3 py-1.5 text-xs text-white"
                            >
                              Onayla
                            </button>

                            <button
                              type="button"
                              disabled={busy}
                              onClick={() => remove(item)}
                              className="rounded bg-red-50 px-3 py-1.5 text-xs text-red-700"
                            >
                              Sil
                            </button>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}

              {!loading && items.length === 0 && (
                <tr>
                  <td
                    colSpan={15}
                    className="p-12 text-center text-slate-500"
                  >
                    Seçilen filtrelere uygun puantaj kaydı bulunamadı.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </ErpShell>
  );
}
