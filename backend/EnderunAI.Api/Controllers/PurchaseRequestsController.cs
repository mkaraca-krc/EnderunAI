using EnderunAI.Api.Contracts.Purchasing;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/purchase-requests")]
public sealed class PurchaseRequestsController(
    AppDbContext db,
    IDocumentNumberService documentNumbers) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] int? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = db.PurchaseRequests.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (status.HasValue)
        {
            if (!Enum.IsDefined(typeof(PurchaseRequestStatus), status.Value))
                return BadRequest(new { message = "Geçersiz satın alma talep durumu." });

            query = query.Where(x => x.Status == (PurchaseRequestStatus)status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            query = query.Where(x =>
                x.RequestNumber.ToLower().Contains(term) ||
                x.Project.Name.ToLower().Contains(term) ||
                x.RequestedByName.ToLower().Contains(term) ||
                (x.Description != null &&
                 x.Description.ToLower().Contains(term)));
        }

        var items = await query
            .OrderByDescending(x => x.RequestDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.ProjectId,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                x.RequestNumber,
                x.RequestDate,
                x.NeededByDate,
                x.RequestedByName,
                x.Description,
                x.Priority,
                x.Status,
                x.IsActive,
                ItemCount = x.Items.Count,
                TotalQuantity = x.Items.Sum(i => i.Quantity)
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await db.PurchaseRequests
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.ProjectId,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                x.RequestNumber,
                x.RequestDate,
                x.NeededByDate,
                x.RequestedByName,
                x.Description,
                x.Priority,
                x.Status,
                x.ApprovedByUserId,
                x.ApprovedAtUtc,
                x.CancelledByUserId,
                x.CancelledAtUtc,
                x.CancellationReason,
                x.IsActive,
                Items = x.Items
                    .OrderBy(i => i.LineNumber)
                    .Select(i => new
                    {
                        i.Id,
                        i.LineNumber,
                        i.InventoryItemId,
                        InventoryItemCode = i.InventoryItem != null ? i.InventoryItem.Code : null,
                        InventoryItemName = i.InventoryItem != null ? i.InventoryItem.Name : null,
                        i.MaterialDescription,
                        i.Quantity,
                        i.Unit,
                        i.RequestedDeliveryDate,
                        i.Notes,
                        i.IsActive
                    })
            })
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? NotFound(new { message = "Satın alma talebi bulunamadı." })
            : Ok(item);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsCreate)]
    public async Task<IActionResult> Create(
        CreatePurchaseRequestRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(
            "AUTO",
            request.RequestedByName,
            request.Priority,
            request.Items);

        if (validation is not null)
            return BadRequest(new { message = validation });

        var project = await db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.ProjectId &&
                     x.CompanyId == request.CompanyId &&
                     x.IsActive,
                cancellationToken);

        if (project is null)
        {
            return BadRequest(new
            {
                message = "Proje bulunamadı veya seçilen şirkete ait değil."
            });
        }

        var requestNumber = await documentNumbers.GenerateAsync(
            request.CompanyId,
            "PURCHASE_REQUEST",
            "PR",
            cancellationToken);

        var entity = new PurchaseRequest
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            RequestNumber = requestNumber,
            RequestDate = request.RequestDate,
            NeededByDate = request.NeededByDate,
            RequestedByName = request.RequestedByName.Trim(),
            Description = request.Description?.Trim(),
            Priority = (PurchaseRequestPriority)request.Priority,
            Status = PurchaseRequestStatus.Draft
        };

        var lineNumber = 1;

        foreach (var item in request.Items)
        {
            entity.Items.Add(new PurchaseRequestItem
            {
                LineNumber = lineNumber++,
                InventoryItemId = item.InventoryItemId,
                MaterialDescription = item.MaterialDescription.Trim(),
                Quantity = item.Quantity,
                Unit = item.Unit.Trim(),
                RequestedDeliveryDate = item.RequestedDeliveryDate,
                Notes = item.Notes?.Trim()
            });
        }

        db.PurchaseRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Satın alma talebi taslak olarak oluşturuldu.",
            entity.Id,
            entity.RequestNumber,
            entity.Status
        });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdatePurchaseRequestRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseRequests
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Satın alma talebi bulunamadı." });

        if (entity.Status != PurchaseRequestStatus.Draft)
        {
            return Conflict(new
            {
                message = "Yalnızca taslak satın alma talepleri güncellenebilir."
            });
        }

        var validation = ValidateRequest(
            entity.RequestNumber,
            request.RequestedByName,
            request.Priority,
            request.Items);

        if (validation is not null)
            return BadRequest(new { message = validation });

        entity.RequestDate = request.RequestDate;
        entity.NeededByDate = request.NeededByDate;
        entity.RequestedByName = request.RequestedByName.Trim();
        entity.Description = request.Description?.Trim();
        entity.Priority = (PurchaseRequestPriority)request.Priority;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        db.PurchaseRequestItems.RemoveRange(entity.Items);

        var lineNumber = 1;

        foreach (var item in request.Items)
        {
            entity.Items.Add(new PurchaseRequestItem
            {
                LineNumber = lineNumber++,
                InventoryItemId = item.InventoryItemId,
                MaterialDescription = item.MaterialDescription.Trim(),
                Quantity = item.Quantity,
                Unit = item.Unit.Trim(),
                RequestedDeliveryDate = item.RequestedDeliveryDate,
                Notes = item.Notes?.Trim()
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Satın alma talebi güncellendi." });
    }

    [HttpPost("{id:guid}/submit")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsEdit)]
    public async Task<IActionResult> Submit(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseRequests
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Satın alma talebi bulunamadı." });

        if (entity.Status != PurchaseRequestStatus.Draft)
        {
            return Conflict(new
            {
                message = "Yalnızca taslak talepler onaya gönderilebilir."
            });
        }

        if (entity.Items.Count == 0)
            return BadRequest(new { message = "Talepte en az bir kalem bulunmalıdır." });

        entity.Status = PurchaseRequestStatus.Submitted;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Satın alma talebi onaya gönderildi." });
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsApprove)]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseRequests
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Satın alma talebi bulunamadı." });

        if (entity.Status != PurchaseRequestStatus.Submitted)
        {
            return Conflict(new
            {
                message = "Yalnızca onaya gönderilmiş talepler onaylanabilir."
            });
        }

        entity.Status = PurchaseRequestStatus.Approved;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Satın alma talebi onaylandı." });
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsEdit)]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancelPurchaseRequestRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseRequests
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Satın alma talebi bulunamadı." });

        if (entity.Status is PurchaseRequestStatus.Completed
            or PurchaseRequestStatus.Cancelled)
        {
            return Conflict(new
            {
                message = "Tamamlanmış veya iptal edilmiş talep yeniden iptal edilemez."
            });
        }

        entity.Status = PurchaseRequestStatus.Cancelled;
        entity.CancelledAtUtc = DateTime.UtcNow;
        entity.CancellationReason = request.Reason?.Trim();
        entity.IsActive = false;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Satın alma talebi iptal edildi." });
    }

    private static string? ValidateRequest(
        string requestNumber,
        string requestedByName,
        int priority,
        IReadOnlyCollection<CreatePurchaseRequestItemRequest> items)
    {
        if (string.IsNullOrWhiteSpace(requestNumber))
            return "Talep numarası zorunludur.";

        if (string.IsNullOrWhiteSpace(requestedByName))
            return "Talep eden kişi zorunludur.";

        if (!Enum.IsDefined(typeof(PurchaseRequestPriority), priority))
            return "Geçersiz öncelik değeri.";

        if (items.Count == 0)
            return "En az bir satın alma talep kalemi eklenmelidir.";

        if (items.Any(x =>
                string.IsNullOrWhiteSpace(x.MaterialDescription) ||
                string.IsNullOrWhiteSpace(x.Unit) ||
                x.Quantity <= 0))
        {
            return "Malzeme açıklaması, birim ve sıfırdan büyük miktar zorunludur.";
        }

        return null;
    }
}
