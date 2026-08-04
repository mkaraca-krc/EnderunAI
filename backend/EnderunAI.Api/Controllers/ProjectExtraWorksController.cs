using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record CreateExtraWorkRequest(
    Guid ProjectId,
    Guid? ProjectHakedisSectionId,
    string PositionCode,
    string Description,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    DateTime WorkDate,
    string? Notes);

/// <param name="ApprovalDocumentId">İşveren onay belgesi. Anahtar
/// teslimde onaylı işaretlemek için zorunludur.</param>
public sealed record ApproveExtraWorkRequest(
    Guid? ApprovalDocumentId,
    string? Notes);

/// <summary>
/// İlave iş / ataşman: keşif üstü gerçekleşmenin kayda geçmiş hali.
///
/// Birim fiyatlı projede doğrudan hakedişe eklenebilir. Anahtar
/// teslimde ancak işveren onayıyla tahsil edilebilir; onay belgesi
/// Dosya Merkezi'nden iliştirilir ve belgesiz onay kabul edilmez —
/// sözlü onay tahsilatta işe yaramaz.
/// </summary>
[ApiController]
[Authorize]
[Route("api/project-extra-works")]
public sealed class ProjectExtraWorksController(
    AppDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken) =>
        Ok(await db.ProjectExtraWorks
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.WorkDate)
            .Select(x => new
            {
                x.Id,
                x.ProjectHakedisSectionId,
                SectionName = x.ProjectHakedisSection != null
                    ? x.ProjectHakedisSection.Name
                    : null,
                x.PositionCode,
                x.Description,
                x.Unit,
                x.Quantity,
                x.UnitPrice,
                x.Amount,
                x.WorkDate,
                ApprovalStatus = (int)x.ApprovalStatus,
                x.ApprovedAtUtc,
                x.ApprovalDocumentId,
                ApprovalDocumentName = x.ApprovalDocument != null
                    ? x.ApprovalDocument.FileName
                    : null,
                x.ProgressPaymentId,
                ProgressPaymentNumber = x.ProgressPayment != null
                    ? x.ProgressPayment.ProgressPaymentNumber
                    : null,
                x.Notes
            })
            .ToListAsync(cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.HakedisCreate)]
    public async Task<IActionResult> Create(
        CreateExtraWorkRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0m)
            return BadRequest(new { message = "Miktar sıfırdan büyük olmalıdır." });

        if (string.IsNullOrWhiteSpace(request.PositionCode))
            return BadRequest(new { message = "Poz kodu zorunludur." });

        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == request.ProjectId)
            .Select(x => new { x.Id, x.ContractType })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        if (project.ContractType == ProjectContractType.Undetermined)
        {
            return Conflict(new
            {
                message = "Projenin sözleşme tipi belirlenmeden ilave iş " +
                          "kaydedilemez; ilave işin anlamı sözleşme tipine bağlıdır."
            });
        }

        var entity = new ProjectExtraWork
        {
            ProjectId = request.ProjectId,
            ProjectHakedisSectionId = request.ProjectHakedisSectionId,
            PositionCode = request.PositionCode.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Unit = request.Unit?.Trim() ?? string.Empty,
            Quantity = request.Quantity,
            UnitPrice = Math.Max(0m, request.UnitPrice),
            Amount = decimal.Round(
                request.Quantity * Math.Max(0m, request.UnitPrice), 2,
                MidpointRounding.AwayFromZero),
            WorkDate = DateTime.SpecifyKind(request.WorkDate.Date, DateTimeKind.Utc),
            // Birim fiyatlıda sözleşmedeki birim fiyat geçerli olduğu
            // için ayrı işveren onayı aranmaz; anahtar teslimde aranır.
            ApprovalStatus = project.ContractType == ProjectContractType.UnitPrice
                ? ExtraWorkApprovalStatus.Approved
                : ExtraWorkApprovalStatus.Pending,
            Notes = request.Notes?.Trim()
        };

        if (entity.ApprovalStatus == ExtraWorkApprovalStatus.Approved)
            entity.ApprovedAtUtc = DateTime.UtcNow;

        db.ProjectExtraWorks.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { entity.Id, message = "İlave iş kaydedildi." });
    }

    /// <summary>
    /// İşveren onayını kaydeder. Anahtar teslimde onay belgesi zorunlu:
    /// belgesiz onay tahsilatta dayanaksız kalır ve kâr erozyonunu
    /// olduğundan küçük gösterirdi.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.HakedisApprove)]
    public async Task<IActionResult> Approve(
        Guid id,
        ApproveExtraWorkRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.ProjectExtraWorks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "İlave iş kaydı bulunamadı." });

        var contractType = await db.Projects
            .Where(x => x.Id == entity.ProjectId)
            .Select(x => x.ContractType)
            .SingleAsync(cancellationToken);

        if (contractType == ProjectContractType.LumpSum &&
            request.ApprovalDocumentId is null)
        {
            return BadRequest(new
            {
                message = "Anahtar teslim projede ilave iş, işveren onay belgesi " +
                          "iliştirilmeden onaylanamaz. Belgeyi Dosya Merkezi'ne " +
                          "yükleyip seçin."
            });
        }

        if (request.ApprovalDocumentId is Guid documentId)
        {
            var documentBelongsToProject = await db.ProjectDocuments
                .AnyAsync(x => x.Id == documentId && x.ProjectId == entity.ProjectId,
                    cancellationToken);

            if (!documentBelongsToProject)
            {
                return BadRequest(new
                {
                    message = "Seçilen belge bu projeye ait değil."
                });
            }
        }

        entity.ApprovalStatus = ExtraWorkApprovalStatus.Approved;
        entity.ApprovalDocumentId = request.ApprovalDocumentId;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        entity.ApprovedByUserId = currentUser.UserId;

        if (!string.IsNullOrWhiteSpace(request.Notes))
            entity.Notes = request.Notes.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "İlave iş onaylandı." });
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission(PermissionCatalog.Keys.HakedisApprove)]
    public async Task<IActionResult> Reject(
        Guid id, ApproveExtraWorkRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.ProjectExtraWorks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "İlave iş kaydı bulunamadı." });

        entity.ApprovalStatus = ExtraWorkApprovalStatus.Rejected;
        entity.ApprovedAtUtc = null;
        entity.ApprovedByUserId = null;

        if (!string.IsNullOrWhiteSpace(request.Notes))
            entity.Notes = request.Notes.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "İlave iş reddedildi olarak işaretlendi." });
    }

    /// <summary>
    /// Hakedişe aktarılabilecek ilave işler — hakediş hazırlanırken poz
    /// satırı olarak önerilir. Reddedilen ve zaten aktarılmış olanlar
    /// listede yer almaz; aynı iş iki kez hakedişe girmemeli.
    /// </summary>
    [HttpGet("transferable")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> Transferable(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken)
    {
        var contractType = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => x.ContractType)
            .SingleOrDefaultAsync(cancellationToken);

        var query = db.ProjectExtraWorks
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.ProgressPaymentId == null &&
                        x.ApprovalStatus != ExtraWorkApprovalStatus.Rejected);

        // Anahtar teslimde yalnızca işveren onaylı ek iş hakedişe girer.
        if (contractType == ProjectContractType.LumpSum)
            query = query.Where(x => x.ApprovalStatus == ExtraWorkApprovalStatus.Approved);

        return Ok(await query
            .OrderBy(x => x.PositionCode)
            .Select(x => new
            {
                x.Id,
                x.ProjectHakedisSectionId,
                x.PositionCode,
                x.Description,
                x.Unit,
                x.Quantity,
                x.UnitPrice,
                x.Amount
            })
            .ToListAsync(cancellationToken));
    }

    /// <summary>İlave işi bir hakedişe bağlar (tekrar aktarımı engeller).</summary>
    [HttpPost("{id:guid}/transfer/{progressPaymentId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    public async Task<IActionResult> Transfer(
        Guid id, Guid progressPaymentId, CancellationToken cancellationToken)
    {
        var entity = await db.ProjectExtraWorks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "İlave iş kaydı bulunamadı." });

        if (entity.ProgressPaymentId is not null)
        {
            return Conflict(new
            {
                message = "Bu ilave iş zaten bir hakedişe aktarılmış."
            });
        }

        var payment = await db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.Id == progressPaymentId)
            .Select(x => new { x.Id, x.ProjectId })
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null || payment.ProjectId != entity.ProjectId)
            return BadRequest(new { message = "Hakediş bu projeye ait değil." });

        entity.ProgressPaymentId = progressPaymentId;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "İlave iş hakedişe bağlandı." });
    }
}
