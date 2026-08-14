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
  PersonnelListItem,
} from "@/services/personnel.service";
import {
  companyService,
  CompanyListItem,
} from "@/services/company.service";
import {
  branchService,
  BranchListItem,
} from "@/services/branch.service";

type PersonnelForm = {
  companyId: string;
  branchId: string;
  employeeNumber: string;
  firstName: string;
  lastName: string;
  identityNumber: string;
  phone: string;
  email: string;
  jobTitle: string;
  profession: string;
  employmentStartDate: string;
  monthlySalary: string;
};

const initialForm: PersonnelForm = {
  companyId: "",
  branchId: "",
  employeeNumber: "",
  firstName: "",
  lastName: "",
  identityNumber: "",
  phone: "",
  email: "",
  jobTitle: "",
  profession: "",
  employmentStartDate: "",
  monthlySalary: "",
};

const statusLabels: Record<number, string> = {
  0: "Aday",
  1: "Aktif",
  2: "İzinli",
  3: "Askıda",
  4: "İşten Ayrıldı",
};

function statusVariant(status: number) {
  if (status === 1) return "success";
  if (status === 2) return "warning";
  if (status === 3 || status === 4) return "danger";
  return "default";
}

function formatDate(value?: string | null) {
  return value
    ? new Date(value).toLocaleDateString("tr-TR")
    : "—";
}

