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
import { ConfirmDialog } from "@/components/ui";
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
  companyService,
  type CompanyListItem,
} from "@/services/company.service";
import {
  branchService,
  type BranchListItem,
} from "@/services/branch.service";
import {
  hrOrganizationService,
  type HrDepartment,
  type HrPosition,
} from "@/services/hr-organization.service";
import {
  personnelService,
  type PersonnelListItem,
} from "@/services/personnel.service";

type OrganizationTab = "chart" | "departments" | "positions";
type StatusFilter = "all" | "active" | "inactive";
type DialogState =
  | { type: "department"; record?: HrDepartment }
  | { type: "position"; record?: HrPosition }
  | null;

type DepartmentForm = {
  companyId: string;
  code: string;
  name: string;
  parentDepartmentId: string;
  managerPersonnelId: string;
  isActive: boolean;
};

type PositionForm = {
  departmentId: string;
  code: string;
  title: string;
  description: string;
  isManagerial: boolean;
  isActive: boolean;
};

const emptyDepartmentForm: DepartmentForm = {
  companyId: "",
  code: "",
  name: "",
  parentDepartmentId: "",
  managerPersonnelId: "",
  isActive: true,
};

const emptyPositionForm: PositionForm = {
  departmentId: "",
  code: "",
  title: "",
  description: "",
  isManagerial: false,
  isActive: true,
};

const normalized = (value?: string | null) =>
  (value ?? "").trim().toLocaleLowerCase("tr-TR");

const positionTitle = (position: HrPosition) =>
  position.title || position.name || "Tanımsız pozisyon";

const personnelName = (personnel: PersonnelListItem) =>
  personnel.fullName ||
  `${personnel.firstName ?? ""} ${personnel.lastName ?? ""}`.trim() ||
  personnel.employeeNumber;

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError) {
    return error.message;
  }

  if (error instanceof Error) {
    return error.message;
  }

  return "İşlem tamamlanamadı. Lütfen tekrar deneyin.";
}

function SectionTitle({
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
        <h2 className="text-base font-semibold text-slate-900">{title}</h2>
        <p className="mt-1 text-sm text-slate-500">{description}</p>
      </div>
      {action}
    </div>
  );
}

function OrganizationNode({
  department,
  departments,
  positions,
  personnel,
  depth,
  managerName,
  onEdit,
  onAddChild,
  onAddPosition,
}: {
  department: HrDepartment;
  departments: HrDepartment[];
  positions: HrPosition[];
  personnel: PersonnelListItem[];
  depth: number;
  managerName: (department: HrDepartment) => string;
  onEdit: (department: HrDepartment) => void;
  onAddChild: (department: HrDepartment) => void;
  onAddPosition: (department: HrDepartment) => void;
}) {
  const children = departments.filter(
    (item) => item.parentDepartmentId === department.id
  );
  const departmentPositions = positions.filter(
    (item) => item.departmentId === department.id
  );
  const titles = new Set(
    departmentPositions.map((item) => normalized(positionTitle(item)))
  );
  const departmentPersonnel = personnel.filter((item) =>
    titles.has(normalized(item.jobTitle))
  );

  return (
    <div className={depth > 0 ? "border-l-2 border-slate-200 pl-4 sm:pl-6" : ""}>
      <article className="group rounded-xl border border-slate-200 bg-white p-4 shadow-sm transition hover:border-slate-300 hover:shadow-md">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
          <div className="flex min-w-0 items-start gap-3">
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand-700 text-lg font-semibold text-white">
              {department.name.charAt(0).toLocaleUpperCase("tr-TR")}
            </div>
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-2">
                <h3 className="truncate font-semibold text-slate-900">
                  {department.name}
                </h3>
                <Badge variant={department.isActive ? "success" : "default"}>
                  {department.isActive ? "Aktif" : "Pasif"}
                </Badge>
                <span className="rounded bg-slate-100 px-2 py-0.5 font-mono text-xs text-slate-600">
                  {department.code}
                </span>
              </div>
              <p className="mt-1 text-sm text-slate-500">
                Yönetici: {managerName(department)}
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-3 text-sm">
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-center">
              <strong className="block text-slate-900">
                {departmentPositions.length}
              </strong>
              <span className="text-xs text-slate-500">Pozisyon</span>
            </div>
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-center">
              <strong className="block text-slate-900">
                {departmentPersonnel.length}
              </strong>
              <span className="text-xs text-slate-500">Personel</span>
            </div>
            <div className="flex gap-1 opacity-100 transition lg:opacity-0 lg:group-hover:opacity-100">
              <Button size="sm" variant="ghost" onClick={() => onAddChild(department)}>
                Alt birim
              </Button>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => onAddPosition(department)}
              >
                Pozisyon
              </Button>
              <Button size="sm" variant="secondary" onClick={() => onEdit(department)}>
                Düzenle
              </Button>
            </div>
          </div>
        </div>

        {departmentPositions.length > 0 && (
          <div className="mt-4 flex flex-wrap gap-2 border-t border-slate-100 pt-3">
            {departmentPositions.slice(0, 8).map((position) => (
              <span
                key={position.id}
                className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-xs text-slate-600"
              >
                {positionTitle(position)}
              </span>
            ))}
            {departmentPositions.length > 8 && (
              <span className="px-2 py-1 text-xs text-slate-500">
                +{departmentPositions.length - 8} pozisyon
              </span>
            )}
          </div>
        )}
      </article>

      {children.length > 0 && (
        <div className="mt-3 space-y-3">
          {children.map((child) => (
            <OrganizationNode
              key={child.id}
              department={child}
              departments={departments}
              positions={positions}
              personnel={personnel}
              depth={depth + 1}
              managerName={managerName}
              onEdit={onEdit}
              onAddChild={onAddChild}
              onAddPosition={onAddPosition}
            />
          ))}
        </div>
      )}
    </div>
  );
}

