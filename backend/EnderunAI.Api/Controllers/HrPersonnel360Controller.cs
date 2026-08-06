using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
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
    ISalaryVisibilityService salaryVisibility,
    IExtraPaymentVisibilityService extraPaymentVisibility,
    SalaryTakeHomeService takeHome,
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

        // --- Ücret gizliliği ---
        // Bu uç personnel.view ile açılıyor ama finansal blokta maaş,
        // bordro ve avans rakamı taşıyor. Şantiye Şefi, Formen ve
        // Teknik Koordinatör'de personnel.view var, salary.view yok:
        // gizleme olmadan 360 kartı herkesin ücretini sızdırıyordu.
        var canViewSalary = await salaryVisibility.CanViewSalaryAsync(cancellationToken);

        // Elden ödeme maaşın üstüne bir kat daha: maaşı göremeyen
        // hiçbir koşulda göremez, gören de ayrıca extra_payment.view
        // istemek zorunda.
        var canViewExtraPayment = canViewSalary &&
            await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken);

        decimal? extraPaymentAmount = null;

        if (canViewExtraPayment)
        {
            var effectiveExtras = await takeHome.LoadEffectiveExtraPaymentsAsync(
                [personnelId], cancellationToken);

            // Yetki varsa kayıt yoksa da 0 döner; null yalnızca
            // "göremiyorsun" demektir.
            extraPaymentAmount = effectiveExtras.GetValueOrDefault(personnelId);
        }

        var officialNetSalary = canViewSalary && currentSalary is not null
            ? SalaryTakeHomeService.ResolveOfficialNet(
                currentSalary,
                await takeHome.TryLoadPayrollParametersAsync(
                    currentSalary.CompanyId,
                    currentSalary.EffectiveStartDate.Year,
                    cancellationToken))
            : null;

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
                MonthlySalary = canViewSalary ? profile.MonthlySalary : null,
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
            // Tutar alanları salary.view yoksa null döner; arayüzde
            // gizlenmez, sorgudan hiç çıkmaz. payrollCount tutar
            // taşımadığı için açık kalır.
            financial = new
            {
                salaryHidden = !canViewSalary,
                currentGrossSalary = canViewSalary
                    ? currentSalary?.GrossSalary ?? profile.MonthlySalary ?? 0m
                    : (decimal?)null,
                currentNetSalary = canViewSalary
                    ? currentSalary?.NetSalary ?? profile.MonthlySalary ?? 0m
                    : (decimal?)null,
                // Kartın hesaplanmış resmî neti: net esaslıda anlaşılan
                // tutar, brüt esaslıda brütten hesaplanan. NetSalary
                // elle girilen ve boş kalabilen eski alan.
                officialNetSalary,
                // Elden ödeme ve toplam ele geçen: yetki yoksa null.
                extraPaymentMonthlyAmount = extraPaymentAmount,
                extraPaymentHidden = extraPaymentAmount is null,
                totalTakeHome =
                    (officialNetSalary ?? (canViewSalary ? currentSalary?.NetSalary : null))
                        is decimal net && extraPaymentAmount is decimal extra
                        ? net + extra
                        : (decimal?)null,
                currentDailyRate = canViewSalary
                    ? currentSalary?.DailyRate ?? 0m
                    : (decimal?)null,
                currentHourlyRate = canViewSalary
                    ? currentSalary?.HourlyRate ?? 0m
                    : (decimal?)null,
                currencyCode =
                    currentSalary?.CurrencyCode ??
                    lastPayroll?.CurrencyCode ??
                    "TRY",
                totalApprovedBonus = canViewSalary
                    ? approvedPayrolls.Sum(x => x.BonusAmount)
                    : (decimal?)null,
                totalDeduction = canViewSalary
                    ? approvedPayrolls.Sum(x => x.TotalDeductions)
                    : (decimal?)null,
                totalApprovedAdvance = canViewSalary
                    ? advances
                        .Where(x =>
                            x.Status == HrApprovalStatus.Approved ||
                            x.Status == HrApprovalStatus.Paid)
                        .Sum(x => x.ApprovedAmount)
                    : (decimal?)null,
                totalPaidAdvance = canViewSalary
                    ? advances
                        .Where(x => x.Status == HrApprovalStatus.Paid)
                        .Sum(x => x.ApprovedAmount)
                    : (decimal?)null,
                totalNetPayroll = canViewSalary
                    ? approvedPayrolls.Sum(x => x.NetPayableAmount)
                    : (decimal?)null,
                lastPayrollNetAmount = canViewSalary
                    ? lastPayroll?.NetPayableAmount ?? 0m
                    : (decimal?)null,
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
