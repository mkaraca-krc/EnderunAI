"use client";

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
  userManagementService,
  type ManagedUser,
  type ManagedUserPayload,
  type PermissionDefinition,
  type UserManagementCatalog,
} from "@/services/user-management.service";

type UserForm = {
  username: string;
  fullName: string;
  email: string;
  roleName: string;
  password: string;
  isActive: boolean;
  selectedPermissions: string[];
};

type CredentialNotice = {
  title: string;
  username: string;
  password: string;
} | null;

const emptyForm: UserForm = {
  username: "",
  fullName: "",
  email: "",
  roleName: "",
  password: "",
  isActive: true,
  selectedPermissions: [],
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
    : date.toLocaleString("tr-TR", {
        dateStyle: "short",
        timeStyle: "short",
      });
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
  const [catalog, setCatalog] = useState<UserManagementCatalog | null>(null);
  const [users, setUsers] = useState<ManagedUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
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

  const presetByName = useMemo(
    () =>
      new Map(
        (catalog?.rolePresets ?? []).map((preset) => [preset.name, preset])
      ),
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
      if (roleFilter !== "all" && user.roleName !== roleFilter) {
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

      return [user.fullName, user.username, user.email, user.roleName].some(
        (value) => normalized(value).includes(term)
      );
    });
  }, [roleFilter, search, statusFilter, users]);

  const summary = useMemo(
    () => ({
      total: users.length,
      active: users.filter((user) => user.isActive).length,
      admins: users.filter((user) =>
        ["Admin", "Genel Müdür"].includes(user.roleName)
      ).length,
      neverLoggedIn: users.filter((user) => !user.lastLoginAtUtc).length,
    }),
    [users]
  );

  const selectedPreset = presetByName.get(form.roleName);
  const selectedPermissionSet = useMemo(
    () => new Set(form.selectedPermissions),
    [form.selectedPermissions]
  );
  const presetPermissionSet = useMemo(
    () => new Set(selectedPreset?.permissions ?? []),
    [selectedPreset]
  );

  const customAddedCount = form.selectedPermissions.filter(
    (permission) => !presetPermissionSet.has(permission)
  ).length;
  const customDeniedCount = [...presetPermissionSet].filter(
    (permission) => !selectedPermissionSet.has(permission)
  ).length;

  function chooseRole(roleName: string) {
    const preset = presetByName.get(roleName);
    setForm((current) => ({
      ...current,
      roleName,
      selectedPermissions: [...(preset?.permissions ?? [])],
    }));
  }

  function openCreate() {
    const defaultRole =
      catalog?.rolePresets.find((preset) => preset.name === "Tekniker") ??
      catalog?.rolePresets[0];

    setEditingUser(null);
    setForm({
      ...emptyForm,
      roleName: defaultRole?.name ?? "",
      selectedPermissions: [...(defaultRole?.permissions ?? [])],
    });
    setError("");
    setEditorOpen(true);
  }

  function openEdit(user: ManagedUser) {
    setEditingUser(user);
    setForm({
      username: user.username,
      fullName: user.fullName,
      email: user.email ?? "",
      roleName: user.roleName,
      password: "",
      isActive: user.isActive,
      selectedPermissions: [...user.effectivePermissions],
    });
    setError("");
    setEditorOpen(true);
  }

  function togglePermission(permission: string) {
    setForm((current) => ({
      ...current,
      selectedPermissions: current.selectedPermissions.includes(permission)
        ? current.selectedPermissions.filter((item) => item !== permission)
        : [...current.selectedPermissions, permission],
    }));
  }

  function resetToPreset() {
    setForm((current) => ({
      ...current,
      selectedPermissions: [...(selectedPreset?.permissions ?? [])],
    }));
  }

  function buildPayload(): ManagedUserPayload {
    const basePermissions = new Set(selectedPreset?.permissions ?? []);
    const selected = new Set(form.selectedPermissions);

    return {
      username: form.username.trim(),
      fullName: form.fullName.trim(),
      email: form.email.trim() || null,
      roleName: form.roleName,
      isActive: form.isActive,
      allowedPermissions: [...selected].filter(
        (permission) => !basePermissions.has(permission)
      ),
      deniedPermissions: [...basePermissions].filter(
        (permission) => !selected.has(permission)
      ),
      ...(editingUser || !form.password.trim()
        ? {}
        : { password: form.password.trim() }),
    };
  }

  async function submitUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!form.roleName) {
      setError("Görev rolü seçilmelidir.");
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

  async function toggleUserStatus(user: ManagedUser) {
    const action = user.isActive ? "pasife almak" : "aktifleştirmek";
    if (
      !window.confirm(
        `${user.fullName} kullanıcısını ${action} istediğinize emin misiniz?`
      )
    ) {
      return;
    }

    setSaving(true);
    setError("");

    try {
      const result = await userManagementService.updateUser(user.id, {
        username: user.username,
        fullName: user.fullName,
        email: user.email,
        roleName: user.roleName,
        isActive: !user.isActive,
        allowedPermissions: user.allowedPermissions,
        deniedPermissions: user.deniedPermissions,
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
    ...(catalog?.rolePresets ?? []).map((preset) => ({
      value: preset.name,
      label: preset.name,
    })),
  ];

  return (
    <ErpShell
      title="Kullanıcılar ve Yetkiler"
      description="Kullanıcı hesabı, görev rolü, erişim kısıtları ve şifre yönetimi"
    >
      <div className="space-y-6">
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
            description="Görev rolü seçildiğinde kısıtlamalar otomatik uygulanır"
            action={<Button onClick={openCreate}>+ Yeni Kullanıcı</Button>}
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
                      <TableHead>Görev rolü</TableHead>
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
                            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-slate-900 text-sm font-semibold text-white">
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
                          <Badge
                            variant={
                              ["Admin", "Genel Müdür"].includes(user.roleName)
                                ? "info"
                                : "default"
                            }
                          >
                            {user.roleName}
                          </Badge>
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
                          <Badge variant={user.isActive ? "success" : "danger"}>
                            {user.isActive ? "Aktif" : "Pasif"}
                          </Badge>
                        </TableCell>
                        <TableCell>
                          <div className="flex justify-end gap-1">
                            <Button
                              size="sm"
                              variant="secondary"
                              onClick={() => openEdit(user)}
                            >
                              Düzenle
                            </Button>
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
                            <Button
                              size="sm"
                              variant="ghost"
                              disabled={saving}
                              className={
                                user.isActive
                                  ? "text-red-600 hover:bg-red-50"
                                  : "text-emerald-700 hover:bg-emerald-50"
                              }
                              onClick={() => void toggleUserStatus(user)}
                            >
                              {user.isActive ? "Pasife al" : "Aktifleştir"}
                            </Button>
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
            title="Otomatik Rol Şablonları"
            description="Kullanıcı oluştururken görevi seçmeniz yeterlidir"
          />
          <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {(catalog?.rolePresets ?? []).map((preset) => (
              <article
                key={preset.name}
                className="rounded-xl border border-slate-200 p-4"
              >
                <div className="flex items-center justify-between gap-3">
                  <strong className="text-sm text-slate-950">
                    {preset.name}
                  </strong>
                  <Badge>{preset.permissions.length} izin</Badge>
                </div>
                <p className="mt-2 text-xs leading-5 text-slate-500">
                  {preset.description}
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
                  Görev rolü varsayılan izinleri otomatik getirir; aşağıdan
                  kullanıcıya özel değişiklik yapabilirsiniz.
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
                    <h3 className="font-semibold text-slate-950">Görev Rolü</h3>
                    <div className="mt-4">
                      <Select
                        label="Otomatik yetki şablonu"
                        required
                        value={form.roleName}
                        options={(catalog?.rolePresets ?? []).map((preset) => ({
                          value: preset.name,
                          label: preset.name,
                        }))}
                        placeholder="Görev seçin"
                        onChange={(event) => chooseRole(event.target.value)}
                      />
                    </div>
                    {selectedPreset && (
                      <div className="mt-3 rounded-lg bg-slate-50 p-3">
                        <p className="text-xs leading-5 text-slate-600">
                          {selectedPreset.description}
                        </p>
                        <div className="mt-2 flex flex-wrap gap-1.5">
                          <Badge variant="success">
                            {selectedPreset.permissions.length} varsayılan
                          </Badge>
                          {customAddedCount > 0 && (
                            <Badge variant="info">
                              +{customAddedCount} ek izin
                            </Badge>
                          )}
                          {customDeniedCount > 0 && (
                            <Badge variant="warning">
                              -{customDeniedCount} kısıtlama
                            </Badge>
                          )}
                        </div>
                      </div>
                    )}
                  </div>

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
                </div>

                <div className="rounded-xl border border-slate-200">
                  <div className="flex flex-col gap-3 border-b border-slate-200 px-4 py-4 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                      <h3 className="font-semibold text-slate-950">
                        Yetki Matrisi
                      </h3>
                      <p className="mt-1 text-xs text-slate-500">
                        İşaretli alanlar kullanıcının erişebileceği işlemlerdir.
                      </p>
                    </div>
                    <Button
                      type="button"
                      size="sm"
                      variant="secondary"
                      onClick={resetToPreset}
                    >
                      Rol varsayılanına dön
                    </Button>
                  </div>

                  <div className="max-h-[62vh] space-y-5 overflow-y-auto p-4">
                    {permissionGroups.map(([module, permissions]) => (
                      <section key={module}>
                        <div className="mb-2 flex items-center justify-between">
                          <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                            {module}
                          </h4>
                          <span className="text-xs text-slate-400">
                            {
                              permissions.filter((permission) =>
                                selectedPermissionSet.has(permission.key)
                              ).length
                            }
                            /{permissions.length}
                          </span>
                        </div>
                        <div className="grid gap-2 xl:grid-cols-2">
                          {permissions.map((permission) => {
                            const checked = selectedPermissionSet.has(
                              permission.key
                            );
                            const isPreset =
                              presetPermissionSet.has(permission.key);
                            const customAdded = checked && !isPreset;
                            const customDenied = !checked && isPreset;

                            return (
                              <label
                                key={permission.key}
                                className={[
                                  "flex cursor-pointer items-start gap-3 rounded-xl border p-3 transition",
                                  checked
                                    ? "border-emerald-200 bg-emerald-50"
                                    : "border-slate-200 bg-white hover:bg-slate-50",
                                ].join(" ")}
                              >
                                <input
                                  type="checkbox"
                                  checked={checked}
                                  onChange={() =>
                                    togglePermission(permission.key)
                                  }
                                  className="mt-0.5 h-4 w-4 rounded border-slate-300"
                                />
                                <span className="min-w-0">
                                  <span className="flex flex-wrap items-center gap-1.5">
                                    <strong className="text-sm text-slate-900">
                                      {permission.name}
                                    </strong>
                                    {customAdded && (
                                      <Badge variant="info">Özel ek</Badge>
                                    )}
                                    {customDenied && (
                                      <Badge variant="warning">Kısıtlandı</Badge>
                                    )}
                                  </span>
                                  <span className="mt-1 block text-xs leading-5 text-slate-500">
                                    {permission.description}
                                  </span>
                                </span>
                              </label>
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
    </ErpShell>
  );
}
