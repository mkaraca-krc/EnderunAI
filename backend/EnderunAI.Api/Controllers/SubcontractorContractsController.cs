using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Subcontractors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Controllers;

/// <summary>Sözleşmenin kapsadığı bir icmal kısmı.</summary>
public sealed record SubcontractorContractSectionRequest(
    Guid ProjectHakedisSectionId,
    decimal SectionAmount,
    int Order);

/// <summary>Ekip tam liste olarak gönderilir.</summary>
public sealed record ReplaceSubcontractorTeamRequest(
    IReadOnlyList<Guid>? PersonnelIds);

public sealed record SaveSubcontractorContractRequest(
    Guid CompanyId,
    Guid CurrentAccountId,
    Guid ProjectId,
    Guid? ProjectSiteId,
    string ContractNumber,
    string WorkDescription,
    int ContractType,
    decimal ContractAmount,
    string? CurrencyCode,
    DateTime StartDate,
    DateTime? EndDate,
    decimal RetentionRate,
    int WithholdingNumerator,
    int WithholdingDenominator,
    int MealResponsibility,
    int AccommodationResponsibility,
    int SocialSecurityResponsibility,
    int MaterialResponsibility,
    int OhsResponsibility,
    string? Notes,
    IReadOnlyList<SubcontractorContractSectionRequest>? Sections);

