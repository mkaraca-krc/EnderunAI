"use client";

import {
  Fragment,
  FormEvent,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";
import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import { Button, Input, Select } from "@/components/ui";
import { ApiError } from "@/lib/api/api-client";
import {
  permissionMatrixService,
  type PermissionDefinition,
  type PermissionMatrix,
} from "@/services/user-management.service";

const SITE_ONLY_POLICY = 1;

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) {
    return error.message;
  }
  return "İşlem tamamlanamadı. Lütfen tekrar deneyin.";
}

export default function PermissionMatrixPage() {
  /**
   * Düğme -> uç -> izin (PermissionMatrixController):
   *   POST  user-management/permission-matrix/toggle -> user-management.edit
   *   PATCH .../roles/{id}/scope-policy              -> user-management.edit
   *   POST  .../roles                                -> user-management.create
   *
   * MATRİS HÜCRELERİ GİZLENMİYOR, PASİFLEŞTİRİLİYOR. Matris bir
   * TABLO: hücreyi kaldırmak satırı bozar ve okuma yetkisi olan
   * kullanıcı mevcut yetki dağılımını göremez hale gelir. Okuma
   * korunuyor, yazma kapanıyor.
   */
  const actions = useModuleActions("user-management");

  const [matrix, setMatrix] = useState<PermissionMatrix | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [pendingCell, setPendingCell] = useState<string | null>(null);
  const [newRoleOpen, setNewRoleOpen] = useState(false);
  const [newRoleName, setNewRoleName] = useState("");
  const [newRoleDescription, setNewRoleDescription] = useState("");
  const [copyFromRole, setCopyFromRole] = useState("");
  const [savingRole, setSavingRole] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const result = await permissionMatrixService.get();
      setMatrix(result);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(""), 3000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const grantSet = useMemo(
    () =>
      new Set(
        (matrix?.grants ?? []).map(
          (grant) => `${grant.roleId}::${grant.permissionKey}`
        )
      ),
    [matrix]
  );

  const permissionGroups = useMemo(() => {
    const groups = new Map<string, PermissionDefinition[]>();
    for (const permission of matrix?.permissions ?? []) {
      const current = groups.get(permission.module) ?? [];
      current.push(permission);
      groups.set(permission.module, current);
    }
    return [...groups.entries()];
  }, [matrix]);

  async function toggleCell(roleId: string, permissionKey: string, roleName: string) {
    if (!matrix) return;
    if (roleName === "Admin") {
      setError("Admin rolünün yetkileri sabittir, değiştirilemez.");
      return;
    }

    const cellKey = `${roleId}::${permissionKey}`;
    const currentlyGranted = grantSet.has(cellKey);
    const nextGranted = !currentlyGranted;

    setPendingCell(cellKey);
    setError("");

    // İyimser güncelleme
    setMatrix((current) => {
      if (!current) return current;
      return {
        ...current,
        grants: nextGranted
          ? [...current.grants, { roleId, permissionKey }]
          : current.grants.filter(
              (grant) =>
                !(grant.roleId === roleId && grant.permissionKey === permissionKey)
            ),
      };
    });

    try {
      await permissionMatrixService.toggle(roleId, permissionKey, nextGranted);
    } catch (requestError) {
      // geri al
      setMatrix((current) => {
        if (!current) return current;
        return {
          ...current,
          grants: currentlyGranted
            ? [...current.grants, { roleId, permissionKey }]
            : current.grants.filter(
                (grant) =>
                  !(grant.roleId === roleId && grant.permissionKey === permissionKey)
              ),
        };
      });
      setError(getErrorMessage(requestError));
    } finally {
      setPendingCell(null);
    }
  }

  async function toggleScopePolicy(roleId: string, currentPolicy: number) {
    const next = currentPolicy === SITE_ONLY_POLICY ? 0 : SITE_ONLY_POLICY;

    setMatrix((current) => {
      if (!current) return current;
      return {
        ...current,
        roles: current.roles.map((role) =>
          role.id === roleId ? { ...role, dataScopePolicy: next } : role
        ),
      };
    });

    try {
      await permissionMatrixService.updateScopePolicy(roleId, next);
      setNotice("Veri kapsamı güncellendi.");
    } catch (requestError) {
      setError(getErrorMessage(requestError));
      await load();
    }
  }

  async function submitNewRole(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!newRoleName.trim()) {
      setError("Rol adı zorunludur.");
      return;
    }

    setSavingRole(true);
    setError("");

    try {
      const result = await permissionMatrixService.createRole(
        newRoleName.trim(),
        newRoleDescription.trim() || undefined,
        copyFromRole || undefined
      );
      setNotice(result.message);
      setNewRoleOpen(false);
      setNewRoleName("");
      setNewRoleDescription("");
      setCopyFromRole("");
      await load();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setSavingRole(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Yetki Matrisi"
      description="Rol × izin tablosu — bir hücreye tıklayın, anında kaydedilir"
    >
      <div className="space-y-4">
        {/* Matris hücreleri anında kaydediliyor; başka yöneticinin değişikliği tazelenmeden görünmüyordu. */}
        <div className="flex justify-end">
          <Button variant="secondary" onClick={() => void load()}>Yenile</Button>
        </div>

        {error && (
          <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}
        {notice && (
          <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
            {notice}
          </div>
        )}

        <div className="flex items-center justify-between">
          <p className="text-sm text-slate-500">
            Satırlar modül + işlem, sütunlar roller. Veri kapsamı satırı, o
            role sahip kullanıcıların Kullanıcı Yönetimi'nde şantiye seçmesini
            zorunlu kılar.
          </p>
          {actions.can("create") && (
            <Button onClick={() => setNewRoleOpen(true)}>+ Rol Ekle/Kopyala</Button>
          )}
        </div>

        {loading ? (
          <div className="rounded-xl border border-slate-200 bg-white py-16 text-center text-sm text-slate-500">
            Yetki matrisi yükleniyor...
          </div>
        ) : !matrix ? null : (
          <div className="overflow-auto rounded-xl border border-slate-200 bg-white">
            <table className="w-full border-collapse text-sm">
              <thead>
                <tr>
                  <th className="sticky left-0 top-0 z-20 min-w-[280px] border-b border-r border-slate-200 bg-slate-50 p-3 text-left">
                    İzin
                  </th>
                  {matrix.roles.map((role) => (
                    <th
                      key={role.id}
                      className="sticky top-0 z-10 min-w-[110px] border-b border-slate-200 bg-slate-50 p-2 text-center align-bottom"
                    >
                      <div className="flex flex-col items-center gap-1">
                        <span className="text-xs font-semibold text-slate-800">
                          {role.name}
                        </span>
                        <button
                          type="button"
                          disabled={role.name === "Admin" || !actions.can("edit")}
                          onClick={() =>
                            void toggleScopePolicy(role.id, role.dataScopePolicy)
                          }
                          className={[
                            "rounded-full px-2 py-0.5 text-[10px] font-medium",
                            role.dataScopePolicy === SITE_ONLY_POLICY
                              ? "bg-amber-100 text-amber-800"
                              : "bg-slate-100 text-slate-500",
                            role.name === "Admin" ? "opacity-50" : "cursor-pointer",
                          ].join(" ")}
                          title="Veri kapsamını değiştir"
                        >
                          {role.dataScopePolicy === SITE_ONLY_POLICY
                            ? "Sadece şantiye"
                            : "Tümü"}
                        </button>
                      </div>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {permissionGroups.map(([module, permissions]) => (
                  <Fragment key={module}>
                    <tr>
                      <td
                        colSpan={matrix.roles.length + 1}
                        className="sticky left-0 z-10 border-b border-slate-200 bg-slate-100 px-3 py-1.5 text-xs font-semibold uppercase tracking-wide text-slate-600"
                      >
                        {module}
                      </td>
                    </tr>
                    {permissions.map((permission) => (
                      <tr key={permission.key} className="hover:bg-slate-50">
                        <td className="sticky left-0 z-10 min-w-[280px] border-b border-r border-slate-100 bg-white p-2.5">
                          <strong className="block text-xs text-slate-900">
                            {permission.name}
                          </strong>
                          <span className="mt-0.5 block text-[11px] leading-4 text-slate-400">
                            {permission.description}
                          </span>
                        </td>
                        {matrix.roles.map((role) => {
                          const cellKey = `${role.id}::${permission.key}`;
                          const granted =
                            role.name === "Admin" || grantSet.has(cellKey);
                          const isPending = pendingCell === cellKey;

                          return (
                            <td
                              key={cellKey}
                              className="border-b border-slate-100 p-2 text-center"
                            >
                              <button
                                type="button"
                                disabled={
                                  role.name === "Admin" ||
                                  isPending ||
                                  !actions.can("edit")
                                }
                                onClick={() =>
                                  void toggleCell(
                                    role.id,
                                    permission.key,
                                    role.name
                                  )
                                }
                                className={[
                                  "inline-flex h-6 w-6 items-center justify-center rounded-md border transition",
                                  granted
                                    ? "border-emerald-500 bg-emerald-500 text-white"
                                    : "border-slate-300 bg-white text-transparent hover:border-slate-400",
                                  role.name === "Admin"
                                    ? "cursor-not-allowed opacity-60"
                                    : "cursor-pointer",
                                  isPending ? "animate-pulse" : "",
                                ].join(" ")}
                              >
                                ✓
                              </button>
                            </td>
                          );
                        })}
                      </tr>
                    ))}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {newRoleOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/55 p-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          onMouseDown={(event) => {
            if (event.currentTarget === event.target && !savingRole) {
              setNewRoleOpen(false);
            }
          }}
        >
          <div className="w-full max-w-md rounded-2xl bg-white shadow-2xl">
            <div className="border-b border-slate-200 px-5 py-4">
              <h2 className="text-lg font-semibold text-slate-950">
                Yeni Rol Ekle
              </h2>
            </div>
            <form onSubmit={submitNewRole}>
              <div className="space-y-4 p-5">
                <Input
                  label="Rol adı"
                  required
                  value={newRoleName}
                  onChange={(event) => setNewRoleName(event.target.value)}
                />
                <Input
                  label="Açıklama (isteğe bağlı)"
                  value={newRoleDescription}
                  onChange={(event) => setNewRoleDescription(event.target.value)}
                />
                <Select
                  label="İzinleri kopyala (isteğe bağlı)"
                  value={copyFromRole}
                  options={[
                    { value: "", label: "Boş rol oluştur" },
                    ...(matrix?.roles ?? []).map((role) => ({
                      value: role.name,
                      label: role.name,
                    })),
                  ]}
                  onChange={(event) => setCopyFromRole(event.target.value)}
                />
              </div>
              <div className="flex justify-end gap-2 border-t border-slate-200 px-5 py-4">
                <Button
                  type="button"
                  variant="ghost"
                  disabled={savingRole}
                  onClick={() => setNewRoleOpen(false)}
                >
                  Vazgeç
                </Button>
                {actions.can("create") && (
                  <Button type="submit" loading={savingRole}>
                    Rolü oluştur
                  </Button>
                )}
              </div>
            </form>
          </div>
        </div>
      )}
    </ErpShell>
  );
}
