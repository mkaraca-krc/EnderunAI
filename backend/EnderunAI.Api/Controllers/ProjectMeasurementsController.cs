using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/project-measurements")]
public sealed class ProjectMeasurementsController(
    AppDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? projectBoqId,
        [FromQuery] ProjectMeasurementStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.ProjectMeasurements.AsNoTracking().AsQueryable();

        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (projectBoqId.HasValue) query = query.Where(x => x.ProjectBoqId == projectBoqId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var items = await query
            .OrderByDescending(x => x.MeasurementDate)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.ProjectId,
                x.Project.Code,
                ProjectName = x.Project.Name,
                x.ProjectBoqId,
                x.ProjectBoq.BoqNumber,
                x.MeasurementNumber,
                x.MeasurementDate,
                x.Status,
                x.CurrencyCode,
                x.TotalAmount,
                x.CreatedAtUtc,
                ItemCount = x.Items.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(items.Select(x => new
        {
            x.Id,
            x.CompanyId,
            x.ProjectId,
            ProjectCode = x.Code,
            x.ProjectName,
            x.ProjectBoqId,
            x.BoqNumber,
            x.MeasurementNumber,
            x.MeasurementDate,
            x.Status,
            x.CurrencyCode,
            x.TotalAmount,
            x.ItemCount,
            x.CreatedAtUtc
        }));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.ProjectMeasurements
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Project)
            .Include(x => x.ProjectBoq)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Metraj bulunamadı." });

        return Ok(new
        {
            item.Id,
            item.CompanyId,
            item.ProjectId,
            ProjectCode = item.Project.Code,
            ProjectName = item.Project.Name,
            item.ProjectBoqId,
            item.ProjectBoq.BoqNumber,
            item.MeasurementNumber,
            item.MeasurementDate,
            item.Status,
            item.CurrencyCode,
            item.TotalAmount,
            item.Description,
            item.Notes,
            item.CancellationReason,
            item.SubmittedAtUtc,
            item.ApprovedAtUtc,
            item.TransferredAtUtc,
            item.ProgressPaymentId,
            Items = item.Items
                .OrderBy(x => x.LineNumber)
                .Select(x => new
                {
                    x.Id,
                    x.ProjectBoqItemId,
                    x.EngineeringPositionId,
                    x.LineNumber,
                    x.PositionCode,
                    x.Description,
                    x.Unit,
                    x.ContractQuantity,
                    x.PreviousQuantity,
                    x.CurrentQuantity,
                    x.CumulativeQuantity,
                    x.RemainingQuantity,
                    x.UnitPrice,
                    x.CurrentAmount,
                    x.CumulativeAmount,
                    x.CompletionRate,
                    x.MeasurementReference,
                    x.Location,
                    x.Block,
                    x.Floor,
                    x.Room,
                    x.Notes
                })
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.HakedisCreate)]
    public async Task<IActionResult> Create(
        CreateProjectMeasurementRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "Metraj en az bir kalem içermelidir." });

        var boq = await db.ProjectBoqs
            .Include(x => x.Items)
            .SingleOrDefaultAsync(
                x => x.Id == request.ProjectBoqId && x.ProjectId == request.ProjectId,
                cancellationToken);

        if (boq is null)
            return NotFound(new { message = "Keşif bu projeye ait değil veya bulunamadı." });

        var duplicateNumber = await db.ProjectMeasurements.AnyAsync(
            x => x.CompanyId == request.CompanyId && x.MeasurementNumber == request.MeasurementNumber,
            cancellationToken);

        if (duplicateNumber)
            return Conflict(new { message = "Bu numarada bir metraj zaten var." });

        var boqItemIds = request.Items.Select(x => x.ProjectBoqItemId).ToArray();
        var previousSums = await db.ProjectMeasurementItems
            .AsNoTracking()
            .Where(x =>
                boqItemIds.Contains(x.ProjectBoqItemId) &&
                x.ProjectMeasurement.Status != ProjectMeasurementStatus.Cancelled)
            .GroupBy(x => x.ProjectBoqItemId)
            .Select(g => new { ProjectBoqItemId = g.Key, Sum = g.Sum(x => x.CurrentQuantity) })
            .ToListAsync(cancellationToken);
        var previousMap = previousSums.ToDictionary(x => x.ProjectBoqItemId, x => x.Sum);

        var items = new List<ProjectMeasurementItem>();
        var lineNumber = 0;

        foreach (var line in request.Items)
        {
            var boqItem = boq.Items.SingleOrDefault(x => x.Id == line.ProjectBoqItemId);
            if (boqItem is null)
            {
                return BadRequest(new
                {
                    message = $"Keşif kalemi bulunamadı: {line.ProjectBoqItemId}"
                });
            }

            lineNumber++;
            var previousQuantity = previousMap.GetValueOrDefault(boqItem.Id);
            var cumulativeQuantity = previousQuantity + line.CurrentQuantity;
            var remainingQuantity = boqItem.ContractQuantity - cumulativeQuantity;

            items.Add(new ProjectMeasurementItem
            {
                ProjectBoqItemId = boqItem.Id,
                EngineeringPositionId = boqItem.EngineeringPositionId,
                LineNumber = lineNumber,
                PositionCode = boqItem.PositionCode,
                Description = boqItem.Description,
                Unit = boqItem.Unit,
                ContractQuantity = boqItem.ContractQuantity,
                PreviousQuantity = previousQuantity,
                CurrentQuantity = line.CurrentQuantity,
                CumulativeQuantity = cumulativeQuantity,
                RemainingQuantity = remainingQuantity,
                UnitPrice = boqItem.UnitPrice,
                CurrentAmount = line.CurrentQuantity * boqItem.UnitPrice,
                CumulativeAmount = cumulativeQuantity * boqItem.UnitPrice,
                CompletionRate = boqItem.ContractQuantity == 0
                    ? 0
                    : cumulativeQuantity / boqItem.ContractQuantity * 100,
                MeasurementReference = line.MeasurementReference?.Trim(),
                Location = line.Location?.Trim(),
                Block = line.Block?.Trim(),
                Floor = line.Floor?.Trim(),
                Room = line.Room?.Trim(),
                Notes = line.Notes?.Trim()
            });
        }

        var measurement = new ProjectMeasurement
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            ProjectBoqId = request.ProjectBoqId,
            MeasurementNumber = request.MeasurementNumber.Trim(),
            MeasurementDate = DateTime.SpecifyKind(request.MeasurementDate.Date, DateTimeKind.Utc),
            Status = ProjectMeasurementStatus.Draft,
            CurrencyCode = boq.CurrencyCode,
            TotalAmount = items.Sum(x => x.CurrentAmount),
            Description = request.Description?.Trim(),
            Notes = request.Notes?.Trim(),
            Items = items
        };

        db.ProjectMeasurements.Add(measurement);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            measurement.Id,
            measurement.MeasurementNumber,
            measurement.Status,
            measurement.TotalAmount
        });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateProjectMeasurementRequest request,
        CancellationToken cancellationToken)
    {
        var measurement = await db.ProjectMeasurements
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (measurement is null)
            return NotFound(new { message = "Metraj bulunamadı." });

        if (measurement.Status != ProjectMeasurementStatus.Draft)
            return Conflict(new { message = "Yalnızca taslak metrajlar güncellenebilir." });

        var boq = await db.ProjectBoqs
            .Include(x => x.Items)
            .SingleAsync(x => x.Id == measurement.ProjectBoqId, cancellationToken);

        var boqItemIds = request.Items.Select(x => x.ProjectBoqItemId).ToArray();
        var previousSums = await db.ProjectMeasurementItems
            .AsNoTracking()
            .Where(x =>
                boqItemIds.Contains(x.ProjectBoqItemId) &&
                x.ProjectMeasurementId != id &&
                x.ProjectMeasurement.Status != ProjectMeasurementStatus.Cancelled)
            .GroupBy(x => x.ProjectBoqItemId)
            .Select(g => new { ProjectBoqItemId = g.Key, Sum = g.Sum(x => x.CurrentQuantity) })
            .ToListAsync(cancellationToken);
        var previousMap = previousSums.ToDictionary(x => x.ProjectBoqItemId, x => x.Sum);

        db.ProjectMeasurementItems.RemoveRange(measurement.Items);
        measurement.Items.Clear();

        var lineNumber = 0;
        foreach (var line in request.Items)
        {
            var boqItem = boq.Items.SingleOrDefault(x => x.Id == line.ProjectBoqItemId);
            if (boqItem is null)
                return BadRequest(new { message = $"Keşif kalemi bulunamadı: {line.ProjectBoqItemId}" });

            lineNumber++;
            var previousQuantity = previousMap.GetValueOrDefault(boqItem.Id);
            var cumulativeQuantity = previousQuantity + line.CurrentQuantity;

            measurement.Items.Add(new ProjectMeasurementItem
            {
                ProjectBoqItemId = boqItem.Id,
                EngineeringPositionId = boqItem.EngineeringPositionId,
                LineNumber = lineNumber,
                PositionCode = boqItem.PositionCode,
                Description = boqItem.Description,
                Unit = boqItem.Unit,
                ContractQuantity = boqItem.ContractQuantity,
                PreviousQuantity = previousQuantity,
                CurrentQuantity = line.CurrentQuantity,
                CumulativeQuantity = cumulativeQuantity,
                RemainingQuantity = boqItem.ContractQuantity - cumulativeQuantity,
                UnitPrice = boqItem.UnitPrice,
                CurrentAmount = line.CurrentQuantity * boqItem.UnitPrice,
                CumulativeAmount = cumulativeQuantity * boqItem.UnitPrice,
                CompletionRate = boqItem.ContractQuantity == 0
                    ? 0
                    : cumulativeQuantity / boqItem.ContractQuantity * 100,
                MeasurementReference = line.MeasurementReference?.Trim(),
                Location = line.Location?.Trim(),
                Block = line.Block?.Trim(),
                Floor = line.Floor?.Trim(),
                Room = line.Room?.Trim(),
                Notes = line.Notes?.Trim()
            });
        }

        measurement.MeasurementDate = DateTime.SpecifyKind(request.MeasurementDate.Date, DateTimeKind.Utc);
        measurement.Description = request.Description?.Trim();
        measurement.Notes = request.Notes?.Trim();
        measurement.TotalAmount = measurement.Items.Sum(x => x.CurrentAmount);
        measurement.UpdatedAtUtc = DateTime.UtcNow;

        // Satırlar yukarıda temizlenip yeniden kuruldu; hepsi yeni. EF
        // anahtarları dolu geldiği için bunları var olan satır sanıp
        // Modified işaretliyor ve kayıt "beklenen 1 satır, etkilenen 0"
        // ile düşüyordu — metraj hiç güncellenemiyordu.
        db.MarkRebuiltAsNew(measurement.Items);

        await db.SaveChangesAsync(cancellationToken);

        return await GetById(id, cancellationToken);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var measurement = await db.ProjectMeasurements.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (measurement is null)
            return NotFound(new { message = "Metraj bulunamadı." });

        if (measurement.Status != ProjectMeasurementStatus.Draft)
            return Conflict(new { message = "Yalnızca taslak metrajlar silinebilir." });

        measurement.IsActive = false;
        measurement.IsDeleted = true;
        measurement.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    [RequirePermission(PermissionCatalog.Keys.HakedisCreate)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        var measurement = await db.ProjectMeasurements.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (measurement is null)
            return NotFound(new { message = "Metraj bulunamadı." });

        if (measurement.Status != ProjectMeasurementStatus.Draft)
            return Conflict(new { message = "Yalnızca taslak metrajlar onaya gönderilebilir." });

        measurement.Status = ProjectMeasurementStatus.PendingApproval;
        measurement.SubmittedAtUtc = DateTime.UtcNow;
        measurement.SubmittedByUserId = currentUser.UserId;
        measurement.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            measurement.Id,
            measurement.MeasurementNumber,
            measurement.Status,
            message = "Metraj onaya gönderildi."
        });
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.HakedisApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var measurement = await db.ProjectMeasurements.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (measurement is null)
            return NotFound(new { message = "Metraj bulunamadı." });

        if (measurement.Status != ProjectMeasurementStatus.PendingApproval)
            return Conflict(new { message = "Yalnızca onay bekleyen metrajlar onaylanabilir." });

        measurement.Status = ProjectMeasurementStatus.Approved;
        measurement.ApprovedAtUtc = DateTime.UtcNow;
        measurement.ApprovedByUserId = currentUser.UserId;
        measurement.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            measurement.Id,
            measurement.MeasurementNumber,
            measurement.Status,
            message = "Metraj onaylandı."
        });
    }

    [HttpPost("{id:guid}/cancel")]
        // YIKICI İŞLEM, DELETE YETKİSİ İSTER: iptal kesinleşmiş belgeyi
        // ters kayıtla geri alıyor — muhasebe fişi doğuruyor, stok/tahsilat
        // hareketi yaratıyor. "Düzeltme" değil "yıkma"; edit bunun için
        // zayıftı. Daraltma öncesi etki ölçüldü: canlıdaki hiçbir kullanıcı
        // iş yapamaz hale gelmedi (edit'i olan herkeste delete de var).
    [RequirePermission(PermissionCatalog.Keys.HakedisDelete)]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancelProjectMeasurementRequest request,
        CancellationToken cancellationToken)
    {
        var measurement = await db.ProjectMeasurements.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (measurement is null)
            return NotFound(new { message = "Metraj bulunamadı." });

        if (measurement.Status == ProjectMeasurementStatus.TransferredToProgressPayment)
        {
            return Conflict(new
            {
                message = "Hakedişe aktarılmış metraj iptal edilemez."
            });
        }

        measurement.Status = ProjectMeasurementStatus.Cancelled;
        measurement.CancellationReason = request.Reason?.Trim();
        measurement.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            measurement.Id,
            measurement.MeasurementNumber,
            measurement.Status,
            message = "Metraj iptal edildi."
        });
    }
}

public sealed record ProjectMeasurementItemRequest(
    Guid ProjectBoqItemId,
    decimal CurrentQuantity,
    string? MeasurementReference,
    string? Location,
    string? Block,
    string? Floor,
    string? Room,
    string? Notes);

public sealed record CreateProjectMeasurementRequest(
    Guid CompanyId,
    Guid ProjectId,
    Guid ProjectBoqId,
    string MeasurementNumber,
    DateTime MeasurementDate,
    string? Description,
    string? Notes,
    List<ProjectMeasurementItemRequest> Items);

public sealed record UpdateProjectMeasurementRequest(
    DateTime MeasurementDate,
    string? Description,
    string? Notes,
    List<ProjectMeasurementItemRequest> Items);

public sealed record CancelProjectMeasurementRequest(string? Reason);
