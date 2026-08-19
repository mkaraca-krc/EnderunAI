using EnderunAI.Api.Contracts.ProjectSites;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}")]
public sealed class HrProjectLaborCostsController(AppDbContext db,
    IScopedData scoped) : ControllerBase
{
    [HttpGet("labor-costs")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetAll(
        Guid projectId,
        [FromQuery] Guid? siteId,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var query = db.HrProjectLaborCosts
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId);

        if (siteId.HasValue)
            query = query.Where(x => x.ProjectSiteId == siteId.Value);

        var rows = await query
            .OrderByDescending(x => x.WorkDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                x.PersonnelId,
                x.ProjectSiteId,
                SiteCode = x.ProjectSite != null ? x.ProjectSite.Code : null,
                SiteName = x.ProjectSite != null ? x.ProjectSite.Name : null,
                x.WorkDate,
                x.NormalHours,
                x.OvertimeHours,
                x.NormalCost,
                x.OvertimeCost,
                x.OtherCost,
                x.TotalLaborCost,
                x.CurrencyCode
            })
            .ToListAsync(cancellationToken);

        var personnelIds = rows.Select(x => x.PersonnelId).Distinct().ToArray();

        /*
         * ADLAR DİKİŞTEN. Bu uç `personnel.view` ile korunuyor ve o
         * izin şantiye kapsamlı rollerde de var; kapsam dışı bir
         * personelin adı burada çözülürse maliyet satırı isimle
         * eşleşir ve kim olduğu sızar.
         */
        var scopedPersonnel = await scoped.PersonnelAsync(cancellationToken);

        var personnelNames = await scopedPersonnel
            .Where(x => personnelIds.Contains(x.Id))
            .ToDictionaryAsync(
                x => x.Id,
                x => x.FirstName + " " + x.LastName,
                cancellationToken);

        return Ok(rows.Select(x => new
        {
            x.Id,
            x.ProjectId,
            x.PersonnelId,
            PersonnelName = personnelNames.GetValueOrDefault(x.PersonnelId, "—"),
            x.ProjectSiteId,
            x.SiteCode,
            x.SiteName,
            x.WorkDate,
            x.NormalHours,
            x.OvertimeHours,
            x.NormalCost,
            x.OvertimeCost,
            x.OtherCost,
            x.TotalLaborCost,
            x.CurrencyCode
        }));
    }

    [HttpPost("labor-costs")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> Create(
        Guid projectId,
        CreateHrProjectLaborCostRequest request,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        // Kapsam dışı personele maliyet yazılamaz.
        var visiblePersonnel = await scoped.PersonnelAsync(cancellationToken);

        var personnel = await visiblePersonnel
            .SingleOrDefaultAsync(x => x.Id == request.PersonnelId, cancellationToken);

        if (personnel is null)
            return NotFound(new { message = "Personel bulunamadı." });

        if (request.ProjectSiteId.HasValue)
        {
            var siteBelongsToProject = await db.ProjectSites.AsNoTracking().AnyAsync(
                x => x.Id == request.ProjectSiteId.Value && x.ProjectId == projectId,
                cancellationToken);

            if (!siteBelongsToProject)
            {
                return BadRequest(new
                {
                    message = "Seçilen şantiye bu projeye ait değil."
                });
            }
        }

        // İcmal satırı seçildiyse aynı projeye ait olmalı.
        if (request.ProjectBoqItemId is Guid boqItemId)
        {
            var boqItemBelongsToProject = await db.ProjectBoqItems.AsNoTracking().AnyAsync(
                x => x.Id == boqItemId && x.ProjectBoq.ProjectId == projectId,
                cancellationToken);

            if (!boqItemBelongsToProject)
            {
                return BadRequest(new
                {
                    message = "Seçilen icmal satırı bu projeye ait değil."
                });
            }
        }

        var totalLaborCost =
            request.NormalCost + request.OvertimeCost + request.OtherCost +
            request.MealCost + request.AccommodationCost +
            request.ShuttleCost + request.CompensationCost;

        var item = new HrProjectLaborCost
        {
            CompanyId = personnel.CompanyId,
            ProjectId = projectId,
            PersonnelId = request.PersonnelId,
            ProjectSiteId = request.ProjectSiteId,
            ProjectBoqItemId = request.ProjectBoqItemId,
            WorkDate = DateTime.SpecifyKind(request.WorkDate.Date, DateTimeKind.Utc),
            NormalHours = request.NormalHours,
            OvertimeHours = request.OvertimeHours,
            NormalCost = request.NormalCost,
            OvertimeCost = request.OvertimeCost,
            OtherCost = request.OtherCost,
            MealCost = request.MealCost,
            AccommodationCost = request.AccommodationCost,
            ShuttleCost = request.ShuttleCost,
            CompensationCost = request.CompensationCost,
            TotalLaborCost = totalLaborCost,
            // Elle girilen satırda kalem bayrağı yok: tamamı hakedişe
            // yansıyan maliyet sayılır, elden payı ayrıca işaretlenir.
            ProgressPaymentCost = totalLaborCost,
            ProgressPaymentCompensationCost = request.CompensationCost,
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? "TRY"
                : request.CurrencyCode.Trim().ToUpperInvariant()
        };

        db.HrProjectLaborCosts.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Personel maliyet kaydı oluşturuldu.",
            item.Id,
            item.TotalLaborCost
        });
    }

    /// <summary>
    /// Şantiye bazında işçilik maliyeti.
    ///
    /// Resmî tutar <c>HrProjectLaborCosts</c> defterinden gelir. Elden
    /// ödemenin payı BU DEFTERE YAZILMAZ — defter projects/personnel
    /// yetkisiyle okunuyor ve elden tutar oradan sızardı. Pay okuma
    /// anında, <c>extra_payment.view</c> doğrulanarak ekleniyor ve
    /// yetkisiz kullanıcı yalnızca resmî rakamı görüyor.
    /// </summary>
    [HttpGet("labor-cost-breakdown")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetBreakdown(
        Guid projectId,
        [FromServices] IExtraPaymentVisibilityService extraPaymentVisibility,
        [FromServices] Services.HumanResources.ExtraPaymentAllocationService allocation,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.Id, x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        var rows = await db.HrProjectLaborCosts
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new { x.ProjectSiteId, x.TotalLaborCost })
            .ToListAsync(cancellationToken);

        var sites = await db.ProjectSites
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);

        var canSeeExtra =
            await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken);

        // Yetki yoksa dağıtım HİÇ hesaplanmaz; sorgu elden tablosuna
        // uğramaz.
        var extraShares = canSeeExtra
            ? await allocation.GetSiteSharesAsync(
                project.CompanyId, projectId, cancellationToken)
            : new Services.HumanResources.SiteExtraPaymentShares(
                new Dictionary<Guid, decimal>(), 0m);

        var siteBreakdown = sites.Select(site =>
        {
            var official = rows
                .Where(x => x.ProjectSiteId == site.Id)
                .Sum(x => x.TotalLaborCost);

            var extra = extraShares.BySite.GetValueOrDefault(site.Id, 0m);

            return new
            {
                site.Id,
                site.Code,
                site.Name,
                // Geriye uyum: mevcut ekranlar Amount'a bakıyor ve
                // resmî rakamı görmeye devam ediyor.
                Amount = official,
                OfficialAmount = official,
                ExtraPaymentAmount = canSeeExtra ? extra : (decimal?)null,
                ActualAmount = canSeeExtra ? decimal.Round(official + extra, 2) : (decimal?)null
            };
        }).ToList();

        var sharedOfficial = rows
            .Where(x => x.ProjectSiteId == null)
            .Sum(x => x.TotalLaborCost);

        // Şantiyesi belli olmayan puantaj günlerinin elden payı; uydurma
        // dağıtım yapmak yerine ayrı gösteriliyor.
        var sharedExtra = extraShares.Unassigned;

        var projectOfficial = rows.Sum(x => x.TotalLaborCost);
        var projectExtra = extraShares.Total;

        return Ok(new
        {
            projectId,
            sites = siteBreakdown,
            sharedCost = sharedOfficial,
            sharedOfficialCost = sharedOfficial,
            sharedExtraPaymentCost = canSeeExtra ? sharedExtra : (decimal?)null,
            projectTotal = projectOfficial,
            projectOfficialTotal = projectOfficial,
            projectExtraPaymentTotal = canSeeExtra
                ? decimal.Round(projectExtra, 2)
                : (decimal?)null,
            projectActualTotal = canSeeExtra
                ? decimal.Round(projectOfficial + projectExtra, 2)
                : (decimal?)null,
            extraPaymentHidden = !canSeeExtra
        });
    }
}
