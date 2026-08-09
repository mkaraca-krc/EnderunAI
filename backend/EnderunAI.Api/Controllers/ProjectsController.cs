using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Contracts.Projects;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public sealed class ProjectsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = db.Projects.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        // Arşivlenen proje aktif listeden düşer; mali raporlar arşivli
        // projenin kayıtlarını göstermeye devam eder.
        if (!includeArchived)
            query = query.Where(x => !x.IsArchived);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.BranchId,
                BranchName = x.Branch.Name,
                x.EmployerCurrentAccountId,
                EmployerName = x.EmployerCurrentAccount != null ? x.EmployerCurrentAccount.Title : null,
                x.Code,
                x.Name,
                x.ContractNumber,
                x.ContractAmount,
                x.CurrencyCode,
                x.VatRate,
                x.WithholdingRate,
                x.IncreaseRate,
                x.CashRetentionRate,
                x.WithholdingTaxRate,
                x.MaterialDeductionRate,
                ContractType = (int)x.ContractType,
                x.DeviationAlertThresholdRate,
                x.Status,
                x.HealthStatus,
                x.IsArchived,
                x.ArchivedAtUtc,
                x.ArchiveReason,
                WarehouseCount = x.Warehouses.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.BranchId,
                BranchName = x.Branch.Name,
                x.EmployerCurrentAccountId,
                EmployerName = x.EmployerCurrentAccount != null ? x.EmployerCurrentAccount.Title : null,
                x.Code,
                x.Name,
                x.ContractNumber,
                x.ContractDate,
                x.ContractAmount,
                x.CurrencyCode,
                x.VatRate,
                x.WithholdingRate,
                x.IncreaseRate,
                x.CashRetentionRate,
                x.WithholdingTaxRate,
                x.MaterialDeductionRate,
                ContractType = (int)x.ContractType,
                ProgressPaymentPeriod = (int)x.ProgressPaymentPeriod,
                x.PaymentTerms,
                x.DeviationAlertThresholdRate,
                x.PlannedStartDate,
                x.PlannedEndDate,
                x.City,
                x.District,
                x.Address,
                x.Status,
                x.HealthStatus,
                x.IsArchived,
                x.ArchivedAtUtc,
                x.ArchiveReason,

                // Zincirin geri yönü: "bu proje hangi tekliften geldi".
                // Teklif modülü öncesi açılan projelerde boştur.
                x.SourceOfferId,
                SourceOfferNumber = db.Offers
                    .Where(o => o.Id == x.SourceOfferId)
                    .Select(o => o.OfferNumber)
                    .FirstOrDefault(),
                SourceOfferTitle = db.Offers
                    .Where(o => o.Id == x.SourceOfferId)
                    .Select(o => o.Title)
                    .FirstOrDefault(),
                Warehouses = x.Warehouses.Select(w => new
                {
                    w.Id,
                    w.Code,
                    w.Name,
                    w.Type,
                    w.IsActive
                })
            })
            .SingleOrDefaultAsync(cancellationToken);

        return project is null
            ? NotFound(new { message = "Proje bulunamadı." })
            : Ok(project);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.ProjectsCreate)]
    public async Task<IActionResult> Create(
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var company = await db.Companies
            .SingleOrDefaultAsync(
                x => x.Id == request.CompanyId && x.IsActive,
                cancellationToken);

        if (company is null)
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });

        var branch = await db.Branches
            .SingleOrDefaultAsync(
                x => x.Id == request.BranchId &&
                     x.CompanyId == request.CompanyId &&
                     x.IsActive,
                cancellationToken);

        if (branch is null)
            return BadRequest(new { message = "Seçilen şube bu şirkete ait değil veya pasif." });

        var (_, employerError) = await ValidateEmployerForStatusAsync(
            request.Status,
            request.EmployerCurrentAccountId,
            request.CompanyId,
            cancellationToken);

        if (employerError is not null)
            return BadRequest(new { message = employerError });

        var code = request.Code.Trim().ToUpperInvariant();

        if (await db.Projects.AnyAsync(
                x => x.CompanyId == request.CompanyId && x.Code == code,
                cancellationToken))
        {
            return Conflict(new { message = "Bu proje kodu zaten kullanılıyor." });
        }

        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken);

        try
        {
            var project = new Project
            {
                CompanyId = request.CompanyId,
                BranchId = request.BranchId,
                EmployerCurrentAccountId = request.EmployerCurrentAccountId,
                Code = code,
                Name = request.Name.Trim(),
                ContractNumber = request.ContractNumber?.Trim(),
                ContractDate = request.ContractDate.HasValue
                    ? DateTime.SpecifyKind(request.ContractDate.Value, DateTimeKind.Utc)
                    : null,
                ContractAmount = request.ContractAmount,
                CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                    ? "TRY"
                    : request.CurrencyCode.Trim().ToUpperInvariant(),
                VatRate = request.VatRate,
                WithholdingRate = request.WithholdingRate?.Trim(),
                PlannedStartDate = request.PlannedStartDate.HasValue
                    ? DateTime.SpecifyKind(request.PlannedStartDate.Value, DateTimeKind.Utc)
                    : null,
                PlannedEndDate = request.PlannedEndDate.HasValue
                    ? DateTime.SpecifyKind(request.PlannedEndDate.Value, DateTimeKind.Utc)
                    : null,
                City = request.City?.Trim(),
                District = request.District?.Trim(),
                Address = request.Address?.Trim(),
                Status = request.Status,
                ContractType = request.ContractType,
                DeviationAlertThresholdRate = request.DeviationAlertThresholdRate,
                HealthStatus = ProjectHealthStatus.Green
            };

            db.Projects.Add(project);
            await db.SaveChangesAsync(cancellationToken);

            var warehouse = new Warehouse
            {
                CompanyId = project.CompanyId,
                BranchId = project.BranchId,
                ProjectId = project.Id,
                Code = $"{project.Code}-DEPO",
                Name = $"{project.Name} Şantiye Deposu",
                Type = WarehouseType.Site,
                Address = project.Address
            };

            db.Warehouses.Add(warehouse);
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return Ok(new
            {
                message = "Proje ve şantiye deposu oluşturuldu.",
                project = new
                {
                    project.Id,
                    project.Code,
                    project.Name,
                    project.Status
                },
                warehouse = new
                {
                    warehouse.Id,
                    warehouse.Code,
                    warehouse.Name,
                    warehouse.Type
                }
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);

            return StatusCode(500, new
            {
                message = ex.Message,
                detail = ex.InnerException?.Message,
                type = ex.GetType().FullName,
                stack = ex.StackTrace
            });
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Proje adı zorunludur." });

        if (request.VatRate < 0 || request.VatRate > 100)
            return BadRequest(new { message = "KDV oranı 0 ile 100 arasında olmalıdır." });

        if (request.IncreaseRate < 0 || request.IncreaseRate > 100)
            return BadRequest(new { message = "Artış yüzdesi 0 ile 100 arasında olmalıdır." });

        if (request.CashRetentionRate < 0 ||
            request.CashRetentionRate > 100)
        {
            return BadRequest(new
            {
                message = "Nakit teminat kesintisi 0 ile 100 arasında olmalıdır."
            });
        }

        if (request.WithholdingTaxRate < 0 ||
            request.WithholdingTaxRate > 100)
        {
            return BadRequest(new
            {
                message = "Stopaj kesintisi 0 ile 100 arasında olmalıdır."
            });
        }

        if (request.MaterialDeductionRate < 0 ||
            request.MaterialDeductionRate > 100)
        {
            return BadRequest(new
            {
                message = "Malzeme kesintisi 0 ile 100 arasında olmalıdır."
            });
        }

        var plannedStartDate = ToUtc(request.PlannedStartDate);
        var plannedEndDate = ToUtc(request.PlannedEndDate);

        if (plannedStartDate.HasValue &&
            plannedEndDate.HasValue &&
            plannedEndDate.Value < plannedStartDate.Value)
        {
            return BadRequest(new
            {
                message = "Planlanan bitiş tarihi başlangıç tarihinden önce olamaz."
            });
        }

        var (_, employerError) = await ValidateEmployerForStatusAsync(
            request.Status,
            request.EmployerCurrentAccountId,
            project.CompanyId,
            cancellationToken);

        if (employerError is not null)
            return BadRequest(new { message = employerError });

        var previousStatus = project.Status;

        project.Status = request.Status;

        // Gerçekleşen tarihler: elle girilen değer her zaman kazanır.
        // Elle girilmediyse durum geçişi damgalar — proje aktife
        // alındığı gün fiilen başlamıştır, tamamlandı işaretlendiği gün
        // fiilen bitmiştir. İkisi de daha önce hiç yazılmıyordu:
        // gecikme hesabı gerçekleşeni değil yalnızca planı görüyordu.
        project.ActualStartDate = ToUtc(request.ActualStartDate)
            ?? project.ActualStartDate;

        project.ActualEndDate = ToUtc(request.ActualEndDate)
            ?? project.ActualEndDate;

        if (project.ActualStartDate is null &&
            request.Status == ProjectStatus.Active &&
            previousStatus != ProjectStatus.Active)
        {
            project.ActualStartDate = DateTime.UtcNow.Date;
        }

        if (project.ActualEndDate is null &&
            request.Status == ProjectStatus.Completed &&
            previousStatus != ProjectStatus.Completed)
        {
            project.ActualEndDate = DateTime.UtcNow.Date;
        }

        project.EmployerCurrentAccountId = request.EmployerCurrentAccountId;
        project.Name = request.Name.Trim();
        project.ContractNumber = request.ContractNumber?.Trim();
        project.ContractDate = ToUtc(request.ContractDate);
        project.ContractAmount = request.ContractAmount;
        project.CurrencyCode =
            string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? "TRY"
                : request.CurrencyCode.Trim().ToUpperInvariant();

        project.VatRate = request.VatRate;
        project.WithholdingRate = request.WithholdingRate?.Trim();
        project.IncreaseRate = request.IncreaseRate;
        project.CashRetentionRate = request.CashRetentionRate;
        project.WithholdingTaxRate = request.WithholdingTaxRate;
        project.MaterialDeductionRate = request.MaterialDeductionRate;
        project.ContractType = request.ContractType;
        project.DeviationAlertThresholdRate = request.DeviationAlertThresholdRate;

        project.PlannedStartDate = plannedStartDate;
        project.PlannedEndDate = plannedEndDate;
        project.City = request.City?.Trim();
        project.District = request.District?.Trim();
        project.Address = request.Address?.Trim();
        project.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Proje bilgileri güncellendi.",
            project.Id,
            project.Code,
            project.Name,
            project.Status,
            project.EmployerCurrentAccountId,
            project.ContractAmount,
            project.CurrencyCode,
            project.VatRate,
            project.WithholdingRate,
            project.IncreaseRate,
            project.CashRetentionRate,
            project.WithholdingTaxRate,
            project.MaterialDeductionRate
        });
    }

    /// <summary>
    /// Keşif statüsünde işveren opsiyoneldir (verilirse sadece şirkete
    /// ait olduğu kontrol edilir, Onaylı/Müşteri şartı aranmaz). Keşif
    /// dışındaki statülerde işveren zorunludur ve Onaylı + Müşteri
    /// rolünde olmalıdır.
    /// </summary>
    private async Task<(CurrentAccount? Employer, string? Error)> ValidateEmployerForStatusAsync(
        ProjectStatus status,
        Guid? employerCurrentAccountId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (employerCurrentAccountId is null)
        {
            return status == ProjectStatus.Kesif
                ? (null, null)
                : (null, "Keşif dışındaki statülerde işveren cari kartı zorunludur.");
        }

        var employer = await db.CurrentAccounts
            .SingleOrDefaultAsync(
                x => x.Id == employerCurrentAccountId.Value && x.CompanyId == companyId,
                cancellationToken);

        if (employer is null)
            return (null, "İşveren cari kartı bulunamadı.");

        if (status != ProjectStatus.Kesif)
        {
            if (employer.Status != CurrentAccountStatus.Approved)
                return (null, "Proje yalnızca onaylanmış cari kart ile açılabilir.");

            if (!employer.Roles.HasFlag(CurrentAccountRoles.Customer))
                return (null, "Seçilen cari kartın müşteri rolü bulunmuyor.");
        }

        return (employer, null);
    }

    [HttpGet("{id:guid}/summary")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetSummary(
        Guid id,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.BranchId,
                BranchName = x.Branch.Name,
                x.EmployerCurrentAccountId,
                EmployerName = x.EmployerCurrentAccount != null ? x.EmployerCurrentAccount.Title : null,
                x.Code,
                x.Name,
                x.ContractNumber,
                x.ContractDate,
                x.ContractAmount,
                x.CurrencyCode,
                x.VatRate,
                x.WithholdingRate,
                x.IncreaseRate,
                x.CashRetentionRate,
                x.WithholdingTaxRate,
                x.MaterialDeductionRate,
                ContractType = (int)x.ContractType,
                x.DeviationAlertThresholdRate,
                x.PlannedStartDate,
                x.PlannedEndDate,
                x.ActualStartDate,
                x.ActualEndDate,
                x.City,
                x.District,
                x.Address,
                x.Status,
                x.HealthStatus,
                x.HealthReason,
                WarehouseCount = x.Warehouses.Count(w => w.IsActive)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        var personnelQuery = db.PersonnelAssignments
            .AsNoTracking()
            .Where(x =>
                x.ProjectId == id &&
                x.IsActive &&
                x.EndDate == null &&
                x.Personnel.IsActive);

        var activePersonnelCount =
            await personnelQuery.CountAsync(cancellationToken);

        var primaryPersonnelCount =
            await personnelQuery.CountAsync(
                x => x.IsPrimaryAssignment,
                cancellationToken);

        var personnelByRole = await personnelQuery
            .GroupBy(x => x.Role ?? "Görev Belirtilmedi")
            .Select(group => new
            {
                Role = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToListAsync(cancellationToken);

        var purchaseRequestCount = await db.PurchaseRequests
            .AsNoTracking()
            .CountAsync(x => x.ProjectId == id, cancellationToken);

        var pendingPurchaseCount = await db.PurchaseRequests
            .AsNoTracking()
            .CountAsync(
                x => x.ProjectId == id &&
                     x.Status != PurchaseRequestStatus.Completed &&
                     x.Status != PurchaseRequestStatus.Cancelled &&
                     x.Status != PurchaseRequestStatus.Rejected,
                cancellationToken);

        return Ok(new
        {
            project,
            metrics = new
            {
                ActivePersonnelCount = activePersonnelCount,
                PrimaryPersonnelCount = primaryPersonnelCount,
                project.WarehouseCount,

                PurchaseRequestCount = purchaseRequestCount,
                PendingPurchaseCount = pendingPurchaseCount,

                ClaimCount = 0,
                PendingClaimCount = 0,

                DocumentCount = 0,
                VehicleCount = 0,
                RiskCount = 0,
                CriticalRiskCount = 0
            },
            personnelByRole,
            aiSummary = new
            {
                CriticalAlertCount = 0,
                WarningCount = activePersonnelCount == 0 ? 1 : 0,
                Messages = activePersonnelCount == 0
                    ? new[]
                    {
                        "Projeye henüz aktif personel atanmamış."
                    }
                    : Array.Empty<string>()
            }
        });
    }

    [HttpGet("{id:guid}/personnel")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetProjectPersonnel(
        Guid id,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var items = await db.PersonnelAssignments
            .AsNoTracking()
            .Where(x => x.ProjectId == id)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.StartDate)
            .Select(x => new
            {
                AssignmentId = x.Id,
                x.PersonnelId,
                x.Personnel.EmployeeNumber,
                x.Personnel.FirstName,
                x.Personnel.LastName,
                FullName =
                    x.Personnel.FirstName + " " + x.Personnel.LastName,
                x.Personnel.Phone,
                x.Personnel.JobTitle,
                x.Personnel.Profession,
                x.Role,
                x.StartDate,
                x.EndDate,
                x.IsPrimaryAssignment,
                AssignmentIsActive = x.IsActive,
                PersonnelIsActive = x.Personnel.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }


    /// <summary>
    /// İcmal satırı bazında dört fiyat ve kâr: sözleşme, kütüphane
    /// referansı (ÇŞB/TEDAŞ), şirketin geçmiş gerçekleşme ortalaması ve
    /// bu projedeki anlık maliyet.
    ///
    /// Kâr her zaman SÖZLEŞME eksi ANLIK MALİYET'tir; referans ve
    /// ortalama karara yardımcı bilgilerdir.
    /// </summary>
    // Poz bazında birim fiyat ve KÂR MARJI taşıyor. projects.view depo,
    // araç ve sekreterya rollerinde de var; sözleşme marjı oradan
    // görünmemeli. İcmal ilerleme ucuyla aynı kapı: tutar içeren proje
    // verisi hakediş görüntüleme iznine bağlı.
    [HttpGet("{id:guid}/kar-analizi")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> GetProfitBreakdown(
        Guid id,
        [FromQuery] int? referenceYear,
        [FromServices] Services.Projects.IBoqProfitService profits,
        CancellationToken cancellationToken)
    {
        var result = await profits.GetAsync(id, referenceYear, cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Silme öncesi etki özeti: bağlı kayıt sayıları ve kalıcı silmeyi
    /// engelleyen kesinleşmiş kayıtlar.
    /// </summary>
    [HttpGet("{id:guid}/deletion-impact")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsDelete)]
    public async Task<IActionResult> GetDeletionImpact(
        Guid id,
        [FromServices] IProjectDeletionService deletionService,
        CancellationToken cancellationToken)
    {
        var impact = await deletionService.GetImpactAsync(id, cancellationToken);

        return impact is null ? NotFound() : Ok(impact);
    }

    /// <summary>Projeyi arşive alır — veriler durur, aktif listelerden düşer.</summary>
    [HttpPost("{id:guid}/archive")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsDelete)]
    public async Task<IActionResult> Archive(
        Guid id,
        [FromBody] ArchiveProjectRequest request,
        [FromServices] IProjectDeletionService deletionService,
        CancellationToken cancellationToken)
    {
        var result = await deletionService.ArchiveAsync(
            id, request?.Reason, GetCurrentUserId(), User.Identity?.Name, cancellationToken);

        return result.Success
            ? Ok(new { message = result.Message })
            : BadRequest(new { message = result.Message });
    }

    [HttpPost("{id:guid}/unarchive")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsDelete)]
    public async Task<IActionResult> Unarchive(
        Guid id,
        [FromServices] IProjectDeletionService deletionService,
        CancellationToken cancellationToken)
    {
        var result = await deletionService.UnarchiveAsync(
            id, GetCurrentUserId(), User.Identity?.Name, cancellationToken);

        return result.Success
            ? Ok(new { message = result.Message })
            : BadRequest(new { message = result.Message });
    }

    /// <summary>
    /// Kalıcı silme. Kesinleşmiş kaydı olan projelerde reddedilir; onay için
    /// proje kodunun birebir yazılması gerekir.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsDelete)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromBody] DeleteProjectRequest request,
        [FromServices] IProjectDeletionService deletionService,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ConfirmationCode))
            return BadRequest(new { message = "Onay için proje kodunu yazmalısınız." });

        var result = await deletionService.HardDeleteAsync(
            id, request.ConfirmationCode, GetCurrentUserId(), User.Identity?.Name, cancellationToken);

        return result.Success
            ? Ok(new { message = result.Message })
            : BadRequest(new { message = result.Message });
    }

    private Guid? GetCurrentUserId()
    {
        var value =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static DateTime? ToUtc(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }
}
