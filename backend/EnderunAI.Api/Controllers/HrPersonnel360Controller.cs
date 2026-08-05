using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Isg;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hr/personnel-360")]
public sealed class HrPersonnel360Controller(
    AppDbContext appDb,
    HrDbContext hrDb,
    IUserAuthorizationService authorizationService,
    Security.CurrentUser.ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// İSG eğitim ve yetki belgesi kayıtları isg.view ile korunuyor.
    /// 360 ekranı personnel.view ile açıldığı için, izni olmayan
    /// kullanıcıya bu bölüm doldurulmadan dönüyor — sayıyı göstermek de
    /// izin sınırını genişletirdi.
    ///
    /// Sağlık raporu bu ekrana hiç girmiyor: tıbbi veri kendi dar
    /// izniyle yalnızca İSG ekranlarında görünür.
    /// </summary>
    private async Task<bool> CanViewIsgAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorizationService.GetAsync(userId, cancellationToken);

        return snapshot is not null && snapshot.IsActive &&
               snapshot.Permissions.Contains(
                   PermissionCatalog.Keys.IsgView, StringComparer.OrdinalIgnoreCase);
    }

    [HttpGet("{personnelId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> Get(
        Guid personnelId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var periodEnd = DbDate(endDate ?? DateTime.UtcNow);
        var periodStart = DbDate(startDate ?? periodEnd.AddDays(-30));

        if (periodEnd < periodStart)
        {
            return BadRequest(new
            {
                message = "Bitiş tarihi başlangıç tarihinden önce olamaz."
            });
        }

        var profile = await appDb.Personnel
            .AsNoTracking()
            .Where(x => x.Id == personnelId)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.BranchId,
                x.EmployeeNumber,
                x.FirstName,
                x.LastName,
                x.IdentityNumber,
                x.BirthDate,
                x.Phone,
                x.Email,
                x.Address,
                x.JobTitle,
                x.Profession,
                x.SgkRegistrationNumber,
                x.EmploymentStartDate,
                x.EmploymentEndDate,
                x.MonthlySalary,
                x.Status
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return NotFound(new
            {
                message = "Personel kaydı bulunamadı."
            });
        }

        var assignments = await appDb.PersonnelAssignments
            .AsNoTracking()
            .Where(x => x.PersonnelId == personnelId)
            .OrderByDescending(x => x.IsPrimaryAssignment)
            .ThenByDescending(x => x.StartDate)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                x.StartDate,
                x.EndDate,
                x.Role,
                x.Notes,
                x.IsPrimaryAssignment
            })
            .ToListAsync(cancellationToken);

        var today = DbDate(DateTime.UtcNow);
        var currentSalary = await hrDb.SalaryDefinitions
            .AsNoTracking()
            .Where(x =>
                x.PersonnelId == personnelId &&
                x.EffectiveStartDate <= today &&
                (!x.EffectiveEndDate.HasValue ||
                 x.EffectiveEndDate.Value >= today))
            .OrderByDescending(x => x.EffectiveStartDate)
            .FirstOrDefaultAsync(cancellationToken);

        var payrolls = await hrDb.PayrollRecords
            .AsNoTracking()
            .Where(x => x.PersonnelId == personnelId)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ToListAsync(cancellationToken);

        var leaves = await hrDb.LeaveRequests
            .AsNoTracking()
            .Where(x =>
                x.PersonnelId == personnelId &&
                x.EndDate >= periodStart &&
                x.StartDate <= periodEnd)
            .ToListAsync(cancellationToken);

        var overtimes = await hrDb.OvertimeRequests
            .AsNoTracking()
            .Where(x =>
                x.PersonnelId == personnelId &&
                x.WorkDate >= periodStart &&
                x.WorkDate <= periodEnd)
            .ToListAsync(cancellationToken);

        var advances = await hrDb.AdvanceRequests
            .AsNoTracking()
            .Where(x => x.PersonnelId == personnelId)
            .ToListAsync(cancellationToken);

        // --- İSG eğitim ve yetki belgeleri ---
        var canViewIsg = await CanViewIsgAsync(cancellationToken);
        var todayOnly = DateOnly.FromDateTime(DateTime.UtcNow);

        var isgTrainings = canViewIsg
            ? await appDb.IsgTrainings
                .AsNoTracking()
                .Where(x => x.PersonnelId == personnelId)
                .OrderByDescending(x => x.TrainingDate)
                .ToListAsync(cancellationToken)
            : [];

        var isgCertificates = canViewIsg
            ? await appDb.IsgCertificates
                .AsNoTracking()
                .Where(x => x.PersonnelId == personnelId)
                .OrderByDescending(x => x.IssueDate)
                .ToListAsync(cancellationToken)
            : [];

        var approvedLeaves = leaves
            .Where(x => x.Status == HrApprovalStatus.Approved)
            .ToArray();
        var approvedOvertimes = overtimes
            .Where(x => x.Status == HrApprovalStatus.Approved)
            .ToArray();
        var approvedPayrolls = payrolls
            .Where(x =>
                x.Status == PayrollStatus.Approved ||
                x.Status == PayrollStatus.Paid)
            .ToArray();
        var lastPayroll = payrolls.FirstOrDefault();

        var alerts = new List<object>();
        var attentionPoints = new List<string>();
        if (currentSalary is null)
        {
            const string title = "Aktif maaş kartı bulunmuyor";
            alerts.Add(new
            {
                code = "SALARY_CARD_MISSING",
                severity = "Medium",
                title,
                description = "Personel için geçerli tarihli maaş kartı tanımlanmalıdır.",
                dueDate = (DateTime?)null
            });
            attentionPoints.Add(title);
        }

        var activeAssignments = assignments.Count(x =>
            !x.EndDate.HasValue || x.EndDate.Value.Date >= today);
        if (activeAssignments == 0)
        {
            const string title = "Aktif proje ataması bulunmuyor";
            alerts.Add(new
            {
                code = "ACTIVE_ASSIGNMENT_MISSING",
                severity = "Low",
                title,
                description = "Personelin güncel proje veya şantiye ataması kontrol edilmelidir.",
                dueDate = (DateTime?)null
            });
            attentionPoints.Add(title);
        }

        var isActive = profile.Status == PersonnelStatus.Active ||
                       profile.Status == PersonnelStatus.OnLeave;
        var riskScore = !isActive
            ? 70
            : currentSalary is null
                ? 30
                : 10;
        var riskLevel = riskScore >= 60
            ? "High"
            : riskScore >= 25
                ? "Medium"
                : "Low";

        var positiveFindings = new List<string>();
        if (isActive)
            positiveFindings.Add("Personel kaydı aktif.");
        if (currentSalary is not null)
            positiveFindings.Add("Geçerli maaş kartı mevcut.");
        if (activeAssignments > 0)
            positiveFindings.Add("Aktif proje veya şantiye ataması mevcut.");

        return Ok(new
        {
            profile = new
            {
                profile.Id,
                profile.CompanyId,
                profile.BranchId,
                profile.EmployeeNumber,
                profile.FirstName,
                profile.LastName,
                fullName = $"{profile.FirstName} {profile.LastName}".Trim(),
                profile.IdentityNumber,
                profile.BirthDate,
                profile.Phone,
                profile.Email,
                profile.Address,
                profile.JobTitle,
                profile.Profession,
                profile.SgkRegistrationNumber,
                profile.EmploymentStartDate,
                profile.EmploymentEndDate,
                profile.MonthlySalary,
                status = (int)profile.Status,
                statusName = PersonnelStatusName(profile.Status)
            },
            assignments = assignments.Select(x => new
            {
                x.Id,
                x.ProjectId,
                x.StartDate,
                x.EndDate,
                x.Role,
                x.Notes,
                x.IsPrimaryAssignment,
                isActive = !x.EndDate.HasValue ||
                           x.EndDate.Value.Date >= today
            }),
            attendance = new
            {
                startDate = periodStart,
                endDate = periodEnd,
                recordCount = 0,
                approvedRecordCount = 0,
                normalHours = 0m,
                overtimeHours = approvedOvertimes.Sum(x => x.ApprovedHours),
                nightShiftHours = approvedOvertimes
                    .Where(x => x.IsNightWork)
                    .Sum(x => x.ApprovedHours),
                sundayHours = approvedOvertimes
                    .Where(x => x.IsSundayWork)
                    .Sum(x => x.ApprovedHours),
                publicHolidayHours = approvedOvertimes
                    .Where(x => x.IsPublicHolidayWork)
                    .Sum(x => x.ApprovedHours),
                totalHours = approvedOvertimes.Sum(x => x.ApprovedHours)
            },
            financial = new
            {
                currentGrossSalary =
                    currentSalary?.GrossSalary ??
                    profile.MonthlySalary ??
                    0m,
                currentNetSalary =
                    currentSalary?.NetSalary ??
                    profile.MonthlySalary ??
                    0m,
                currentDailyRate = currentSalary?.DailyRate ?? 0m,
                currentHourlyRate = currentSalary?.HourlyRate ?? 0m,
                currencyCode =
                    currentSalary?.CurrencyCode ??
                    lastPayroll?.CurrencyCode ??
                    "TRY",
                totalApprovedBonus = approvedPayrolls.Sum(x => x.BonusAmount),
                totalDeduction = approvedPayrolls.Sum(x => x.TotalDeductions),
                totalApprovedAdvance = advances
                    .Where(x =>
                        x.Status == HrApprovalStatus.Approved ||
                        x.Status == HrApprovalStatus.Paid)
                    .Sum(x => x.ApprovedAmount),
                totalPaidAdvance = advances
                    .Where(x => x.Status == HrApprovalStatus.Paid)
                    .Sum(x => x.ApprovedAmount),
                totalNetPayroll = approvedPayrolls.Sum(x => x.NetPayableAmount),
                lastPayrollNetAmount = lastPayroll?.NetPayableAmount ?? 0m,
                payrollCount = payrolls.Count
            },
            humanResources = new
            {
                leaveCount = leaves.Count,
                approvedLeaveDays = approvedLeaves.Sum(x => x.TotalDays),
                overtimeRequestCount = overtimes.Count,
                approvedOvertimeHours = approvedOvertimes.Sum(x => x.ApprovedHours),
                trainingCount = isgTrainings.Count,
                // Süresi dolmamış (veya süresiz) eğitim "geçerli" sayılır.
                completedTrainingCount = isgTrainings.Count(x =>
                    x.ValidUntil is null || x.ValidUntil >= todayOnly),
                certificateCount = isgCertificates.Count,
                validCertificateCount = isgCertificates.Count(x =>
                    x.ExpiryDate is null || x.ExpiryDate >= todayOnly),
                expiredCertificateCount = isgCertificates.Count(x =>
                    x.ExpiryDate is not null && x.ExpiryDate < todayOnly),
                // İSG bölümü izin yokluğundan boş dönüyorsa true.
                isgGizli = !canViewIsg,
                competencyCount = 0,
                verifiedCompetencyCount = 0,
                performanceReviewCount = 0,
                latestPerformanceScore = (decimal?)null,
                openDisciplinaryCount = 0,
                activeAssetCount = 0,
                careerActionCount = 0
            },
            trainings = isgTrainings.Select(x => new
            {
                x.Id,
                trainingType = (int)x.TrainingType,
                x.Topic,
                x.TrainingDate,
                x.DurationHours,
                x.ValidUntil,
                x.TrainerName,
                validityStatusName = IsgValidityCalculator.StatusName(
                    IsgValidityCalculator.Evaluate(x.ValidUntil, todayOnly)),
                validityColor = IsgValidityCalculator.StatusColor(
                    IsgValidityCalculator.Evaluate(x.ValidUntil, todayOnly))
            }),
            certificates = isgCertificates.Select(x => new
            {
                x.Id,
                certificateType = (int)x.CertificateType,
                x.CustomTypeName,
                x.CertificateNumber,
                x.IssuedBy,
                x.IssueDate,
                x.ExpiryDate,
                validityStatusName = IsgValidityCalculator.StatusName(
                    IsgValidityCalculator.Evaluate(x.ExpiryDate, todayOnly)),
                validityColor = IsgValidityCalculator.StatusColor(
                    IsgValidityCalculator.Evaluate(x.ExpiryDate, todayOnly))
            }),
            competencies = Array.Empty<object>(),
            performanceReviews = Array.Empty<object>(),
            disciplinaryRecords = Array.Empty<object>(),
            assets = Array.Empty<object>(),
            careerHistory = Array.Empty<object>(),
            alerts,
            analysis = new
            {
                riskLevel,
                riskScore,
                summary = riskLevel switch
                {
                    "High" => "Personel kaydı ve aktif çalışma durumu kontrol edilmelidir.",
                    "Medium" => "Personel kaydı çalışıyor; eksik İK tanımları tamamlanmalıdır.",
                    _ => "Personelin temel İK kayıtları güncel ve kullanılabilir durumda."
                },
                positiveFindings,
                attentionPoints
            }
        });
    }

    private static DateTime DbDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    private static string PersonnelStatusName(PersonnelStatus status) =>
        status switch
        {
            PersonnelStatus.Candidate => "Aday",
            PersonnelStatus.Active => "Aktif",
            PersonnelStatus.OnLeave => "İzinli",
            PersonnelStatus.Suspended => "Askıda",
            PersonnelStatus.Terminated => "İşten ayrıldı",
            _ => status.ToString()
        };
}
