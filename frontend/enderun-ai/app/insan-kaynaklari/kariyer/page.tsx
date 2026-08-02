"use client";

import Link from "next/link";
import {
  FormEvent,
  ReactNode,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";
import ErpShell from "@/components/erp/erp-shell";
import {
  Badge,
  Button,
  Card,
  CardContent,
  EmptyState,
  Input,
  Select,
  StatCard,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import { ApiError } from "@/lib/api/api-client";
import {
  branchService,
  type BranchListItem,
} from "@/services/branch.service";
import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";
import {
  hrCareerService,
  type CareerAnalysis,
  type CareerMovement,
  type CareerMovementKind,
} from "@/services/hr-career.service";
import {
  hrOrganizationService,
  type HrDepartment,
  type HrPosition,
} from "@/services/hr-organization.service";
import {
  personnelService,
  type PersonnelListItem,
} from "@/services/personnel.service";
import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

type ViewMode = "table" | "timeline";

type MovementForm = {
  kind: CareerMovementKind;
  personnelId: string;
  effectiveDate: string;
  companyId: string;
  branchId: string;
  departmentId: string;
  positionId: string;
  projectId: string;
  salary: string;
  role: string;
  reason: string;
  notes: string;
};

const movementDefinitions: Array<{
  kind: CareerMovementKind;
  label: string;
  shortLabel: string;
  description: string;
  tone: "success" | "info" | "warning" | "danger" | "default";
}> = [
  {
    kind: "hire",
    label: "İşe Giriş",
    shortLabel: "İşe giriş",
    description: "İlk şirket, şube, kadro ve ücret kaydı",
    tone: "success",
  },
  {
    kind: "promotion",
    label: "Terfi",
    shortLabel: "Terfi",
    description: "Yeni görev, pozisyon ve ücret değişikliği",
    tone: "info",
  },
  {
    kind: "position-change",
    label: "Pozisyon Değişikliği",
    shortLabel: "Pozisyon",
    description: "Aynı veya farklı birimde yeni görev",
    tone: "info",
  },
  {
    kind: "department-change",
    label: "Departman Değişikliği",
    shortLabel: "Departman",
    description: "Organizasyon birimi değişikliği",
    tone: "warning",
  },
  {
    kind: "salary-change",
    label: "Maaş Değişikliği",
    shortLabel: "Maaş",
    description: "Yeni aylık ücretin yürürlük kaydı",
    tone: "warning",
  },
  {
    kind: "project-change",
    label: "Proje Değişikliği",
    shortLabel: "Proje",
    description: "Şantiye veya proje görevlendirmesi",
    tone: "default",
  },
  {
    kind: "terminate",
    label: "İşten Ayrılış",
    shortLabel: "Ayrılış",
    description: "Çıkış tarihi ve ayrılış nedeni",
    tone: "danger",
  },
];

const emptyForm: MovementForm = {
  kind: "promotion",
  personnelId: "",
  effectiveDate: new Date().toISOString().slice(0, 10),
  companyId: "",
  branchId: "",
  departmentId: "",
  positionId: "",
  projectId: "",
  salary: "",
  role: "",
  reason: "",
  notes: "",
};

const normalized = (value?: string | null) =>
  (value ?? "").trim().toLocaleLowerCase("tr-TR");

function personName(personnel?: PersonnelListItem) {
  if (!personnel) {
    return "Personel bulunamadı";
  }

  return (
    personnel.fullName ||
    `${personnel.firstName ?? ""} ${personnel.lastName ?? ""}`.trim() ||
    personnel.employeeNumber
  );
}

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) {
    return error.message;
  }

  return "İşlem tamamlanamadı. Lütfen tekrar deneyin.";
}

function displayDate(value?: string | null) {
  if (!value) {
    return "—";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleDateString("tr-TR");
}

function currency(value?: number | null) {
  if (value === undefined || value === null) {
    return "—";
  }

  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency: "TRY",
    maximumFractionDigits: 0,
  }).format(value);
}

function movementDate(movement: CareerMovement) {
  return (
    movement.effectiveDate ||
    movement.movementDate ||
    movement.date ||
    movement.createdAtUtc ||
    movement.createdAt ||
    ""
  );
}

function movementKind(movement: CareerMovement): CareerMovementKind | null {
  const rawName = normalized(
    String(
      movement.movementTypeName ??
        movement.typeName ??
        movement.movementType ??
        movement.type ??
        ""
    )
  );

  if (rawName.includes("hire") || rawName.includes("işe") || rawName === "0") {
    return "hire";
  }
  if (rawName.includes("promotion") || rawName.includes("terfi") || rawName === "1") {
    return "promotion";
  }
  if (
    rawName.includes("position") ||
    rawName.includes("pozisyon") ||
    rawName === "2"
  ) {
    return "position-change";
  }
  if (
    rawName.includes("department") ||
    rawName.includes("departman") ||
    rawName === "3"
  ) {
    return "department-change";
  }
  if (rawName.includes("salary") || rawName.includes("maaş") || rawName === "4") {
    return "salary-change";
  }
  if (rawName.includes("project") || rawName.includes("proje") || rawName === "5") {
    return "project-change";
  }
  if (
    rawName.includes("terminate") ||
    rawName.includes("ayrıl") ||
    rawName.includes("çıkış") ||
    rawName === "6" ||
    rawName === "7"
  ) {
    return "terminate";
  }

  return null;
}

