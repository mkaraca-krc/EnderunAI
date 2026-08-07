using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Assets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>Şantiyeden servis talebi.</summary>
/// <param name="ToolAssetId">Arızalanan alet.</param>
/// <param name="ProjectId">Talebi açan şantiyenin projesi; merkez
/// talebinde boş.</param>
/// <param name="ProjectSiteId">Şantiye.</param>
/// <param name="FaultDescription">Arıza tanımı.</param>
/// <param name="Urgency">Aciliyet.</param>
public sealed record CreateToolServiceRequest(
    Guid ToolAssetId,
    Guid? ProjectId,
    Guid? ProjectSiteId,
    string FaultDescription,
    int Urgency);

/// <summary>Servis kararı.</summary>
/// <param name="Decision">1 garanti, 2 ücretli dış servis, 3 yerinde,
/// 4 hurda.</param>
/// <param name="DecisionNote">Gerekçe.</param>
/// <param name="ServiceProviderName">Dış servis firması.</param>
/// <param name="ServiceCost">Onarım bedeli; garanti kararında sıfır
/// olmalıdır.</param>
public sealed record DecideToolServiceRequest(
    int Decision,
    string? DecisionNote,
    string? ServiceProviderName,
    decimal ServiceCost);

/// <summary>Durum ilerletme.</summary>
/// <param name="Status">Hedef durum.</param>
public sealed record AdvanceToolServiceRequest(int Status);

