namespace EnderunAI.Api.Models;

public enum JobPostingStatus
{
    Draft = 0,
    Published = 1,
    Closed = 2,
    Cancelled = 3
}

public enum JobCandidateStatus
{
    New = 0,
    Screening = 1,
    Shortlisted = 2,
    Rejected = 3,
    Hired = 4
}

public enum JobApplicationStatus
{
    Applied = 0,
    Reviewing = 1,
    InterviewScheduled = 2,
    Offered = 3,
    Accepted = 4,
    Rejected = 5,
    Withdrawn = 6
}

public enum CandidateInterviewStatus
{
    Scheduled = 0,
    Completed = 1,
    Cancelled = 2,
    NoShow = 3
}

public sealed class JobPosting : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? PositionId { get; set; }

    public string PostingNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? LocationName { get; set; }
    public string? EmploymentType { get; set; }
    public int RequiredHeadcount { get; set; } = 1;
    public string Description { get; set; } = string.Empty;
    public string? Requirements { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? ClosingDateUtc { get; set; }
    public JobPostingStatus Status { get; set; } = JobPostingStatus.Draft;

    public ICollection<JobApplication> Applications { get; set; } = new List<JobApplication>();
}

public sealed class JobCandidate : BaseEntity
{
    public Guid CompanyId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? IdentityNumber { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Profession { get; set; }
    public int? TotalExperienceYears { get; set; }
    public string? EducationLevel { get; set; }
    public string? Source { get; set; }
    public string? Notes { get; set; }
    public JobCandidateStatus Status { get; set; } = JobCandidateStatus.New;
}

public sealed class JobApplication : BaseEntity
{
    public Guid CompanyId { get; set; }

    public Guid JobPostingId { get; set; }
    public JobPosting JobPosting { get; set; } = null!;

    public Guid CandidateId { get; set; }
    public JobCandidate Candidate { get; set; } = null!;

    public DateTime ApplicationDateUtc { get; set; } = DateTime.UtcNow;
    public decimal? ExpectedSalary { get; set; }
    public string CurrencyCode { get; set; } = "TRY";
    public DateTime? AvailableStartDate { get; set; }
    public string? EvaluationNote { get; set; }
    public JobApplicationStatus Status { get; set; } = JobApplicationStatus.Applied;

    public ICollection<CandidateInterview> Interviews { get; set; } = new List<CandidateInterview>();
}

public sealed class CandidateInterview : BaseEntity
{
    public Guid CompanyId { get; set; }

    public Guid JobApplicationId { get; set; }
    public JobApplication JobApplication { get; set; } = null!;

    public DateTime PlannedAtUtc { get; set; }
    public string? InterviewType { get; set; }
    public string? LocationOrLink { get; set; }
    public string? InterviewerName { get; set; }
    public decimal? Score { get; set; }
    public string? EvaluationNote { get; set; }
    public bool? IsRecommended { get; set; }
    public CandidateInterviewStatus Status { get; set; } = CandidateInterviewStatus.Scheduled;
}