function movementDefinition(movement: CareerMovement) {
  const kind = movementKind(movement);
  return (
    movementDefinitions.find((item) => item.kind === kind) ?? {
      kind: "position-change" as const,
      label:
        movement.movementTypeName ||
        movement.typeName ||
        "Kariyer Hareketi",
      shortLabel: "Hareket",
      description: "Personel kariyer kaydı",
      tone: "default" as const,
    }
  );
}

function movementPersonId(movement: CareerMovement) {
  return String(
    movement.personnelId ??
      movement.employeeId ??
      movement.staffId ??
      ""
  );
}

function movementPersonName(
  movement: CareerMovement,
  personnelById: Map<string, PersonnelListItem>
) {
  return (
    movement.personnelName ||
    String(movement.employeeName ?? movement.fullName ?? "") ||
    personName(personnelById.get(movementPersonId(movement)))
  );
}

function firstText(...values: unknown[]) {
  const value = values.find(
    (item) => typeof item === "string" && item.trim().length > 0
  );
  return typeof value === "string" ? value : "";
}

function movementSummary(movement: CareerMovement) {
  const kind = movementKind(movement);

  if (kind === "hire") {
    return firstText(
      movement.newPositionName,
      movement.newDepartmentName,
      movement.description,
      movement.notes,
      "İşe giriş kaydı oluşturuldu."
    );
  }
  if (kind === "promotion" || kind === "position-change") {
    return firstText(
      movement.newPositionName,
      movement.description,
      movement.reason,
      movement.notes,
      "Pozisyon bilgisi güncellendi."
    );
  }
  if (kind === "department-change") {
    return firstText(
      movement.newDepartmentName,
      movement.description,
      movement.reason,
      movement.notes,
      "Departman bilgisi güncellendi."
    );
  }
  if (kind === "salary-change") {
    return movement.newSalary !== undefined && movement.newSalary !== null
      ? `Yeni aylık ücret: ${currency(movement.newSalary)}`
      : firstText(
          movement.description,
          movement.reason,
          movement.notes,
          "Ücret bilgisi güncellendi."
        );
  }
  if (kind === "project-change") {
    return firstText(
      movement.newProjectName,
      movement.description,
      movement.reason,
      movement.notes,
      "Proje görevlendirmesi güncellendi."
    );
  }

  return firstText(
    movement.reason,
    movement.description,
    movement.notes,
    "İşten ayrılış kaydı oluşturuldu."
  );
}

function DetailLine({
  label,
  children,
}: {
  label: string;
  children: ReactNode;
}) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-slate-100 py-2.5 last:border-0">
      <span className="text-xs font-medium uppercase tracking-wide text-slate-500">
        {label}
      </span>
      <span className="text-right text-sm font-medium text-slate-800">
        {children}
      </span>
    </div>
  );
}

function SectionHeader({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-3 border-b border-slate-200 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
      <div>
        <h2 className="font-semibold text-slate-900">{title}</h2>
        <p className="mt-1 text-sm text-slate-500">{description}</p>
      </div>
      {action}
    </div>
  );
}

