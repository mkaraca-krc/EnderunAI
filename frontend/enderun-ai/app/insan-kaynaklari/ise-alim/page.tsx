"use client";

import { FormEvent, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { date, dateTime } from "@/lib/format/turkish";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
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
import { branchService, type BranchListItem } from "@/services/branch.service";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { useModuleActions } from "@/lib/auth/module-actions";
import {
  hrRecruitmentService,
  type CandidateInterview,
  type JobApplication,
  type JobCandidate,
  type JobPosting,
  type RecruitmentPayload,
} from "@/services/hr-recruitment.service";

type Tab = "postings" | "candidates" | "applications" | "interviews";
type EditorState =
  | { kind: Tab; id: string | null; values: Record<string, string | boolean> }
  | null;

const postingStatus: Record<number, string> = {
  0: "Taslak",
  1: "Yayında",
  2: "Kapalı",
  3: "İptal",
};
const candidateStatus: Record<number, string> = {
  0: "Yeni",
  1: "Ön Değerlendirme",
  2: "Mülakat",
  3: "Teklif",
  4: "İşe Alındı",
  5: "Olumsuz",
  6: "Pasif",
};
const applicationStatus: Record<number, string> = {
  0: "Başvurdu",
  1: "Ön Değerlendirme",
  2: "Mülakat",
  3: "Teklif",
  4: "İşe Alındı",
  5: "Olumsuz",
  6: "Geri Çekildi",
};
const interviewStatus: Record<number, string> = {
  0: "Planlandı",
  1: "Tamamlandı",
  2: "İptal",
  3: "Katılmadı",
};
const interviewTypes: Record<number, string> = {
  0: "Telefon",
  1: "Çevrim içi",
  2: "Yüz yüze",
  3: "Teknik",
  4: "İK",
};

const tabs: Array<{ value: Tab; label: string }> = [
  { value: "postings", label: "İş İlanları" },
  { value: "candidates", label: "Adaylar" },
  { value: "applications", label: "Başvurular" },
  { value: "interviews", label: "Mülakatlar" },
];
const createLabels: Record<Tab, string> = {
  postings: "İş İlanı",
  candidates: "Aday",
  applications: "Başvuru",
  interviews: "Mülakat",
};

function dateValue(value?: string | null) {
  return value ? value.slice(0, 10) : "";
}

function dateTimeValue(value?: string | null) {
  return value ? value.slice(0, 16) : "";
}

function displayDate(value?: string | null, includeTime = false) {
  if (!value) return "—";
  return includeTime ? dateTime(value) : date(value);
}

function fullName(candidate?: JobCandidate) {
  if (!candidate) return "—";
  return candidate.fullName || `${candidate.firstName} ${candidate.lastName}`.trim();
}

function applicationCandidate(
  application: JobApplication,
  candidates: JobCandidate[]
) {
  if (application.candidateFullName) return application.candidateFullName;
  const id = application.candidateId || application.jobCandidateId;
  return fullName(candidates.find((candidate) => candidate.id === id));
}

function applicationPosting(
  application: JobApplication,
  postings: JobPosting[]
) {
  return (
    application.jobPostingTitle ||
    postings.find((posting) => posting.id === application.jobPostingId)?.title ||
    "—"
  );
}

function interviewApplication(
  interview: CandidateInterview,
  applications: JobApplication[]
) {
  const id = interview.jobApplicationId || interview.applicationId;
  return applications.find((application) => application.id === id);
}

function statusVariant(status: number) {
  if ([1, 4].includes(status)) return "success" as const;
  if ([2, 3].includes(status)) return "warning" as const;
  if ([5, 6].includes(status)) return "danger" as const;
  return "default" as const;
}

function optionMap(labels: Record<number, string>) {
  return Object.entries(labels).map(([value, label]) => ({ value, label }));
}

function emptyEditor(kind: Tab): EditorState {
  if (kind === "postings") {
    return {
      kind,
      id: null,
      values: {
        companyId: "",
        branchId: "",
        title: "",
        departmentName: "",
        positionTitle: "",
        description: "",
        requirements: "",
        workLocation: "",
        employmentType: "0",
        headcount: "1",
        applicationDeadline: "",
        status: "0",
      },
    };
  }
  if (kind === "candidates") {
    return {
      kind,
      id: null,
      values: {
        firstName: "",
        lastName: "",
        identityNumber: "",
        birthDate: "",
        phone: "",
        email: "",
        address: "",
        educationLevel: "",
        yearsOfExperience: "",
        currentPosition: "",
        notes: "",
        status: "0",
        isActive: true,
      },
    };
  }
  if (kind === "applications") {
    return {
      kind,
      id: null,
      values: {
        jobPostingId: "",
        candidateId: "",
        applicationDate: new Date().toISOString().slice(0, 10),
        source: "",
        expectedSalary: "",
        notes: "",
        status: "0",
      },
    };
  }
  return {
    kind,
    id: null,
    values: {
      jobApplicationId: "",
      scheduledAt: "",
      type: "2",
      location: "",
      interviewerName: "",
      score: "",
      feedback: "",
      notes: "",
      status: "0",
    },
  };
}

export default function HrRecruitmentPage() {
  /*
   * Aksiyon izinleri UÇLARDAN (HrRecruitmentController):
   *   POST   postings|candidates|applications|interviews      -> personnel.create
   *   PUT    .../{id}                                          -> personnel.edit
   *   POST   postings/{id}/publish                             -> personnel.edit
   *   DELETE .../{id}                                          -> personnel.delete
   *
   * Dört varlık (ilan, aday, başvuru, görüşme) aynı izin ailesini
   * paylaşıyor; uçlar öyle kurulmuş.
   */
  const actions = useModuleActions("personnel");

  const [tab, setTab] = useState<Tab>("postings");
  const [postings, setPostings] = useState<JobPosting[]>([]);
  const [candidates, setCandidates] = useState<JobCandidate[]>([]);
  const [applications, setApplications] = useState<JobApplication[]>([]);
  const [interviews, setInterviews] = useState<CandidateInterview[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [branches, setBranches] = useState<BranchListItem[]>([]);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  /** Onay bekleyen işe alım işlemi. */
  const [pending, setPending] = useState<
    | { kind: "publish"; item: JobPosting }
    | { kind: "delete"; tab: Tab; id: string; label: string }
    | null
  >(null);

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [editor, setEditor] = useState<EditorState>(null);

  async function loadScreen() {
    setLoading(true);
    setError("");
    try {
      const [
        postingResult,
        candidateResult,
        applicationResult,
        interviewResult,
        companyResult,
        branchResult,
      ] = await Promise.all([
        hrRecruitmentService.getPostings(),
        hrRecruitmentService.getCandidates(),
        hrRecruitmentService.getApplications(),
        hrRecruitmentService.getInterviews(),
        companyService.getAll(),
        branchService.getAll(),
      ]);
      setPostings(postingResult);
      setCandidates(candidateResult);
      setApplications(applicationResult);
      setInterviews(interviewResult);
      setCompanies(companyResult.filter((company) => company.isActive !== false));
      setBranches(branchResult.filter((branch) => branch.isActive !== false));
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşe alım verileri yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadScreen();
  }, []);

  useEffect(() => {
    setSearch("");
    setStatus("");
  }, [tab]);

  const summary = useMemo(
    () => ({
      openPostings: postings.filter((item) => item.status === 1).length,
      candidates: candidates.filter((item) => item.isActive !== false).length,
      applications: applications.filter((item) => ![4, 5, 6].includes(item.status))
        .length,
      interviews: interviews.filter(
        (item) => item.status === 0 && new Date(item.scheduledAt) >= new Date()
      ).length,
    }),
    [applications, candidates, interviews, postings]
  );

  const visiblePostings = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");
    return postings.filter((item) => {
      const haystack = [
        item.title,
        item.code,
        item.companyName,
        item.departmentName,
        item.department,
        item.positionTitle,
        item.position,
        item.workLocation,
      ]
        .filter(Boolean)
        .join(" ")
        .toLocaleLowerCase("tr-TR");
      return (!term || haystack.includes(term)) && (!status || item.status === Number(status));
    });
  }, [postings, search, status]);

  const visibleCandidates = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");
    return candidates.filter((item) => {
      const haystack = [
        fullName(item),
        item.email,
        item.phone,
        item.currentPosition,
        item.identityNumber,
      ]
        .filter(Boolean)
        .join(" ")
        .toLocaleLowerCase("tr-TR");
      return (!term || haystack.includes(term)) && (!status || item.status === Number(status));
    });
  }, [candidates, search, status]);

  const visibleApplications = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");
    return applications.filter((item) => {
      const haystack = [
        applicationCandidate(item, candidates),
        applicationPosting(item, postings),
        item.source,
      ]
        .filter(Boolean)
        .join(" ")
        .toLocaleLowerCase("tr-TR");
      return (!term || haystack.includes(term)) && (!status || item.status === Number(status));
    });
  }, [applications, candidates, postings, search, status]);

  const visibleInterviews = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("tr-TR");
    return interviews.filter((item) => {
      const application = interviewApplication(item, applications);
      const haystack = [
        item.candidateFullName,
        item.jobPostingTitle,
        item.interviewerName,
        item.location,
        application && applicationCandidate(application, candidates),
        application && applicationPosting(application, postings),
      ]
        .filter(Boolean)
        .join(" ")
        .toLocaleLowerCase("tr-TR");
      return (!term || haystack.includes(term)) && (!status || item.status === Number(status));
    });
  }, [applications, candidates, interviews, postings, search, status]);

  const currentStatusOptions =
    tab === "postings"
      ? postingStatus
      : tab === "candidates"
        ? candidateStatus
        : tab === "applications"
          ? applicationStatus
          : interviewStatus;

  const branchOptions = useMemo(() => {
    const companyId = String(editor?.values.companyId || "");
    return branches.filter((branch) => !companyId || branch.companyId === companyId);
  }, [branches, editor?.values.companyId]);

  function updateEditor(key: string, value: string | boolean) {
    setEditor((current) =>
      current ? { ...current, values: { ...current.values, [key]: value } } : current
    );
  }

  function openCreate() {
    const next = emptyEditor(tab);
    if (next?.kind === "postings" && companies.length === 1) {
      next.values.companyId = companies[0].id;
    }
    setError("");
    setSuccess("");
    setEditor(next);
  }

  function openPosting(item: JobPosting) {
    setEditor({
      kind: "postings",
      id: item.id,
      values: {
        companyId: item.companyId || "",
        branchId: item.branchId || "",
        title: item.title,
        departmentName: item.departmentName || item.department || "",
        positionTitle: item.positionTitle || item.position || "",
        description: item.description || "",
        requirements: item.requirements || "",
        workLocation: item.workLocation || "",
        employmentType: String(item.employmentType ?? 0),
        headcount: String(item.headcount ?? item.openPositionCount ?? 1),
        applicationDeadline: dateValue(item.applicationDeadline),
        status: String(item.status),
      },
    });
  }

  function openCandidate(item: JobCandidate) {
    setEditor({
      kind: "candidates",
      id: item.id,
      values: {
        firstName: item.firstName,
        lastName: item.lastName,
        identityNumber: item.identityNumber || "",
        birthDate: dateValue(item.birthDate),
        phone: item.phone || "",
        email: item.email || "",
        address: item.address || "",
        educationLevel: item.educationLevel || "",
        yearsOfExperience:
          item.yearsOfExperience == null ? "" : String(item.yearsOfExperience),
        currentPosition: item.currentPosition || "",
        notes: item.notes || "",
        status: String(item.status),
        isActive: item.isActive !== false,
      },
    });
  }

  function openApplication(item: JobApplication) {
    setEditor({
      kind: "applications",
      id: item.id,
      values: {
        jobPostingId: item.jobPostingId,
        candidateId: item.candidateId || item.jobCandidateId || "",
        applicationDate: dateValue(item.applicationDate),
        source: item.source || "",
        expectedSalary: item.expectedSalary == null ? "" : String(item.expectedSalary),
        notes: item.notes || "",
        status: String(item.status),
      },
    });
  }

  function openInterview(item: CandidateInterview) {
    setEditor({
      kind: "interviews",
      id: item.id,
      values: {
        jobApplicationId: item.jobApplicationId || item.applicationId || "",
        scheduledAt: dateTimeValue(item.scheduledAt),
        type: String(item.type),
        location: item.location || "",
        interviewerName: item.interviewerName || "",
        score: item.score == null ? "" : String(item.score),
        feedback: item.feedback || "",
        notes: item.notes || "",
        status: String(item.status),
      },
    });
  }

  function editorPayload(current: NonNullable<EditorState>): RecruitmentPayload {
    const value = current.values;
    if (current.kind === "postings") {
      return {
        companyId: value.companyId,
        branchId: value.branchId || null,
        title: value.title,
        department: value.departmentName || null,
        departmentName: value.departmentName || null,
        position: value.positionTitle || null,
        positionTitle: value.positionTitle || null,
        description: value.description || null,
        requirements: value.requirements || null,
        workLocation: value.workLocation || null,
        employmentType: Number(value.employmentType || 0),
        headcount: Number(value.headcount || 1),
        openPositionCount: Number(value.headcount || 1),
        applicationDeadline: value.applicationDeadline || null,
        status: Number(value.status || 0),
        isActive: true,
      };
    }
    if (current.kind === "candidates") {
      return {
        firstName: value.firstName,
        lastName: value.lastName,
        identityNumber: value.identityNumber || null,
        birthDate: value.birthDate || null,
        phone: value.phone || null,
        email: value.email || null,
        address: value.address || null,
        educationLevel: value.educationLevel || null,
        yearsOfExperience:
          value.yearsOfExperience === "" ? null : Number(value.yearsOfExperience),
        currentPosition: value.currentPosition || null,
        notes: value.notes || null,
        status: Number(value.status || 0),
        isActive: value.isActive !== false,
      };
    }
    if (current.kind === "applications") {
      return {
        jobPostingId: value.jobPostingId,
        candidateId: value.candidateId,
        jobCandidateId: value.candidateId,
        applicationDate: value.applicationDate || null,
        source: value.source || null,
        expectedSalary:
          value.expectedSalary === "" ? null : Number(value.expectedSalary),
        notes: value.notes || null,
        status: Number(value.status || 0),
      };
    }
    return {
      jobApplicationId: value.jobApplicationId,
      applicationId: value.jobApplicationId,
      scheduledAt: value.scheduledAt,
      type: Number(value.type || 0),
      location: value.location || null,
      interviewerName: value.interviewerName || null,
      score: value.score === "" ? null : Number(value.score),
      feedback: value.feedback || null,
      notes: value.notes || null,
      status: Number(value.status || 0),
    };
  }

  async function saveEditor(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editor) return;
    setSaving(true);
    setError("");
    setSuccess("");
    try {
      const payload = editorPayload(editor);
      if (editor.kind === "postings") {
        if (editor.id) await hrRecruitmentService.updatePosting(editor.id, payload);
        else await hrRecruitmentService.createPosting(payload);
      } else if (editor.kind === "candidates") {
        if (editor.id) await hrRecruitmentService.updateCandidate(editor.id, payload);
        else await hrRecruitmentService.createCandidate(payload);
      } else if (editor.kind === "applications") {
        if (editor.id) await hrRecruitmentService.updateApplication(editor.id, payload);
        else await hrRecruitmentService.createApplication(payload);
      } else {
        if (editor.id) await hrRecruitmentService.updateInterview(editor.id, payload);
        else await hrRecruitmentService.createInterview(payload);
      }
      setEditor(null);
      setSuccess(editor.id ? "Kayıt güncellendi." : "Yeni kayıt oluşturuldu.");
      await loadScreen();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kayıt işlemi tamamlanamadı.");
    } finally {
      setSaving(false);
    }
  }

  async function publishPosting(item: JobPosting) {
    setPending(null);
    setError("");
    try {
      await hrRecruitmentService.publishPosting(item.id);
      setSuccess("İş ilanı yayınlandı.");
      await loadScreen();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İlan yayınlanamadı.");
    }
  }

  // `label` artık parametre değil: silme metnini ConfirmDialog
  // kuruyor, bu işlev yalnızca isteği gönderiyor.
  async function remove(kind: Tab, id: string) {
    setPending(null);
    setError("");
    try {
      if (kind === "postings") await hrRecruitmentService.deletePosting(id);
      else if (kind === "candidates") await hrRecruitmentService.deleteCandidate(id);
      else if (kind === "applications") await hrRecruitmentService.deleteApplication(id);
      else await hrRecruitmentService.deleteInterview(id);
      setSuccess("Kayıt silindi.");
      await loadScreen();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kayıt silinemedi.");
    }
  }

  const visibleCount =
    tab === "postings"
      ? visiblePostings.length
      : tab === "candidates"
        ? visibleCandidates.length
        : tab === "applications"
          ? visibleApplications.length
          : visibleInterviews.length;

  return (
    <ErpShell
      design="redwood"
      title="İşe Alım"
      description="İlan, aday, başvuru ve mülakat süreçlerini tek merkezden yönetin"
    >
      {error && (
        <div className="mb-5 flex items-start justify-between gap-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <span>{error}</span>
          <button type="button" onClick={() => setError("")} aria-label="Uyarıyı kapat">×</button>
        </div>
      )}
      {success && (
        <div className="mb-5 flex items-start justify-between gap-4 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          <span>{success}</span>
          <button type="button" onClick={() => setSuccess("")} aria-label="Bildirimi kapat">×</button>
        </div>
      )}

      <div className="mb-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard title="Yayındaki İlan" value={loading ? "…" : summary.openPostings} description="Aday kabul eden ilanlar" icon="▤" />
        <StatCard title="Aktif Aday" value={loading ? "…" : summary.candidates} description="Aday havuzundaki kayıtlar" icon="♙" />
        <StatCard title="Açık Başvuru" value={loading ? "…" : summary.applications} description="Değerlendirmesi süren" icon="⌁" />
        <StatCard title="Planlı Mülakat" value={loading ? "…" : summary.interviews} description="Yaklaşan görüşmeler" icon="◷" />
      </div>

      <Card>
        <CardHeader className="space-y-4">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
            <div>
              <h2 className="text-lg font-semibold text-slate-900">İşe alım operasyonu</h2>
              <p className="mt-1 text-sm text-slate-500">
                Veriler doğrudan İK işe alım servisinden alınır.
              </p>
            </div>
            {actions.can("create") && actions.can("create") && (
              <Button onClick={openCreate}>
                + Yeni {createLabels[tab]}
              </Button>
            )}
          </div>

          <div className="flex gap-1 overflow-x-auto rounded-xl border border-slate-200 bg-slate-50 p-1">
            {tabs.map((item) => (
              <button
                key={item.value}
                type="button"
                onClick={() => setTab(item.value)}
                className={`min-w-max rounded-lg px-4 py-2 text-sm font-medium transition ${
                  tab === item.value
                    ? "bg-white text-slate-900 shadow-sm"
                    : "text-slate-500 hover:text-slate-800"
                }`}
              >
                {item.label}
              </button>
            ))}
          </div>

          <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_240px_auto]">
            <Input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Kayıtlarda ara..."
            />
            <Select
              value={status}
              onChange={(event) => setStatus(event.target.value)}
              placeholder="Tüm durumlar"
              options={optionMap(currentStatusOptions)}
            />
            <div className="flex items-center text-sm text-slate-500">
              {visibleCount} kayıt
            </div>
          </div>
        </CardHeader>

        <CardContent>
          {loading ? (
            <div className="py-16 text-center text-sm text-slate-500">
              İşe alım kayıtları yükleniyor...
            </div>
          ) : visibleCount === 0 ? (
            <EmptyState
              title="Kayıt bulunamadı"
              description="Filtreleri değiştirin veya yeni bir kayıt oluşturun."
              action={<Button onClick={openCreate}>Yeni Kayıt</Button>}
            />
          ) : tab === "postings" ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>İlan / Pozisyon</TableHead>
                  <TableHead>Şirket / Şube</TableHead>
                  <TableHead>Lokasyon</TableHead>
                  <TableHead>Kontenjan</TableHead>
                  <TableHead>Son Başvuru</TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead className="text-right">İşlem</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visiblePostings.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>
                      <strong className="block text-slate-900">{item.title}</strong>
                      <span className="text-xs text-slate-500">
                        {item.positionTitle || item.position || item.departmentName || item.code || "—"}
                      </span>
                    </TableCell>
                    <TableCell>
                      {item.companyName || "—"}
                      <span className="block text-xs text-slate-500">{item.branchName || "Merkez"}</span>
                    </TableCell>
                    <TableCell>{item.workLocation || "—"}</TableCell>
                    <TableCell>{item.headcount ?? item.openPositionCount ?? "—"}</TableCell>
                    <TableCell>{displayDate(item.applicationDeadline)}</TableCell>
                    <TableCell>
                      <Badge variant={statusVariant(item.status)}>
                        {item.statusName || postingStatus[item.status] || `Durum ${item.status}`}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-2">
                        {item.status === 0 && actions.can("edit") && (
                          <Button size="sm" onClick={() => setPending({ kind: "publish", item })}>Yayınla</Button>
                        )}
                        {actions.can("edit") && (
                          <Button size="sm" variant="secondary" onClick={() => openPosting(item)}>Düzenle</Button>
                        )}
                        {actions.can("delete") && actions.can("delete") && actions.can("delete") && actions.can("delete") && (
                          <Button size="sm" variant="ghost" onClick={() =>
                              setPending({
                                kind: "delete",
                                tab: "postings",
                                id: item.id,
                                label: item.title,
                              })
                            }>Sil</Button>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : tab === "candidates" ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Aday</TableHead>
                  <TableHead>İletişim</TableHead>
                  <TableHead>Mevcut Pozisyon</TableHead>
                  <TableHead>Deneyim</TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead className="text-right">İşlem</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visibleCandidates.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>
                      <strong className="block text-slate-900">{fullName(item)}</strong>
                      <span className="text-xs text-slate-500">{item.educationLevel || "Eğitim bilgisi yok"}</span>
                    </TableCell>
                    <TableCell>
                      {item.phone || "—"}
                      <span className="block text-xs text-slate-500">{item.email || "—"}</span>
                    </TableCell>
                    <TableCell>{item.currentPosition || "—"}</TableCell>
                    <TableCell>
                      {item.yearsOfExperience == null ? "—" : `${item.yearsOfExperience} yıl`}
                    </TableCell>
                    <TableCell>
                      <Badge variant={statusVariant(item.status)}>
                        {item.statusName || candidateStatus[item.status] || `Durum ${item.status}`}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-2">
                        {actions.can("edit") && (
                          <Button size="sm" variant="secondary" onClick={() => openCandidate(item)}>Düzenle</Button>
                        )}
                        <Button size="sm" variant="ghost" onClick={() =>
                            setPending({
                              kind: "delete",
                              tab: "candidates",
                              id: item.id,
                              label: fullName(item),
                            })
                          }>Sil</Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : tab === "applications" ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Aday</TableHead>
                  <TableHead>İş İlanı</TableHead>
                  <TableHead>Başvuru Tarihi</TableHead>
                  <TableHead>Kaynak</TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead className="text-right">İşlem</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visibleApplications.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell><strong className="text-slate-900">{applicationCandidate(item, candidates)}</strong></TableCell>
                    <TableCell>{applicationPosting(item, postings)}</TableCell>
                    <TableCell>{displayDate(item.applicationDate || item.createdAt)}</TableCell>
                    <TableCell>{item.source || "—"}</TableCell>
                    <TableCell>
                      <Badge variant={statusVariant(item.status)}>
                        {item.statusName || applicationStatus[item.status] || `Durum ${item.status}`}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-2">
                        {actions.can("edit") && (
                          <Button size="sm" variant="secondary" onClick={() => openApplication(item)}>Düzenle</Button>
                        )}
                        <Button size="sm" variant="ghost" onClick={() =>
                            setPending({
                              kind: "delete",
                              tab: "applications",
                              id: item.id,
                              label: applicationCandidate(item, candidates),
                            })
                          }>Sil</Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Aday / İlan</TableHead>
                  <TableHead>Tarih</TableHead>
                  <TableHead>Tür</TableHead>
                  <TableHead>Görüşmeci / Yer</TableHead>
                  <TableHead>Durum</TableHead>
                  <TableHead className="text-right">İşlem</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visibleInterviews.map((item) => {
                  const application = interviewApplication(item, applications);
                  const candidateName =
                    item.candidateFullName ||
                    (application && applicationCandidate(application, candidates)) ||
                    "—";
                  const postingTitle =
                    item.jobPostingTitle ||
                    (application && applicationPosting(application, postings)) ||
                    "—";
                  return (
                    <TableRow key={item.id}>
                      <TableCell>
                        <strong className="block text-slate-900">{candidateName}</strong>
                        <span className="text-xs text-slate-500">{postingTitle}</span>
                      </TableCell>
                      <TableCell>{displayDate(item.scheduledAt, true)}</TableCell>
                      <TableCell>{item.typeName || interviewTypes[item.type] || `Tür ${item.type}`}</TableCell>
                      <TableCell>
                        {item.interviewerName || "—"}
                        <span className="block text-xs text-slate-500">{item.location || "—"}</span>
                      </TableCell>
                      <TableCell>
                        <Badge variant={statusVariant(item.status)}>
                          {item.statusName || interviewStatus[item.status] || `Durum ${item.status}`}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <div className="flex justify-end gap-2">
                          {actions.can("edit") && (
                            <Button size="sm" variant="secondary" onClick={() => openInterview(item)}>Düzenle</Button>
                          )}
                          <Button size="sm" variant="ghost" onClick={() =>
                            setPending({
                              kind: "delete",
                              tab: "interviews",
                              id: item.id,
                              label: candidateName,
                            })
                          }>Sil</Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {editor && (
        <div className="fixed inset-0 z-50 flex justify-end bg-slate-950/40 backdrop-blur-[1px]">
          <button
            type="button"
            className="min-w-0 flex-1 cursor-default"
            onClick={() => !saving && setEditor(null)}
            aria-label="Formu kapat"
          />
          <aside className="h-full w-full max-w-2xl overflow-y-auto bg-white shadow-2xl">
            <div className="sticky top-0 z-10 flex items-center justify-between border-b border-slate-200 bg-white px-6 py-5">
              <div>
                <span className="text-xs font-semibold tracking-widest text-slate-500">İŞE ALIM</span>
                <h2 className="mt-1 text-xl font-semibold text-slate-900">
                  {editor.id ? "Kaydı Düzenle" : "Yeni Kayıt"}
                </h2>
              </div>
              <button
                type="button"
                onClick={() => !saving && setEditor(null)}
                className="flex h-10 w-10 items-center justify-center rounded-lg border border-slate-200 text-xl text-slate-500 hover:bg-slate-50"
                aria-label="Formu kapat"
              >
                ×
              </button>
            </div>

            <form onSubmit={saveEditor} className="space-y-5 p-6">
              {editor.kind === "postings" && (
                <>
                  <div className="grid gap-4 md:grid-cols-2">
                    <Select
                      label="Şirket"
                      value={String(editor.values.companyId)}
                      onChange={(event) => {
                        updateEditor("companyId", event.target.value);
                        updateEditor("branchId", "");
                      }}
                      placeholder="Şirket seçin"
                      required
                      options={companies.map((item) => ({ value: item.id, label: `${item.code} · ${item.name}` }))}
                    />
                    <Select
                      label="Şube"
                      value={String(editor.values.branchId)}
                      onChange={(event) => updateEditor("branchId", event.target.value)}
                      placeholder="Şube seçin"
                      options={branchOptions.map((item) => ({ value: item.id, label: `${item.code} · ${item.name}` }))}
                    />
                  </div>
                  <Input label="İlan Başlığı" value={String(editor.values.title)} onChange={(event) => updateEditor("title", event.target.value)} required />
                  <div className="grid gap-4 md:grid-cols-2">
                    <Input label="Departman" value={String(editor.values.departmentName)} onChange={(event) => updateEditor("departmentName", event.target.value)} />
                    <Input label="Pozisyon" value={String(editor.values.positionTitle)} onChange={(event) => updateEditor("positionTitle", event.target.value)} />
                    <Input label="Çalışma Yeri" value={String(editor.values.workLocation)} onChange={(event) => updateEditor("workLocation", event.target.value)} />
                    <Input label="Kontenjan" type="number" min="1" value={String(editor.values.headcount)} onChange={(event) => updateEditor("headcount", event.target.value)} />
                    <Select
                      label="Çalışma Türü"
                      value={String(editor.values.employmentType)}
                      onChange={(event) => updateEditor("employmentType", event.target.value)}
                      options={[
                        { value: "0", label: "Tam zamanlı" },
                        { value: "1", label: "Yarı zamanlı" },
                        { value: "2", label: "Dönemsel" },
                        { value: "3", label: "Staj" },
                      ]}
                    />
                    <Input label="Son Başvuru Tarihi" type="date" value={String(editor.values.applicationDeadline)} onChange={(event) => updateEditor("applicationDeadline", event.target.value)} />
                  </div>
                  <label className="block">
                    <span className="mb-1.5 block text-sm font-medium text-slate-700">İlan Açıklaması</span>
                    <textarea className="min-h-28 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:border-slate-500" value={String(editor.values.description)} onChange={(event) => updateEditor("description", event.target.value)} />
                  </label>
                  <label className="block">
                    <span className="mb-1.5 block text-sm font-medium text-slate-700">Aranan Nitelikler</span>
                    <textarea className="min-h-24 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:border-slate-500" value={String(editor.values.requirements)} onChange={(event) => updateEditor("requirements", event.target.value)} />
                  </label>
                  {editor.id && (
                    <Select label="Durum" value={String(editor.values.status)} onChange={(event) => updateEditor("status", event.target.value)} options={optionMap(postingStatus)} />
                  )}
                </>
              )}

              {editor.kind === "candidates" && (
                <>
                  <div className="grid gap-4 md:grid-cols-2">
                    <Input label="Ad" value={String(editor.values.firstName)} onChange={(event) => updateEditor("firstName", event.target.value)} required />
                    <Input label="Soyad" value={String(editor.values.lastName)} onChange={(event) => updateEditor("lastName", event.target.value)} required />
                    <Input label="TC Kimlik Numarası" value={String(editor.values.identityNumber)} onChange={(event) => updateEditor("identityNumber", event.target.value)} maxLength={11} />
                    <Input label="Doğum Tarihi" type="date" value={String(editor.values.birthDate)} onChange={(event) => updateEditor("birthDate", event.target.value)} />
                    <Input label="Telefon" value={String(editor.values.phone)} onChange={(event) => updateEditor("phone", event.target.value)} />
                    <Input label="E-posta" type="email" value={String(editor.values.email)} onChange={(event) => updateEditor("email", event.target.value)} />
                    <Input label="Eğitim Seviyesi" value={String(editor.values.educationLevel)} onChange={(event) => updateEditor("educationLevel", event.target.value)} />
                    <Input label="Deneyim (Yıl)" type="number" min="0" value={String(editor.values.yearsOfExperience)} onChange={(event) => updateEditor("yearsOfExperience", event.target.value)} />
                    <Input label="Mevcut / Son Pozisyon" value={String(editor.values.currentPosition)} onChange={(event) => updateEditor("currentPosition", event.target.value)} className="md:col-span-2" />
                    <Input label="Adres" value={String(editor.values.address)} onChange={(event) => updateEditor("address", event.target.value)} className="md:col-span-2" />
                  </div>
                  <label className="block">
                    <span className="mb-1.5 block text-sm font-medium text-slate-700">Notlar</span>
                    <textarea className="min-h-24 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:border-slate-500" value={String(editor.values.notes)} onChange={(event) => updateEditor("notes", event.target.value)} />
                  </label>
                  {editor.id && (
                    <div className="grid gap-4 md:grid-cols-2">
                      <Select label="Durum" value={String(editor.values.status)} onChange={(event) => updateEditor("status", event.target.value)} options={optionMap(candidateStatus)} />
                      <label className="flex items-center gap-3 rounded-lg border border-slate-200 px-4 py-3">
                        <input type="checkbox" checked={Boolean(editor.values.isActive)} onChange={(event) => updateEditor("isActive", event.target.checked)} />
                        <span className="text-sm font-medium text-slate-700">Aktif aday</span>
                      </label>
                    </div>
                  )}
                </>
              )}

              {editor.kind === "applications" && (
                <>
                  <Select
                    label="İş İlanı"
                    value={String(editor.values.jobPostingId)}
                    onChange={(event) => updateEditor("jobPostingId", event.target.value)}
                    placeholder="İlan seçin"
                    required
                    options={postings.map((item) => ({ value: item.id, label: item.title }))}
                  />
                  <Select
                    label="Aday"
                    value={String(editor.values.candidateId)}
                    onChange={(event) => updateEditor("candidateId", event.target.value)}
                    placeholder="Aday seçin"
                    required
                    options={candidates.map((item) => ({ value: item.id, label: fullName(item) }))}
                  />
                  <div className="grid gap-4 md:grid-cols-2">
                    <Input label="Başvuru Tarihi" type="date" value={String(editor.values.applicationDate)} onChange={(event) => updateEditor("applicationDate", event.target.value)} />
                    <Input label="Başvuru Kaynağı" value={String(editor.values.source)} onChange={(event) => updateEditor("source", event.target.value)} />
                    <Input label="Ücret Beklentisi" type="number" min="0" step="0.01" value={String(editor.values.expectedSalary)} onChange={(event) => updateEditor("expectedSalary", event.target.value)} />
                    {editor.id && (
                      <Select label="Durum" value={String(editor.values.status)} onChange={(event) => updateEditor("status", event.target.value)} options={optionMap(applicationStatus)} />
                    )}
                  </div>
                  <label className="block">
                    <span className="mb-1.5 block text-sm font-medium text-slate-700">Notlar</span>
                    <textarea className="min-h-24 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:border-slate-500" value={String(editor.values.notes)} onChange={(event) => updateEditor("notes", event.target.value)} />
                  </label>
                </>
              )}

              {editor.kind === "interviews" && (
                <>
                  <Select
                    label="Başvuru"
                    value={String(editor.values.jobApplicationId)}
                    onChange={(event) => updateEditor("jobApplicationId", event.target.value)}
                    placeholder="Başvuru seçin"
                    required
                    options={applications.map((item) => ({
                      value: item.id,
                      label: `${applicationCandidate(item, candidates)} · ${applicationPosting(item, postings)}`,
                    }))}
                  />
                  <div className="grid gap-4 md:grid-cols-2">
                    <Input label="Mülakat Tarihi ve Saati" type="datetime-local" value={String(editor.values.scheduledAt)} onChange={(event) => updateEditor("scheduledAt", event.target.value)} required />
                    <Select label="Mülakat Türü" value={String(editor.values.type)} onChange={(event) => updateEditor("type", event.target.value)} options={optionMap(interviewTypes)} />
                    <Input label="Görüşme Yeri / Bağlantı" value={String(editor.values.location)} onChange={(event) => updateEditor("location", event.target.value)} />
                    <Input label="Görüşmeci" value={String(editor.values.interviewerName)} onChange={(event) => updateEditor("interviewerName", event.target.value)} />
                    {editor.id && (
                      <>
                        <Select label="Durum" value={String(editor.values.status)} onChange={(event) => updateEditor("status", event.target.value)} options={optionMap(interviewStatus)} />
                        <Input label="Puan" type="number" min="0" max="100" value={String(editor.values.score)} onChange={(event) => updateEditor("score", event.target.value)} />
                      </>
                    )}
                  </div>
                  {editor.id && (
                    <label className="block">
                      <span className="mb-1.5 block text-sm font-medium text-slate-700">Değerlendirme</span>
                      <textarea className="min-h-24 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:border-slate-500" value={String(editor.values.feedback)} onChange={(event) => updateEditor("feedback", event.target.value)} />
                    </label>
                  )}
                  <label className="block">
                    <span className="mb-1.5 block text-sm font-medium text-slate-700">Notlar</span>
                    <textarea className="min-h-20 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:border-slate-500" value={String(editor.values.notes)} onChange={(event) => updateEditor("notes", event.target.value)} />
                  </label>
                </>
              )}

              <div className="sticky bottom-0 flex justify-end gap-3 border-t border-slate-200 bg-white py-4">
                <Button type="button" variant="secondary" onClick={() => setEditor(null)} disabled={saving}>
                  Vazgeç
                </Button>
                <Button type="submit" loading={saving}>
                  {editor.id ? "Değişiklikleri Kaydet" : "Kaydı Oluştur"}
                </Button>
              </div>
            </form>
          </aside>
        </div>
      )}
      {pending && (
        <ConfirmDialog
          open
          title={
            pending.kind === "publish" ? "İlanı Yayınla" : "Kaydı Sil"
          }
          description={
            pending.kind === "publish"
              ? `“${pending.item.title}” ilanı yayınlanacak ve başvuruya açılacak.`
              : `“${pending.label}” kaydı kalıcı olarak silinecek. Bu işlem geri alınamaz.`
          }
          confirmLabel={
            pending.kind === "publish" ? "Yayınla" : "Kaydı Sil"
          }
          error={error}
          onCancel={() => setPending(null)}
          onConfirm={() =>
            pending.kind === "publish"
              ? void publishPosting(pending.item)
              : void remove(pending.tab, pending.id)
          }
        />
      )}
    </ErpShell>
  );
}