export default function OrganizationPage() {
  const [departments, setDepartments] = useState<HrDepartment[]>([]);
  const [positions, setPositions] = useState<HrPosition[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [personnel, setPersonnel] = useState<PersonnelListItem[]>([]);
  const [activeTab, setActiveTab] = useState<OrganizationTab>("chart");
  const [companyFilter, setCompanyFilter] = useState("all");
  const [branchFilter, setBranchFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("active");
  const [search, setSearch] = useState("");
  const [dialog, setDialog] = useState<DialogState>(null);
  const [departmentForm, setDepartmentForm] =
    useState<DepartmentForm>(emptyDepartmentForm);
  const [positionForm, setPositionForm] =
    useState<PositionForm>(emptyPositionForm);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  /** Silinmek üzere onay bekleyen organizasyon kaydı. */
  const [pending, setPending] = useState<
    | { kind: "department"; record: HrDepartment }
    | { kind: "position"; record: HrPosition }
    | null
  >(null);

  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const loadData = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [departmentRows, positionRows, companyRows, branchRows, personnelRows] =
        await Promise.all([
          hrOrganizationService.getDepartments(),
          hrOrganizationService.getPositions(),
          companyService.getAll(),
          branchService.getAll(),
          personnelService.getAll(),
        ]);

      setDepartments(departmentRows ?? []);
      setPositions(positionRows ?? []);
      setCompanies(companyRows ?? []);
      setBranches(branchRows ?? []);
      setPersonnel(personnelRows ?? []);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  useEffect(() => {
    if (!notice) {
      return;
    }

    const timer = window.setTimeout(() => setNotice(""), 3500);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const companyById = useMemo(
    () => new Map(companies.map((item) => [item.id, item])),
    [companies]
  );
  const personnelById = useMemo(
    () => new Map(personnel.map((item) => [item.id, item])),
    [personnel]
  );
  const departmentById = useMemo(
    () => new Map(departments.map((item) => [item.id, item])),
    [departments]
  );

  const availableBranches = useMemo(
    () =>
      branches.filter(
        (item) => companyFilter === "all" || item.companyId === companyFilter
      ),
    [branches, companyFilter]
  );

  const scopedPersonnel = useMemo(
    () =>
      personnel.filter((item) => {
        if (companyFilter !== "all" && item.companyId !== companyFilter) {
          return false;
        }
        if (branchFilter !== "all" && item.branchId !== branchFilter) {
          return false;
        }
        return item.isActive !== false;
      }),
    [branchFilter, companyFilter, personnel]
  );

  const scopedPositions = useMemo(
    () =>
      positions.filter((item) => {
        const department = departmentById.get(item.departmentId);
        const companyId = item.companyId || department?.companyId;

        if (companyFilter !== "all" && companyId !== companyFilter) {
          return false;
        }
        if (statusFilter === "active" && !item.isActive) {
          return false;
        }
        if (statusFilter === "inactive" && item.isActive) {
          return false;
        }

        const term = normalized(search);
        if (!term) {
          return true;
        }

        return [
          item.code,
          positionTitle(item),
          item.departmentName,
          department?.name,
          item.description,
        ].some((value) => normalized(value).includes(term));
      }),
    [companyFilter, departmentById, positions, search, statusFilter]
  );

  const scopedDepartments = useMemo(() => {
    const term = normalized(search);

    return departments.filter((item) => {
      if (companyFilter !== "all" && item.companyId !== companyFilter) {
        return false;
      }
      if (statusFilter === "active" && !item.isActive) {
        return false;
      }
      if (statusFilter === "inactive" && item.isActive) {
        return false;
      }
      if (!term) {
        return true;
      }

      const relatedPositions = positions.filter(
        (position) => position.departmentId === item.id
      );

      return (
        [item.code, item.name, item.companyName, item.managerPersonnelName].some(
          (value) => normalized(value).includes(term)
        ) ||
        relatedPositions.some((position) =>
          normalized(positionTitle(position)).includes(term)
        )
      );
    });
  }, [companyFilter, departments, positions, search, statusFilter]);

  const rootDepartments = useMemo(() => {
    const visibleIds = new Set(scopedDepartments.map((item) => item.id));
    return scopedDepartments.filter(
      (item) =>
        !item.parentDepartmentId || !visibleIds.has(item.parentDepartmentId)
    );
  }, [scopedDepartments]);

  const matchedPersonnelForPosition = useCallback(
    (position: HrPosition) => {
      const title = normalized(positionTitle(position));
      return scopedPersonnel.filter((item) => normalized(item.jobTitle) === title);
    },
    [scopedPersonnel]
  );

  const managerName = useCallback(
    (department: HrDepartment) => {
      if (department.managerPersonnelName || department.managerName) {
        return department.managerPersonnelName || department.managerName || "—";
      }

      const manager = department.managerPersonnelId
        ? personnelById.get(department.managerPersonnelId)
        : undefined;

      return manager ? personnelName(manager) : "Atanmamış";
    },
    [personnelById]
  );

  const managersCount = useMemo(
    () =>
      new Set(
        scopedDepartments
          .map((item) => item.managerPersonnelId)
          .filter((value): value is string => Boolean(value))
      ).size,
    [scopedDepartments]
  );
  const occupiedPositions = useMemo(
    () =>
      scopedPositions.filter(
        (position) => matchedPersonnelForPosition(position).length > 0
      ).length,
    [matchedPersonnelForPosition, scopedPositions]
  );

  const companyOptions = useMemo(
    () => [
      { label: "Tüm şirketler", value: "all" },
      ...companies.map((item) => ({
        label: `${item.code} · ${item.name}`,
        value: item.id,
      })),
    ],
    [companies]
  );
  const branchOptions = useMemo(
    () => [
      { label: "Tüm şubeler", value: "all" },
      ...availableBranches.map((item) => ({
        label: `${item.code} · ${item.name}`,
        value: item.id,
      })),
    ],
    [availableBranches]
  );
  const statusOptions = [
    { label: "Aktif kayıtlar", value: "active" },
    { label: "Tüm durumlar", value: "all" },
    { label: "Pasif kayıtlar", value: "inactive" },
  ];

  function openNewDepartment(parent?: HrDepartment) {
    const initialCompany =
      parent?.companyId ||
      (companyFilter !== "all" ? companyFilter : companies[0]?.id) ||
      "";

    setDepartmentForm({
      ...emptyDepartmentForm,
      companyId: initialCompany,
      parentDepartmentId: parent?.id ?? "",
    });
    setDialog({ type: "department" });
    setError("");
  }

  function openEditDepartment(record: HrDepartment) {
    setDepartmentForm({
      companyId: record.companyId,
      code: record.code,
      name: record.name,
      parentDepartmentId: record.parentDepartmentId ?? "",
      managerPersonnelId: record.managerPersonnelId ?? "",
      isActive: record.isActive,
    });
    setDialog({ type: "department", record });
    setError("");
  }

  function openNewPosition(department?: HrDepartment) {
    setPositionForm({
      ...emptyPositionForm,
      departmentId:
        department?.id ||
        scopedDepartments[0]?.id ||
        departments[0]?.id ||
        "",
    });
    setDialog({ type: "position" });
    setError("");
  }

  function openEditPosition(record: HrPosition) {
    setPositionForm({
      departmentId: record.departmentId,
      code: record.code,
      title: positionTitle(record),
      description: record.description ?? "",
      isManagerial: record.isManagerial ?? false,
      isActive: record.isActive,
    });
    setDialog({ type: "position", record });
    setError("");
  }

  async function submitDepartment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (dialog?.type !== "department") {
      return;
    }

    setSaving(true);
    setError("");

    try {
      const commonPayload = {
        code: departmentForm.code.trim(),
        name: departmentForm.name.trim(),
        parentDepartmentId: departmentForm.parentDepartmentId || null,
        managerPersonnelId: departmentForm.managerPersonnelId || null,
      };

      if (dialog.record) {
        await hrOrganizationService.updateDepartment(dialog.record.id, {
          ...commonPayload,
          isActive: departmentForm.isActive,
        });
        setNotice("Departman güncellendi.");
      } else {
        await hrOrganizationService.createDepartment({
          companyId: departmentForm.companyId,
          ...commonPayload,
        });
        setNotice("Yeni departman oluşturuldu.");
      }

      setDialog(null);
      await loadData();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setSaving(false);
    }
  }

  async function submitPosition(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (dialog?.type !== "position") {
      return;
    }

    const department = departmentById.get(positionForm.departmentId);
    setSaving(true);
    setError("");

    try {
      const commonPayload = {
        departmentId: positionForm.departmentId,
        code: positionForm.code.trim(),
        title: positionForm.title.trim(),
        name: positionForm.title.trim(),
        description: positionForm.description.trim() || null,
        isManagerial: positionForm.isManagerial,
      };

      if (dialog.record) {
        await hrOrganizationService.updatePosition(dialog.record.id, {
          ...commonPayload,
          isActive: positionForm.isActive,
        });
        setNotice("Pozisyon güncellendi.");
      } else {
        await hrOrganizationService.createPosition({
          companyId: department?.companyId,
          ...commonPayload,
        });
        setNotice("Yeni pozisyon oluşturuldu.");
      }

      setDialog(null);
      await loadData();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setSaving(false);
    }
  }

  async function deleteDepartment(record: HrDepartment) {
    setPending(null);
    setError("");
    try {
      await hrOrganizationService.deleteDepartment(record.id);
      setDialog(null);
      setNotice("Departman silindi.");
      await loadData();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    }
  }

  async function deletePosition(record: HrPosition) {
    setPending(null);
    setError("");
    try {
      await hrOrganizationService.deletePosition(record.id);
      setDialog(null);
      setNotice("Pozisyon silindi.");
      await loadData();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    }
  }

  const departmentDialogCompanies = companies.map((item) => ({
    label: `${item.code} · ${item.name}`,
    value: item.id,
  }));
  const departmentDialogParents = departments
    .filter(
      (item) =>
        item.companyId === departmentForm.companyId &&
        item.id !==
          (dialog?.type === "department" ? dialog.record?.id : undefined)
    )
    .map((item) => ({
      label: `${item.code} · ${item.name}`,
      value: item.id,
    }));
  const departmentManagers = personnel
    .filter(
      (item) =>
        item.companyId === departmentForm.companyId && item.isActive !== false
    )
    .map((item) => ({
      label: `${personnelName(item)} · ${item.jobTitle || "Pozisyon yok"}`,
      value: item.id,
    }));
  const positionDepartments = departments
    .filter((item) => item.isActive)
    .filter(
      (item) =>
        companyFilter === "all" || item.companyId === companyFilter
    )
    .map((item) => ({
      label: `${item.code} · ${item.name}`,
      value: item.id,
    }));

  return (
    <ErpShell
      design="redwood"
      title="Organizasyon Yönetimi"
      description="Şirket yapısını, departman hiyerarşisini, yöneticileri ve pozisyonları tek merkezden yönetin."
    >
      <div className="space-y-5">
        <section className="overflow-hidden rounded-2xl bg-brand-700 px-5 py-6 text-white shadow-sm sm:px-7">
          <div className="flex flex-col gap-5 lg:flex-row lg:items-center lg:justify-between">
            <div>
              <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-400">
                Enderun AI · İnsan Kaynakları
              </span>
              <h2 className="mt-2 text-2xl font-semibold tracking-tight sm:text-3xl">
                Organizasyon merkezi
              </h2>
              <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-300">
                Gerçek departman ve pozisyon kayıtlarıyla organizasyon şemasını
                canlı izleyin; yönetici ve kadro yapısını yönetin.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Link
                href="/insan-kaynaklari/personeller"
                className="inline-flex h-10 items-center rounded-lg border border-slate-700 px-4 text-sm font-medium text-white transition hover:bg-brand-600"
              >
                Personellere git
              </Link>
              <Button variant="secondary" onClick={() => openNewDepartment()}>
                + Yeni departman
              </Button>
              <Button
                className="bg-emerald-500 text-slate-950 hover:bg-emerald-400"
                onClick={() => openNewPosition()}
                disabled={departments.length === 0}
              >
                + Yeni pozisyon
              </Button>
            </div>
          </div>
        </section>

        {error && (
          <div className="flex flex-col gap-3 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 sm:flex-row sm:items-center sm:justify-between">
            <span>{error}</span>
            <Button size="sm" variant="secondary" onClick={() => void loadData()}>
              Yeniden dene
            </Button>
          </div>
        )}

        {notice && (
          <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm font-medium text-emerald-700">
            {notice}
          </div>
        )}

        <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            title="Aktif Departman"
            value={scopedDepartments.filter((item) => item.isActive).length}
            description={`${rootDepartments.length} üst organizasyon birimi`}
            icon="▤"
          />
          <StatCard
            title="Aktif Pozisyon"
            value={scopedPositions.filter((item) => item.isActive).length}
            description={`${occupiedPositions} pozisyonda personel var`}
            icon="♙"
          />
          <StatCard
            title="Atanmış Yönetici"
            value={managersCount}
            description={`${Math.max(
              scopedDepartments.length - managersCount,
              0
            )} birimde yönetici bekleniyor`}
            icon="◎"
          />
          <StatCard
            title="Kapsamdaki Personel"
            value={scopedPersonnel.length}
            description={
              branchFilter === "all"
                ? "Aktif personel toplamı"
                : "Seçili şubedeki aktif personel"
            }
            icon="♟"
          />
        </section>

        <Card>
          <CardContent className="p-4">
            <div className="grid gap-3 lg:grid-cols-[minmax(240px,1fr)_220px_220px_190px_auto]">
              <Input
                aria-label="Organizasyonda ara"
                placeholder="Departman, kod, yönetici veya pozisyon ara..."
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
              <Select
                aria-label="Şirket filtresi"
                value={companyFilter}
                options={companyOptions}
                onChange={(event) => {
                  setCompanyFilter(event.target.value);
                  setBranchFilter("all");
                }}
              />
              <Select
                aria-label="Şube filtresi"
                value={branchFilter}
                options={branchOptions}
                onChange={(event) => setBranchFilter(event.target.value)}
                helperText="Personel kapsamını belirler"
              />
              <Select
                aria-label="Durum filtresi"
                value={statusFilter}
                options={statusOptions}
                onChange={(event) =>
                  setStatusFilter(event.target.value as StatusFilter)
                }
              />
              <Button variant="ghost" onClick={() => void loadData()}>
                ↻ Yenile
              </Button>
            </div>
          </CardContent>
        </Card>

        <div className="flex gap-1 overflow-x-auto rounded-xl border border-slate-200 bg-white p-1 shadow-sm">
          {[
            { key: "chart", label: "Organizasyon Şeması" },
            { key: "departments", label: `Departmanlar (${scopedDepartments.length})` },
            { key: "positions", label: `Pozisyonlar (${scopedPositions.length})` },
          ].map((tab) => (
            <button
              key={tab.key}
              type="button"
              onClick={() => setActiveTab(tab.key as OrganizationTab)}
              className={[
                "whitespace-nowrap rounded-lg px-4 py-2.5 text-sm font-medium transition",
                activeTab === tab.key
                  ? "bg-brand-700 text-white"
                  : "text-slate-600 hover:bg-slate-100",
              ].join(" ")}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {loading ? (
          <Card>
            <CardContent className="flex min-h-64 items-center justify-center">
              <div className="text-center">
                <div className="mx-auto h-8 w-8 animate-spin rounded-full border-2 border-slate-200 border-t-slate-900" />
                <p className="mt-3 text-sm text-slate-500">
                  Organizasyon verileri yükleniyor...
                </p>
              </div>
            </CardContent>
          </Card>
        ) : activeTab === "chart" ? (
          <Card>
            <SectionTitle
              title="Organizasyon Şeması"
              description="Üst birimlerden alt departmanlara uzanan canlı hiyerarşi"
              action={
                companyFilter === "all" ? (
                  <Badge variant="info">Tüm şirketler</Badge>
                ) : (
                  <Badge variant="info">
                    {companyById.get(companyFilter)?.name || "Seçili şirket"}
                  </Badge>
                )
              }
            />
            <CardContent className="p-5">
              {rootDepartments.length === 0 ? (
                <EmptyState
                  title="Organizasyon birimi bulunamadı"
                  description="Filtreleri temizleyin veya yeni bir departman oluşturun."
                  action={
                    <Button onClick={() => openNewDepartment()}>
                      Yeni departman
                    </Button>
                  }
                />
              ) : (
                <div className="space-y-4">
                  {rootDepartments.map((department) => (
                    <OrganizationNode
                      key={department.id}
                      department={department}
                      departments={scopedDepartments}
                      positions={positions}
                      personnel={scopedPersonnel}
                      depth={0}
                      managerName={managerName}
                      onEdit={openEditDepartment}
                      onAddChild={openNewDepartment}
                      onAddPosition={openNewPosition}
                    />
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        ) : activeTab === "departments" ? (
          <Card>
            <SectionTitle
              title="Departman Kayıtları"
              description="Birim kodu, üst departman, yönetici ve aktiflik bilgileri"
              action={
                <Button size="sm" onClick={() => openNewDepartment()}>
                  + Departman
                </Button>
              }
            />
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Kod / Departman</TableHead>
                    <TableHead>Şirket</TableHead>
                    <TableHead>Üst Birim</TableHead>
                    <TableHead>Yönetici</TableHead>
                    <TableHead>Pozisyon</TableHead>
                    <TableHead>Durum</TableHead>
                    <TableHead className="text-right">İşlemler</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {scopedDepartments.map((department) => {
                    const positionCount = positions.filter(
                      (item) => item.departmentId === department.id
                    ).length;

                    return (
                      <TableRow key={department.id}>
                        <TableCell>
                          <div>
                            <span className="font-mono text-xs text-slate-500">
                              {department.code}
                            </span>
                            <strong className="mt-0.5 block text-slate-900">
                              {department.name}
                            </strong>
                          </div>
                        </TableCell>
                        <TableCell>
                          {department.companyName ||
                            companyById.get(department.companyId)?.name ||
                            "—"}
                        </TableCell>
                        <TableCell>
                          {department.parentDepartmentName ||
                            (department.parentDepartmentId
                              ? departmentById.get(department.parentDepartmentId)
                                  ?.name
                              : "Üst birim")}
                        </TableCell>
                        <TableCell>{managerName(department)}</TableCell>
                        <TableCell>{positionCount}</TableCell>
                        <TableCell>
                          <Badge
                            variant={department.isActive ? "success" : "default"}
                          >
                            {department.isActive ? "Aktif" : "Pasif"}
                          </Badge>
                        </TableCell>
                        <TableCell>
                          <div className="flex justify-end gap-1">
                            <Button
                              size="sm"
                              variant="ghost"
                              onClick={() => openNewPosition(department)}
                            >
                              Pozisyon
                            </Button>
                            <Button
                              size="sm"
                              variant="secondary"
                              onClick={() => openEditDepartment(department)}
                            >
                              Düzenle
                            </Button>
                            <Button
                              size="sm"
                              variant="ghost"
                              className="text-red-600 hover:bg-red-50"
                              onClick={() => setPending({ kind: "department", record: department })}
                            >
                              Sil
                            </Button>
                          </div>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </div>
            {scopedDepartments.length === 0 && (
              <CardContent>
                <EmptyState
                  title="Departman bulunamadı"
                  description="Arama ve filtre ölçütlerini değiştirin."
                />
              </CardContent>
            )}
          </Card>
        ) : (
          <Card>
            <SectionTitle
              title="Pozisyon Kataloğu"
              description="Departmanlara bağlı görev ve kadro tanımları"
              action={
                <Button
                  size="sm"
                  onClick={() => openNewPosition()}
                  disabled={departments.length === 0}
                >
                  + Pozisyon
                </Button>
              }
            />
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Kod / Pozisyon</TableHead>
                    <TableHead>Departman</TableHead>
                    <TableHead>Şirket</TableHead>
                    <TableHead>Personel</TableHead>
                    <TableHead>Nitelik</TableHead>
                    <TableHead>Durum</TableHead>
                    <TableHead className="text-right">İşlemler</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {scopedPositions.map((position) => {
                    const department = departmentById.get(position.departmentId);
                    const assigned = matchedPersonnelForPosition(position);

                    return (
                      <TableRow key={position.id}>
                        <TableCell>
                          <div>
                            <span className="font-mono text-xs text-slate-500">
                              {position.code}
                            </span>
                            <strong className="mt-0.5 block text-slate-900">
                              {positionTitle(position)}
                            </strong>
                            {position.description && (
                              <span className="mt-1 line-clamp-1 block max-w-xs text-xs text-slate-500">
                                {position.description}
                              </span>
                            )}
                          </div>
                        </TableCell>
                        <TableCell>
                          {position.departmentName || department?.name || "—"}
                        </TableCell>
                        <TableCell>
                          {position.companyName ||
                            companyById.get(
                              position.companyId || department?.companyId || ""
                            )?.name ||
                            "—"}
                        </TableCell>
                        <TableCell>
                          <div className="flex items-center gap-2">
                            <strong className="text-slate-900">
                              {assigned.length}
                            </strong>
                            <span className="text-xs text-slate-500">kişi</span>
                          </div>
                        </TableCell>
                        <TableCell>
                          {position.isManagerial ? (
                            <Badge variant="info">Yönetici</Badge>
                          ) : (
                            <Badge>Standart</Badge>
                          )}
                        </TableCell>
                        <TableCell>
                          <Badge
                            variant={position.isActive ? "success" : "default"}
                          >
                            {position.isActive ? "Aktif" : "Pasif"}
                          </Badge>
                        </TableCell>
                        <TableCell>
                          <div className="flex justify-end gap-1">
                            <Button
                              size="sm"
                              variant="secondary"
                              onClick={() => openEditPosition(position)}
                            >
                              Düzenle
                            </Button>
                            <Button
                              size="sm"
                              variant="ghost"
                              className="text-red-600 hover:bg-red-50"
                              onClick={() => setPending({ kind: "position", record: position })}
                            >
                              Sil
                            </Button>
                          </div>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </div>
            {scopedPositions.length === 0 && (
              <CardContent>
                <EmptyState
                  title="Pozisyon bulunamadı"
                  description="Departman seçimini veya filtreleri değiştirin."
                />
              </CardContent>
            )}
          </Card>
        )}
      </div>

      {dialog && (
        <div
          className="fixed inset-0 z-50 flex items-end justify-center bg-slate-950/50 p-0 backdrop-blur-sm sm:items-center sm:p-4"
          role="dialog"
          aria-modal="true"
          onMouseDown={(event) => {
            if (event.currentTarget === event.target && !saving) {
              setDialog(null);
            }
          }}
        >
          <div className="max-h-[92vh] w-full overflow-y-auto rounded-t-2xl bg-white shadow-2xl sm:max-w-2xl sm:rounded-2xl">
            <div className="sticky top-0 z-10 flex items-start justify-between border-b border-slate-200 bg-white px-5 py-4">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">
                  {dialog.type === "department"
                    ? dialog.record
                      ? "Departmanı Düzenle"
                      : "Yeni Departman"
                    : dialog.record
                      ? "Pozisyonu Düzenle"
                      : "Yeni Pozisyon"}
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  {dialog.type === "department"
                    ? "Organizasyon birimi ve yönetici bağlantısını tanımlayın."
                    : "Departmana bağlı görev ve kadro tanımını oluşturun."}
                </p>
              </div>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => setDialog(null)}
                disabled={saving}
                aria-label="Pencereyi kapat"
              >
                ✕
              </Button>
            </div>

            {dialog.type === "department" ? (
              <form onSubmit={submitDepartment}>
                <div className="space-y-4 p-5">
                  {!dialog.record && (
                    <Select
                      label="Şirket"
                      required
                      value={departmentForm.companyId}
                      options={departmentDialogCompanies}
                      placeholder="Şirket seçin"
                      onChange={(event) =>
                        setDepartmentForm((current) => ({
                          ...current,
                          companyId: event.target.value,
                          parentDepartmentId: "",
                          managerPersonnelId: "",
                        }))
                      }
                    />
                  )}
                  {dialog.record && (
                    <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                      <span className="block text-xs font-medium uppercase tracking-wide text-slate-500">
                        Şirket
                      </span>
                      <strong className="mt-1 block text-sm text-slate-900">
                        {companyById.get(departmentForm.companyId)?.name || "—"}
                      </strong>
                    </div>
                  )}
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Input
                      label="Departman kodu"
                      required
                      value={departmentForm.code}
                      placeholder="Örn. IK-01"
                      onChange={(event) =>
                        setDepartmentForm((current) => ({
                          ...current,
                          code: event.target.value.toLocaleUpperCase("tr-TR"),
                        }))
                      }
                    />
                    <Input
                      label="Departman adı"
                      required
                      value={departmentForm.name}
                      placeholder="Örn. İnsan Kaynakları"
                      onChange={(event) =>
                        setDepartmentForm((current) => ({
                          ...current,
                          name: event.target.value,
                        }))
                      }
                    />
                  </div>
                  <Select
                    label="Üst departman"
                    value={departmentForm.parentDepartmentId}
                    options={departmentDialogParents}
                    placeholder="Üst birim yok"
                    onChange={(event) =>
                      setDepartmentForm((current) => ({
                        ...current,
                        parentDepartmentId: event.target.value,
                      }))
                    }
                  />
                  <Select
                    label="Departman yöneticisi"
                    value={departmentForm.managerPersonnelId}
                    options={departmentManagers}
                    placeholder="Yönetici atanmamış"
                    onChange={(event) =>
                      setDepartmentForm((current) => ({
                        ...current,
                        managerPersonnelId: event.target.value,
                      }))
                    }
                  />
                  {dialog.record && (
                    <label className="flex items-center gap-3 rounded-xl border border-slate-200 p-4">
                      <input
                        type="checkbox"
                        checked={departmentForm.isActive}
                        onChange={(event) =>
                          setDepartmentForm((current) => ({
                            ...current,
                            isActive: event.target.checked,
                          }))
                        }
                        className="h-4 w-4 rounded border-slate-300"
                      />
                      <span>
                        <strong className="block text-sm text-slate-900">
                          Aktif departman
                        </strong>
                        <span className="text-xs text-slate-500">
                          Pasif birimler yeni organizasyon seçimlerinde gösterilmez.
                        </span>
                      </span>
                    </label>
                  )}
                </div>
                <div className="flex justify-end gap-2 border-t border-slate-200 px-5 py-4">
                  {dialog.record && (
                    <Button
                      type="button"
                      variant="danger"
                      className="mr-auto"
                      disabled={saving}
                      onClick={() =>
                        setPending({
                          kind: "department",
                          record: dialog.record!,
                        })
                      }
                    >
                      Sil
                    </Button>
                  )}
                  <Button
                    type="button"
                    variant="ghost"
                    disabled={saving}
                    onClick={() => setDialog(null)}
                  >
                    Vazgeç
                  </Button>
                  <Button type="submit" loading={saving}>
                    {dialog.record ? "Değişiklikleri kaydet" : "Departmanı oluştur"}
                  </Button>
                </div>
              </form>
            ) : (
              <form onSubmit={submitPosition}>
                <div className="space-y-4 p-5">
                  <Select
                    label="Departman"
                    required
                    value={positionForm.departmentId}
                    options={positionDepartments}
                    placeholder="Departman seçin"
                    onChange={(event) =>
                      setPositionForm((current) => ({
                        ...current,
                        departmentId: event.target.value,
                      }))
                    }
                  />
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Input
                      label="Pozisyon kodu"
                      required
                      value={positionForm.code}
                      placeholder="Örn. IK-UZMAN"
                      onChange={(event) =>
                        setPositionForm((current) => ({
                          ...current,
                          code: event.target.value.toLocaleUpperCase("tr-TR"),
                        }))
                      }
                    />
                    <Input
                      label="Pozisyon adı"
                      required
                      value={positionForm.title}
                      placeholder="Örn. İK Uzmanı"
                      onChange={(event) =>
                        setPositionForm((current) => ({
                          ...current,
                          title: event.target.value,
                        }))
                      }
                    />
                  </div>
                  <div>
                    <label
                      htmlFor="position-description"
                      className="mb-1.5 block text-sm font-medium text-slate-700"
                    >
                      Görev açıklaması
                    </label>
                    <textarea
                      id="position-description"
                      rows={4}
                      value={positionForm.description}
                      placeholder="Pozisyonun temel görev ve sorumluluklarını yazın..."
                      onChange={(event) =>
                        setPositionForm((current) => ({
                          ...current,
                          description: event.target.value,
                        }))
                      }
                      className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-slate-500 focus:ring-2 focus:ring-slate-100"
                    />
                  </div>
                  <div className="grid gap-3 sm:grid-cols-2">
                    <label className="flex items-center gap-3 rounded-xl border border-slate-200 p-4">
                      <input
                        type="checkbox"
                        checked={positionForm.isManagerial}
                        onChange={(event) =>
                          setPositionForm((current) => ({
                            ...current,
                            isManagerial: event.target.checked,
                          }))
                        }
                        className="h-4 w-4 rounded border-slate-300"
                      />
                      <span>
                        <strong className="block text-sm text-slate-900">
                          Yönetici pozisyonu
                        </strong>
                        <span className="text-xs text-slate-500">
                          Organizasyon sorumluluğu taşır.
                        </span>
                      </span>
                    </label>
                    {dialog.record && (
                      <label className="flex items-center gap-3 rounded-xl border border-slate-200 p-4">
                        <input
                          type="checkbox"
                          checked={positionForm.isActive}
                          onChange={(event) =>
                            setPositionForm((current) => ({
                              ...current,
                              isActive: event.target.checked,
                            }))
                          }
                          className="h-4 w-4 rounded border-slate-300"
                        />
                        <span>
                          <strong className="block text-sm text-slate-900">
                            Aktif pozisyon
                          </strong>
                          <span className="text-xs text-slate-500">
                            Kadro ve personel seçiminde kullanılabilir.
                          </span>
                        </span>
                      </label>
                    )}
                  </div>
                </div>
                <div className="flex justify-end gap-2 border-t border-slate-200 px-5 py-4">
                  {dialog.record && (
                    <Button
                      type="button"
                      variant="danger"
                      className="mr-auto"
                      disabled={saving}
                      onClick={() =>
                        setPending({
                          kind: "position",
                          record: dialog.record!,
                        })
                      }
                    >
                      Sil
                    </Button>
                  )}
                  <Button
                    type="button"
                    variant="ghost"
                    disabled={saving}
                    onClick={() => setDialog(null)}
                  >
                    Vazgeç
                  </Button>
                  <Button type="submit" loading={saving}>
                    {dialog.record ? "Değişiklikleri kaydet" : "Pozisyonu oluştur"}
                  </Button>
                </div>
              </form>
            )}
          </div>
        </div>
      )}
      {pending && (
        <ConfirmDialog
          open
          title={
            pending.kind === "department"
              ? "Departmanı Sil"
              : "Pozisyonu Sil"
          }
          description={
            pending.kind === "department"
              ? `${pending.record.name} departmanı silinecek. Alt birimi veya bağlı pozisyonu varsa sunucu işlemi reddeder.`
              : `${positionTitle(pending.record)} pozisyonu silinecek. Bu pozisyona bağlı personel varsa sunucu işlemi reddeder.`
          }
          confirmLabel="Sil"
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={() =>
            pending.kind === "department"
              ? void deleteDepartment(pending.record)
              : void deletePosition(pending.record)
          }
        />
      )}
    </ErpShell>
  );
}
