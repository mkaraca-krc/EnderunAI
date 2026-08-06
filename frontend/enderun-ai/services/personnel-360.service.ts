import { apiClient } from "@/lib/api/api-client";

export type Personnel360Assignment = {
  id: string;
  projectId: string;
  startDate: string;
  endDate?: string | null;
  role?: string | null;
  notes?: string | null;
  isPrimaryAssignment: boolean;
  isActive: boolean;
};

export type Personnel360Attendance = {
  startDate: string;
  endDate: string;
  recordCount: number;
  approvedRecordCount: number;
  normalHours: number;
  overtimeHours: number;
  nightShiftHours: number;
  sundayHours: number;
  publicHolidayHours: number;
  totalHours: number;
};

export type Personnel360HrSummary = {
  leaveCount: number;
  approvedLeaveDays: number;
  overtimeRequestCount: number;
  approvedOvertimeHours: number;
  trainingCount: number;
  completedTrainingCount: number;
  certificateCount: number;
  validCertificateCount: number;
  expiredCertificateCount: number;
  competencyCount: number;
  verifiedCompetencyCount: number;
  performanceReviewCount: number;
  latestPerformanceScore?: number | null;
  openDisciplinaryCount: number;
  activeAssetCount: number;
  careerActionCount: number;
};

export type Personnel360Alert = {
  code: string;
  severity: "High" | "Medium" | "Low" | string;
  title: string;
  description: string;
  dueDate?: string | null;
};

export type Personnel360Response = {
  profile: {
    id: string;
    companyId: string;
    branchId?: string | null;
    employeeNumber: string;
    firstName: string;
    lastName: string;
    fullName: string;
    identityNumber?: string | null;
    birthDate?: string | null;
    phone?: string | null;
    email?: string | null;
    address?: string | null;
    jobTitle?: string | null;
    profession?: string | null;
    sgkRegistrationNumber?: string | null;
    employmentStartDate?: string | null;
    employmentEndDate?: string | null;
    monthlySalary?: number | null;
    status: number;
    statusName: string;
  };
  assignments: Personnel360Assignment[];
  attendance: Personnel360Attendance;
  // Tutar alanları salary.view yoksa null gelir (salaryHidden), elden
  // ödeme ayrıca extra_payment.view ister (extraPaymentHidden).
  financial: {
    salaryHidden: boolean;
    currentGrossSalary: number | null;
    currentNetSalary: number | null;
    officialNetSalary: number | null;
    extraPaymentMonthlyAmount: number | null;
    extraPaymentHidden: boolean;
    totalTakeHome: number | null;
    currentDailyRate: number | null;
    currentHourlyRate: number | null;
    currencyCode: string;
    totalApprovedBonus: number | null;
    totalDeduction: number | null;
    totalApprovedAdvance: number | null;
    totalPaidAdvance: number | null;
    totalNetPayroll: number | null;
    lastPayrollNetAmount: number | null;
    payrollCount: number;
  };
  humanResources: Personnel360HrSummary;
  trainings: Array<{
    id: string;
    plannedStartDate: string;
    plannedEndDate?: string | null;
    completedAtUtc?: string | null;
    examScore?: number | null;
    passed?: boolean | null;
    trainerName?: string | null;
    locationName?: string | null;
    certificateNumber?: string | null;
    certificateExpiryDate?: string | null;
    statusName: string;
    notes?: string | null;
  }>;
  certificates: Array<{
    id: string;
    certificateNumber?: string | null;
    issuingAuthority?: string | null;
    issueDate: string;
    expiryDate?: string | null;
    renewalDate?: string | null;
    isVerified: boolean;
    statusName: string;
    notes?: string | null;
  }>;
  competencies: Array<{
    id: string;
    level: number;
    levelName: string;
    score?: number | null;
    assessmentDate: string;
    expiryDate?: string | null;
    assessedByName?: string | null;
    isVerified: boolean;
    notes?: string | null;
  }>;
  performanceReviews: Array<{
    id: string;
    year: number;
    periodNumber: number;
    periodName: string;
    attendanceScore: number;
    productivityScore: number;
    qualityScore: number;
    isgScore: number;
    teamworkScore: number;
    disciplineScore: number;
    managerScore: number;
    overallScore: number;
    strengths?: string | null;
    improvementAreas?: string | null;
    goals?: string | null;
    managerName?: string | null;
    statusName: string;
  }>;
  disciplinaryRecords: Array<{
    id: string;
    incidentDate: string;
    subject: string;
    incidentDescription: string;
    decisionText?: string | null;
    decisionByName?: string | null;
    statusName: string;
  }>;
  assets: Array<{
    id: string;
    assetType: string;
    assetCode: string;
    assetName: string;
    serialNumber?: string | null;
    assignmentDate: string;
    plannedReturnDate?: string | null;
    actualReturnDate?: string | null;
    conditionAtAssignment?: string | null;
    conditionAtReturn?: string | null;
    status: number;
    statusName: string;
    notes?: string | null;
  }>;
  careerHistory: Array<{
    id: string;
    actionType: number;
    actionTypeName: string;
    effectiveDate: string;
    previousSalary?: number | null;
    newSalary?: number | null;
    reason?: string | null;
    approvedByName?: string | null;
    notes?: string | null;
  }>;
  alerts: Personnel360Alert[];
  analysis: {
    riskLevel: string;
    riskScore: number;
    summary: string;
    positiveFindings: string[];
    attentionPoints: string[];
  };
};

function query(startDate?: string, endDate?: string) {
  const params = new URLSearchParams();
  if (startDate) params.set("startDate", startDate);
  if (endDate) params.set("endDate", endDate);
  const value = params.toString();
  return value ? `?${value}` : "";
}

export const personnel360Service = {
  get(personnelId: string, startDate?: string, endDate?: string) {
    return apiClient<Personnel360Response>(
      `hr/personnel-360/${personnelId}${query(startDate, endDate)}`
    );
  },
};
