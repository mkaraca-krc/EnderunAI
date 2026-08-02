using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hr/recruitment")]
public sealed class HrRecruitmentController(AppDbContext db) : ControllerBase
{
    private static readonly string[] EmploymentTypeNames =
        { "Tam Zamanlı", "Yarı Zamanlı", "Sözleşmeli", "Staj", "Mevsimlik" };

    private static readonly string[] InterviewTypeNames =
        { "Telefon", "Video", "Yüz Yüze", "Teknik" };

    // ---- Job postings ----

    [HttpGet("postings")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetPostings(
        [FromQuery] Guid? companyId,
        [FromQuery] int? status,
        CancellationToken cancellationToken)
    {
        var query = db.JobPostings.AsNoTracking().AsQueryable();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (status.HasValue) query = query.Where(x => (int)x.Status == status.Value);

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new { Posting = x, ApplicationCount = x.Applications.Count })
            .ToListAsync(cancellationToken);

        var companyNames = await CompanyNameMap(rows.Select(r => r.Posting.CompanyId), cancellationToken);
        return Ok(rows.Select(r => PostingDto(r.Posting, r.ApplicationCount, companyNames)));
    }

    [HttpPost("postings")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> CreatePosting(
        SaveJobPostingRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "İlan başlığı zorunludur." });

        var companyId = await ResolveCompanyIdAsync(request.CompanyId, cancellationToken);
        if (companyId is null)
            return BadRequest(new { message = "Geçerli bir şirket bulunamadı." });

        var sequence = await db.JobPostings.CountAsync(x => x.CompanyId == companyId.Value, cancellationToken);

        var item = new JobPosting
        {
            CompanyId = companyId.Value,
            PostingNumber = $"ILN-{DateTime.UtcNow:yyyy}-{sequence + 1:D4}"
        };
        ApplyPosting(item, request);