/// <summary>
/// Taşeron sözleşmeleri.
///
/// Taşeron ayrı bir kart değil: "taşeron" işaretli bir CARİ + bu
/// sözleşmedir. Ayrı bir taşeron tablosu, aynı firmayı hem tedarikçi
/// hem taşeron olarak iki kez kaydettirir ve cari bakiyeyi ikiye
/// bölerdi.
///
/// Kapsam tikleri (yemek/konaklama/SGK/malzeme/İSG) hakedişin kesinti
/// kalemlerini belirler; hakediş bunları sözleşmeden okur, kullanıcı
/// elle kurmaz.
///
/// Tutar alanları burada gizlenmiyor: subcontractor.view zaten dar bir
/// izin. Elden ödeme ve elden avans AYRI tablolarda durur ve
/// extra_payment.* ister (T5/T6).
/// </summary>
[ApiController]
[Authorize]
[Route("api/subcontractor-contracts")]
public sealed class SubcontractorContractsController(
    AppDbContext db,
    SubcontractorTeamService teamService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? currentAccountId,
        CancellationToken cancellationToken)
    {
        var query = db.SubcontractorContracts.AsNoTracking();

        if (companyId is Guid company)
            query = query.Where(x => x.CompanyId == company);

        if (projectId is Guid project)
            query = query.Where(x => x.ProjectId == project);

        if (currentAccountId is Guid account)
            query = query.Where(x => x.CurrentAccountId == account);

        var items = await query
            .OrderByDescending(x => x.StartDate)
            .ThenBy(x => x.ContractNumber)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.CurrentAccountId,
                SubcontractorTitle = x.CurrentAccount.Title,
                x.ProjectId,
                ProjectName = x.Project.Name,
                x.ProjectSiteId,
                ProjectSiteName = x.ProjectSite != null ? x.ProjectSite.Name : null,
                x.ContractNumber,
                x.WorkDescription,
                ContractType = (int)x.ContractType,
                ContractTypeName = ContractTypeName(x.ContractType),
                x.ContractAmount,
                x.CurrencyCode,
                x.StartDate,
                x.EndDate,
                Status = (int)x.Status,
                StatusName = StatusName(x.Status),
                x.RetentionRate,
                x.WithholdingNumerator,
                x.WithholdingDenominator,
                MealResponsibility = (int)x.MealResponsibility,
                AccommodationResponsibility = (int)x.AccommodationResponsibility,
                SocialSecurityResponsibility = (int)x.SocialSecurityResponsibility,
                MaterialResponsibility = (int)x.MaterialResponsibility,
                OhsResponsibility = (int)x.OhsResponsibility,
                x.Notes,
                SectionCount = x.Sections.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorView)]
    public async Task<IActionResult> GetById(
        Guid id, CancellationToken cancellationToken)
    {
        var item = await db.SubcontractorContracts
            .AsNoTracking()
            .Include(x => x.Sections)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Taşeron sözleşmesi bulunamadı." });

        var sectionNames = await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == item.ProjectId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return Ok(new
        {
            item.Id,
            item.CompanyId,
            item.CurrentAccountId,
            item.ProjectId,
            item.ProjectSiteId,
            item.ContractNumber,
            item.WorkDescription,
            ContractType = (int)item.ContractType,
            ContractTypeName = ContractTypeName(item.ContractType),
            item.ContractAmount,
            item.CurrencyCode,
            item.StartDate,
            item.EndDate,
            Status = (int)item.Status,
            StatusName = StatusName(item.Status),
            item.RetentionRate,
            item.WithholdingNumerator,
            item.WithholdingDenominator,
            MealResponsibility = (int)item.MealResponsibility,
            AccommodationResponsibility = (int)item.AccommodationResponsibility,
            SocialSecurityResponsibility = (int)item.SocialSecurityResponsibility,
            MaterialResponsibility = (int)item.MaterialResponsibility,
            OhsResponsibility = (int)item.OhsResponsibility,
            item.Notes,
            Sections = item.Sections
                .OrderBy(x => x.Order)
                .Select(x => new
                {
                    x.Id,
                    x.ProjectHakedisSectionId,
                    SectionName = sectionNames.GetValueOrDefault(
                        x.ProjectHakedisSectionId),
                    x.SectionAmount,
                    x.Order
                })
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    public async Task<IActionResult> Create(
        SaveSubcontractorContractRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request, null, cancellationToken);

        if (validation is not null)
            return validation;

        var item = new SubcontractorContract
        {
            CompanyId = request.CompanyId,
            CurrentAccountId = request.CurrentAccountId,
            ProjectId = request.ProjectId,
            ProjectSiteId = request.ProjectSiteId
        };

        Apply(item, request);

        db.SubcontractorContracts.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            item.Id,
            message = "Taşeron sözleşmesi oluşturuldu."
        });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    public async Task<IActionResult> Update(
        Guid id,
        SaveSubcontractorContractRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.SubcontractorContracts
            .Include(x => x.Sections)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Taşeron sözleşmesi bulunamadı." });

        var validation = await ValidateAsync(request, id, cancellationToken);

        if (validation is not null)
            return validation;

        // Şirket, cari ve proje değiştirilemez: değişseydi bu sözleşmeye
        // bağlı hakediş ve maliyet kayıtları başka bir projeye/cariye
        // sessizce taşınırdı.
        if (item.CurrentAccountId != request.CurrentAccountId ||
            item.ProjectId != request.ProjectId ||
            item.CompanyId != request.CompanyId)
        {
            return BadRequest(new
            {
                message =
                    "Şirket, taşeron carisi ve proje değiştirilemez. " +
                    "Yeni bir sözleşme açın."
            });
        }

        item.ProjectSiteId = request.ProjectSiteId;
        Apply(item, request);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Taşeron sözleşmesi güncellendi." });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken)
    {
        var item = await db.SubcontractorContracts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Taşeron sözleşmesi bulunamadı." });

        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Taşeron sözleşmesi silindi." });
    }

    // ---------- Taşeron ekibi (SGK bizde) ----------

    /// <summary>
    /// Sözleşmenin ekibi: SGK bizdeyken bizim bordromuzda olan taşeron
    /// işçileri. Ücret rakamı DÖNMEZ — burada yalnızca kimlerin ekipte
    /// olduğu var; bordro maliyeti hakediş ekranında tek toplam olarak
    /// görünür.
    /// </summary>
    [HttpGet("{id:guid}/team")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorView)]
    public async Task<IActionResult> GetTeam(
        Guid id, CancellationToken cancellationToken)
    {
        var contract = await db.SubcontractorContracts
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.CompanyId,
                x.SocialSecurityResponsibility
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (contract is null)
            return NotFound(new { message = "Taşeron sözleşmesi bulunamadı." });

        var members = await db.Personnel
            .AsNoTracking()
            .Where(x => x.SubcontractorContractId == id)
            .OrderBy(x => x.FirstName)
            .ThenBy(x => x.LastName)
            .Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                FullName = x.FirstName + " " + x.LastName,
                x.JobTitle,
                x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            socialSecurityWithUs =
                contract.SocialSecurityResponsibility ==
                SubcontractorResponsibility.Us,
            members
        });
    }

    /// <summary>
    /// Ekibi verilen listeyle değiştirir. Liste tam gönderilir:
    /// gönderilmeyen üyenin bağı kopar.
    /// </summary>
    [HttpPut("{id:guid}/team")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    public async Task<IActionResult> ReplaceTeam(
        Guid id,
        ReplaceSubcontractorTeamRequest request,
        CancellationToken cancellationToken)
    {
        var contract = await db.SubcontractorContracts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (contract is null)
            return NotFound(new { message = "Taşeron sözleşmesi bulunamadı." });

        var failure = await teamService.ReplaceTeamAsync(
            contract, request.PersonnelIds ?? [], cancellationToken);

        return failure is not null
            ? BadRequest(new { message = failure })
            : Ok(new { message = "Taşeron ekibi güncellendi." });
    }

    // ---------- Yardımcılar ----------

    private void Apply(
        SubcontractorContract item, SaveSubcontractorContractRequest request)
    {
        item.ContractNumber = request.ContractNumber.Trim();
        item.WorkDescription = request.WorkDescription.Trim();
        item.ContractType = (ProjectContractType)request.ContractType;
        item.ContractAmount = request.ContractAmount;
        item.CurrencyCode = NormalizeCurrency(request.CurrencyCode);
        item.StartDate = UtcDate(request.StartDate);
        item.EndDate = request.EndDate is DateTime end ? UtcDate(end) : null;
        item.RetentionRate = request.RetentionRate;
        item.WithholdingNumerator = request.WithholdingNumerator;
        item.WithholdingDenominator = request.WithholdingDenominator;
        item.MealResponsibility =
            (SubcontractorResponsibility)request.MealResponsibility;
        item.AccommodationResponsibility =
            (SubcontractorResponsibility)request.AccommodationResponsibility;
        item.SocialSecurityResponsibility =
            (SubcontractorResponsibility)request.SocialSecurityResponsibility;
        item.MaterialResponsibility =
            (SubcontractorResponsibility)request.MaterialResponsibility;
        item.OhsResponsibility =
            (SubcontractorResponsibility)request.OhsResponsibility;
        item.Notes = string.IsNullOrWhiteSpace(request.Notes)
            ? null
            : request.Notes.Trim();

        var requested = request.Sections ?? [];

        // Kısımlar tam liste olarak gelir: gönderilmeyen kaldırılır.
        // Fark hesabı yerine tam liste, ekranın gösterdiğiyle kaydın
        // birebir aynı kalmasını garanti ediyor.
        foreach (var existing in item.Sections.ToList())
        {
            if (requested.Any(x =>
                    x.ProjectHakedisSectionId == existing.ProjectHakedisSectionId))
            {
                continue;
            }

            item.Sections.Remove(existing);
            db.SubcontractorContractSections.Remove(existing);
        }

        foreach (var section in requested)
        {
            var existing = item.Sections.SingleOrDefault(x =>
                x.ProjectHakedisSectionId == section.ProjectHakedisSectionId);

            if (existing is null)
            {
                item.Sections.Add(new SubcontractorContractSection
                {
                    ProjectHakedisSectionId = section.ProjectHakedisSectionId,
                    SectionAmount = section.SectionAmount,
                    Order = section.Order
                });
                continue;
            }

            existing.SectionAmount = section.SectionAmount;
            existing.Order = section.Order;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private async Task<IActionResult?> ValidateAsync(
        SaveSubcontractorContractRequest request,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ContractNumber))
            return BadRequest(new { message = "Sözleşme numarası zorunludur." });

        if (string.IsNullOrWhiteSpace(request.WorkDescription))
            return BadRequest(new { message = "İş tanımı zorunludur." });

        if (request.ContractAmount <= 0m)
        {
            return BadRequest(new
            {
                message = "Sözleşme bedeli sıfırdan büyük olmalıdır."
            });
        }

        if (!Enum.IsDefined(typeof(ProjectContractType), request.ContractType))
            return BadRequest(new { message = "Geçersiz sözleşme tipi." });

        var contractType = (ProjectContractType)request.ContractType;

        // Karma sözleşme kabul edilmiyor: bir kısmı götürü bir kısmı
        // birim fiyatlı bir taşeron işi, iki ayrı sözleşmedir ve
        // hakedişleri de ayrı yürür.
        if (contractType is ProjectContractType.Undetermined
            or ProjectContractType.Mixed)
        {
            return BadRequest(new
            {
                message =
                    "Taşeron sözleşmesi götürü ya da birim fiyatlı olmalıdır."
            });
        }

        foreach (var (value, name) in new[]
        {
            (request.MealResponsibility, "Yemek"),
            (request.AccommodationResponsibility, "Konaklama"),
            (request.SocialSecurityResponsibility, "Sigorta-SGK"),
            (request.MaterialResponsibility, "Malzeme"),
            (request.OhsResponsibility, "İSG")
        })
        {
            if (!Enum.IsDefined(typeof(SubcontractorResponsibility), value))
                return BadRequest(new { message = $"{name} kapsamı geçersiz." });
        }

        if (request.RetentionRate is < 0m or > 100m)
        {
            return BadRequest(new
            {
                message = "Teminat oranı 0 ile 100 arasında olmalıdır."
            });
        }

        // Tevkifat ya tamamen boş ya da her iki tarafı dolu olmalı;
        // yarım bırakılan bir oran faturada sessizce sıfır tevkifat
        // üretirdi.
        if (request.WithholdingNumerator < 0 || request.WithholdingDenominator < 0)
            return BadRequest(new { message = "Tevkifat oranı negatif olamaz." });

        if ((request.WithholdingNumerator == 0) !=
            (request.WithholdingDenominator == 0))
        {
            return BadRequest(new
            {
                message = "Tevkifat oranının payı ve paydası birlikte girilmelidir."
            });
        }

        if (request.WithholdingDenominator > 0 &&
            request.WithholdingNumerator > request.WithholdingDenominator)
        {
            return BadRequest(new
            {
                message = "Tevkifat payı paydadan büyük olamaz."
            });
        }

        if (request.EndDate is DateTime end && end.Date < request.StartDate.Date)
        {
            return BadRequest(new
            {
                message = "Bitiş tarihi başlangıç tarihinden önce olamaz."
            });
        }

        var account = await db.CurrentAccounts
            .AsNoTracking()
            .Where(x => x.Id == request.CurrentAccountId &&
                        x.CompanyId == request.CompanyId)
            .Select(x => new { x.Roles, x.Title })
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return BadRequest(new
            {
                message = "Seçilen şirkete ait cari bulunamadı."
            });
        }

        // Cari "taşeron" işaretli değilse sözleşme açılmaz: aksi halde
        // müşteri ya da banka carisine taşeron hakedişi bağlanabilirdi.
        if (!account.Roles.HasFlag(CurrentAccountRoles.Subcontractor))
        {
            return BadRequest(new
            {
                message =
                    $"{account.Title} carisi taşeron olarak işaretli değil. " +
                    "Cari kartında Taşeron rolünü işaretleyin."
            });
        }

        var projectExists = await db.Projects.AnyAsync(
            x => x.Id == request.ProjectId && x.CompanyId == request.CompanyId,
            cancellationToken);

        if (!projectExists)
        {
            return BadRequest(new
            {
                message = "Seçilen şirkete ait proje bulunamadı."
            });
        }

        if (request.ProjectSiteId is Guid siteId)
        {
            var siteBelongs = await db.ProjectSites.AnyAsync(
                x => x.Id == siteId && x.ProjectId == request.ProjectId,
                cancellationToken);

            if (!siteBelongs)
            {
                return BadRequest(new
                {
                    message = "Seçilen şantiye bu projeye ait değil."
                });
            }
        }

        var duplicate = await db.SubcontractorContracts.AnyAsync(
            x => x.CompanyId == request.CompanyId &&
                 x.ContractNumber == request.ContractNumber.Trim() &&
                 (!excludedId.HasValue || x.Id != excludedId.Value),
            cancellationToken);

        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu sözleşme numarası zaten kullanılıyor."
            });
        }

        return await ValidateSectionsAsync(request, cancellationToken);
    }

    private async Task<IActionResult?> ValidateSectionsAsync(
        SaveSubcontractorContractRequest request,
        CancellationToken cancellationToken)
    {
        var sections = request.Sections ?? [];

        if (sections.Count == 0)
        {
            // Götürüde ilerleme kısım bazında giriliyor; kısım yoksa
            // hakediş hesaplanamaz.
            return (ProjectContractType)request.ContractType ==
                   ProjectContractType.LumpSum
                ? BadRequest(new
                {
                    message =
                        "Götürü sözleşmede en az bir icmal kısmı seçilmelidir; " +
                        "ilerleme kısım bazında giriliyor."
                })
                : null;
        }

        var duplicateSection = sections
            .GroupBy(x => x.ProjectHakedisSectionId)
            .Any(g => g.Count() > 1);

        if (duplicateSection)
        {
            return BadRequest(new
            {
                message = "Aynı icmal kısmı birden fazla kez seçilemez."
            });
        }

        if (sections.Any(x => x.SectionAmount < 0m))
            return BadRequest(new { message = "Kısım bedeli negatif olamaz." });

        var sectionIds = sections.Select(x => x.ProjectHakedisSectionId).ToArray();

        var validSectionCount = await db.ProjectHakedisSections.CountAsync(
            x => sectionIds.Contains(x.Id) && x.ProjectId == request.ProjectId,
            cancellationToken);

        if (validSectionCount != sectionIds.Length)
        {
            return BadRequest(new
            {
                message = "Seçilen icmal kısımlarının tamamı bu projeye ait değil."
            });
        }

        // Kısım bedelleri toplamı sözleşme bedelini aşamaz: aşarsa
        // götürüdeki ağırlıklı ilerleme %100'ü geçer.
        var sectionTotal = sections.Sum(x => x.SectionAmount);

        if (decimal.Round(sectionTotal, 2) >
            decimal.Round(request.ContractAmount, 2))
        {
            return BadRequest(new
            {
                message =
                    $"Kısım bedelleri toplamı ({TurkishFormat.Amount(sectionTotal)}) " +
                    "sözleşme bedelini " +
                    $"({TurkishFormat.Amount(request.ContractAmount)}) aşamaz."
            });
        }

        return null;
    }

    private static string NormalizeCurrency(string? value)
    {
        var currency = string.IsNullOrWhiteSpace(value)
            ? "TRY"
            : value.Trim().ToUpperInvariant();
        return currency.Length == 3 ? currency : "TRY";
    }

    private static DateTime UtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static string ContractTypeName(ProjectContractType type) => type switch
    {
        ProjectContractType.LumpSum => "Götürü",
        ProjectContractType.UnitPrice => "Birim fiyatlı",
        _ => "Belirsiz"
    };

    private static string StatusName(SubcontractorContractStatus status) => status switch
    {
        SubcontractorContractStatus.Draft => "Taslak",
        SubcontractorContractStatus.Active => "Aktif",
        SubcontractorContractStatus.Completed => "Tamamlandı",
        SubcontractorContractStatus.Cancelled => "İptal",
        _ => "Bilinmiyor"
    };
}
