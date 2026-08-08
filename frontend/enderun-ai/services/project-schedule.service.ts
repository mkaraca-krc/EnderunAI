import { apiClient } from "@/lib/api/api-client";

/** Bağımlılık türü — sahada en yaygını Bitir-Başla. */
export const DEPENDENCY_TYPE = {
  FinishToStart: 0,
  StartToStart: 1,
  FinishToFinish: 2,
  StartToFinish: 3,
} as const;

export const DEPENDENCY_TYPE_LABELS: Record<number, string> = {
  0: "Bitir-Başla",
  1: "Başla-Başla",
  2: "Bitir-Bitir",
  3: "Başla-Bitir",
};

export const DEPENDENCY_TYPE_HINTS: Record<number, string> = {
  0: "Öncül bitmeden ardıl başlamaz (en yaygın).",
  1: "Öncül başlamadan ardıl başlamaz.",
  2: "Öncül bitmeden ardıl bitemez.",
  3: "Öncül başlamadan ardıl bitemez.",
};

/** Haftanın çalışılan günleri — bayrak toplamı. */
export const WORK_WEEK = {
  MondayToFriday: 31,
  MondayToSaturday: 63,
  AllDays: 127,
} as const;

export const WORK_WEEK_LABELS: Record<number, string> = {
  31: "Pazartesi–Cuma",
  63: "Pazartesi–Cumartesi",
  127: "Takvim günü (her gün)",
};

export const SCHEDULE_STATUS = {
  Draft: 0,
  Active: 1,
  Archived: 2,
} as const;

export const SCHEDULE_STATUS_LABELS: Record<number, string> = {
  0: "Taslak",
  1: "Yürürlükte",
  2: "Arşivlendi",
};

/** Gerçekleşme yüzdesinin kaynağı — ekranda yazılır. */
export const PROGRESS_SOURCE = {
  None: 0,
  BoqItem: 1,
  Section: 2,
  Children: 3,
  Manual: 4,
} as const;

export const DELAY_PENALTY_KIND = {
  None: 0,
  RateOfContractPerDay: 1,
  FixedAmountPerDay: 2,
} as const;

export const DELAY_PENALTY_KIND_LABELS: Record<number, string> = {
  0: "Ceza yok",
  1: "Sözleşme bedelinin günlük oranı",
  2: "Günlük sabit tutar",
};

export const RESOURCE_KIND = {
  Personnel: 0,
  Subcontractor: 1,
} as const;

export type ScheduleResource = {
  id: string;
  kind: number;
  kindName: string;
  personnelId?: string | null;
  subcontractorContractId?: string | null;
  name: string;
  role?: string | null;
  notes?: string | null;
};

export type ScheduleActivity = {
  id: string;
  parentActivityId?: string | null;
  name: string;
  order: number;
  sectionId?: string | null;
  sectionName?: string | null;
  boqItemId?: string | null;
  boqItemCode?: string | null;
  boqItemDescription?: string | null;
  plannedStart: string;
  plannedEnd: string;
  baselineStart?: string | null;
  baselineEnd?: string | null;
  durationWorkDays: number;
  totalFloatWorkDays: number;
  isCritical: boolean;
  shiftedWorkDays: number;
  baselineSlipWorkDays?: number | null;
  manualProgressRate?: number | null;
  progressRate: number;
  progressSource: number;
  progressSourceName: string;
  employerRate?: number | null;
  expectedRate: number;
  forecastFinish?: string | null;
  slipWorkDays: number;
  projectImpactWorkDays: number;
  isBehind: boolean;
  isCompleted: boolean;
  forecastNote?: string | null;
  resources: ScheduleResource[];
  notes?: string | null;
};

export type ScheduleDependency = {
  id: string;
  predecessorActivityId: string;
  predecessorName: string;
  successorActivityId: string;
  successorName: string;
  type: number;
  typeName: string;
  lagWorkDays: number;
};

