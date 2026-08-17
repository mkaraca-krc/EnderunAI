"use client";

import {
  FormEvent,
  ReactNode,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";
import Link from "next/link";
import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { dateTime } from "@/lib/format/turkish";
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
import { useModuleActions } from "@/lib/auth/module-actions";
import {
  userManagementService,
  type ManagedUser,
  type ManagedUserPayload,
  type PermissionDefinition,
  type UserManagementCatalog,
} from "@/services/user-management.service";

const SITE_ONLY_POLICY = 1;

type UserForm = {
  username: string;
  fullName: string;
  honorific: string;
  email: string;
  roleNames: string[];
  projectSiteIds: string[];
  password: string;
  isActive: boolean;
  allowedPermissions: string[];
  deniedPermissions: string[];
  workHoursExempt: boolean;
};

type CredentialNotice = {
  title: string;
  username: string;
  password: string;
} | null;

const emptyForm: UserForm = {
  username: "",
  fullName: "",
  honorific: "",
  email: "",
  roleNames: [],
  projectSiteIds: [],
  password: "",
  isActive: true,
  allowedPermissions: [],
  deniedPermissions: [],
  workHoursExempt: false,
};

function normalized(value?: string | null) {
  return (value ?? "").trim().toLocaleLowerCase("tr-TR");
}

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) {
    return error.message;
  }

  return "İşlem tamamlanamadı. Lütfen tekrar deneyin.";
}

function displayDate(value?: string | null) {
  if (!value) {
    return "Henüz giriş yapmadı";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : dateTime(date);
}

function initials(fullName: string) {
  return fullName
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part.charAt(0).toLocaleUpperCase("tr-TR"))
    .join("");
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
        <h2 className="font-semibold text-slate-950">{title}</h2>
        <p className="mt-1 text-sm text-slate-500">{description}</p>
      </div>
      {action}
    </div>
  );
}

