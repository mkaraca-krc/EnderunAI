"use client";

import Link from "next/link";
import { FormEvent, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
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
import {
  personnelService,
  type PersonnelDetail,
  type PersonnelListItem,
} from "@/services/personnel.service";
import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";
import {
  branchService,
  type BranchListItem,
} from "@/services/branch.service";
import {
  projectService,
  type ProjectListItem,
} from "@/services/project.service";

type ViewMode = "table" | "cards";
type ActivityFilter = "all" | "active" | "passive";

type PersonnelForm = {
  companyId: string;
  branchId: string;
  employeeNumber: string;
  firstName: string;
  lastName: string;
  identityNumber: string;
  birthDate: string;
  phone: string;
  email: string;
  address: string;
  jobTitle: string;
  profession: string;
  sgkRegistrationNumber: string;
  employmentStartDate: string;
  employmentEndDate: string;
  monthlySalary: string;
  status: string;
  isActive: boolean;
};

const emptyForm: PersonnelForm = {
  companyId: "",
  branchId: "",
  employeeNumber: "",
  firstName: "",
  lastName: "",
  identityNumber: "",
  birthDate: "",
  phone: "",
  email: "",
  address: "",
  jobTitle: "",
  profession: "",
  sgkRegistrationNumber: "",
  employmentStartDate: "",
  employmentEndDate: "",
  monthlySalary: "",
  status: "1",
  isActive: true,
};

const statusLabels: Record<number, string> = {
  0: "Aday",
  1: "Aktif",
  2: "İzinli",
  3: "Askıda",
  4: "İşten Ayrıldı",
};

const statusOptions = Object.entries(statusLabels).map(([value, label]) => ({
  value,
  label,
}));

function badgeVariant(status: number): "default" | "success" | "warning" | "danger" {
  if (status === 1) return "success";
  if (status === 2) return "warning";
  if (status === 3 || status === 4) return "danger";
  return "default";
}

function dateValue(value?: string | null) {
  return value ? value.slice(0, 10) : "";
}

function displayDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

function initials(item: Pick<PersonnelListItem, "firstName" | "lastName">) {
  return `${item.firstName?.[0] ?? ""}${item.lastName?.[0] ?? ""}`.toLocaleUpperCase("tr-TR");
}

function uniqueOptions(values: Array<string | null | undefined>) {
  return [...new Set(values.map((value) => value?.trim()).filter(Boolean) as string[])]
    .sort((a, b) => a.localeCompare(b, "tr"))
    .map((value) => ({ value, label: value }));
}

function activeAssignment(item: PersonnelListItem) {
  return (
    item.activeAssignments?.find((assignment) => assignment.isPrimaryAssignment) ??
    item.activeAssignments?.[0]
  );
}

function PersonnelShortcuts({ personnelId }: { personnelId: string }) {
  const links = [
    { label: "360°", href: `/insan-kaynaklari/personel-360?personnelId=${personnelId}` },
    { label: "Bordro", href: `/insan-kaynaklari/bordro?personnelId=${personnelId}` },
    { label: "Puantaj", href: `/insan-kaynaklari/puantaj?personnelId=${personnelId}` },
    { label: "İzin", href: `/insan-kaynaklari/izinler?personnelId=${personnelId}` },
    { label: "Zimmet", href: `/insan-kaynaklari/zimmetler?personnelId=${personnelId}` },
  ];

  return (
    <div className="flex flex-wrap gap-1.5">
      {links.map((link) => (
        <Link
          key={link.label}
          href={link.href}
          className="inline-flex h-8 items-center rounded-lg border border-slate-200 bg-white px-2.5 text-xs font-medium text-slate-700 transition hover:border-slate-400 hover:bg-slate-50"
        >
          {link.label}
        </Link>
      ))}
    </div>
  );
}

export default function HrPersonnelPage() {
  const [items, setItems] = useState<PersonnelListItem[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [viewMode, setViewMode] = useState<ViewMode>("table");
  const [search, setSearch] = useState("");
  const [activity, setActivity] = useState<ActivityFilter>("all");
  const [companyId, setCompanyId] = useState("");
  const [branchId, setBranchId] = useState("");
  const [profession, setProfession] = useState("");
  const [jobTitle, setJobTitle] = useState("");
  const [projectId, setProjectId] = useState("");
  const [formOpen, setFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<PersonnelForm>(emptyForm);

  async function loadScreen() {
    setLoading(true);
    setError("");

    try {
      const [personnelResult, companyResult, branchResult, projectResult] =
        await Promise.all([
          personnelService.getAll(),
          companyService.getAll(),
          branchService.getAll(),
          projectService.getAll(),
        ]);

      setItems(personnelResult);
      setCompanies(companyResult.filter((company) => company.isActive !== false));
      setBranches(branchResult.filter((branch) => branch.isActive !== false));
      setProjects(projectResult);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Personel ekranı yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }

  async function reloadPersonnel() {
    const result = await personnelService.getAll();
    setItems(result);
  }

  useEffect(() => {
    void loadScreen();
  }, []);

  useEffect(() => {
    if (branchId && !branches.some((branch) => branch.id === branchId && (!companyId || branch.companyId === companyId))) {
      setBranchId("");
    }
  }, [branchId, branches, companyId]);

  const visibleBranches = useMemo(
    () => branches.filter((branch) => !companyId || branch.companyId === companyId),
    [branches, companyId]
  );

  const visibleProjects = useMemo(
    () => projects.filter((project) => !companyId || project.companyId === companyId),
    [projects, companyId]
  );

  const professionOptions = useMemo(
    () => uniqueOptions(items.map((item) => item.profession)),
    [items]
  );

  const jobTitleOptions = useMemo(
    () => uniqueOptions(items.map((item) => item.jobTitle)),
    [items]
  );

  const filteredItems = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");

    return items.filter((item) => {
      const searchable = [
        item.fullName,
        item.employeeNumber,
        item.identityNumber,
        item.phone,
        item.email,
      ]
        .filter(Boolean)
        .join(" ")
        .toLocaleLowerCase("tr-TR");

      if (term && !searchable.includes(term)) return false;
      if (activity === "active" && !item.isActive) return false;
      if (activity === "passive" && item.isActive) return false;
      if (companyId && item.companyId !== companyId) return false;
      if (branchId && item.branchId !== branchId) return false;
      if (profession && item.profession !== profession) return false;
      if (jobTitle && item.jobTitle !== jobTitle) return false;
      if (
        projectId &&
        !item.activeAssignments?.some((assignment) => assignment.projectId === projectId)
      ) {
        return false;
      }

      return true;
    });
  }, [
    activity,
    branchId,
    companyId,
    items,
    jobTitle,
    profession,
    projectId,
    search,
  ]);

  const summary = useMemo(
    () => ({
      total: items.length,
      active: items.filter((item) => item.isActive).length,
      assigned: items.filter((item) => item.activeAssignments?.length > 0).length,
      onLeave: items.filter((item) => item.status === 2 && item.isActive).length,
    }),
    [items]
  );

  const formBranches = useMemo(
    () => branches.filter((branch) => !form.companyId || branch.companyId === form.companyId),
    [branches, form.companyId]
  );

  function updateForm<K extends keyof PersonnelForm>(key: K, value: PersonnelForm[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function resetFilters() {
    setSearch("");
    setActivity("all");
    setCompanyId("");
    setBranchId("");
    setProfession("");
    setJobTitle("");
    setProjectId("");
  }

  function openCreate() {
    const defaultCompanyId = companies.length === 1 ? companies[0].id : "";
    setEditingId(null);
    setForm({ ...emptyForm, companyId: defaultCompanyId });
    setError("");
    setSuccess("");
    setFormOpen(true);
  }

  async function openEdit(id: string) {
    setEditingId(id);
    setFormOpen(true);
    setDetailLoading(true);
    setError("");
    setSuccess("");

    try {
      const detail: PersonnelDetail = await personnelService.getById(id);
      setForm({
        companyId: detail.companyId,
        branchId: detail.branchId ?? "",
        employeeNumber: detail.employeeNumber,
        firstName: detail.firstName,
        lastName: detail.lastName,
        identityNumber: detail.identityNumber ?? "",
        birthDate: dateValue(detail.birthDate),
        phone: detail.phone ?? "",
        email: detail.email ?? "",
        address: detail.address ?? "",
        jobTitle: detail.jobTitle ?? "",
        profession: detail.profession ?? "",
        sgkRegistrationNumber: detail.sgkRegistrationNumber ?? "",
        employmentStartDate: dateValue(detail.employmentStartDate),
        employmentEndDate: dateValue(detail.employmentEndDate),
        monthlySalary: detail.monthlySalary == null ? "" : String(detail.monthlySalary),
        status: String(detail.status),
        isActive: detail.isActive,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Personel detayı yüklenemedi.");
      setFormOpen(false);
    } finally {
      setDetailLoading(false);
    }
  }

  async function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      if (editingId) {
        await personnelService.update(editingId, {
          branchId: form.branchId || null,
          firstName: form.firstName,
          lastName: form.lastName,
          identityNumber: form.identityNumber || null,
          birthDate: form.birthDate || null,
          phone: form.phone || null,
          email: form.email || null,
          address: form.address || null,
          jobTitle: form.jobTitle || null,
          profession: form.profession || null,
          sgkRegistrationNumber: form.sgkRegistrationNumber || null,
          employmentStartDate: form.employmentStartDate || null,
          employmentEndDate: form.employmentEndDate || null,
          monthlySalary: form.monthlySalary ? Number(form.monthlySalary) : null,
          status: Number(form.status),
          isActive: form.isActive,
        });
        setSuccess("Personel kaydı güncellendi.");
      } else {
        await personnelService.create({
          companyId: form.companyId,
          branchId: form.branchId || null,
          employeeNumber: form.employeeNumber,
          firstName: form.firstName,
          lastName: form.lastName,
          identityNumber: form.identityNumber || null,
          birthDate: form.birthDate || null,
          phone: form.phone || null,
          email: form.email || null,
          address: form.address || null,
          jobTitle: form.jobTitle || null,
          profession: form.profession || null,
          sgkRegistrationNumber: form.sgkRegistrationNumber || null,
          employmentStartDate: form.employmentStartDate || null,
          monthlySalary: form.monthlySalary ? Number(form.monthlySalary) : null,
        });
        setSuccess("Yeni personel kaydı oluşturuldu.");
      }

      await reloadPersonnel();
      setFormOpen(false);
      setEditingId(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Personel kaydı tamamlanamadı.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      title="Personeller"
      description="Çalışan kartları, organizasyon bilgileri ve İK işlemleri"
    >
      {error && (
        <div className="mb-5 flex items-start justify-between gap-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <span>{error}</span>
          <button type="button" onClick={() => setError("")} aria-label="Uyarıyı kapat">
            ×
          </button>
        </div>
      )}

      {success && (
        <div className="mb-5 flex items-start justify-between gap-4 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          <span>{success}</span>
          <button type="button" onClick={() => setSuccess("")} aria-label="Bildirimi kapat">
            ×
          </button>
        </div>
      )}

      <div className="mb-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard title="Toplam Personel" value={loading ? "…" : summary.total} description="Tüm personel kayıtları" icon="♙" />
        <StatCard title="Aktif Personel" value={loading ? "…" : summary.active} description="Çalışmaya devam eden" icon="✓" />
        <StatCard title="Şantiyede Görevli" value={loading ? "…" : summary.assigned} description="Aktif proje ataması" icon="▣" />
        <StatCard title="İzinli Personel" value={loading ? "…" : summary.onLeave} description="Güncel izin durumu" icon="◷" />
      </div>

      <Card className="mb-6">
        <CardHeader className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">Personel havuzu</h2>
            <p className="mt-1 text-sm text-slate-500">
              Gerçek personel servisinden gelen kayıtları filtreleyin ve yönetin.
            </p>
          </div>

          <div className="flex flex-wrap gap-2">
            <div className="inline-flex rounded-lg border border-slate-200 bg-slate-50 p-1">
              <button
                type="button"
                onClick={() => setViewMode("table")}
                className={`h-8 rounded-md px-3 text-sm font-medium transition ${
                  viewMode === "table" ? "bg-white text-slate-900 shadow-sm" : "text-slate-500"
                }`}
              >
                Tablo
              </button>
              <button
                type="button"
                onClick={() => setViewMode("cards")}
                className={`h-8 rounded-md px-3 text-sm font-medium transition ${
                  viewMode === "cards" ? "bg-white text-slate-900 shadow-sm" : "text-slate-500"
                }`}
              >
                Kart
              </button>
            </div>
            <Button onClick={openCreate}>+ Yeni Personel</Button>
          </div>
        </CardHeader>

        <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Ad, sicil, TC, telefon veya e-posta ara"
            className="xl:col-span-2"
          />
          <Select
            value={activity}
            onChange={(event) => setActivity(event.target.value as ActivityFilter)}
            options={[
              { value: "all", label: "Tüm durumlar" },
              { value: "active", label: "Aktif kayıtlar" },
              { value: "passive", label: "Pasif kayıtlar" },
            ]}
          />
          <Select
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
            placeholder="Tüm şirketler"
            options={companies.map((company) => ({
              value: company.id,
              label: `${company.code} · ${company.name}`,
            }))}
          />
          <Select
            value={branchId}
            onChange={(event) => setBranchId(event.target.value)}
            placeholder="Tüm şubeler"
            options={visibleBranches.map((branch) => ({
              value: branch.id,
              label: `${branch.code} · ${branch.name}`,
            }))}
          />
          <Select
            value={profession}
            onChange={(event) => setProfession(event.target.value)}
            placeholder="Tüm departman / meslekler"
            options={professionOptions}
          />
          <Select
            value={jobTitle}
            onChange={(event) => setJobTitle(event.target.value)}
            placeholder="Tüm pozisyonlar"
            options={jobTitleOptions}
          />
          <Select
            value={projectId}
            onChange={(event) => setProjectId(event.target.value)}
            placeholder="Tüm şantiyeler"
            options={visibleProjects.map((project) => ({
              value: project.id,
              label: `${project.code} · ${project.name}`,
            }))}
          />

          <div className="flex items-center justify-between gap-3 md:col-span-2 xl:col-span-4">
            <span className="text-sm text-slate-500">
              {filteredItems.length} / {items.length} personel gösteriliyor
            </span>
            <Button variant="ghost" size="sm" onClick={resetFilters}>
              Filtreleri Temizle
            </Button>
          </div>
        </CardContent>
      </Card>

      {loading ? (
        <Card>
          <CardContent className="py-16 text-center text-sm text-slate-500">
            Personel kayıtları yükleniyor...
          </CardContent>
        </Card>
      ) : filteredItems.length === 0 ? (
        <Card>
          <CardContent>
            <EmptyState
              title="Filtrelere uygun personel bulunamadı"
              description="Filtreleri temizleyin veya yeni bir personel kaydı oluşturun."
              icon="♙"
              action={<Button onClick={openCreate}>Yeni Personel</Button>}
            />
          </CardContent>
        </Card>
      ) : viewMode === "cards" ? (
        <div className="grid gap-4 md:grid-cols-2 2xl:grid-cols-3">
          {filteredItems.map((item) => {
            const assignment = activeAssignment(item);
            return (
              <Card key={item.id} className="overflow-hidden">
                <CardContent className="p-0">
                  <div className="flex items-start gap-4 border-b border-slate-100 p-5">
                    <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-slate-900 text-sm font-semibold text-white">
                      {initials(item)}
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <h3 className="truncate font-semibold text-slate-900">{item.fullName}</h3>
                          <p className="mt-1 text-xs text-slate-500">{item.employeeNumber}</p>
                        </div>
                        <Badge variant={badgeVariant(item.status)}>
                          {statusLabels[item.status] ?? "Bilinmiyor"}
                        </Badge>
                      </div>
                    </div>
                  </div>

                  <div className="grid gap-3 p-5 text-sm">
                    <div className="grid grid-cols-[92px_1fr] gap-3">
                      <span className="text-slate-500">Pozisyon</span>
                      <strong className="font-medium text-slate-800">{item.jobTitle || "—"}</strong>
                    </div>
                    <div className="grid grid-cols-[92px_1fr] gap-3">
                      <span className="text-slate-500">Departman</span>
                      <strong className="font-medium text-slate-800">{item.profession || "—"}</strong>
                    </div>
                    <div className="grid grid-cols-[92px_1fr] gap-3">
                      <span className="text-slate-500">Şirket / Şube</span>
                      <strong className="font-medium text-slate-800">
                        {item.companyName}
                        {item.branchName ? ` · ${item.branchName}` : ""}
                      </strong>
                    </div>
                    <div className="grid grid-cols-[92px_1fr] gap-3">
                      <span className="text-slate-500">Şantiye</span>
                      <strong className="font-medium text-slate-800">
                        {assignment ? `${assignment.projectCode} · ${assignment.projectName}` : "Atama bekliyor"}
                      </strong>
                    </div>
                  </div>

                  <div className="border-t border-slate-100 bg-slate-50/70 p-4">
                    <PersonnelShortcuts personnelId={item.id} />
                    <Button
                      variant="secondary"
                      size="sm"
                      className="mt-3 w-full"
                      onClick={() => void openEdit(item.id)}
                    >
                      Personeli Düzenle
                    </Button>
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      ) : (
        <Card className="overflow-hidden">
          <CardHeader className="flex items-center justify-between gap-4">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">Personel listesi</h2>
              <p className="mt-1 text-sm text-slate-500">Organizasyon ve aktif şantiye dağılımı</p>
            </div>
            <Badge variant="info">{filteredItems.length} kayıt</Badge>
          </CardHeader>
          <CardContent className="overflow-x-auto p-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Personel</TableHead>
                  <TableHead>Şirket / Şube</TableHead>
                  <TableHead>Departman / Pozisyon</TableHead>
                  <TableHead>Şantiye</TableHead>
                  <TableHead>İşe Giriş</TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead>Hızlı İşlemler</TableHead>
                  <TableHead className="text-right">İşlem</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filteredItems.map((item) => {
                  const assignment = activeAssignment(item);
                  return (
                    <TableRow key={item.id}>
                      <TableCell>
                        <div className="flex items-center gap-3">
                          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-slate-100 text-xs font-semibold text-slate-700">
                            {initials(item)}
                          </div>
                          <div>
                            <strong className="block whitespace-nowrap text-slate-900">{item.fullName}</strong>
                            <span className="mt-1 block text-xs text-slate-500">{item.employeeNumber}</span>
                          </div>
                        </div>
                      </TableCell>
                      <TableCell>
                        <span className="block whitespace-nowrap text-slate-800">{item.companyName}</span>
                        <span className="mt-1 block whitespace-nowrap text-xs text-slate-500">{item.branchName || "Şube belirtilmedi"}</span>
                      </TableCell>
                      <TableCell>
                        <span className="block whitespace-nowrap text-slate-800">{item.profession || "—"}</span>
                        <span className="mt-1 block whitespace-nowrap text-xs text-slate-500">{item.jobTitle || "Pozisyon belirtilmedi"}</span>
                      </TableCell>
                      <TableCell>
                        {assignment ? (
                          <>
                            <span className="block max-w-52 truncate text-slate-800">{assignment.projectName}</span>
                            <span className="mt-1 block text-xs text-slate-500">{assignment.projectCode}</span>
                          </>
                        ) : (
                          <Badge variant="warning">Atama bekliyor</Badge>
                        )}
                      </TableCell>
                      <TableCell className="whitespace-nowrap">{displayDate(item.employmentStartDate)}</TableCell>
                      <TableCell>
                        <Badge variant={badgeVariant(item.status)}>
                          {statusLabels[item.status] ?? "Bilinmiyor"}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <PersonnelShortcuts personnelId={item.id} />
                      </TableCell>
                      <TableCell className="text-right">
                        <Button variant="secondary" size="sm" onClick={() => void openEdit(item.id)}>
                          Düzenle
                        </Button>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      {formOpen && (
        <div className="fixed inset-0 z-50 flex justify-end bg-slate-950/40 backdrop-blur-[1px]">
          <button
            type="button"
            className="min-w-0 flex-1 cursor-default"
            onClick={() => !saving && setFormOpen(false)}
            aria-label="Formu kapat"
          />
          <aside className="h-full w-full max-w-3xl overflow-y-auto bg-white shadow-2xl">
            <div className="sticky top-0 z-10 flex items-center justify-between border-b border-slate-200 bg-white px-6 py-5">
              <div>
                <span className="text-xs font-semibold tracking-widest text-slate-500">PERSONEL KARTI</span>
                <h2 className="mt-1 text-xl font-semibold text-slate-900">
                  {editingId ? "Personeli Düzenle" : "Yeni Personel"}
                </h2>
              </div>
              <button
                type="button"
                onClick={() => !saving && setFormOpen(false)}
                className="flex h-10 w-10 items-center justify-center rounded-lg border border-slate-200 text-xl text-slate-500 hover:bg-slate-50"
                aria-label="Formu kapat"
              >
                ×
              </button>
            </div>

            {detailLoading ? (
              <div className="p-12 text-center text-sm text-slate-500">Personel bilgileri yükleniyor...</div>
            ) : (
              <form onSubmit={handleSave} className="p-6">
                <div className="mb-6 rounded-xl border border-slate-200 bg-slate-50 p-4">
                  <h3 className="font-semibold text-slate-900">Organizasyon bilgileri</h3>
                  <div className="mt-4 grid gap-4 md:grid-cols-2">
                    <Select
                      label="Şirket"
                      value={form.companyId}
                      onChange={(event) => {
                        updateForm("companyId", event.target.value);
                        updateForm("branchId", "");
                      }}
                      placeholder="Şirket seçin"
                      required
                      disabled={Boolean(editingId)}
                      options={companies.map((company) => ({
                        value: company.id,
                        label: `${company.code} · ${company.name}`,
                      }))}
                    />
                    <Select
                      label="Şube"
                      value={form.branchId}
                      onChange={(event) => updateForm("branchId", event.target.value)}
                      placeholder="Şube seçin"
                      options={formBranches.map((branch) => ({
                        value: branch.id,
                        label: `${branch.code} · ${branch.name}`,
                      }))}
                    />
                    <Input
                      label="Sicil / Personel Numarası"
                      value={form.employeeNumber}
                      onChange={(event) => updateForm("employeeNumber", event.target.value)}
                      disabled={Boolean(editingId)}
                      required
                    />
                    <Select
                      label="Personel Durumu"
                      value={form.status}
                      onChange={(event) => updateForm("status", event.target.value)}
                      options={statusOptions}
                      disabled={!editingId}
                    />
                    <Input
                      label="Departman / Meslek"
                      value={form.profession}
                      onChange={(event) => updateForm("profession", event.target.value)}
                    />
                    <Input
                      label="Pozisyon / Görev"
                      value={form.jobTitle}
                      onChange={(event) => updateForm("jobTitle", event.target.value)}
                    />
                  </div>
                </div>

                <div className="mb-6">
                  <h3 className="font-semibold text-slate-900">Kimlik ve iletişim</h3>
                  <div className="mt-4 grid gap-4 md:grid-cols-2">
                    <Input label="Ad" value={form.firstName} onChange={(event) => updateForm("firstName", event.target.value)} required />
                    <Input label="Soyad" value={form.lastName} onChange={(event) => updateForm("lastName", event.target.value)} required />
                    <Input label="TC Kimlik Numarası" value={form.identityNumber} onChange={(event) => updateForm("identityNumber", event.target.value)} maxLength={11} inputMode="numeric" />
                    <Input label="Doğum Tarihi" type="date" value={form.birthDate} onChange={(event) => updateForm("birthDate", event.target.value)} />
                    <Input label="Telefon" value={form.phone} onChange={(event) => updateForm("phone", event.target.value)} />
                    <Input label="E-posta" type="email" value={form.email} onChange={(event) => updateForm("email", event.target.value)} />
                    <Input label="Adres" value={form.address} onChange={(event) => updateForm("address", event.target.value)} className="md:col-span-2" />
                  </div>
                </div>

                <div className="mb-8 rounded-xl border border-slate-200 bg-slate-50 p-4">
                  <h3 className="font-semibold text-slate-900">İstihdam ve ücret</h3>
                  <div className="mt-4 grid gap-4 md:grid-cols-2">
                    <Input label="SGK Sicil Numarası" value={form.sgkRegistrationNumber} onChange={(event) => updateForm("sgkRegistrationNumber", event.target.value)} />
                    <Input label="Aylık Ücret" type="number" min="0" step="0.01" value={form.monthlySalary} onChange={(event) => updateForm("monthlySalary", event.target.value)} />
                    <Input label="İşe Giriş Tarihi" type="date" value={form.employmentStartDate} onChange={(event) => updateForm("employmentStartDate", event.target.value)} />
                    <Input label="İşten Çıkış Tarihi" type="date" value={form.employmentEndDate} onChange={(event) => updateForm("employmentEndDate", event.target.value)} disabled={!editingId} />
                    {editingId && (
                      <label className="flex items-center gap-3 rounded-lg border border-slate-200 bg-white px-4 py-3 md:col-span-2">
                        <input
                          type="checkbox"
                          checked={form.isActive}
                          onChange={(event) => updateForm("isActive", event.target.checked)}
                          className="h-4 w-4 rounded border-slate-300"
                        />
                        <span>
                          <strong className="block text-sm text-slate-800">Aktif personel kaydı</strong>
                          <span className="text-xs text-slate-500">Pasife alınan personel aktif işlem listelerinden çıkarılır.</span>
                        </span>
                      </label>
                    )}
                  </div>
                </div>

                <div className="sticky bottom-0 flex justify-end gap-3 border-t border-slate-200 bg-white py-4">
                  <Button type="button" variant="secondary" onClick={() => setFormOpen(false)} disabled={saving}>
                    Vazgeç
                  </Button>
                  <Button type="submit" loading={saving}>
                    {editingId ? "Değişiklikleri Kaydet" : "Personeli Kaydet"}
                  </Button>
                </div>
              </form>
            )}
          </aside>
        </div>
      )}
    </ErpShell>
  );
}