export default function PersonnelPage() {
  const [items, setItems] = useState<PersonnelListItem[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [form, setForm] = useState<PersonnelForm>(initialForm);
  const [search, setSearch] = useState("");
  const [companyFilter, setCompanyFilter] = useState("");
  const [showForm, setShowForm] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  async function loadPersonnel(
    companyId?: string,
    searchText?: string
  ) {
    setLoading(true);
    setError("");

    try {
      const result = await personnelService.getAll({
        companyId: companyId || undefined,
        search: searchText?.trim() || undefined,
      });

      setItems(result);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Personel listesi yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    async function loadInitialData() {
      try {
        const [companyResult, personnelResult] = await Promise.all([
          companyService.getAll(),
          personnelService.getAll(),
        ]);

        setCompanies(companyResult);
        setItems(personnelResult);

        if (companyResult.length === 1) {
          setForm((current) => ({
            ...current,
            companyId: companyResult[0].id,
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Personel ekranı yüklenemedi."
        );
      } finally {
        setLoading(false);
      }
    }

    loadInitialData();
  }, []);

  useEffect(() => {
    async function loadBranches() {
      if (!form.companyId) {
        setBranches([]);
        return;
      }

      try {
        const result = await branchService.getAll(form.companyId);
        setBranches(result);

        if (
          form.branchId &&
          !result.some((branch) => branch.id === form.branchId)
        ) {
          setForm((current) => ({
            ...current,
            branchId: "",
          }));
        }
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Şubeler yüklenemedi."
        );
      }
    }

    loadBranches();
  }, [form.companyId, form.branchId]);

  const activeCount = useMemo(
    () => items.filter((item) => item.status === 1 && item.isActive).length,
    [items]
  );

  const assignedCount = useMemo(
    () =>
      items.filter((item) => item.activeAssignments.length > 0).length,
    [items]
  );

  const unassignedCount = useMemo(
    () =>
      items.filter((item) => item.activeAssignments.length === 0).length,
    [items]
  );

  function updateForm<K extends keyof PersonnelForm>(
    key: K,
    value: PersonnelForm[K]
  ) {
    setForm((current) => ({
      ...current,
      [key]: value,
    }));
  }

  async function handleSearch(event: FormEvent) {
    event.preventDefault();
    await loadPersonnel(companyFilter, search);
  }

  async function clearFilters() {
    setSearch("");
    setCompanyFilter("");
    await loadPersonnel();
  }

  async function handleCreate(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      await personnelService.create({
        companyId: form.companyId,
        branchId: form.branchId || null,
        employeeNumber: form.employeeNumber,
        firstName: form.firstName,
        lastName: form.lastName,
        identityNumber: form.identityNumber || null,
        birthDate: null,
        phone: form.phone || null,
        email: form.email || null,
        address: null,
        jobTitle: form.jobTitle || null,
        profession: form.profession || null,
        sgkRegistrationNumber: null,
        employmentStartDate: form.employmentStartDate || null,
        monthlySalary: form.monthlySalary
          ? Number(form.monthlySalary)
          : null,
      });

      setSuccess("Personel kaydı başarıyla oluşturuldu.");
      setForm((current) => ({
        ...initialForm,
        companyId:
          companies.length === 1
            ? companies[0].id
            : current.companyId,
      }));
      setShowForm(false);
      await loadPersonnel(companyFilter, search);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Personel kaydı oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Personel Yönetimi"
      description="Personel kayıtları, proje görevlendirmeleri ve ekip dağılımı"
    >
      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {success && (
        <div className="mb-5 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          {success}
        </div>
      )}

      <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <StatCard
          title="Toplam Personel"
          value={loading ? "…" : items.length}
          description="Kayıtlı personel"
          icon="♙"
        />

        <StatCard
          title="Aktif Personel"
          value={loading ? "…" : activeCount}
          description="Aktif çalışan"
          icon="✓"
        />

        <StatCard
          title="Projede Görevli"
          value={loading ? "…" : assignedCount}
          description="Aktif görevlendirme"
          icon="▣"
        />

        <StatCard
          title="Projesiz Personel"
          value={loading ? "…" : unassignedCount}
          description="Atama bekliyor"
          icon="!"
        />
      </div>

      <div className="mb-6 flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
        <form
          onSubmit={handleSearch}
          className="flex flex-1 flex-col gap-3 md:flex-row"
        >
          <Input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Ad, soyad, personel no veya TC ile ara"
            className="md:min-w-80"
          />

          <Select
            value={companyFilter}
            onChange={(event) =>
              setCompanyFilter(event.target.value)
            }
            placeholder="Tüm şirketler"
            options={companies.map((company) => ({
              label: `${company.code} · ${company.name}`,
              value: company.id,
            }))}
          />

          <div className="flex gap-2">
            <Button type="submit" variant="secondary">
              Ara
            </Button>

            <Button
              type="button"
              variant="ghost"
              onClick={clearFilters}
            >
              Temizle
            </Button>
          </div>
        </form>

        {/* İşe giriş/çıkış İK tarafında işleniyor; liste filtreye
            dokunmadan tazelenemiyordu. */}
        <Button
          type="button"
          variant="secondary"
          disabled={loading}
          onClick={() => void loadPersonnel(companyFilter, search)}
        >
          Yenile
        </Button>

        <Button
          type="button"
          onClick={() => setShowForm((value) => !value)}
        >
          {showForm ? "Formu Kapat" : "+ Yeni Personel"}
        </Button>
      </div>

      {showForm && (
        <Card className="mb-6">
          <CardHeader>
            <h2 className="text-lg font-semibold text-slate-900">
              Yeni Personel
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Temel personel ve işe giriş bilgilerini kaydedin.
            </p>
          </CardHeader>

          <CardContent>
            <form onSubmit={handleCreate}>
              <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
                <Select
                  label="Şirket"
                  value={form.companyId}
                  onChange={(event) =>
                    updateForm("companyId", event.target.value)
                  }
                  placeholder="Şirket seçin"
                  required
                  options={companies.map((company) => ({
                    label: `${company.code} · ${company.name}`,
                    value: company.id,
                  }))}
                />

                <Select
                  label="Şube"
                  value={form.branchId}
                  onChange={(event) =>
                    updateForm("branchId", event.target.value)
                  }
                  placeholder="Şube seçin"
                  options={branches.map((branch) => ({
                    label: `${branch.code} · ${branch.name}`,
                    value: branch.id,
                  }))}
                />

                <Input
                  label="Personel Numarası"
                  value={form.employeeNumber}
                  onChange={(event) =>
                    updateForm("employeeNumber", event.target.value)
                  }
                  placeholder="Örn. PRS-0001"
                  required
                />

                <Input
                  label="Ad"
                  value={form.firstName}
                  onChange={(event) =>
                    updateForm("firstName", event.target.value)
                  }
                  required
                />

                <Input
                  label="Soyad"
                  value={form.lastName}
                  onChange={(event) =>
                    updateForm("lastName", event.target.value)
                  }
                  required
                />

                <Input
                  label="TC Kimlik Numarası"
                  value={form.identityNumber}
                  onChange={(event) =>
                    updateForm("identityNumber", event.target.value)
                  }
                  maxLength={11}
                />

                <Input
                  label="Telefon"
                  value={form.phone}
                  onChange={(event) =>
                    updateForm("phone", event.target.value)
                  }
                />

                <Input
                  label="E-posta"
                  type="email"
                  value={form.email}
                  onChange={(event) =>
                    updateForm("email", event.target.value)
                  }
                />

                <Input
                  label="Görevi"
                  value={form.jobTitle}
                  onChange={(event) =>
                    updateForm("jobTitle", event.target.value)
                  }
                  placeholder="Örn. Elektrik Ustası"
                />

                <Input
                  label="Mesleği"
                  value={form.profession}
                  onChange={(event) =>
                    updateForm("profession", event.target.value)
                  }
                  placeholder="Örn. Elektrikçi"
                />

                <Input
                  label="İşe Giriş Tarihi"
                  type="date"
                  value={form.employmentStartDate}
                  onChange={(event) =>
                    updateForm(
                      "employmentStartDate",
                      event.target.value
                    )
                  }
                />

                <Input
                  label="Aylık Ücret"
                  type="number"
                  min="0"
                  step="0.01"
                  value={form.monthlySalary}
                  onChange={(event) =>
                    updateForm("monthlySalary", event.target.value)
                  }
                />
              </div>

              <div className="mt-6 flex justify-end gap-3">
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => setShowForm(false)}
                >
                  Vazgeç
                </Button>

                <Button type="submit" loading={saving}>
                  Personeli Kaydet
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <div className="flex items-center justify-between gap-4">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">
                Personel Listesi
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                Personel kayıtları ve aktif proje görevleri
              </p>
            </div>

            <Badge variant="info">
              {items.length} kayıt
            </Badge>
          </div>
        </CardHeader>

        <CardContent>
          {loading ? (
            <div className="py-12 text-center text-sm text-slate-500">
              Personel kayıtları yükleniyor...
            </div>
          ) : items.length === 0 ? (
            <EmptyState
              title="Personel kaydı bulunamadı"
              description="Yeni personel ekleyerek personel yönetimini başlatabilirsiniz."
              icon="♙"
              action={
                <Button
                  type="button"
                  onClick={() => setShowForm(true)}
                >
                  Yeni Personel
                </Button>
              }
            />
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Personel</TableHead>
                  <TableHead>Görev / Meslek</TableHead>
                  <TableHead>Şube</TableHead>
                  <TableHead>Aktif Proje</TableHead>
                  <TableHead>İşe Giriş</TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead className="text-right">
                    İşlem
                  </TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {items.map((item) => {
                  const primaryAssignment =
                    item.activeAssignments.find(
                      (assignment) =>
                        assignment.isPrimaryAssignment
                    ) ?? item.activeAssignments[0];

                  return (
                    <TableRow key={item.id}>
                      <TableCell>
                        <div>
                          <strong className="block text-slate-900">
                            {item.fullName}
                          </strong>
                          <span className="mt-1 block text-xs text-slate-500">
                            {item.employeeNumber}
                            {item.phone ? ` · ${item.phone}` : ""}
                          </span>
                        </div>
                      </TableCell>

                      <TableCell>
                        <div>
                          <span className="block text-slate-800">
                            {item.jobTitle || "—"}
                          </span>
                          <span className="mt-1 block text-xs text-slate-500">
                            {item.profession || "Meslek belirtilmedi"}
                          </span>
                        </div>
                      </TableCell>

                      <TableCell>
                        {item.branchName || "—"}
                      </TableCell>

                      <TableCell>
                        {primaryAssignment ? (
                          <div>
                            <strong className="block text-slate-800">
                              {primaryAssignment.projectName}
                            </strong>
                            <span className="mt-1 block text-xs text-slate-500">
                              {primaryAssignment.projectCode}
                              {primaryAssignment.role
                                ? ` · ${primaryAssignment.role}`
                                : ""}
                            </span>
                          </div>
                        ) : (
                          <Badge variant="warning">
                            Atama bekliyor
                          </Badge>
                        )}
                      </TableCell>

                      <TableCell>
                        {formatDate(item.employmentStartDate)}
                      </TableCell>

                      <TableCell>
                        <Badge
                          variant={statusVariant(item.status)}
                        >
                          {statusLabels[item.status] ??
                            "Bilinmiyor"}
                        </Badge>
                      </TableCell>

                      <TableCell className="text-right">
                        <Link
                          href={`/personel/${item.id}`}
                          className="inline-flex h-9 items-center justify-center rounded-lg border border-slate-300 bg-white px-3 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                        >
                          Personel Kartı
                        </Link>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </ErpShell>
  );
}