/// <summary>
/// Alet servis talepleri: şantiyeden talep, merkeze transfer, karar,
/// serviste takip, dönüş ya da hurda.
///
/// MALİYET talebi AÇAN şantiyenin projesine yazılır — aleti bozan işin
/// maliyetidir. Garanti kapsamında tutar sıfırdır ve hiçbir maliyet
/// kaydı oluşmaz.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tool-service-requests")]
public sealed class ToolServiceRequestsController(
    AppDbContext db,
    ToolServiceWorkflow workflow) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? toolAssetId,
        [FromQuery] Guid? projectId,
        [FromQuery] int? status,
        [FromQuery] bool? openOnly,
        CancellationToken cancellationToken)
    {
        var query = db.ToolServiceRequests.AsNoTracking();

        if (companyId is Guid cid) query = query.Where(x => x.CompanyId == cid);
        if (toolAssetId is Guid aid) query = query.Where(x => x.ToolAssetId == aid);
        if (projectId is Guid pid) query = query.Where(x => x.ProjectId == pid);
        if (status is int s) query = query.Where(x => (int)x.Status == s);

        if (openOnly == true)
        {
            query = query.Where(x =>
                x.Status == ToolServiceStatus.Requested ||
                x.Status == ToolServiceStatus.Transferred ||
                x.Status == ToolServiceStatus.InService);
        }

        return Ok(await query
            .OrderByDescending(x => x.RequestDate)
            .Select(x => new
            {
                x.Id,
                x.RequestNumber,
                x.ToolAssetId,
                AssetCode = x.ToolAsset.Code,
                AssetName = x.ToolAsset.Name,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                x.ProjectSiteId,
                SiteName = x.ProjectSite != null ? x.ProjectSite.Name : null,
                x.RequestDate,
                x.FaultDescription,
                Urgency = (int)x.Urgency,
                Status = (int)x.Status,
                Decision = (int)x.Decision,
                x.DecisionNote,
                x.ServiceProviderName,
                x.ServiceCost,
                x.ReplacementPurchaseRequestId,
                x.CompletedAtUtc
            })
            .ToListAsync(cancellationToken));
    }

    /// <summary>
    /// Servis talebi açar. Alet serviste sayılır ama ZİMMET KAPANMAZ:
    /// kişi hâlâ sorumludur, alet yalnızca geçici olarak elinden
    /// çıkmıştır.
    /// </summary>
    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> Create(
        CreateToolServiceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FaultDescription))
            return BadRequest(new { message = "Arıza tanımı zorunludur." });

        if (!Enum.IsDefined(typeof(ToolServiceUrgency), request.Urgency))
            return BadRequest(new { message = "Geçersiz aciliyet." });

        var asset = await db.ToolAssets
            .SingleOrDefaultAsync(x => x.Id == request.ToolAssetId, cancellationToken);

        if (asset is null)
            return NotFound(new { message = "Alet bulunamadı." });

        if (asset.Status == ToolAssetStatus.Scrapped)
            return BadRequest(new { message = "Hurdaya ayrılmış alet için servis talebi açılamaz." });

        var alreadyOpen = await db.ToolServiceRequests.AnyAsync(
            x => x.ToolAssetId == asset.Id &&
                 (x.Status == ToolServiceStatus.Requested ||
                  x.Status == ToolServiceStatus.Transferred ||
                  x.Status == ToolServiceStatus.InService),
            cancellationToken);

        if (alreadyOpen)
        {
            return BadRequest(new
            {
                message =
                    "Bu alet için açık bir servis talebi zaten var; " +
                    "iki talebin maliyeti birbirine karışır."
            });
        }

        if (request.ProjectSiteId is Guid siteId && request.ProjectId is Guid projectId)
        {
            var siteBelongs = await db.ProjectSites.AnyAsync(
                x => x.Id == siteId && x.ProjectId == projectId, cancellationToken);

            if (!siteBelongs)
                return BadRequest(new { message = "Şantiye bu projeye ait değil." });
        }

        var raw = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var entity = new ToolServiceRequest
        {
            CompanyId = asset.CompanyId,
            ToolAssetId = asset.Id,
            RequestNumber = await workflow.NextServiceNumberAsync(
                asset.CompanyId, cancellationToken),
            ProjectId = request.ProjectId,
            ProjectSiteId = request.ProjectSiteId,
            RequestDate = DateTime.UtcNow.Date,
            FaultDescription = request.FaultDescription.Trim(),
            Urgency = (ToolServiceUrgency)request.Urgency,
            Status = ToolServiceStatus.Requested,
            RequestedByUserId = Guid.TryParse(raw, out var userId) ? userId : null
        };

        db.ToolServiceRequests.Add(entity);

        // Alet kullanımdan çıkar; zimmet açık kalır.
        asset.Status = ToolAssetStatus.InService;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Servis talebi açıldı. Alet serviste; zimmet açık kalıyor.",
            entity.Id,
            entity.RequestNumber
        });
    }

    /// <summary>
    /// Servis kararı: garanti / ücretli / yerinde / hurda.
    /// </summary>
    [HttpPost("{id:guid}/decide")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> Decide(
        Guid id, DecideToolServiceRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(ToolServiceDecision), request.Decision) ||
            request.Decision == (int)ToolServiceDecision.Pending)
        {
            return BadRequest(new { message = "Geçerli bir karar seçilmelidir." });
        }

        if (string.IsNullOrWhiteSpace(request.DecisionNote))
            return BadRequest(new { message = "Karar gerekçesi zorunludur." });

        if (request.ServiceCost < 0m)
            return BadRequest(new { message = "Onarım bedeli negatif olamaz." });

        var entity = await db.ToolServiceRequests
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Servis talebi bulunamadı." });

        if (!entity.IsOpen)
            return BadRequest(new { message = "Kapanmış talepte karar değiştirilemez." });

        var decision = (ToolServiceDecision)request.Decision;

        // Garanti kapsamında bedel olmaz: ödemediğimiz bir masrafı
        // projeye yazmak, işin maliyetini olduğundan yüksek gösterir.
        if (decision == ToolServiceDecision.ExternalWarranty && request.ServiceCost > 0m)
        {
            return BadRequest(new
            {
                message =
                    "Garanti kapsamındaki onarımda bedel sıfır olmalıdır. " +
                    "Ücret alınıyorsa kararı 'ücretli dış servis' seçin."
            });
        }

        entity.Decision = decision;
        entity.DecisionNote = request.DecisionNote.Trim();
        entity.ServiceProviderName = string.IsNullOrWhiteSpace(request.ServiceProviderName)
            ? null
            : request.ServiceProviderName.Trim();
        entity.ServiceCost = decimal.Round(request.ServiceCost, 2);
        entity.DecidedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Servis kararı kaydedildi." });
    }

    /// <summary>
    /// Talebi bir sonraki duruma taşır (transfer / serviste /
    /// tamamlandı / hurda).
    /// </summary>
    [HttpPost("{id:guid}/advance")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> Advance(
        Guid id, AdvanceToolServiceRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(ToolServiceStatus), request.Status))
            return BadRequest(new { message = "Geçersiz durum." });

        var entity = await db.ToolServiceRequests
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Servis talebi bulunamadı." });

        var target = (ToolServiceStatus)request.Status;

        // Kapanış kararsız yapılamaz: maliyetin nereye yazılacağı
        // karara bağlı.
        if (target is ToolServiceStatus.Completed or ToolServiceStatus.Scrapped &&
            entity.Decision == ToolServiceDecision.Pending)
        {
            return BadRequest(new
            {
                message = "Talep kapatılmadan önce servis kararı verilmelidir."
            });
        }

        try
        {
            await workflow.AdvanceAsync(entity, target, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return Ok(new
        {
            message = "Servis talebi güncellendi.",
            status = (int)entity.Status,
            costWritten = entity.ProjectCostTransactionId is not null
        });
    }

    /// <summary>
    /// Hurdaya ayrılan alet için yerine alım talebi taslağı üretir.
    /// </summary>
    [HttpPost("{id:guid}/replacement-request")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsCreate)]
    public async Task<IActionResult> CreateReplacement(
        Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.ToolServiceRequests
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Servis talebi bulunamadı." });

        var raw = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var (created, skipped) = await workflow.CreateReplacementRequestAsync(
            entity,
            Guid.TryParse(raw, out var userId) ? userId : null,
            cancellationToken);

        return created is null
            ? BadRequest(new { message = skipped })
            : Ok(new
            {
                message = "Yerine alım talebi taslak olarak açıldı.",
                purchaseRequestId = created.Id,
                created.RequestNumber
            });
    }
}