export default function CareerPage() {
  const [movements, setMovements] = useState<CareerMovement[]>([]);
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [departments, setDepartments] = useState<HrDepartment[]>([]);
  const [positions, setPositions] = useState<HrPosition[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [analysis, setAnalysis] = useState<CareerAnalysis | null>(null);
  const [selectedPersonnelId, setSelectedPersonnelId] = useState("");
  const [viewMode, setViewMode] = useState<ViewMode>("table");
  const [search, setSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState("all");
  const [companyFilter, setCompanyFilter] = useState("all");
  const [branchFilter, setBranchFilter] = useState("all");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [editorOpen, setEditorOpen] = useState(false);
  const [form, setForm] = useState<MovementForm>(emptyForm);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const loadScreen = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [
        movementRows,
        personnelRows,
        companyRows,
        branchRows,
        departmentRows,
        positionRows,
        projectRows,
      ] = await Promise.all([
        hrCareerService.getAll(),
        personnelService.getAll(),
        companyService.getAll(),
        branchService.getAll(),
        hrOrganizationService.getDepartments(),
        hrOrganizationService.getPositions(),
        projectService.getAll(),
      ]);

      setMovements(movementRows ?? []);
      setPersonnel(personnelRows ?? []);
      setCompanies(companyRows ?? []);
      setBranches(branchRows ?? []);
      setDepartments(departmentRows ?? []);
      setPositions(positionRows ?? []);
      setProjects(projectRows ?? []);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadScreen();
  }, [loadScreen]);

  useEffect(() => {
    if (!notice) {
      return;
    }

    const timer = window.setTimeout(() => setNotice(""), 3500);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const personnelById = useMemo(
    () => new Map(personnel.map((item) => [item.id, item])),
    [personnel]
  );
  const companyById = useMemo(
    () => new Map(companies.map((item) => [item.id, item])),
    [companies]
  );

  const selectedPersonnel = personnelById.get(selectedPersonnelId);

  const loadPersonnelDetail = useCallback(
    async (personnelId: string) => {
      setSelectedPersonnelId(personnelId);
      setDetailLoading(true);
      setAnalysis(null);

      try {
        const [historyResult, analysisResult] = await Promise.allSettled([
          hrCareerService.getPersonnelHistory(personnelId),
          hrCareerService.getPersonnelAnalysis(personnelId),
        ]);

        if (historyResult.status === "fulfilled") {
          const history = historyResult.value;
          setMovements((current) => {
            const otherPersonnel = current.filter(
              (item) => movementPersonId(item) !== personnelId
            );
            return [...otherPersonnel, ...history];
          });
        }

        if (analysisResult.status === "fulfilled") {
          setAnalysis(analysisResult.value);
        }
      } finally {
        setDetailLoading(false);
      }
    },
    []
  );

  const availableBranches = useMemo(
    () =>
      branches.filter(
        (item) =>
          companyFilter === "all" || item.companyId === companyFilter
      ),
    [branches, companyFilter]
  );

  const visibleMovements = useMemo(() => {
    const term = normalized(search);
    const from = dateFrom ? new Date(`${dateFrom}T00:00:00`) : null;
    const to = dateTo ? new Date(`${dateTo}T23:59:59`) : null;

    return [...movements]
      .filter((movement) => {
        const personnelItem = personnelById.get(movementPersonId(movement));
        const kind = movementKind(movement);
        const dateText = movementDate(movement);
        const date = dateText ? new Date(dateText) : null;

        if (typeFilter !== "all" && kind !== typeFilter) {
          return false;
        }
        if (
          companyFilter !== "all" &&
          personnelItem?.companyId !== companyFilter &&
          movement.newCompanyId !== companyFilter &&
          movement.oldCompanyId !== companyFilter
        ) {
          return false;
        }
        if (
          branchFilter !== "all" &&
          personnelItem?.branchId !== branchFilter &&
          movement.newBranchId !== branchFilter &&
          movement.oldBranchId !== branchFilter
        ) {
          return false;
        }
        if (from && (!date || date < from)) {
          return false;
        }
        if (to && (!date || date > to)) {
          return false;
        }
        if (!term) {
          return true;
        }

        return [
          movementPersonName(movement, personnelById),
          movement.employeeNumber,
          personnelItem?.employeeNumber,
          movementDefinition(movement).label,
          movementSummary(movement),
          movement.oldDepartmentName,
          movement.newDepartmentName,
          movement.oldPositionName,
          movement.newPositionName,
          movement.oldProjectName,
          movement.newProjectName,
          movement.reason,
          movement.notes,
        ].some((value) => normalized(String(value ?? "")).includes(term));
      })
      .sort(
        (left, right) =>
          new Date(movementDate(right) || 0).getTime() -
          new Date(movementDate(left) || 0).getTime()
      );
  }, [
    branchFilter,
    companyFilter,
    dateFrom,
    dateTo,
    movements,
    personnelById,
    search,
    typeFilter,
  ]);

  const selectedHistory = useMemo(
    () =>
      movements
        .filter(
          (movement) =>
            movementPersonId(movement) === selectedPersonnelId
        )
        .sort(
          (left, right) =>
            new Date(movementDate(right) || 0).getTime() -
            new Date(movementDate(left) || 0).getTime()
        ),
    [movements, selectedPersonnelId]
  );

  const summary = useMemo(() => {
    const currentMonth = new Date().toISOString().slice(0, 7);
    return {
      total: movements.length,
      monthly: movements.filter((item) =>
        movementDate(item).startsWith(currentMonth)
      ).length,
      promotions: movements.filter(
        (item) => movementKind(item) === "promotion"
      ).length,
      changes: movements.filter((item) =>
        [
          "position-change",
          "department-change",
          "project-change",
        ].includes(movementKind(item) ?? "")
      ).length,
    };
  }, [movements]);

  const formPersonnel = personnelById.get(form.personnelId);
  const formCompanyId = form.companyId || formPersonnel?.companyId || "";
  const formBranches = branches.filter(
    (item) => !formCompanyId || item.companyId === formCompanyId
  );
  const formDepartments = departments.filter(
    (item) => !formCompanyId || item.companyId === formCompanyId
  );
  const formDepartmentIds = new Set(formDepartments.map((item) => item.id));
  const formPositions = positions.filter(
    (item) =>
      (!form.departmentId || item.departmentId === form.departmentId) &&
      (!formCompanyId ||
        item.companyId === formCompanyId ||
        formDepartmentIds.has(item.departmentId))
  );
  const formProjects = projects.filter(
    (item) => !formCompanyId || item.companyId === formCompanyId
  );

  const companyOptions = [
    { value: "all", label: "Tüm şirketler" },
    ...companies.map((item) => ({
      value: item.id,
      label: `${item.code} · ${item.name}`,
    })),
  ];
  const branchOptions = [
    { value: "all", label: "Tüm şubeler" },
    ...availableBranches.map((item) => ({
      value: item.id,
      label: `${item.code} · ${item.name}`,
    })),
  ];
  const typeOptions = [
    { value: "all", label: "Tüm hareket türleri" },
    ...movementDefinitions.map((item) => ({
      value: item.kind,
      label: item.label,
    })),
  ];
  const personnelOptions = personnel
    .filter((item) => item.isActive !== false || form.kind === "hire")
    .map((item) => ({
      value: item.id,
      label: `${item.employeeNumber} · ${personName(item)}`,
    }));

  function openEditor(kind: CareerMovementKind = "promotion") {
    setForm({
      ...emptyForm,
      kind,
      personnelId: selectedPersonnelId,
      companyId: selectedPersonnel?.companyId ?? "",
      branchId: selectedPersonnel?.branchId ?? "",
    });
    setError("");
    setEditorOpen(true);
  }

  function selectFormPersonnel(personnelId: string) {
    const selected = personnelById.get(personnelId);
    setForm((current) => ({
      ...current,
      personnelId,
      companyId: selected?.companyId ?? "",
      branchId: selected?.branchId ?? "",
      departmentId: "",
      positionId: "",
      projectId: "",
      salary:
        current.kind === "salary-change" && selected?.monthlySalary
          ? String(selected.monthlySalary)
          : current.salary,
    }));
  }

  async function submitMovement(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!form.personnelId || !form.effectiveDate) {
      setError("Personel ve yürürlük tarihi zorunludur.");
      return;
    }

    const salary = form.salary ? Number(form.salary) : null;
    const common = {
      personnelId: form.personnelId,
      effectiveDate: form.effectiveDate,
      movementDate: form.effectiveDate,
      reason: form.reason.trim() || null,
      description: form.notes.trim() || form.reason.trim() || null,
      notes: form.notes.trim() || null,
    };

    let payload: Record<string, unknown> = common;

    if (form.kind === "hire") {
      payload = {
        ...common,
        hireDate: form.effectiveDate,
        employmentStartDate: form.effectiveDate,
        startDate: form.effectiveDate,
        companyId: form.companyId || formCompanyId || null,
        branchId: form.branchId || null,
        departmentId: form.departmentId || null,
        positionId: form.positionId || null,
        projectId: form.projectId || null,
        salary,
        monthlySalary: salary,
        role: form.role.trim() || null,
      };
    } else if (form.kind === "promotion") {
      payload = {
        ...common,
        departmentId: form.departmentId || null,
        newDepartmentId: form.departmentId || null,
        positionId: form.positionId || null,
        newPositionId: form.positionId || null,
        salary,
        newSalary: salary,
        monthlySalary: salary,
      };
    } else if (form.kind === "position-change") {
      payload = {
        ...common,
        positionId: form.positionId || null,
        newPositionId: form.positionId || null,
      };
    } else if (form.kind === "department-change") {
      payload = {
        ...common,
        departmentId: form.departmentId || null,
        newDepartmentId: form.departmentId || null,
      };
    } else if (form.kind === "salary-change") {
      payload = {
        ...common,
        salary,
        newSalary: salary,
        monthlySalary: salary,
      };
    } else if (form.kind === "project-change") {
      payload = {
        ...common,
        projectId: form.projectId || null,
        newProjectId: form.projectId || null,
        role: form.role.trim() || null,
        isPrimaryAssignment: true,
      };
    } else {
      payload = {
        ...common,
        terminationDate: form.effectiveDate,
        employmentEndDate: form.effectiveDate,
        endDate: form.effectiveDate,
      };
    }

    setSaving(true);
    setError("");

    try {
      const response = await hrCareerService.create(form.kind, payload);
      setNotice(response.message || "Kariyer hareketi kaydedildi.");
      setEditorOpen(false);
      await loadScreen();
      if (form.personnelId) {
        await loadPersonnelDetail(form.personnelId);
      }
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setSaving(false);
    }
  }

  const currentDefinition = movementDefinitions.find(
    (item) => item.kind === form.kind
  )!;

  return (
    <ErpShell
      title="Kariyer Yönetimi"
      description="Personel hareketleri, terfi geçmişi ve kariyer analizleri"
    >
      <div className="space-y-6">
        <div className="flex flex-col gap-4 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:flex-row lg:items-center lg:justify-between">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="info">İnsan Kaynakları</Badge>
              <span className="text-xs font-medium uppercase tracking-wider text-slate-400">
                Kariyer ve Organizasyon Hareketleri
              </span>
            </div>
            <h1 className="mt-3 text-2xl font-semibold tracking-tight text-slate-950">
              Personel kariyerini tek zaman çizgisinde yönetin
            </h1>
            <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-500">
              İşe girişten terfi ve proje değişikliğine kadar tüm hareketleri
              gerçek personel kayıtlarıyla izleyin.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => void loadScreen()}>
              Yenile
            </Button>
            <Button onClick={() => openEditor()}>+ Kariyer Hareketi</Button>
          </div>
        </div>

        {error && !editorOpen && (
          <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}
        {notice && (
          <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
            {notice}
          </div>
        )}

        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            title="Toplam Hareket"
            value={loading ? "…" : summary.total}
            description="Kayıtlı kariyer geçmişi"
            icon="↕"
          />
          <StatCard
            title="Bu Ay"
            value={loading ? "…" : summary.monthly}
            description="Yürürlüğe giren kayıt"
            icon="◷"
          />
          <StatCard
            title="Terfi"
            value={loading ? "…" : summary.promotions}
            description="Toplam terfi hareketi"
            icon="↑"
          />
          <StatCard
            title="Görev Değişimi"
            value={loading ? "…" : summary.changes}
            description="Pozisyon, birim ve proje"
            icon="◎"
          />
        </div>

        <Card>
          <CardContent className="space-y-4 p-5">
            <div className="grid gap-3 lg:grid-cols-[minmax(240px,1.4fr)_repeat(3,minmax(160px,0.7fr))]">
              <Input
                label="Arama"
                value={search}
                placeholder="Personel, sicil, pozisyon, proje..."
                onChange={(event) => setSearch(event.target.value)}
              />
              <Select
                label="Hareket türü"
                value={typeFilter}
                options={typeOptions}
                onChange={(event) => setTypeFilter(event.target.value)}
              />
              <Select
                label="Şirket"
                value={companyFilter}
                options={companyOptions}
                onChange={(event) => {
                  setCompanyFilter(event.target.value);
                  setBranchFilter("all");
                }}
              />
              <Select
                label="Şube"
                value={branchFilter}
                options={branchOptions}
                onChange={(event) => setBranchFilter(event.target.value)}
              />
            </div>
            <div className="flex flex-col gap-3 border-t border-slate-100 pt-4 sm:flex-row sm:items-end sm:justify-between">
              <div className="grid flex-1 gap-3 sm:max-w-xl sm:grid-cols-2">
                <Input
                  label="Başlangıç tarihi"
                  type="date"
                  value={dateFrom}
                  onChange={(event) => setDateFrom(event.target.value)}
                />
                <Input
                  label="Bitiş tarihi"
                  type="date"
                  value={dateTo}
                  onChange={(event) => setDateTo(event.target.value)}
                />
              </div>
              <div className="flex rounded-lg border border-slate-200 bg-slate-50 p-1">
                <Button
                  size="sm"
                  variant={viewMode === "table" ? "secondary" : "ghost"}
                  onClick={() => setViewMode("table")}
                >
                  Tablo
                </Button>
                <Button
                  size="sm"
                  variant={viewMode === "timeline" ? "secondary" : "ghost"}
                  onClick={() => setViewMode("timeline")}
                >
                  Zaman Çizgisi
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>

        <div className="grid gap-6 2xl:grid-cols-[minmax(0,1fr)_360px]">
          <Card>
            <SectionHeader
              title={viewMode === "table" ? "Kariyer Hareketleri" : "Kariyer Zaman Çizgisi"}
              description={`${visibleMovements.length} kayıt gösteriliyor`}
            />

            {loading ? (
              <CardContent>
                <div className="py-16 text-center text-sm text-slate-500">
                  Kariyer kayıtları yükleniyor...
                </div>
              </CardContent>
            ) : visibleMovements.length === 0 ? (
              <CardContent>
                <EmptyState
                  title="Kariyer hareketi bulunamadı"
                  description="Filtreleri değiştirin veya ilk kariyer hareketini oluşturun."
                  action={
                    <Button onClick={() => openEditor()}>
                      Kariyer Hareketi Ekle
                    </Button>
                  }
                />
              </CardContent>
            ) : viewMode === "table" ? (
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Tarih / Tür</TableHead>
                      <TableHead>Personel</TableHead>
                      <TableHead>Önceki</TableHead>
                      <TableHead>Yeni</TableHead>
                      <TableHead>Açıklama</TableHead>
                      <TableHead className="text-right">İşlem</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {visibleMovements.map((movement, index) => {
                      const definition = movementDefinition(movement);
                      const personnelId = movementPersonId(movement);
                      const before = firstText(
                        movement.oldPositionName,
                        movement.oldDepartmentName,
                        movement.oldProjectName,
                        movement.oldCompanyName
                      );
                      const after = firstText(
                        movement.newPositionName,
                        movement.newDepartmentName,
                        movement.newProjectName,
                        movement.newCompanyName
                      );

                      return (
                        <TableRow key={movement.id || `${personnelId}-${index}`}>
                          <TableCell>
                            <div className="min-w-32">
                              <Badge variant={definition.tone}>
                                {definition.shortLabel}
                              </Badge>
                              <span className="mt-2 block text-xs text-slate-500">
                                {displayDate(movementDate(movement))}
                              </span>
                            </div>
                          </TableCell>
                          <TableCell>
                            <button
                              type="button"
                              className="text-left"
                              onClick={() =>
                                personnelId &&
                                void loadPersonnelDetail(personnelId)
                              }
                            >
                              <strong className="block text-sm text-slate-900 hover:underline">
                                {movementPersonName(movement, personnelById)}
                              </strong>
                              <span className="mt-1 block text-xs text-slate-500">
                                {movement.employeeNumber ||
                                  personnelById.get(personnelId)?.employeeNumber ||
                                  "—"}
                              </span>
                            </button>
                          </TableCell>
                          <TableCell className="text-sm text-slate-500">
                            {movement.oldSalary !== undefined &&
                            movement.oldSalary !== null
                              ? currency(movement.oldSalary)
                              : before || "—"}
                          </TableCell>
                          <TableCell>
                            <span className="font-medium text-slate-900">
                              {movement.newSalary !== undefined &&
                              movement.newSalary !== null
                                ? currency(movement.newSalary)
                                : after || "—"}
                            </span>
                          </TableCell>
                          <TableCell>
                            <span className="line-clamp-2 max-w-sm text-sm text-slate-600">
                              {movementSummary(movement)}
                            </span>
                          </TableCell>
                          <TableCell>
                            <div className="flex justify-end">
                              <Button
                                size="sm"
                                variant="ghost"
                                onClick={() =>
                                  personnelId &&
                                  void loadPersonnelDetail(personnelId)
                                }
                              >
                                Geçmiş
                              </Button>
                            </div>
                          </TableCell>
                        </TableRow>
                      );
                    })}
                  </TableBody>
                </Table>
              </div>
            ) : (
              <CardContent>
                <div className="space-y-0">
                  {visibleMovements.map((movement, index) => {
                    const definition = movementDefinition(movement);
                    const personnelId = movementPersonId(movement);
                    return (
                      <article
                        key={movement.id || `${personnelId}-${index}`}
                        className="relative grid grid-cols-[32px_minmax(0,1fr)] gap-3 pb-6 last:pb-0"
                      >
                        {index < visibleMovements.length - 1 && (
                          <span className="absolute bottom-0 left-[15px] top-8 w-px bg-slate-200" />
                        )}
                        <span className="relative z-10 mt-1 flex h-8 w-8 items-center justify-center rounded-full border-4 border-white bg-slate-900 text-xs text-white shadow">
                          {definition.kind === "promotion" ? "↑" : "•"}
                        </span>
                        <button
                          type="button"
                          onClick={() =>
                            personnelId &&
                            void loadPersonnelDetail(personnelId)
                          }
                          className="rounded-xl border border-slate-200 bg-white p-4 text-left transition hover:border-slate-300 hover:shadow-sm"
                        >
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <div className="flex flex-wrap items-center gap-2">
                              <Badge variant={definition.tone}>
                                {definition.label}
                              </Badge>
                              <strong className="text-sm text-slate-900">
                                {movementPersonName(movement, personnelById)}
                              </strong>
                            </div>
                            <span className="text-xs text-slate-500">
                              {displayDate(movementDate(movement))}
                            </span>
                          </div>
                          <p className="mt-2 text-sm text-slate-600">
                            {movementSummary(movement)}
                          </p>
                        </button>
                      </article>
                    );
                  })}
                </div>
              </CardContent>
            )}
          </Card>

          <div className="space-y-6">
            <Card>
              <SectionHeader
                title="Personel Kariyer Özeti"
                description="Geçmiş ve analiz için personel seçin"
              />
              <CardContent className="space-y-4">
                <Select
                  label="Personel"
                  value={selectedPersonnelId}
                  options={personnelOptions}
                  placeholder="Personel seçin"
                  onChange={(event) =>
                    event.target.value &&
                    void loadPersonnelDetail(event.target.value)
                  }
                />

                {detailLoading ? (
                  <div className="py-8 text-center text-sm text-slate-500">
                    Kariyer özeti hazırlanıyor...
                  </div>
                ) : selectedPersonnel ? (
                  <>
                    <div className="rounded-xl bg-slate-950 p-4 text-white">
                      <div className="flex items-center gap-3">
                        <span className="flex h-11 w-11 items-center justify-center rounded-xl bg-white/10 text-lg font-semibold">
                          {personName(selectedPersonnel).charAt(0)}
                        </span>
                        <div className="min-w-0">
                          <strong className="block truncate">
                            {personName(selectedPersonnel)}
                          </strong>
                          <span className="mt-1 block text-xs text-slate-300">
                            {selectedPersonnel.employeeNumber} ·{" "}
                            {selectedPersonnel.jobTitle || "Pozisyon tanımsız"}
                          </span>
                        </div>
                      </div>
                    </div>

                    <div>
                      <DetailLine label="Şirket">
                        {selectedPersonnel.companyName ||
                          companyById.get(selectedPersonnel.companyId)?.name ||
                          "—"}
                      </DetailLine>
                      <DetailLine label="Şube">
                        {selectedPersonnel.branchName || "—"}
                      </DetailLine>
                      <DetailLine label="İşe giriş">
                        {displayDate(selectedPersonnel.employmentStartDate)}
                      </DetailLine>
                      <DetailLine label="Hareket sayısı">
                        {analysis?.totalMovements ?? selectedHistory.length}
                      </DetailLine>
                      <DetailLine label="Terfi">
                        {analysis?.promotionCount ??
                          selectedHistory.filter(
                            (item) => movementKind(item) === "promotion"
                          ).length}
                      </DetailLine>
                    </div>

                    {(analysis?.careerSummary ||
                      analysis?.recommendation ||
                      analysis?.recommendations?.length) && (
                      <div className="rounded-xl border border-indigo-100 bg-indigo-50 p-4">
                        <span className="text-xs font-semibold uppercase tracking-wide text-indigo-700">
                          Kariyer Analizi
                        </span>
                        <p className="mt-2 text-sm leading-6 text-indigo-950">
                          {analysis.careerSummary ||
                            analysis.recommendation ||
                            analysis.recommendations?.join(" ")}
                        </p>
                      </div>
                    )}

                    <div className="grid gap-2">
                      <Button onClick={() => openEditor()}>
                        Yeni Hareket Ekle
                      </Button>
                      <Link
                        href={`/insan-kaynaklari/personel-360?personnelId=${selectedPersonnel.id}`}
                        className="flex h-10 items-center justify-center rounded-lg border border-slate-300 bg-white px-4 text-sm font-medium text-slate-800 transition hover:bg-slate-50"
                      >
                        Personel 360&apos;a Git
                      </Link>
                    </div>
                  </>
                ) : (
                  <EmptyState
                    title="Personel seçilmedi"
                    description="Kariyer geçmişini ve analizini görmek için listeden bir personel seçin."
                  />
                )}
              </CardContent>
            </Card>

            <Card>
              <SectionHeader
                title="Hızlı Hareketler"
                description="Sık kullanılan personel işlemleri"
              />
              <CardContent className="grid gap-2">
                {movementDefinitions.slice(0, 6).map((definition) => (
                  <button
                    key={definition.kind}
                    type="button"
                    onClick={() => openEditor(definition.kind)}
                    className="flex items-center justify-between rounded-xl border border-slate-200 px-3 py-3 text-left transition hover:border-slate-300 hover:bg-slate-50"
                  >
                    <span>
                      <strong className="block text-sm text-slate-900">
                        {definition.label}
                      </strong>
                      <span className="mt-0.5 block text-xs text-slate-500">
                        {definition.description}
                      </span>
                    </span>
                    <span className="text-slate-400">→</span>
                  </button>
                ))}
              </CardContent>
            </Card>
          </div>
        </div>
      </div>

      {editorOpen && (
        <div
          className="fixed inset-0 z-50 flex items-end justify-center bg-slate-950/55 p-0 backdrop-blur-sm sm:items-center sm:p-4"
          role="dialog"
          aria-modal="true"
          onMouseDown={(event) => {
            if (event.currentTarget === event.target && !saving) {
              setEditorOpen(false);
            }
          }}
        >
          <div className="max-h-[94vh] w-full overflow-y-auto rounded-t-2xl bg-white shadow-2xl sm:max-w-3xl sm:rounded-2xl">
            <div className="sticky top-0 z-10 flex items-start justify-between border-b border-slate-200 bg-white px-5 py-4">
              <div>
                <h2 className="text-lg font-semibold text-slate-950">
                  Yeni Kariyer Hareketi
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  {currentDefinition.description}
                </p>
              </div>
              <Button
                size="sm"
                variant="ghost"
                disabled={saving}
                onClick={() => setEditorOpen(false)}
              >
                ✕
              </Button>
            </div>

            <form onSubmit={submitMovement}>
              <div className="space-y-5 p-5">
                {error && (
                  <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                    {error}
                  </div>
                )}

                <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                  {movementDefinitions.map((definition) => (
                    <button
                      key={definition.kind}
                      type="button"
                      onClick={() =>
                        setForm((current) => ({
                          ...current,
                          kind: definition.kind,
                          departmentId: "",
                          positionId: "",
                          projectId: "",
                          salary: "",
                          role: "",
                          reason: "",
                        }))
                      }
                      className={[
                        "rounded-xl border p-3 text-left transition",
                        form.kind === definition.kind
                          ? "border-slate-900 bg-slate-900 text-white"
                          : "border-slate-200 bg-white text-slate-800 hover:border-slate-300",
                      ].join(" ")}
                    >
                      <strong className="block text-sm">
                        {definition.shortLabel}
                      </strong>
                      <span
                        className={[
                          "mt-1 block text-xs",
                          form.kind === definition.kind
                            ? "text-slate-300"
                            : "text-slate-500",
                        ].join(" ")}
                      >
                        {definition.description}
                      </span>
                    </button>
                  ))}
                </div>

                <div className="grid gap-4 sm:grid-cols-2">
                  <Select
                    label="Personel"
                    required
                    value={form.personnelId}
                    options={personnelOptions}
                    placeholder="Personel seçin"
                    onChange={(event) =>
                      selectFormPersonnel(event.target.value)
                    }
                  />
                  <Input
                    label="Yürürlük tarihi"
                    required
                    type="date"
                    value={form.effectiveDate}
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        effectiveDate: event.target.value,
                      }))
                    }
                  />
                </div>

                {form.kind === "hire" && (
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Select
                      label="Şirket"
                      required
                      value={formCompanyId}
                      options={companies.map((item) => ({
                        value: item.id,
                        label: `${item.code} · ${item.name}`,
                      }))}
                      placeholder="Şirket seçin"
                      onChange={(event) =>
                        setForm((current) => ({
                          ...current,
                          companyId: event.target.value,
                          branchId: "",
                          departmentId: "",
                          positionId: "",
                          projectId: "",
                        }))
                      }
                    />
                    <Select
                      label="Şube"
                      value={form.branchId}
                      options={formBranches.map((item) => ({
                        value: item.id,
                        label: `${item.code} · ${item.name}`,
                      }))}
                      placeholder="Şube seçin"
                      onChange={(event) =>
                        setForm((current) => ({
                          ...current,
                          branchId: event.target.value,
                        }))
                      }
                    />
                  </div>
                )}

                {["hire", "promotion", "department-change"].includes(
                  form.kind
                ) && (
                  <Select
                    label={
                      form.kind === "department-change"
                        ? "Yeni departman"
                        : "Departman"
                    }
                    required={form.kind === "department-change"}
                    value={form.departmentId}
                    options={formDepartments.map((item) => ({
                      value: item.id,
                      label: `${item.code} · ${item.name}`,
                    }))}
                    placeholder="Departman seçin"
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        departmentId: event.target.value,
                        positionId: "",
                      }))
                    }
                  />
                )}

                {["hire", "promotion", "position-change"].includes(
                  form.kind
                ) && (
                  <Select
                    label={
                      form.kind === "promotion"
                        ? "Terfi pozisyonu"
                        : form.kind === "position-change"
                          ? "Yeni pozisyon"
                          : "Pozisyon"
                    }
                    required={form.kind !== "hire"}
                    value={form.positionId}
                    options={formPositions.map((item) => ({
                      value: item.id,
                      label: `${item.code} · ${
                        item.title || item.name || "Pozisyon"
                      }`,
                    }))}
                    placeholder="Pozisyon seçin"
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        positionId: event.target.value,
                      }))
                    }
                  />
                )}

                {["hire", "project-change"].includes(form.kind) && (
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Select
                      label={
                        form.kind === "project-change"
                          ? "Yeni proje / şantiye"
                          : "Başlangıç projesi"
                      }
                      required={form.kind === "project-change"}
                      value={form.projectId}
                      options={formProjects.map((item) => ({
                        value: item.id,
                        label: `${item.code} · ${item.name}`,
                      }))}
                      placeholder="Proje seçin"
                      onChange={(event) =>
                        setForm((current) => ({
                          ...current,
                          projectId: event.target.value,
                        }))
                      }
                    />
                    <Input
                      label="Görev / rol"
                      value={form.role}
                      placeholder="Örn. Proje Sorumlusu"
                      onChange={(event) =>
                        setForm((current) => ({
                          ...current,
                          role: event.target.value,
                        }))
                      }
                    />
                  </div>
                )}

                {["hire", "promotion", "salary-change"].includes(form.kind) && (
                  <Input
                    label={
                      form.kind === "salary-change"
                        ? "Yeni aylık ücret"
                        : "Aylık ücret"
                    }
                    required={form.kind === "salary-change"}
                    type="number"
                    min="0"
                    step="0.01"
                    value={form.salary}
                    placeholder="0,00"
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        salary: event.target.value,
                      }))
                    }
                  />
                )}

                <Input
                  label={
                    form.kind === "terminate"
                      ? "Ayrılış nedeni"
                      : "Hareket nedeni"
                  }
                  required={form.kind === "terminate"}
                  value={form.reason}
                  placeholder="Kararın gerekçesini yazın"
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      reason: event.target.value,
                    }))
                  }
                />

                <div>
                  <label
                    htmlFor="career-notes"
                    className="mb-1.5 block text-sm font-medium text-slate-700"
                  >
                    Açıklama ve notlar
                  </label>
                  <textarea
                    id="career-notes"
                    rows={4}
                    value={form.notes}
                    placeholder="Karar, onay ve geçiş detaylarını yazın..."
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        notes: event.target.value,
                      }))
                    }
                    className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-slate-500 focus:ring-2 focus:ring-slate-100"
                  />
                </div>
              </div>

              <div className="sticky bottom-0 flex justify-end gap-2 border-t border-slate-200 bg-white px-5 py-4">
                <Button
                  type="button"
                  variant="ghost"
                  disabled={saving}
                  onClick={() => setEditorOpen(false)}
                >
                  Vazgeç
                </Button>
                <Button type="submit" loading={saving}>
                  {currentDefinition.label} Kaydını Oluştur
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}
    </ErpShell>
  );
}