        db.JobPostings.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        var companyNames = await CompanyNameMap(new[] { item.CompanyId }, cancellationToken);
        return Ok(PostingDto(item, 0, companyNames));
    }

    [HttpPut("postings/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> UpdatePosting(
        Guid id,
        SaveJobPostingRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.JobPostings.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "İlan bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "İlan başlığı zorunludur." });

        ApplyPosting(item, request);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var count = await db.JobApplications.CountAsync(x => x.JobPostingId == id, cancellationToken);
        var companyNames = await CompanyNameMap(new[] { item.CompanyId }, cancellationToken);
        return Ok(PostingDto(item, count, companyNames));
    }

    [HttpPost("postings/{id:guid}/publish")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> PublishPosting(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.JobPostings.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "İlan bulunamadı." });

        item.Status = JobPostingStatus.Published;
        item.PublishedAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "İlan yayınlandı." });
    }

    [HttpDelete("postings/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelDelete)]
    public async Task<IActionResult> DeletePosting(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.JobPostings.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "İlan bulunamadı." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---- Candidates ----

    [HttpGet("candidates")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetCandidates(CancellationToken cancellationToken)
    {
        var items = await db.JobCandidates.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(CandidateDto));
    }

    [HttpPost("candidates")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> CreateCandidate(
        SaveJobCandidateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return BadRequest(new { message = "Ad ve soyad zorunludur." });

        var companyId = await ResolveCompanyIdAsync(request.CompanyId, cancellationToken);
        if (companyId is null)
            return BadRequest(new { message = "Geçerli bir şirket bulunamadı." });

        var item = new JobCandidate { CompanyId = companyId.Value };
        ApplyCandidate(item, request);

        db.JobCandidates.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(CandidateDto(item));
    }

    [HttpPut("candidates/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> UpdateCandidate(
        Guid id,
        SaveJobCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.JobCandidates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Aday bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return BadRequest(new { message = "Ad ve soyad zorunludur." });

        ApplyCandidate(item, request);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(CandidateDto(item));
    }

    [HttpDelete("candidates/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelDelete)]
    public async Task<IActionResult> DeleteCandidate(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.JobCandidates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Aday bulunamadı." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---- Applications ----

    [HttpGet("applications")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetApplications(CancellationToken cancellationToken)
    {
        var items = await db.JobApplications.AsNoTracking()
            .Include(x => x.JobPosting)
            .Include(x => x.Candidate)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(ApplicationDto));
    }

    [HttpPost("applications")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> CreateApplication(
        SaveJobApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var posting = await db.JobPostings.SingleOrDefaultAsync(x => x.Id == request.JobPostingId, cancellationToken);
        if (posting is null)
            return NotFound(new { message = "İlan bulunamadı." });

        var candidateExists = await db.JobCandidates.AnyAsync(x => x.Id == request.CandidateId, cancellationToken);
        if (!candidateExists)
            return NotFound(new { message = "Aday bulunamadı." });

        var duplicate = await db.JobApplications.AnyAsync(
            x => x.JobPostingId == request.JobPostingId && x.CandidateId == request.CandidateId,
            cancellationToken);
        if (duplicate)
            return Conflict(new { message = "Bu aday bu ilana zaten başvurmuş." });

        var item = new JobApplication
        {
            CompanyId = posting.CompanyId,
            JobPostingId = request.JobPostingId,
            CandidateId = request.CandidateId
        };
        ApplyApplication(item, request);

        db.JobApplications.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        await db.Entry(item).Reference(x => x.JobPosting).LoadAsync(cancellationToken);
        await db.Entry(item).Reference(x => x.Candidate).LoadAsync(cancellationToken);

        return Ok(ApplicationDto(item));
    }

    [HttpPut("applications/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> UpdateApplication(
        Guid id,
        SaveJobApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.JobApplications
            .Include(x => x.JobPosting)
            .Include(x => x.Candidate)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Başvuru bulunamadı." });

        ApplyApplication(item, request);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ApplicationDto(item));
    }

    [HttpDelete("applications/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelDelete)]
    public async Task<IActionResult> DeleteApplication(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.JobApplications.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Başvuru bulunamadı." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---- Interviews ----

    [HttpGet("interviews")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetInterviews(CancellationToken cancellationToken)
    {
        var items = await db.CandidateInterviews.AsNoTracking()
            .Include(x => x.JobApplication).ThenInclude(x => x.JobPosting)
            .Include(x => x.JobApplication).ThenInclude(x => x.Candidate)
            .OrderByDescending(x => x.PlannedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(InterviewDto));
    }

    [HttpPost("interviews")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> CreateInterview(
        SaveCandidateInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var application = await db.JobApplications
            .Include(x => x.JobPosting)
            .Include(x => x.Candidate)
            .SingleOrDefaultAsync(x => x.Id == request.JobApplicationId, cancellationToken);

        if (application is null)
            return NotFound(new { message = "Başvuru bulunamadı." });

        var item = new CandidateInterview
        {
            CompanyId = application.CompanyId,
            JobApplicationId = request.JobApplicationId
        };
        ApplyInterview(item, request);

        db.CandidateInterviews.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        item.JobApplication = application;
        return Ok(InterviewDto(item));
    }

    [HttpPut("interviews/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> UpdateInterview(
        Guid id,
        SaveCandidateInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.CandidateInterviews
            .Include(x => x.JobApplication).ThenInclude(x => x.JobPosting)
            .Include(x => x.JobApplication).ThenInclude(x => x.Candidate)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Mülakat bulunamadı." });

        ApplyInterview(item, request);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(InterviewDto(item));
    }

    [HttpDelete("interviews/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelDelete)]
    public async Task<IActionResult> DeleteInterview(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.CandidateInterviews.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Mülakat bulunamadı." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---- helpers ----

    private static DateTime? ToUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    private async Task<Guid?> ResolveCompanyIdAsync(Guid? requested, CancellationToken cancellationToken)
    {
        if (requested.HasValue && requested.Value != Guid.Empty)
            return requested.Value;

        var first = await db.Companies.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return first == Guid.Empty ? null : first;
    }

    private async Task<Dictionary<Guid, string>> CompanyNameMap(
        IEnumerable<Guid> companyIds, CancellationToken cancellationToken) =>
        await db.Companies.AsNoTracking()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

    private static void ApplyPosting(JobPosting item, SaveJobPostingRequest request)
    {
        item.Title = request.Title.Trim();
        item.LocationName = request.WorkLocation?.Trim();
        item.EmploymentType = EmploymentTypeNames.ElementAtOrDefault(request.EmploymentType) ?? request.EmploymentType.ToString();
        item.RequiredHeadcount = request.Headcount <= 0 ? 1 : request.Headcount;
        item.Description = request.Description?.Trim() ?? string.Empty;
        item.Requirements = request.Requirements?.Trim();
        item.ClosingDateUtc = ToUtc(request.ApplicationDeadline);
        item.Status = (JobPostingStatus)request.Status;
        item.IsActive = request.IsActive;
    }

    private static void ApplyCandidate(JobCandidate item, SaveJobCandidateRequest request)
    {
        item.FirstName = request.FirstName.Trim();
        item.LastName = request.LastName.Trim();
        item.IdentityNumber = request.IdentityNumber?.Trim();
        item.BirthDate = ToUtc(request.BirthDate);
        item.PhoneNumber = request.Phone?.Trim();
        item.Email = request.Email?.Trim();
        item.Profession = request.CurrentPosition?.Trim();
        item.TotalExperienceYears = request.YearsOfExperience;
        item.EducationLevel = request.EducationLevel?.Trim();
        item.Notes = request.Notes?.Trim();
        item.Status = (JobCandidateStatus)request.Status;
        item.IsActive = request.IsActive;
    }

    private static void ApplyApplication(JobApplication item, SaveJobApplicationRequest request)
    {
        item.ApplicationDateUtc = ToUtc(request.ApplicationDate) ?? item.ApplicationDateUtc;
        item.ExpectedSalary = request.ExpectedSalary;
        item.EvaluationNote = request.Notes?.Trim();
        item.Status = (JobApplicationStatus)request.Status;
    }

    private static void ApplyInterview(CandidateInterview item, SaveCandidateInterviewRequest request)
    {
        item.PlannedAtUtc = DateTime.SpecifyKind(request.ScheduledAt, DateTimeKind.Utc);
        item.InterviewType = InterviewTypeNames.ElementAtOrDefault(request.Type) ?? request.Type.ToString();
        item.LocationOrLink = request.Location?.Trim();
        item.InterviewerName = request.InterviewerName?.Trim();
        item.Score = request.Score;
        item.EvaluationNote = request.Feedback?.Trim() ?? request.Notes?.Trim();
        item.Status = (CandidateInterviewStatus)request.Status;
    }

    private static object PostingDto(
        JobPosting x, int applicationCount, IReadOnlyDictionary<Guid, string> companyNames)
    {
        var employmentTypeIndex = Array.IndexOf(EmploymentTypeNames, x.EmploymentType);

        return new
        {
            x.Id,
            x.CompanyId,
            CompanyName = companyNames.GetValueOrDefault(x.CompanyId),
            x.ProjectId,
            code = x.PostingNumber,
            x.Title,
            department = (string?)null,
            departmentName = (string?)null,
            position = (string?)null,
            positionTitle = (string?)null,
            x.Description,
            x.Requirements,
            employmentType = employmentTypeIndex >= 0 ? employmentTypeIndex : 0,
            employmentTypeName = x.EmploymentType,
            workLocation = x.LocationName,
            headcount = x.RequiredHeadcount,
            openPositionCount = x.RequiredHeadcount,
            applicationDeadline = x.ClosingDateUtc,
            Status = (int)x.Status,
            statusName = x.Status.ToString(),
            x.IsActive,
            publishedAt = x.PublishedAtUtc,
            createdAt = x.CreatedAtUtc,
            applicationCount,
            applicationsCount = applicationCount
        };
    }

    private static object CandidateDto(JobCandidate x) => new
    {
        x.Id,
        x.FirstName,
        x.LastName,
        fullName = $"{x.FirstName} {x.LastName}".Trim(),
        x.IdentityNumber,
        x.BirthDate,
        phone = x.PhoneNumber,
        x.Email,
        address = x.City,
        x.EducationLevel,
        schoolName = (string?)null,
        yearsOfExperience = x.TotalExperienceYears,
        x.CurrentCompany,
        currentPosition = x.Profession,
        x.CvFilePath,
        linkedinUrl = (string?)null,
        x.Notes,
        Status = (int)x.Status,
        statusName = x.Status.ToString(),
        x.IsActive,
        createdAt = x.CreatedAtUtc
    };

    private static object ApplicationDto(JobApplication x) => new
    {
        x.Id,
        x.JobPostingId,
        jobPostingTitle = x.JobPosting?.Title,
        candidateId = x.CandidateId,
        jobCandidateId = x.CandidateId,
        candidateFullName = x.Candidate is null
            ? null
            : $"{x.Candidate.FirstName} {x.Candidate.LastName}".Trim(),
        applicationDate = x.ApplicationDateUtc,
        source = x.Candidate?.Source,
        x.ExpectedSalary,
        Status = (int)x.Status,
        statusName = x.Status.ToString(),
        notes = x.EvaluationNote,
        createdAt = x.CreatedAtUtc
    };

    private static object InterviewDto(CandidateInterview x)
    {
        var typeIndex = Array.IndexOf(InterviewTypeNames, x.InterviewType);

        return new
        {
            x.Id,
            x.JobApplicationId,
            applicationId = x.JobApplicationId,
            candidateId = x.JobApplication?.CandidateId,
            candidateFullName = x.JobApplication?.Candidate is null
                ? null
                : $"{x.JobApplication.Candidate.FirstName} {x.JobApplication.Candidate.LastName}".Trim(),
            jobPostingTitle = x.JobApplication?.JobPosting?.Title,
            scheduledAt = x.PlannedAtUtc,
            type = typeIndex >= 0 ? typeIndex : 0,
            typeName = x.InterviewType,
            location = x.LocationOrLink,
            interviewerName = x.InterviewerName,
            x.InterviewerUserId,
            Status = (int)x.Status,
            statusName = x.Status.ToString(),
            x.Score,
            feedback = x.EvaluationNote,
            notes = x.EvaluationNote,
            createdAt = x.CreatedAtUtc
        };
    }
}

public sealed record SaveJobPostingRequest(
    Guid? CompanyId,
    string Title,
    string? WorkLocation,
    int EmploymentType,
    int Headcount,
    string? Description,
    string? Requirements,
    DateTime? ApplicationDeadline,
    int Status,
    bool IsActive);

public sealed record SaveJobCandidateRequest(
    string FirstName,
    string LastName,
    string? IdentityNumber,
    DateTime? BirthDate,
    string? Phone,
    string? Email,
    string? Address,
    string? EducationLevel,
    int? YearsOfExperience,
    string? CurrentPosition,
    string? Notes,
    int Status,
    bool IsActive,
    Guid? CompanyId = null);

public sealed record SaveJobApplicationRequest(
    Guid JobPostingId,
    Guid CandidateId,
    DateTime? ApplicationDate,
    string? Source,
    decimal? ExpectedSalary,
    string? Notes,
    int Status);

public sealed record SaveCandidateInterviewRequest(
    Guid JobApplicationId,
    DateTime ScheduledAt,
    int Type,
    string? Location,
    string? InterviewerName,
    decimal? Score,
    string? Feedback,
    string? Notes,
    int Status);
