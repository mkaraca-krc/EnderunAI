using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Assets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>Alet kartı oluşturma/güncelleme.</summary>
/// <param name="CompanyId">Şirket.</param>
/// <param name="Code">Şirket içinde benzersiz alet kodu.</param>
/// <param name="Name">Alet adı.</param>
/// <param name="Brand">Marka.</param>
/// <param name="Model">Model.</param>
/// <param name="SerialNumber">Seri numarası; girilirse benzersiz.</param>
/// <param name="PurchaseDate">Alım tarihi.</param>
/// <param name="PurchaseCost">Alım bedeli.</param>
/// <param name="WarrantyEndDate">Garanti bitişi.</param>
/// <param name="LocationType">0 merkez, 1 şantiye.</param>
/// <param name="ProjectSiteId">Şantiyedeyse hangi şantiye.</param>
/// <param name="Notes">Not.</param>
public sealed record SaveToolAssetRequest(
    Guid CompanyId,
    string Code,
    string Name,
    string? Brand,
    string? Model,
    string? SerialNumber,
    DateTime? PurchaseDate,
    decimal? PurchaseCost,
    DateTime? WarrantyEndDate,
    int LocationType,
    Guid? ProjectSiteId,
    string? Notes);

/// <summary>
/// Demirbaş / el aleti kartları.
///
/// Sarftan ayrıdır: alet tüketilmez, kullanılır ve geri gelir. Kart
/// olmadan servis geçmişi ve garanti takibi tutulamaz — zimmet
/// kaydındaki serbest metin her seferinde yeniden yazıldığı için
/// "bu matkap kaç kez arızalandı" sorusu cevapsız kalırdı.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tool-assets")]
public sealed class ToolAssetsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] int? status,
        [FromQuery] Guid? projectSiteId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = db.ToolAssets.AsNoTracking();

        if (companyId is Guid cid) query = query.Where(x => x.CompanyId == cid);
        if (status is int s) query = query.Where(x => (int)x.Status == s);
        if (projectSiteId is Guid siteId)
            query = query.Where(x => x.ProjectSiteId == siteId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.Code.ToLower().Contains(term) ||
                x.Name.ToLower().Contains(term) ||
                (x.SerialNumber != null && x.SerialNumber.ToLower().Contains(term)));
        }

        return Ok(await query
            .OrderBy(x => x.Code)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.Code,
                x.Name,
                x.Brand,
                x.Model,
                x.SerialNumber,
                x.PurchaseDate,
                x.PurchaseCost,
                x.WarrantyEndDate,
                Status = (int)x.Status,
                StatusName = StatusName(x.Status),
                LocationType = (int)x.LocationType,
                x.ProjectSiteId,
                SiteName = x.ProjectSite != null ? x.ProjectSite.Name : null,
                x.AssignedPersonnelId,
                AssignedPersonnelName = x.AssignedPersonnel != null
                    ? x.AssignedPersonnel.FirstName + " " + x.AssignedPersonnel.LastName
                    : null,
                x.Notes
            })
            .ToListAsync(cancellationToken));
    }

    /// <summary>
    /// Alet kartı ve servis geçmişi özeti — kaç kez arızalandı, toplam
    /// ne kadara mal oldu.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] ToolServiceWorkflow workflow,
        CancellationToken cancellationToken)
    {
        var asset = await db.ToolAssets
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.Code,
                x.Name,
                x.Brand,
                x.Model,
                x.SerialNumber,
                x.PurchaseDate,
                x.PurchaseCost,
                x.WarrantyEndDate,
                Status = (int)x.Status,
                StatusName = StatusName(x.Status),
                LocationType = (int)x.LocationType,
                x.ProjectSiteId,
                x.AssignedPersonnelId,
                x.Notes
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (asset is null)
            return NotFound(new { message = "Alet bulunamadı." });

        var (count, totalCost, lastDate) =
            await workflow.GetHistorySummaryAsync(id, cancellationToken);

        var history = await db.ToolServiceRequests
            .AsNoTracking()
            .Where(x => x.ToolAssetId == id)
            .OrderByDescending(x => x.RequestDate)
            .Select(x => new
            {
                x.Id,
                x.RequestNumber,
                x.RequestDate,
                x.FaultDescription,
                Status = (int)x.Status,
                Decision = (int)x.Decision,
                x.ServiceCost,
                ProjectCode = x.Project != null ? x.Project.Code : null
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            asset,
            serviceCount = count,
            serviceTotalCost = totalCost,
            lastServiceDate = lastDate,
            history
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> Create(
        SaveToolAssetRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request, null, cancellationToken);
        if (validation is not null)
            return BadRequest(new { message = validation });

        var asset = new ToolAsset
        {
            CompanyId = request.CompanyId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Brand = Clean(request.Brand),
            Model = Clean(request.Model),
            SerialNumber = Clean(request.SerialNumber),
            PurchaseDate = AsUtc(request.PurchaseDate),
            PurchaseCost = request.PurchaseCost,
            WarrantyEndDate = AsUtc(request.WarrantyEndDate),
            LocationType = (ToolAssetLocationType)request.LocationType,
            ProjectSiteId = request.ProjectSiteId,
            Notes = Clean(request.Notes)
        };

        db.ToolAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Alet kartı oluşturuldu.", asset.Id, asset.Code });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> Update(
        Guid id, SaveToolAssetRequest request, CancellationToken cancellationToken)
    {
        var asset = await db.ToolAssets
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (asset is null)
            return NotFound(new { message = "Alet bulunamadı." });

        var validation = await ValidateAsync(request, id, cancellationToken);
        if (validation is not null)
            return BadRequest(new { message = validation });

        asset.Code = request.Code.Trim();
        asset.Name = request.Name.Trim();
        asset.Brand = Clean(request.Brand);
        asset.Model = Clean(request.Model);
        asset.SerialNumber = Clean(request.SerialNumber);
        asset.PurchaseDate = AsUtc(request.PurchaseDate);
        asset.PurchaseCost = request.PurchaseCost;
        asset.WarrantyEndDate = AsUtc(request.WarrantyEndDate);
        asset.LocationType = (ToolAssetLocationType)request.LocationType;
        asset.ProjectSiteId = request.ProjectSiteId;
        asset.Notes = Clean(request.Notes);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Alet kartı güncellendi." });
    }

    /// <summary>
    /// Garantisi yaklaşan aletler — yenileme ya da servis kararı için.
    /// </summary>
    [HttpGet("warranty-expiring")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetWarrantyExpiring(
        [FromQuery] Guid? companyId,
        [FromQuery] int? days,
        CancellationToken cancellationToken)
    {
        var horizonDays = days is > 0 and <= 365 ? days.Value : 60;
        var today = DateTime.UtcNow.Date;
        var horizon = today.AddDays(horizonDays);

        var query = db.ToolAssets
            .AsNoTracking()
            .Where(x =>
                x.Status != ToolAssetStatus.Scrapped &&
                x.WarrantyEndDate != null &&
                x.WarrantyEndDate >= today &&
                x.WarrantyEndDate <= horizon);

        if (companyId is Guid cid) query = query.Where(x => x.CompanyId == cid);

        return Ok(await query
            .OrderBy(x => x.WarrantyEndDate)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.WarrantyEndDate,
                Status = (int)x.Status
            })
            .ToListAsync(cancellationToken));
    }

    private async Task<string?> ValidateAsync(
        SaveToolAssetRequest request, Guid? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return "Alet kodu zorunludur.";

        if (string.IsNullOrWhiteSpace(request.Name))
            return "Alet adı zorunludur.";

        if (!Enum.IsDefined(typeof(ToolAssetLocationType), request.LocationType))
            return "Geçersiz yer türü.";

        if (request.LocationType == (int)ToolAssetLocationType.Site &&
            request.ProjectSiteId is null)
        {
            return "Şantiyedeyse şantiye seçilmelidir.";
        }

        var code = request.Code.Trim();

        var codeTaken = await db.ToolAssets.AnyAsync(
            x => x.CompanyId == request.CompanyId &&
                 x.Code == code &&
                 (excludeId == null || x.Id != excludeId),
            ct);

        if (codeTaken)
            return "Bu alet kodu zaten kullanılıyor.";

        // Seri numarası tekilliği: aynı seriyi iki karta yazmak servis
        // geçmişini ikiye böler ve arıza sıklığını olduğundan düşük
        // gösterir.
        var serial = Clean(request.SerialNumber);

        if (serial is not null)
        {
            var serialTaken = await db.ToolAssets.AnyAsync(
                x => x.CompanyId == request.CompanyId &&
                     x.SerialNumber == serial &&
                     (excludeId == null || x.Id != excludeId),
                ct);

            if (serialTaken)
                return "Bu seri numarası başka bir alet kartında kayıtlı.";
        }

        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc);

    private static string StatusName(ToolAssetStatus status) => status switch
    {
        ToolAssetStatus.InWarehouse => "Depoda",
        ToolAssetStatus.InUse => "Kullanımda",
        ToolAssetStatus.InService => "Serviste",
        _ => "Hurda"
    };
}