export type ProjectSchedule = {
  id: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  name: string;
  status: number;
  statusName: string;
  workWeek: number;
  workWeekName: string;
  holidays: string[];
  baselineRevisionNumber: number;
  baselineSetAtUtc?: string | null;
  projectStart?: string | null;
  projectFinish?: string | null;
  deadline?: string | null;
  hasContractDeadline: boolean;
  deadlineFloatWorkDays?: number | null;
  asOf: string;
  hasContractSummary: boolean;
  progressRate: number;
  employerRate?: number | null;
  forecastFinish?: string | null;
  delayWorkDays: number;
  drivingActivityIds: string[];
  activities: ScheduleActivity[];
  dependencies: ScheduleDependency[];
  criticalActivityIds: string[];
  warnings: string[];
};

export type ProjectScheduleResponse = {
  hasSchedule: boolean;
  sectionCount: number;
  message?: string;
  projectCode?: string;
  projectName?: string;
  schedule?: ProjectSchedule;
};

export type BaselineRevision = {
  id: string;
  revisionNumber: number;
  setAtUtc: string;
  setByUserId?: string | null;
  reason?: string | null;
  activityCount: number;
  plannedStartDate?: string | null;
  plannedEndDate?: string | null;
};

export type DelayPenaltyResult = {
  applicable: boolean;
  dailyAmount: number;
  rawAmount: number;
  amount: number;
  capApplied: boolean;
  note?: string | null;
};

export type DelayPenaltyView = {
  contractAmount?: number | null;
  currencyCode: string;
  contractDeadlineDate?: string | null;
  plannedEndDate?: string | null;
  hasContractDeadline: boolean;
  delayPenaltyKind: number;
  delayPenaltyValue: number;
  delayPenaltyCapRate?: number | null;
  deadline?: string | null;
  forecastFinish?: string | null;
  delayCalendarDays: number;
  penalty: DelayPenaltyResult;
  disclaimer: string;
};

export type ScheduleAlert = {
  projectId: string;
  projectCode: string;
  projectName: string;
  scheduleId: string;
  deadline?: string | null;
  hasContractDeadline: boolean;
  plannedFinish?: string | null;
  forecastFinish?: string | null;
  delayWorkDays: number;
  deadlineFloatWorkDays?: number | null;
  daysToDeadline?: number | null;
  criticalRiskCount: number;
  deadlineAtRisk: boolean;
  progressRate: number;
  penalty?: DelayPenaltyResult | null;
};

export type ScheduleAlertResponse = {
  horizonDays: number;
  showsPenalty: boolean;
  items: ScheduleAlert[];
};

export type ResourceConflict = {
  kind: number;
  resourceId: string;
  resourceName: string;
  firstActivityId: string;
  firstActivityName: string;
  secondActivityId: string;
  secondActivityName: string;
  overlapStart: string;
  overlapFinish: string;
  overlapWorkDays: number;
  bothCritical: boolean;
  severity: string;
};

export type ResourceSuggestions = {
  sectionId?: string | null;
  subcontractors: {
    id: string;
    contractNumber: string;
    workDescription: string;
    name: string;
    coversSection: boolean;
  }[];
  personnel: {
    id: string;
    employeeNumber: string;
    name: string;
    onThisProject: boolean;
  }[];
};

export type ActivityInput = {
  name: string;
  plannedStartDate: string;
  plannedEndDate: string;
  parentActivityId?: string | null;
  projectHakedisSectionId?: string | null;
  projectBoqItemId?: string | null;
  manualProgressRate?: number | null;
  order?: number | null;
  notes?: string | null;
};

