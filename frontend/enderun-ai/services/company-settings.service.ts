import { apiClient } from "@/lib/api/api-client";

export type CompanyBankAccount = {
  id: string;
  bankName: string;
  iban: string;
  accountHolder?: string | null;
  currencyCode?: string | null;
};

export type CompanySettings = {
  id: string;
  code: string;
  name: string;
  tradeName?: string | null;
  taxOffice?: string | null;
  taxNumber?: string | null;
  mersisNumber?: string | null;
  tradeRegistryNumber?: string | null;
  phone?: string | null;
  email?: string | null;
  website?: string | null;
  address?: string | null;
  logoUrl?: string | null;
  bankAccounts: CompanyBankAccount[];
};

export type UpdateCompanySettingsPayload = {
  name: string;
  tradeName?: string | null;
  taxOffice?: string | null;
  taxNumber?: string | null;
  mersisNumber?: string | null;
  tradeRegistryNumber?: string | null;
  phone?: string | null;
  email?: string | null;
  website?: string | null;
  address?: string | null;
};

export type RoleWorkHourWindowItem = {
  dayOfWeek: number;
  startTime: string;
  endTime: string;
};

export type RoleWorkHourWindows = {
  id: string;
  name: string;
  windows: RoleWorkHourWindowItem[];
};

const root = "company-settings";

export const companySettingsService = {
  get() {
    return apiClient<CompanySettings>(root);
  },
  update(payload: UpdateCompanySettingsPayload) {
    return apiClient<{ message: string; company: CompanySettings }>(root, {
      method: "PUT",
      body: payload,
    });
  },
  async uploadLogo(file: File) {
    const formData = new FormData();
    formData.append("file", file);

    const response = await fetch(`/api/backend/${root}/logo`, {
      method: "POST",
      credentials: "include",
      body: formData,
    });

    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      throw new Error(payload?.message ?? "Logo yüklenemedi.");
    }

    return payload as { message: string; logoUrl: string };
  },
  addBankAccount(payload: {
    bankName: string;
    iban: string;
    accountHolder?: string;
    currencyCode?: string;
  }) {
    return apiClient<{ message: string; id: string }>(
      `${root}/bank-accounts`,
      { method: "POST", body: payload }
    );
  },
  deleteBankAccount(id: string) {
    return apiClient<void>(`${root}/bank-accounts/${id}`, {
      method: "DELETE",
    });
  },
  getWorkHourWindows() {
    return apiClient<RoleWorkHourWindows[]>(`${root}/work-hour-windows`);
  },
  updateWorkHourWindows(roleId: string, windows: RoleWorkHourWindowItem[]) {
    return apiClient<{ message: string }>(
      `${root}/work-hour-windows/${roleId}`,
      { method: "PUT", body: { windows } }
    );
  },
};
