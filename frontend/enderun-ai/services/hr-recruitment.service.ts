import { apiClient } from "@/lib/api/api-client";

export type JobPosting = {
  id: string;
  companyId?: string;
  companyName?: string;
  branchId?: string | null;
  branchName?: string | null;
  projectId?: string | null;
  projectName?: string | null;
  code?: string | null;
  title: string;
  department?: string | null;
  departmentName?: string | null;
  position?: string | null;
  positionTitle?: string | null;
  description?: string | null;
  requirements?: string | null;
  responsibilities?: string | null;
  employmentType?: number;
  employmentTypeName?: string | null;
  workLocation?: string | null;
  headcount?: number;
  openPositionCount?: number;
  applicationDeadline?: string | null;
  status: number;
  statusName?: string | null;
  isActive?: boolean;
  publishedAt?: string | null;
  createdAt?: string;
  applicationCount?: number;
  applicationsCount?: number;
};

export type JobCandidate = {
  id: string;
  firstName: string;
  lastName: string;
  fullName?: string;
  identityNumber?: string | null;
  birthDate?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  educationLevel?: string | null;
  schoolName?: string | null;
  departmentName?: string | null;
  yearsOfExperience?: number | null;
  currentCompany?: string | null;
  currentPosition?: string | null;
  cvFilePath?: string | null;
  linkedinUrl?: string | null;
  notes?: string | null;
  status: number;
  statusName?: string | null;
  isActive?: boolean;
  createdAt?: string;
};

export type JobApplication = {
  id: string;
  jobPostingId: string;
  jobPostingTitle?: string;
  candidateId?: string;
  jobCandidateId?: string;
  candidateFullName?: string;
  applicationDate?: string;
  source?: string | null;
  expectedSalary?: number | null;
  status: number;
  statusName?: string | null;
  notes?: string | null;
  createdAt?: string;
};

export type CandidateInterview = {
  id: string;
  jobApplicationId?: string;
  applicationId?: string;
  candidateId?: string;
  candidateFullName?: string;
  jobPostingTitle?: string;
  scheduledAt: string;
  type: number;
  typeName?: string | null;
  location?: string | null;
  interviewerPersonnelId?: string | null;
  interviewerName?: string | null;
  status: number;
  statusName?: string | null;
  score?: number | null;
  feedback?: string | null;
  notes?: string | null;
  createdAt?: string;
};

export type RecruitmentPayload = Record<string, unknown>;

const root = "hr/recruitment";

export const hrRecruitmentService = {
  getPostings() {
    return apiClient<JobPosting[]>(`${root}/postings`);
  },
  createPosting(payload: RecruitmentPayload) {
    return apiClient<JobPosting>(`${root}/postings`, {
      method: "POST",
      body: payload,
    });
  },
  updatePosting(id: string, payload: RecruitmentPayload) {
    return apiClient<JobPosting>(`${root}/postings/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
  publishPosting(id: string) {
    return apiClient<{ message?: string }>(`${root}/postings/${id}/publish`, {
      method: "POST",
    });
  },
  deletePosting(id: string) {
    return apiClient<void>(`${root}/postings/${id}`, { method: "DELETE" });
  },

  getCandidates() {
    return apiClient<JobCandidate[]>(`${root}/candidates`);
  },
  createCandidate(payload: RecruitmentPayload) {
    return apiClient<JobCandidate>(`${root}/candidates`, {
      method: "POST",
      body: payload,
    });
  },
  updateCandidate(id: string, payload: RecruitmentPayload) {
    return apiClient<JobCandidate>(`${root}/candidates/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
  deleteCandidate(id: string) {
    return apiClient<void>(`${root}/candidates/${id}`, { method: "DELETE" });
  },

  getApplications() {
    return apiClient<JobApplication[]>(`${root}/applications`);
  },
  createApplication(payload: RecruitmentPayload) {
    return apiClient<JobApplication>(`${root}/applications`, {
      method: "POST",
      body: payload,
    });
  },
  updateApplication(id: string, payload: RecruitmentPayload) {
    return apiClient<JobApplication>(`${root}/applications/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
  deleteApplication(id: string) {
    return apiClient<void>(`${root}/applications/${id}`, { method: "DELETE" });
  },

  getInterviews() {
    return apiClient<CandidateInterview[]>(`${root}/interviews`);
  },
  createInterview(payload: RecruitmentPayload) {
    return apiClient<CandidateInterview>(`${root}/interviews`, {
      method: "POST",
      body: payload,
    });
  },
  updateInterview(id: string, payload: RecruitmentPayload) {
    return apiClient<CandidateInterview>(`${root}/interviews/${id}`, {
      method: "PUT",
      body: payload,
    });
  },
  deleteInterview(id: string) {
    return apiClient<void>(`${root}/interviews/${id}`, { method: "DELETE" });
  },
};