export const projectScheduleService = {
  get: (projectId: string) =>
    apiClient<ProjectScheduleResponse>(`projects/${projectId}/is-programi`),

  create: (
    projectId: string,
    body: { name?: string; workWeek?: number; seedFromSections: boolean }
  ) =>
    apiClient<{ id: string; seededActivityCount: number; message: string }>(
      `projects/${projectId}/is-programi`,
      { method: "POST", body }
    ),

  update: (
    scheduleId: string,
    body: { name?: string; workWeek?: number; status?: number; notes?: string | null }
  ) =>
    apiClient<{ message: string }>(`is-programi/${scheduleId}`, {
      method: "PUT",
      body,
    }),

  seedFromSections: (scheduleId: string) =>
    apiClient<{ addedActivityCount: number; message: string }>(
      `is-programi/${scheduleId}/kisimlardan-olustur`,
      { method: "POST", body: {} }
    ),

  createActivity: (scheduleId: string, body: ActivityInput) =>
    apiClient<{ id: string; message: string }>(
      `is-programi/${scheduleId}/aktiviteler`,
      { method: "POST", body }
    ),

  updateActivity: (activityId: string, body: ActivityInput) =>
    apiClient<{ message: string }>(`is-programi/aktiviteler/${activityId}`, {
      method: "PUT",
      body,
    }),

  deleteActivity: (activityId: string) =>
    apiClient<{ message: string }>(`is-programi/aktiviteler/${activityId}`, {
      method: "DELETE",
    }),

  createDependency: (
    scheduleId: string,
    body: {
      predecessorActivityId: string;
      successorActivityId: string;
      type: number;
      lagWorkDays: number;
    }
  ) =>
    apiClient<{ id: string; message: string }>(
      `is-programi/${scheduleId}/bagimliliklar`,
      { method: "POST", body }
    ),

  deleteDependency: (dependencyId: string) =>
    apiClient<{ message: string }>(`is-programi/bagimliliklar/${dependencyId}`, {
      method: "DELETE",
    }),

  saveBaseline: (scheduleId: string, reason: string | null) =>
    apiClient<{ revisionNumber: number; message: string }>(
      `is-programi/${scheduleId}/baseline`,
      { method: "POST", body: { reason } }
    ),

  baselineHistory: (scheduleId: string) =>
    apiClient<BaselineRevision[]>(`is-programi/${scheduleId}/baseline-gecmisi`),

  replaceHolidays: (
    scheduleId: string,
    holidays: { date: string; name?: string | null }[]
  ) =>
    apiClient<{ count: number; message: string }>(
      `is-programi/${scheduleId}/tatiller`,
      { method: "PUT", body: { holidays } }
    ),

  alerts: (projectId?: string) =>
    apiClient<ScheduleAlertResponse>(
      `is-programi/uyarilar${projectId ? `?projectId=${projectId}` : ""}`
    ),

  delayPenalty: (projectId: string) =>
    apiClient<DelayPenaltyView>(`projects/${projectId}/gecikme-cezasi`),

  updateDeadline: (
    projectId: string,
    body: {
      contractDeadlineDate?: string | null;
      delayPenaltyKind: number;
      delayPenaltyValue: number;
      delayPenaltyCapRate?: number | null;
    }
  ) =>
    apiClient<{ message: string }>(`projects/${projectId}/termin`, {
      method: "PUT",
      body,
    }),

  assignResource: (
    activityId: string,
    body: {
      kind: number;
      personnelId?: string | null;
      subcontractorContractId?: string | null;
      role?: string | null;
      notes?: string | null;
    }
  ) =>
    apiClient<{ id: string; message: string; conflicts: ResourceConflict[] }>(
      `is-programi/aktiviteler/${activityId}/kaynaklar`,
      { method: "POST", body }
    ),

  removeResource: (assignmentId: string) =>
    apiClient<{ message: string }>(`is-programi/kaynaklar/${assignmentId}`, {
      method: "DELETE",
    }),

  conflicts: (scheduleId: string) =>
    apiClient<{ criticalCount: number; items: ResourceConflict[] }>(
      `is-programi/${scheduleId}/kaynak-cakismalari`
    ),

  resourceSuggestions: (activityId: string) =>
    apiClient<ResourceSuggestions>(
      `is-programi/aktiviteler/${activityId}/kaynak-onerileri`
    ),
};
