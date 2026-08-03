import { apiClient } from "@/lib/api/api-client";

export type PermissionDefinition = {
  key: string;
  module: string;
  name: string;
  description: string;
};

export type RoleSummary = {
  name: string;
  description?: string | null;
  dataScopePolicy: number; // 0 = Tümü, 1 = Sadece atandığı şantiyeler
};

export type SiteSummary = {
  id: string;
  code: string;
  name: string;
  projectCode: string;
  projectName: string;
};

export type UserManagementCatalog = {
  permissions: PermissionDefinition[];
  roles: RoleSummary[];
  sites: SiteSummary[];
};

export type ManagedUserProjectSite = {
  id: string;
  code: string;
  name: string;
};

export type ManagedUser = {
  id: string;
  username: string;
  fullName: string;
  /** Kullanıcının seçtiği hitap: "Bey" | "Hanım" | null. */
  honorific?: string | null;
  email?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  lastLoginAtUtc?: string | null;
  workHoursExempt: boolean;
  roleNames: string[];
  roleName: string;
  projectSiteIds: string[];
  projectSites: ManagedUserProjectSite[];
  allowedPermissions: string[];
  deniedPermissions: string[];
  effectivePermissions: string[];
};

export type ManagedUserPayload = {
  username: string;
  fullName: string;
  honorific?: string | null;
  email?: string | null;
  roleNames: string[];
  isActive: boolean;
  allowedPermissions: string[];
  deniedPermissions: string[];
  projectSiteIds: string[];
  password?: string;
  workHoursExempt: boolean;
};

export type ManagedUserResult = {
  message: string;
  temporaryPassword?: string;
  user: ManagedUser;
};

export type PasswordResetResult = {
  message: string;
  temporaryPassword: string;
};

export type PermissionMatrixGrant = {
  roleId: string;
  permissionKey: string;
};

export type PermissionMatrixRole = {
  id: string;
  name: string;
  description?: string | null;
  dataScopePolicy: number;
};

export type PermissionMatrix = {
  permissions: PermissionDefinition[];
  roles: PermissionMatrixRole[];
  grants: PermissionMatrixGrant[];
};

const root = "user-management";

export const userManagementService = {
  getCatalog() {
    return apiClient<UserManagementCatalog>(`${root}/catalog`);
  },
  getUsers() {
    return apiClient<ManagedUser[]>(`${root}/users`);
  },
  createUser(payload: ManagedUserPayload) {
    return apiClient<ManagedUserResult>(`${root}/users`, {
      method: "POST",
      body: payload,
    });
  },
  updateUser(id: string, payload: ManagedUserPayload) {
    return apiClient<ManagedUserResult>(`${root}/users/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
  resetPassword(id: string, newPassword?: string) {
    return apiClient<PasswordResetResult>(
      `${root}/users/${id}/reset-password`,
      {
        method: "POST",
        body: {
          newPassword: newPassword?.trim() || null,
        },
      }
    );
  },
};

export const permissionMatrixService = {
  get() {
    return apiClient<PermissionMatrix>(`${root}/permission-matrix`);
  },
  toggle(roleId: string, permissionKey: string, granted: boolean) {
    return apiClient<{ message: string }>(`${root}/permission-matrix/toggle`, {
      method: "POST",
      body: { roleId, permissionKey, granted },
    });
  },
  updateScopePolicy(roleId: string, dataScopePolicy: number) {
    return apiClient<{ message: string }>(
      `${root}/permission-matrix/roles/${roleId}/scope-policy`,
      {
        method: "PATCH",
        body: { dataScopePolicy },
      }
    );
  },
  createRole(name: string, description?: string, copyFromRoleName?: string) {
    return apiClient<{ message: string; id: string }>(
      `${root}/permission-matrix/roles`,
      {
        method: "POST",
        body: { name, description, copyFromRoleName },
      }
    );
  },
};