export default function UserManagementPage() {
  /*
   * Aksiyon izinleri UÇLARDAN (UserManagementController):
   *   POST users                       -> user-management.create
   *   PUT  users/{id}                  -> user-management.edit
   *   POST users/{id}/reset-password   -> user-management.edit
   *
   * ŞİFRE SIFIRLAMA edit'e bağlı, ayrı bir yetki değil. Uç öyle;
   * ayrı bir anahtar gerekiyorsa o backend kararı.
   */
  const actions = useModuleActions("user-management");

  const [catalog, setCatalog] = useState<UserManagementCatalog | null>(null);
  const [users, setUsers] = useState<ManagedUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [pendingToggle, setPendingToggle] = useState<ManagedUser | null>(
    null
  );
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState("active");
  const [editorOpen, setEditorOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<ManagedUser | null>(null);
  const [form, setForm] = useState<UserForm>(emptyForm);
  const [credentialNotice, setCredentialNotice] =
    useState<CredentialNotice>(null);
  const [resetUser, setResetUser] = useState<ManagedUser | null>(null);
  const [resetPassword, setResetPassword] = useState("");

  const loadData = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [catalogResult, userRows] = await Promise.all([
        userManagementService.getCatalog(),
        userManagementService.getUsers(),
      ]);

      setCatalog(catalogResult);
      setUsers(userRows ?? []);
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

    const timer = window.setTimeout(() => setNotice(""), 4000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const roleByName = useMemo(
    () => new Map((catalog?.roles ?? []).map((role) => [role.name, role])),
    [catalog]
  );

  const permissionGroups = useMemo(() => {
    const groups = new Map<string, PermissionDefinition[]>();

    for (const permission of catalog?.permissions ?? []) {
      const current = groups.get(permission.module) ?? [];
      current.push(permission);
      groups.set(permission.module, current);
    }

    return [...groups.entries()];
  }, [catalog]);

  const visibleUsers = useMemo(() => {
    const term = normalized(search);

    return users.filter((user) => {
      if (roleFilter !== "all" && !user.roleNames.includes(roleFilter)) {
        return false;
      }
      if (statusFilter === "active" && !user.isActive) {
        return false;
      }
      if (statusFilter === "inactive" && user.isActive) {
        return false;
      }

      if (!term) {
        return true;
      }

      return [
        user.fullName,
        user.username,
        user.email,
        ...user.roleNames,
      ].some((value) => normalized(value ?? "").includes(term));
    });
  }, [roleFilter, search, statusFilter, users]);

  const summary = useMemo(
    () => ({
      total: users.length,
      active: users.filter((user) => user.isActive).length,
      admins: users.filter((user) =>
        user.roleNames.some((name) =>
          ["Admin", "Genel Müdür"].includes(name)
        )
      ).length,
      neverLoggedIn: users.filter((user) => !user.lastLoginAtUtc).length,
    }),
    [users]
  );

  const requiresSiteSelection = form.roleNames.some(
    (name) => roleByName.get(name)?.dataScopePolicy === SITE_ONLY_POLICY
  );

  const allowedSet = useMemo(
    () => new Set(form.allowedPermissions),
    [form.allowedPermissions]
  );
  const deniedSet = useMemo(
    () => new Set(form.deniedPermissions),
    [form.deniedPermissions]
  );

  function toggleRole(roleName: string) {
    setForm((current) => ({
      ...current,
      roleNames: current.roleNames.includes(roleName)
        ? current.roleNames.filter((name) => name !== roleName)
        : [...current.roleNames, roleName],
    }));
  }

  function toggleSite(siteId: string) {
    setForm((current) => ({
      ...current,
      projectSiteIds: current.projectSiteIds.includes(siteId)
        ? current.projectSiteIds.filter((id) => id !== siteId)
        : [...current.projectSiteIds, siteId],
    }));
  }

  function openCreate() {
    setEditingUser(null);
    setForm(emptyForm);
    setError("");
    setEditorOpen(true);
  }

  function openEdit(user: ManagedUser) {
    setEditingUser(user);
    setForm({
      username: user.username,
      fullName: user.fullName,
      honorific: user.honorific ?? "",
      email: user.email ?? "",
      roleNames: [...user.roleNames],
      projectSiteIds: [...user.projectSiteIds],
      password: "",
      isActive: user.isActive,
      allowedPermissions: [...user.allowedPermissions],
      deniedPermissions: [...user.deniedPermissions],
      workHoursExempt: user.workHoursExempt,
    });
    setError("");
    setEditorOpen(true);
  }

  function setPermissionOverride(
    key: string,
    mode: "default" | "allow" | "deny"
  ) {
    setForm((current) => ({
      ...current,
      allowedPermissions:
        mode === "allow"
          ? [...new Set([...current.allowedPermissions, key])]
          : current.allowedPermissions.filter((item) => item !== key),
      deniedPermissions:
        mode === "deny"
          ? [...new Set([...current.deniedPermissions, key])]
          : current.deniedPermissions.filter((item) => item !== key),
    }));
  }

  function buildPayload(): ManagedUserPayload {
    return {
      username: form.username.trim(),
      fullName: form.fullName.trim(),
      honorific: form.honorific || null,
      email: form.email.trim() || null,
      roleNames: form.roleNames,
      isActive: form.isActive,
      allowedPermissions: form.allowedPermissions,
      deniedPermissions: form.deniedPermissions,
      projectSiteIds: requiresSiteSelection ? form.projectSiteIds : [],
      workHoursExempt: form.workHoursExempt,
      ...(editingUser || !form.password.trim()
        ? {}
        : { password: form.password.trim() }),
    };
  }

  async function submitUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (form.roleNames.length === 0) {
      setError("En az bir görev rolü seçilmelidir.");
      return;
    }

    if (requiresSiteSelection && form.projectSiteIds.length === 0) {
      setError("Seçilen rol için en az bir şantiye ataması zorunludur.");
      return;
    }

    setSaving(true);
    setError("");

    try {
      const result = editingUser
        ? await userManagementService.updateUser(
            editingUser.id,
            buildPayload()
          )
        : await userManagementService.createUser(buildPayload());

      setEditorOpen(false);
      setNotice(result.message);

      if (result.temporaryPassword) {
        setCredentialNotice({
          title: "Yeni kullanıcı giriş bilgisi",
          username: result.user.username,
          password: result.temporaryPassword,
        });
      }

      await loadData();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setSaving(false);
    }
  }

  /**
   * Kullanıcıyı aktif/pasif yap.
   *
   * PASİFE ALMA ERİŞİMİ ANINDA KESER; onay bu yüzden ayrı bir adım.
   * Tarayıcı diyaloğu kullanıcı adını düz metin içinde gösteriyordu
   * ve yanlış satıra tıklandığı fark edilmiyordu.
   */
  async function toggleUserStatus(user: ManagedUser) {
    setPendingToggle(null);
    setSaving(true);
    setError("");

    try {
      const result = await userManagementService.updateUser(user.id, {
        username: user.username,
        fullName: user.fullName,
        email: user.email,
        roleNames: user.roleNames,
        isActive: !user.isActive,
        allowedPermissions: user.allowedPermissions,
        deniedPermissions: user.deniedPermissions,
        projectSiteIds: user.projectSiteIds,
        workHoursExempt: user.workHoursExempt,
      });
      setNotice(result.message);
      await loadData();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setSaving(false);
    }
  }

  async function submitPasswordReset(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!resetUser) {
      return;
    }

    setSaving(true);
    setError("");

    try {
      const result = await userManagementService.resetPassword(
        resetUser.id,
        resetPassword
      );
      setCredentialNotice({
        title: "Sıfırlanan giriş bilgisi",
        username: resetUser.username,
        password: result.temporaryPassword,
      });
      setResetUser(null);
      setResetPassword("");
      setNotice(result.message);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setSaving(false);
    }
  }

  async function copyCredential(value: string) {
    await navigator.clipboard.writeText(value);
    setNotice("Geçici şifre panoya kopyalandı.");
  }

  const roleOptions = [
    { value: "all", label: "Tüm görev rolleri" },
    ...(catalog?.roles ?? []).map((role) => ({
      value: role.name,
      label: role.name,
    })),
  ];

  return (
    <ErpShell
      design="redwood"
      title="Kullanıcılar ve Yetkiler"
      description="Kullanıcı hesabı, görev rolleri, şantiye ataması ve şifre yönetimi"
    >
      <div className="space-y-6">
        {/* Rol ve yetki değişiklikleri başka yöneticiler tarafından da yapılıyor. */}
        <div className="flex justify-end">
          <Button variant="secondary" onClick={() => void loadData()}>Yenile</Button>
        </div>

        {error && !editorOpen && !resetUser && (
          <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}
        {notice && (
          <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
            {notice}
          </div>
        )}

        {credentialNotice && (
          <Card className="border-indigo-200 bg-indigo-50">
            <CardContent className="flex flex-col gap-4 p-5 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <span className="text-xs font-semibold uppercase tracking-wide text-indigo-700">
                  {credentialNotice.title}
                </span>
                <p className="mt-2 text-sm text-indigo-950">
                  Kullanıcı: <strong>{credentialNotice.username}</strong>
                </p>
                <p className="mt-1 font-mono text-lg font-semibold text-indigo-950">
                  {credentialNotice.password}
                </p>
                <p className="mt-2 text-xs text-indigo-700">
                  Bu geçici şifre yalnızca şimdi gösterilir. Güvenli şekilde
                  kullanıcıya iletin.
                </p>
              </div>
              <div className="flex gap-2">
                <Button
                  variant="secondary"
                  onClick={() =>
                    void copyCredential(credentialNotice.password)
                  }
                >
                  Şifreyi kopyala
                </Button>
                <Button variant="ghost" onClick={() => setCredentialNotice(null)}>
                  Kapat
                </Button>
              </div>
            </CardContent>
          </Card>
        )}

        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            title="Toplam kullanıcı"
            value={summary.total}
            description="Sistemde kayıtlı hesap"
            icon="♙"
          />
          <StatCard
            title="Aktif kullanıcı"
            value={summary.active}
            description="Giriş yapabilen hesap"
            icon="✓"
          />
          <StatCard
            title="Üst yönetim"
            value={summary.admins}
            description="Admin ve Genel Müdür"
            icon="◆"
          />
          <StatCard
            title="İlk giriş bekleyen"
            value={summary.neverLoggedIn}
            description="Henüz oturum açmamış"
            icon="◷"
          />
        </div>

        <Card>
          <SectionHeader
            title="Kullanıcı Hesapları"
            description="Bir kullanıcıya birden fazla rol atanabilir; izinler birleşir"
            action={
              <div className="flex gap-2">
                <Link href="/sistem-yonetimi/yetki-matrisi">
                  <Button variant="secondary">Yetki Matrisi</Button>
                </Link>
                {actions.can("create") && (
                  <Button onClick={openCreate}>+ Yeni Kullanıcı</Button>
                )}
              </div>
            }
          />
          <CardContent className="space-y-4">
            <div className="grid gap-3 lg:grid-cols-[1fr_240px_180px]">
              <Input
                value={search}
                placeholder="Ad, kullanıcı adı, e-posta veya rol ara..."
                onChange={(event) => setSearch(event.target.value)}
              />
              <Select
                value={roleFilter}
                options={roleOptions}
                onChange={(event) => setRoleFilter(event.target.value)}
              />
              <Select
                value={statusFilter}
                options={[
                  { value: "all", label: "Tüm durumlar" },
                  { value: "active", label: "Aktif kullanıcılar" },
                  { value: "inactive", label: "Pasif kullanıcılar" },
                ]}
                onChange={(event) => setStatusFilter(event.target.value)}
              />
            </div>

            {loading ? (
              <div className="py-12 text-center text-sm text-slate-500">
                Kullanıcılar yükleniyor...
              </div>
            ) : visibleUsers.length === 0 ? (
              <EmptyState
                title="Kullanıcı bulunamadı"
                description="Filtreleri değiştirin veya yeni kullanıcı oluşturun."
              />
            ) : (
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Kullanıcı</TableHead>
                      <TableHead>Görev rolleri</TableHead>
                      <TableHead>Şantiye</TableHead>
                      <TableHead>Yetki özeti</TableHead>
                      <TableHead>Son giriş</TableHead>
                      <TableHead>Durum</TableHead>
                      <TableHead className="text-right">İşlemler</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {visibleUsers.map((user) => (
                      <TableRow key={user.id}>
                        <TableCell>
                          <div className="flex items-center gap-3">
                            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand-700 text-sm font-semibold text-white">
                              {initials(user.fullName)}
                            </span>
                            <div>
                              <strong className="block text-slate-950">
                                {user.fullName}
                              </strong>
                              <span className="mt-0.5 block text-xs text-slate-500">
                                @{user.username}
                                {user.email ? ` · ${user.email}` : ""}
                              </span>
                            </div>
                          </div>
                        </TableCell>
                        <TableCell>
                          <div className="flex flex-wrap gap-1">
                            {user.roleNames.map((name) => (
                              <Badge
                                key={name}
                                variant={
                                  ["Admin", "Genel Müdür"].includes(name)
                                    ? "info"
                                    : "default"
                                }
                              >
                                {name}
                              </Badge>
                            ))}
                          </div>
                        </TableCell>
                        <TableCell>
                          {user.projectSites.length === 0 ? (
                            <span className="text-xs text-slate-400">—</span>
                          ) : (
                            <span className="text-xs text-slate-600">
                              {user.projectSites
                                .map((site) => site.code)
                                .join(", ")}
                            </span>
                          )}
                        </TableCell>
                        <TableCell>
                          <div className="flex flex-wrap items-center gap-1.5">
                            <Badge variant="success">
                              {user.effectivePermissions.length} izin
                            </Badge>
                            {user.allowedPermissions.length > 0 && (
                              <Badge variant="info">
                                +{user.allowedPermissions.length} özel
                              </Badge>
                            )}
                            {user.deniedPermissions.length > 0 && (
                              <Badge variant="warning">
                                -{user.deniedPermissions.length} kısıt
                              </Badge>
                            )}
                          </div>
                        </TableCell>
                        <TableCell>
                          <span className="text-sm text-slate-600">
                            {displayDate(user.lastLoginAtUtc)}
                          </span>
                        </TableCell>
                        <TableCell>
                          <div className="flex flex-wrap gap-1">
                            <Badge variant={user.isActive ? "success" : "danger"}>
                              {user.isActive ? "Aktif" : "Pasif"}
                            </Badge>
                            {user.workHoursExempt && (
                              <Badge variant="info">Mesai istisnası</Badge>
                            )}
                          </div>
                        </TableCell>
                        <TableCell>
                          <div className="flex justify-end gap-1">
                            {actions.can("edit") && (
                              <Button
                                size="sm"
                                variant="secondary"
                                onClick={() => openEdit(user)}
                              >
                                Düzenle
                              </Button>
                            )}
                            {actions.can("edit") && (
                              <Button
                                size="sm"
                                variant="ghost"
                                onClick={() => {
                                  setResetPassword("");
                                  setResetUser(user);
                                }}
                              >
                                Şifre
                              </Button>
                            )}
                            {actions.can("edit") && (
                              <Button
                                size="sm"
                                variant="ghost"
                                disabled={saving}
                                className={
                                  user.isActive
                                    ? "text-red-600 hover:bg-red-50"
                                    : "text-emerald-700 hover:bg-emerald-50"
                                }
                                onClick={() => setPendingToggle(user)}
                              >
                                {user.isActive ? "Pasife al" : "Aktifleştir"}
                              </Button>
                            )}
                          </div>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <SectionHeader
            title="Görev Rolleri"
            description="Rol başına izinler Yetki Matrisi ekranından yönetilir"
            action={
              <Link href="/sistem-yonetimi/yetki-matrisi">
                <Button variant="secondary">Yetki Matrisine Git</Button>
              </Link>
            }
          />
          <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {(catalog?.roles ?? []).map((role) => (
              <article
                key={role.name}
                className="rounded-xl border border-slate-200 p-4"
              >
                <div className="flex items-center justify-between gap-3">
                  <strong className="text-sm text-slate-950">
                    {role.name}
                  </strong>
                  {role.dataScopePolicy === SITE_ONLY_POLICY && (
                    <Badge variant="warning">Sadece şantiye</Badge>
                  )}
                </div>
                <p className="mt-2 text-xs leading-5 text-slate-500">
                  {role.description}
                </p>
              </article>
            ))}
          </CardContent>
        </Card>
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
          <div className="max-h-[95vh] w-full overflow-y-auto rounded-t-2xl bg-white shadow-2xl sm:max-w-5xl sm:rounded-2xl">
            <div className="sticky top-0 z-20 flex items-start justify-between border-b border-slate-200 bg-white px-5 py-4">
              <div>
                <h2 className="text-lg font-semibold text-slate-950">
                  {editingUser ? "Kullanıcıyı Düzenle" : "Yeni Kullanıcı"}
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Bir veya daha fazla rol seçin; izinler birleşir. İzinlerin
                  kendisi Yetki Matrisi'nden yönetilir.
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

            <form onSubmit={submitUser}>
              <div className="grid gap-6 p-5 lg:grid-cols-[340px_1fr]">
                <div className="space-y-4">
                  {error && (
                    <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                      {error}
                    </div>
                  )}

                  <div className="rounded-xl border border-slate-200 p-4">
                    <h3 className="font-semibold text-slate-950">
                      Hesap Bilgileri
                    </h3>
                    <div className="mt-4 space-y-4">
                      <Input
                        label="Ad soyad"
                        required
                        value={form.fullName}
                        placeholder="Örn. Ahmet Yılmaz"
                        onChange={(event) =>
                          setForm((current) => ({
                            ...current,
                            fullName: event.target.value,
                          }))
                        }
                      />
                      <label className="block">
                        <span className="mb-1.5 block text-sm font-medium text-slate-700">
                          Hitap
                        </span>
                        <select
                          value={form.honorific}
                          onChange={(event) =>
                            setForm((current) => ({
                              ...current,
                              honorific: event.target.value,
                            }))
                          }
                          className="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm"
                        >
                          <option value="">Belirtilmedi (Sayın ...)</option>
                          <option value="Bey">Bey</option>
                          <option value="Hanım">Hanım</option>
                        </select>
                        <span className="mt-1 block text-xs text-slate-500">
                          Karşılamada kullanılır. Belirtilmezse cinsiyet tahmin
                          edilmez, nötr &quot;Sayın&quot; hitabı kullanılır.
                        </span>
                      </label>
                      <Input
                        label="Kullanıcı adı"
                        required
                        value={form.username}
                        placeholder="Örn. ahmet.yilmaz"
                        onChange={(event) =>
                          setForm((current) => ({
                            ...current,
                            username: event.target.value,
                          }))
                        }
                      />
                      <Input
                        label="E-posta"
                        type="email"
                        value={form.email}
                        placeholder="kullanici@enderunenerji.com.tr"
                        onChange={(event) =>
                          setForm((current) => ({
                            ...current,
                            email: event.target.value,
                          }))
                        }
                      />
                      {!editingUser && (
                        <Input
                          label="İlk şifre (isteğe bağlı)"
                          type="password"
                          minLength={10}
                          value={form.password}
                          placeholder="Boşsa güvenli şifre üretilir"
                          onChange={(event) =>
                            setForm((current) => ({
                              ...current,
                              password: event.target.value,
                            }))
                          }
                        />
                      )}
                    </div>
                  </div>

                  <div className="rounded-xl border border-slate-200 p-4">
                    <h3 className="font-semibold text-slate-950">
                      Görev Rolleri
                    </h3>
                    <p className="mt-1 text-xs text-slate-500">
                      Birden fazla rol seçilebilir; izinler birleşir.
                    </p>
                    <div className="mt-3 grid gap-2">
                      {(catalog?.roles ?? []).map((role) => (
                        <label
                          key={role.name}
                          className={[
                            "flex cursor-pointer items-start gap-3 rounded-lg border p-2.5 transition",
                            form.roleNames.includes(role.name)
                              ? "border-emerald-200 bg-emerald-50"
                              : "border-slate-200 bg-white hover:bg-slate-50",
                          ].join(" ")}
                        >
                          <input
                            type="checkbox"
                            checked={form.roleNames.includes(role.name)}
                            onChange={() => toggleRole(role.name)}
                            className="mt-0.5 h-4 w-4 rounded border-slate-300"
                          />
                          <span className="min-w-0">
                            <span className="flex flex-wrap items-center gap-1.5">
                              <strong className="text-sm text-slate-900">
                                {role.name}
                              </strong>
                              {role.dataScopePolicy === SITE_ONLY_POLICY && (
                                <Badge variant="warning">Sadece şantiye</Badge>
                              )}
                            </span>
                            <span className="mt-0.5 block text-xs leading-5 text-slate-500">
                              {role.description}
                            </span>
                          </span>
                        </label>
                      ))}
                    </div>
                  </div>

                  {requiresSiteSelection && (
                    <div className="rounded-xl border border-amber-200 bg-amber-50 p-4">
                      <h3 className="font-semibold text-amber-900">
                        Şantiye Ataması (zorunlu)
                      </h3>
                      <p className="mt-1 text-xs text-amber-800">
                        Seçilen rol sadece atanan şantiyelerin verisini
                        görebilir.
                      </p>
                      <div className="mt-3 max-h-56 space-y-1.5 overflow-y-auto">
                        {(catalog?.sites ?? []).map((site) => (
                          <label
                            key={site.id}
                            className="flex cursor-pointer items-center gap-2.5 rounded-lg border border-amber-200 bg-white p-2 text-sm"
                          >
                            <input
                              type="checkbox"
                              checked={form.projectSiteIds.includes(site.id)}
                              onChange={() => toggleSite(site.id)}
                              className="h-4 w-4 rounded border-slate-300"
                            />
                            <span>
                              <strong>{site.code}</strong> — {site.name}
                              <span className="ml-1 text-xs text-slate-400">
                                ({site.projectCode})
                              </span>
                            </span>
                          </label>
                        ))}
                        {(catalog?.sites ?? []).length === 0 && (
                          <p className="text-xs text-amber-700">
                            Tanımlı şantiye bulunamadı.
                          </p>
                        )}
                      </div>
                    </div>
                  )}

                  <label className="flex items-center gap-3 rounded-xl border border-slate-200 p-4">
                    <input
                      type="checkbox"
                      checked={form.isActive}
                      onChange={(event) =>
                        setForm((current) => ({
                          ...current,
                          isActive: event.target.checked,
                        }))
                      }
                      className="h-4 w-4 rounded border-slate-300"
                    />
                    <span>
                      <strong className="block text-sm text-slate-950">
                        Kullanıcı aktif
                      </strong>
                      <span className="text-xs text-slate-500">
                        Pasif kullanıcı sisteme giriş yapamaz.
                      </span>
                    </span>
                  </label>

                  <label className="flex items-center gap-3 rounded-xl border border-slate-200 p-4">
                    <input
                      type="checkbox"
                      checked={form.workHoursExempt}
                      onChange={(event) =>
                        setForm((current) => ({
                          ...current,
                          workHoursExempt: event.target.checked,
                        }))
                      }
                      className="h-4 w-4 rounded border-slate-300"
                    />
                    <span>
                      <strong className="block text-sm text-slate-950">
                        Mesai saati istisnası
                      </strong>
                      <span className="text-xs text-slate-500">
                        İşaretlenirse rol bazlı mesai penceresi bu kullanıcı
                        için uygulanmaz, her zaman giriş yapabilir.
                      </span>
                    </span>
                  </label>
                </div>

                <div className="rounded-xl border border-slate-200">
                  <div className="border-b border-slate-200 px-4 py-4">
                    <h3 className="font-semibold text-slate-950">
                      Kullanıcıya Özel İstisnalar
                    </h3>
                    <p className="mt-1 text-xs text-slate-500">
                      Varsayılan olarak izinler yukarıda seçilen rollerden
                      gelir. Burada yalnızca bu kullanıcıya özel ek izin veya
                      kısıtlama tanımlayın.
                    </p>
                  </div>

                  <div className="max-h-[62vh] space-y-5 overflow-y-auto p-4">
                    {permissionGroups.map(([module, permissions]) => (
                      <section key={module}>
                        <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
                          {module}
                        </h4>
                        <div className="grid gap-2 xl:grid-cols-2">
                          {permissions.map((permission) => {
                            const mode = allowedSet.has(permission.key)
                              ? "allow"
                              : deniedSet.has(permission.key)
                                ? "deny"
                                : "default";

                            return (
                              <div
                                key={permission.key}
                                className="rounded-xl border border-slate-200 bg-white p-3"
                              >
                                <div className="flex flex-wrap items-center justify-between gap-2">
                                  <span className="min-w-0">
                                    <strong className="text-sm text-slate-900">
                                      {permission.name}
                                    </strong>
                                    <span className="mt-0.5 block text-xs leading-5 text-slate-500">
                                      {permission.description}
                                    </span>
                                  </span>
                                  <div className="flex shrink-0 gap-1">
                                    <button
                                      type="button"
                                      onClick={() =>
                                        setPermissionOverride(
                                          permission.key,
                                          "default"
                                        )
                                      }
                                      className={[
                                        "rounded-full px-2.5 py-1 text-xs",
                                        mode === "default"
                                          ? "bg-brand-700 text-white"
                                          : "bg-slate-100 text-slate-600",
                                      ].join(" ")}
                                    >
                                      Rolden
                                    </button>
                                    <button
                                      type="button"
                                      onClick={() =>
                                        setPermissionOverride(
                                          permission.key,
                                          "allow"
                                        )
                                      }
                                      className={[
                                        "rounded-full px-2.5 py-1 text-xs",
                                        mode === "allow"
                                          ? "bg-emerald-600 text-white"
                                          : "bg-emerald-50 text-emerald-700",
                                      ].join(" ")}
                                    >
                                      + İzin ver
                                    </button>
                                    <button
                                      type="button"
                                      onClick={() =>
                                        setPermissionOverride(
                                          permission.key,
                                          "deny"
                                        )
                                      }
                                      className={[
                                        "rounded-full px-2.5 py-1 text-xs",
                                        mode === "deny"
                                          ? "bg-red-600 text-white"
                                          : "bg-red-50 text-red-700",
                                      ].join(" ")}
                                    >
                                      Kısıtla
                                    </button>
                                  </div>
                                </div>
                              </div>
                            );
                          })}
                        </div>
                      </section>
                    ))}
                  </div>
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
                  {editingUser ? "Değişiklikleri kaydet" : "Kullanıcı oluştur"}
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {resetUser && (
        <div
          className="fixed inset-0 z-[60] flex items-end justify-center bg-slate-950/55 p-0 backdrop-blur-sm sm:items-center sm:p-4"
          role="dialog"
          aria-modal="true"
        >
          <div className="w-full rounded-t-2xl bg-white shadow-2xl sm:max-w-lg sm:rounded-2xl">
            <div className="border-b border-slate-200 px-5 py-4">
              <h2 className="text-lg font-semibold text-slate-950">
                Şifre Sıfırla
              </h2>
              <p className="mt-1 text-sm text-slate-500">
                {resetUser.fullName} · @{resetUser.username}
              </p>
            </div>
            <form onSubmit={submitPasswordReset}>
              <div className="space-y-4 p-5">
                {error && (
                  <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                    {error}
                  </div>
                )}
                <Input
                  label="Yeni şifre (isteğe bağlı)"
                  type="password"
                  minLength={10}
                  value={resetPassword}
                  placeholder="Boş bırakırsanız güvenli şifre üretilir"
                  onChange={(event) => setResetPassword(event.target.value)}
                />
                <p className="rounded-xl bg-amber-50 px-4 py-3 text-xs leading-5 text-amber-800">
                  İşlem tamamlandığında geçici şifre yalnızca bir kez
                  gösterilecektir.
                </p>
              </div>
              <div className="flex justify-end gap-2 border-t border-slate-200 px-5 py-4">
                <Button
                  type="button"
                  variant="ghost"
                  disabled={saving}
                  onClick={() => {
                    setResetUser(null);
                    setResetPassword("");
                    setError("");
                  }}
                >
                  Vazgeç
                </Button>
                <Button type="submit" loading={saving}>
                  Şifreyi sıfırla
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}
      {pendingToggle && (
        <ConfirmDialog
          open
          title={
            pendingToggle.isActive
              ? "Kullanıcıyı Pasife Al"
              : "Kullanıcıyı Aktifleştir"
          }
          description={
            pendingToggle.isActive
              ? `${pendingToggle.fullName} (${pendingToggle.username}) pasife alınacak ve sisteme GİRİŞ YAPAMAYACAK. Açık oturumu varsa bir sonraki istekte kesilir.`
              : `${pendingToggle.fullName} (${pendingToggle.username}) aktifleştirilecek ve mevcut rolleriyle sisteme girebilecek.`
          }
          confirmLabel={
            pendingToggle.isActive ? "Pasife Al" : "Aktifleştir"
          }
          busy={saving}
          error={error}
          onCancel={() => setPendingToggle(null)}
          onConfirm={() => void toggleUserStatus(pendingToggle)}
        />
      )}
    </ErpShell>
  );
}
