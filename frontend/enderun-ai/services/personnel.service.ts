import { apiClient } from "@/lib/api/api-client";

export type PersonnelAssignmentItem = {
  id: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  role?: string | null;
  startDate: string;
  endDate?: string | null;
  isPrimaryAssignment: boolean;
  isActive?: boolean;
};

export type PersonnelActiveSiteAssignment = {
  id: string;
  projectSiteId: string;
  siteCode: string;
  siteName: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  role?: string | null;
  startDate: string;
};

export type PersonnelListItem = {
  id: string;
  companyId: string;
  companyName: string;
  branchId?: string | null;
  branchName?: string | null;
  employeeNumber: string;
  firstName: string;
  lastName: string;
  fullName: string;
  identityNumber?: string | null;
  phone?: string | null;
  email?: string | null;
  jobTitle?: string | null;
  profession?: string | null;
  employmentStartDate?: string | null;
  /** Fazla mesai muvafakatinin geçerli olduğu yıl. Boşsa alınmamış. */
  overtimeConsentYear?: number | null;
  overtimeConsentDate?: string | null;
  employmentEndDate?: string | null;
  monthlySalary?: number | null;
  status: number;
  isActive: boolean;
  /** 0 = Atanmadı, 1 = Merkez, 2 = Şantiye. */
  workLocationType: number;
  /**
   * Görev yeri belirlenmemiş VEYA şantiye seçilip aktif ataması
   * olmayan personel. Şantiye seçili olması tek başına yetmez.
   */
  isAwaitingWorkLocation: boolean;
  activeAssignments: PersonnelAssignmentItem[];
  activeSiteAssignment?: PersonnelActiveSiteAssignment | null;
};

export const WorkLocationType = {
  Unassigned: 0,
  HeadOffice: 1,
  ProjectSite: 2,
} as const;

export const WORK_LOCATION_LABELS: Record<number, string> = {
  0: "Atanmadı",
  1: "Merkez",
  2: "Şantiye",
};

export type SetWorkLocationRequest = {
  workLocationType: number;
  projectSiteId?: string | null;
  branchId?: string | null;
  startDate?: string | null;
  role?: string | null;
  notes?: string | null;
};

export type PersonnelDetail = PersonnelListItem & {
  birthDate?: string | null;
  address?: string | null;
  sgkRegistrationNumber?: string | null;
  assignments: PersonnelAssignmentItem[];
};

export type CreatePersonnelRequest = {
  companyId: string;
  branchId?: string | null;
  employeeNumber: string;
  firstName: string;
  lastName: string;
  identityNumber?: string | null;
  birthDate?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  jobTitle?: string | null;
  profession?: string | null;
  sgkRegistrationNumber?: string | null;
  employmentStartDate?: string | null;
  /** Fazla mesai muvafakatinin geçerli olduğu yıl. Boşsa alınmamış. */
  overtimeConsentYear?: number | null;
  overtimeConsentDate?: string | null;
  monthlySalary?: number | null;
};

export type UpdatePersonnelRequest = {
  branchId?: string | null;
  firstName: string;
  lastName: string;
  identityNumber?: string | null;
  birthDate?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  jobTitle?: string | null;
  profession?: string | null;
  sgkRegistrationNumber?: string | null;
  employmentStartDate?: string | null;
  /** Fazla mesai muvafakatinin geçerli olduğu yıl. Boşsa alınmamış. */
  overtimeConsentYear?: number | null;
  overtimeConsentDate?: string | null;
  employmentEndDate?: string | null;
  monthlySalary?: number | null;
  status: number;
  isActive: boolean;
};

export type AssignPersonnelRequest = {
  projectId: string;
  startDate: string;
  endDate?: string | null;
  role?: string | null;
  notes?: string | null;
  isPrimaryAssignment: boolean;
};

/** Eksik alanın hangi süreci engellediği. */
export const DATA_SEVERITY = {
  PayrollBlocking: 0,
  OfficialBlocking: 1,
  Operational: 2,
} as const;

