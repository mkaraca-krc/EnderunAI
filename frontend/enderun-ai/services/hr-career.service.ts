import { apiClient } from "@/lib/api/api-client";

export type CareerMovementKind =
  | "hire"
  | "promotion"
  | "position-change"
  | "department-change"
  | "salary-change"
  | "project-change"
  | "terminate";

export type CareerMovement = {
  id: string;
  personnelId: string;
  personnelName?: string | null;
  employeeNumber?: string | null;
  movementType?: number | string | null;
  movementTypeName?: string | null;
  type?: number | string | null;
  typeName?: string | null;
  effectiveDate?: string | null;
  movementDate?: string | null;
  date?: string | null;
  oldCompanyId?: string | null;
  oldCompanyName?: string | null;
  newCompanyId?: string | null;
  newCompanyName?: string | null;
  oldBranchId?: string | null;
  oldBranchName?: string | null;
  newBranchId?: string | null;
  newBranchName?: string | null;
  oldDepartmentId?: string | null;
  oldDepartmentName?: string | null;
  newDepartmentId?: string | null;
  newDepartmentName?: string | null;
  oldPositionId?: string | null;
  oldPositionName?: string | null;
  newPositionId?: string | null;
  newPositionName?: string | null;
  oldProjectId?: string | null;
  oldProjectName?: string | null;
  newProjectId?: string | null;
  newProjectName?: string | null;
  oldSalary?: number | null;
  newSalary?: number | null;
  role?: string | null;
  reason?: string | null;
  description?: string | null;
  notes?: string | null;
  createdAt?: string | null;
  createdAtUtc?: string | null;
  [key: string]: unknown;
};

export type CareerAnalysis = {
  personnelId?: string;
  personnelName?: string | null;
  totalMovements?: number;
  promotionCount?: number;
  departmentChangeCount?: number;
  positionChangeCount?: number;
  projectChangeCount?: number;
  salaryChangeCount?: number;
  currentDepartmentName?: string | null;
  currentPositionName?: string | null;
  currentProjectName?: string | null;
  currentSalary?: number | null;
  lastPromotionDate?: string | null;
  nextPromotionCandidate?: boolean;
  promotionReadinessScore?: number | null;
  careerSummary?: string | null;
  recommendation?: string | null;
  recommendations?: string[] | null;
  [key: string]: unknown;
};

export type CareerMovementPayload = Record<string, unknown>;

const root = "hr/career";

function arrayFromResponse<T>(response: unknown): T[] {
  if (Array.isArray(response)) {
    return response as T[];
  }

  if (response && typeof response === "object") {
    const record = response as Record<string, unknown>;
    const possibleRows = [
      record.items,
      record.data,
      record.results,
      record.movements,
      record.history,
    ];
    const rows = possibleRows.find(Array.isArray);
    return (rows ?? []) as T[];
  }

  return [];
}

export const hrCareerService = {
  async getAll() {
    const response = await apiClient<unknown>(root);
    return arrayFromResponse<CareerMovement>(response);
  },

  async getPersonnelHistory(personnelId: string) {
    const response = await apiClient<unknown>(
      `${root}/personnel/${personnelId}`
    );
    return arrayFromResponse<CareerMovement>(response);
  },

  getPersonnelAnalysis(personnelId: string) {
    return apiClient<CareerAnalysis>(`${root}/analysis/${personnelId}`);
  },

  create(kind: CareerMovementKind, payload: CareerMovementPayload) {
    return apiClient<{ message?: string; id?: string }>(`${root}/${kind}`, {
      method: "POST",
      body: payload,
    });
  },
};
