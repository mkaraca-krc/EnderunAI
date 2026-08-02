import { apiClient } from "@/lib/api/api-client";

export type PermissionDefinition = {
  key: string;
  module: string;
  name: string;
  description: string;
};

export type RolePreset = {
  name: string;
  description: string;
  permissions: string[];
};

export type UserManagementCatalog = {
  permissions: PermissionDefinition[];
  rolePresets: RolePreset[];
};

export type ManagedUser = {
  id: string;
  username: string;
  fullName: string;
  email?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  lastLoginAtUtc?: string | null;
  roleName: string;
  allowedPermissions: string[];
  deniedPermissions: string[];
  effectivePermissions: string[];
};

export type ManagedUserPayload = {
  username: string;
  fullName: string;
  email?: string | null;
  roleName: string;
  isActive: boolean;
  allowedPermissions: string[];
  deniedPermissions: string[];
  password?: string;
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