export type PersonnelDataIssue = {
  field: string;
  label: string;
  severity: number;
  severityName: string;
  reason: string;
};

export type PersonnelDataCompleteness = {
  personnelId: string;
  employeeNumber: string;
  fullName: string;
  issues: PersonnelDataIssue[];
  payrollReady: boolean;
  officialReady: boolean;
  completionRate: number;
};

export type PersonnelDataCompletenessSummary = {
  total: number;
  payrollReadyCount: number;
  officialReadyCount: number;
  completeCount: number;
  byField: Record<string, number>;
  items: PersonnelDataCompleteness[];
};

/** Gönderilmeyen alan değiştirilmez; bu uç alan doldurmak için. */
export type CompletePersonnelDataRequest = {
  identityNumber?: string | null;
  sgkRegistrationNumber?: string | null;
  phone?: string | null;
  jobTitle?: string | null;
  birthDate?: string | null;
  employmentStartDate?: string | null;
  /** Fazla mesai muvafakatinin geçerli olduğu yıl. Boşsa alınmamış. */
  overtimeConsentYear?: number | null;
  overtimeConsentDate?: string | null;
  branchId?: string | null;
};

export const personnelService = {
  dataCompleteness(companyId?: string) {
    const suffix = companyId ? `?companyId=${companyId}` : "";

    return apiClient<PersonnelDataCompletenessSummary>(
      `hr/personnel/veri-eksikleri${suffix}`
    );
  },

  completeData(id: string, payload: CompletePersonnelDataRequest) {
    return apiClient<{ message: string; filledFields: string[] }>(
      `hr/personnel/${id}/veri-tamamla`,
      { method: "PUT", body: payload }
    );
  },

  getAll(params?: {
    companyId?: string;
    projectId?: string;
    search?: string;
  }) {
    const query = new URLSearchParams();

    if (params?.companyId) {
      query.set("companyId", params.companyId);
    }

    if (params?.projectId) {
      query.set("projectId", params.projectId);
    }

    if (params?.search) {
      query.set("search", params.search);
    }

    const suffix = query.toString() ? `?${query.toString()}` : "";

    return apiClient<PersonnelListItem[]>(`hr/personnel${suffix}`);
  },

  getById(id: string) {
    return apiClient<PersonnelDetail>(`hr/personnel/${id}`);
  },

  create(payload: CreatePersonnelRequest) {
    return apiClient<{
      message: string;
      id: string;
      employeeNumber: string;
      firstName: string;
      lastName: string;
    }>("hr/personnel", {
      method: "POST",
      body: payload,
    });
  },

  update(id: string, payload: UpdatePersonnelRequest) {
    return apiClient<{ message: string }>(`hr/personnel/${id}`, {
      method: "PUT",
      body: payload,
    });
  },

  assignToProject(id: string, payload: AssignPersonnelRequest) {
    return apiClient<{ message: string; id: string }>(
      `hr/personnel/${id}/assignments`,
      {
        method: "POST",
        body: payload,
      }
    );
  },

  /**
   * Görev yeri belirleme. Şantiye seçilirse mevcut aktif atama
   * kapatılıp yenisi açılır; merkez/atanmadı seçilirse aktif atama
   * kapatılır.
   */
  setWorkLocation(id: string, payload: SetWorkLocationRequest) {
    return apiClient<{ message: string; workLocationType: number }>(
      `hr/personnel/${id}/gorev-yeri`,
      {
        method: "PUT",
        body: payload,
      }
    );
  },

  closeAssignment(assignmentId: string, endDate?: string) {
    const query = endDate
      ? `?endDate=${encodeURIComponent(endDate)}`
      : "";

    return apiClient<{ message: string }>(
      `hr/personnel/assignments/${assignmentId}/close${query}`,
      {
        method: "PUT",
      }
    );
  },
};
