using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hr/assets")]
public sealed class HrAssetsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? personnelId,
        [FromQuery] Guid? projectId,
        [FromQuery] int? status,
        [FromQuery] string? assetType,
        [FromQuery] string? search,
        [FromQuery] bool? overdueOnly,
        CancellationToken cancellationToken)
    {
        var query = db.HrAssetAssignments.AsNoTracking().AsQueryable();

        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (personnelId.HasValue) query = query.Where(x => x.PersonnelId == personnelId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (status.HasValue) query = query.Where(x => (int)x.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(assetType))
            query = query.Where(x => x.AssetType == assetType);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.AssetName.ToLower().Contains(term) ||
                x.AssetCode.ToLower().Contains(term) ||
                (x.SerialNumber != null && x.SerialNumber.ToLower().Contains(term)));
        }

        var now = DateTime.UtcNow;
        if (overdueOnly == true)
        {
            query = query.Where(x =>
                x.Status == HrAssetAssignmentStatus.Assigned &&
                x.PlannedReturnDate.HasValue &&
                x.PlannedReturnDate.Value < now);
        }

        var rows = await query
            .OrderByDescending(x => x.AssignmentDate)
            .ToListAsync(cancellationToken);

        return Ok(await ToDtosAsync(rows, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.HrAssetAssignments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Zimmet kaydı bulunamadı." });

        var dtos = await ToDtosAsync(new[] { item }, cancellationToken);
        return Ok(dtos[0]);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> Create(
        SaveAssetAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AssetType) ||
            string.IsNullOrWhiteSpace(request.AssetCode) ||
            string.IsNullOrWhiteSpace(request.AssetName))
        {
            return BadRequest(new { message = "Zimmet tipi, kodu ve adı zorunludur." });
        }

        var item = new HrAssetAssignment
        {
            CompanyId = request.CompanyId,
            PersonnelId = request.PersonnelId,
            ProjectId = request.ProjectId,
            AssetType = request.AssetType.Trim(),
            AssetCode = request.AssetCode.Trim(),
            AssetName = request.AssetName.Trim(),
            SerialNumber = request.SerialNumber?.Trim(),
            AssignmentDate = DateTime.SpecifyKind(request.AssignmentDate.Date, DateTimeKind.Utc),
            PlannedReturnDate = ToUtcDate(request.PlannedReturnDate),
            ConditionAtAssignment = request.ConditionAtAssignment?.Trim(),
            DocumentPath = request.DocumentPath?.Trim(),
            Status = HrAssetAssignmentStatus.Assigned,
            Notes = request.Notes?.Trim()
        };

        db.HrAssetAssignments.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        var dtos = await ToDtosAsync(new[] { item }, cancellationToken);
        return Ok(dtos[0]);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateAssetAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.HrAssetAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Zimmet kaydı bulunamadı." });

        item.PersonnelId = request.PersonnelId;
        item.ProjectId = request.ProjectId;
        item.AssetType = request.AssetType.Trim();
        item.AssetCode = request.AssetCode.Trim();
        item.AssetName = request.AssetName.Trim();
        item.SerialNumber = request.SerialNumber?.Trim();
        item.AssignmentDate = DateTime.SpecifyKind(request.AssignmentDate.Date, DateTimeKind.Utc);
        item.PlannedReturnDate = ToUtcDate(request.PlannedReturnDate);
        item.ActualReturnDate = ToUtcDate(request.ActualReturnDate);
        item.ConditionAtAssignment = request.ConditionAtAssignment?.Trim();
        item.ConditionAtReturn = request.ConditionAtReturn?.Trim();
        item.DocumentPath = request.DocumentPath?.Trim();
        item.Status = (HrAssetAssignmentStatus)request.Status;
        item.Notes = request.Notes?.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var dtos = await ToDtosAsync(new[] { item }, cancellationToken);
        return Ok(dtos[0]);
    }

    [HttpPost("{id:guid}/return")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> Return(
        Guid id,
        ReturnAssetRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.HrAssetAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Zimmet kaydı bulunamadı." });

        item.Status = HrAssetAssignmentStatus.Returned;
        item.ActualReturnDate = ToUtcDate(request.ReturnDate) ?? DateTime.UtcNow;
        item.ConditionAtReturn = request.ConditionAtReturn?.Trim();
        item.DocumentPath = request.DocumentPath?.Trim() ?? item.DocumentPath;
        item.Notes = request.Notes?.Trim() ?? item.Notes;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var dtos = await ToDtosAsync(new[] { item }, cancellationToken);
        return Ok(dtos[0]);
    }

    [HttpPost("{id:guid}/lost")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> MarkLost(
        Guid id,
        MarkLostRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.HrAssetAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Zimmet kaydı bulunamadı." });

        item.Status = HrAssetAssignmentStatus.Lost;
        item.Notes = string.IsNullOrWhiteSpace(item.Notes)
            ? $"Kayıp: {request.Reason}"
            : $"{item.Notes}\nKayıp: {request.Reason}";
        item.DocumentPath = request.DocumentPath?.Trim() ?? item.DocumentPath;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var dtos = await ToDtosAsync(new[] { item }, cancellationToken);
        return Ok(dtos[0]);
    }

    [HttpPost("{id:guid}/damaged")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> MarkDamaged(
        Guid id,
        MarkDamagedRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.HrAssetAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Zimmet kaydı bulunamadı." });

        item.Status = HrAssetAssignmentStatus.Damaged;
        item.ConditionAtReturn = request.DamageDescription.Trim();
        item.DocumentPath = request.DocumentPath?.Trim() ?? item.DocumentPath;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var dtos = await ToDtosAsync(new[] { item }, cancellationToken);
        return Ok(dtos[0]);
    }

    [HttpPost("{id:guid}/change-project")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> ChangeProject(
        Guid id,
        ChangeAssetProjectRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.HrAssetAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Zimmet kaydı bulunamadı." });

        item.ProjectId = request.ProjectId;
        item.Notes = request.Notes?.Trim() ?? item.Notes;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var dtos = await ToDtosAsync(new[] { item }, cancellationToken);
        return Ok(dtos[0]);
    }

    [HttpPost("{id:guid}/transfer-personnel")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> TransferPersonnel(
        Guid id,
        TransferAssetPersonnelRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.HrAssetAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Zimmet kaydı bulunamadı." });

        var newPersonnelExists = await db.Personnel.AnyAsync(
            x => x.Id == request.NewPersonnelId, cancellationToken);
        if (!newPersonnelExists)
            return NotFound(new { message = "Devredilecek personel bulunamadı." });

        item.PersonnelId = request.NewPersonnelId;
        item.ProjectId = request.NewProjectId ?? item.ProjectId;
        item.ConditionAtAssignment = request.ConditionAtTransfer?.Trim() ?? item.ConditionAtAssignment;
        item.AssignmentDate = DateTime.SpecifyKind(request.TransferDate.Date, DateTimeKind.Utc);
        item.Notes = request.Notes?.Trim() ?? item.Notes;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var dtos = await ToDtosAsync(new[] { item }, cancellationToken);
        return Ok(dtos[0]);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancelAssetRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.HrAssetAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Zimmet kaydı bulunamadı." });

        item.Status = HrAssetAssignmentStatus.Cancelled;
        item.Notes = string.IsNullOrWhiteSpace(item.Notes)
            ? $"İptal: {request.Reason}"
            : $"{item.Notes}\nİptal: {request.Reason}";
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var dtos = await ToDtosAsync(new[] { item }, cancellationToken);
        return Ok(dtos[0]);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.HrAssetAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Zimmet kaydı bulunamadı." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Zimmet kaydı silindi." });
    }

    [HttpGet("dashboard")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        var query = db.HrAssetAssignments.AsNoTracking().AsQueryable();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);

        var rows = await query
            .Select(x => new { x.AssetType, x.Status, x.PlannedReturnDate })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        bool IsOverdue(HrAssetAssignmentStatus status, DateTime? planned) =>
            status == HrAssetAssignmentStatus.Assigned &&
            planned.HasValue && planned.Value < now;

        var assetTypes = rows
            .GroupBy(x => x.AssetType)
            .Select(g => new
            {
                assetType = g.Key,
                totalCount = g.Count(),
                assignedCount = g.Count(x => x.Status == HrAssetAssignmentStatus.Assigned),
                returnedCount = g.Count(x => x.Status == HrAssetAssignmentStatus.Returned),
                lostCount = g.Count(x => x.Status == HrAssetAssignmentStatus.Lost),
                damagedCount = g.Count(x => x.Status == HrAssetAssignmentStatus.Damaged),
                overdueCount = g.Count(x => IsOverdue(x.Status, x.PlannedReturnDate))
            })
            .OrderBy(x => x.assetType)
            .ToList();

        return Ok(new
        {
            companyId,
            projectId,
            totalCount = rows.Count,
            assignedCount = rows.Count(x => x.Status == HrAssetAssignmentStatus.Assigned),
            returnedCount = rows.Count(x => x.Status == HrAssetAssignmentStatus.Returned),
            lostCount = rows.Count(x => x.Status == HrAssetAssignmentStatus.Lost),
            damagedCount = rows.Count(x => x.Status == HrAssetAssignmentStatus.Damaged),
            cancelledCount = rows.Count(x => x.Status == HrAssetAssignmentStatus.Cancelled),
            overdueCount = rows.Count(x => IsOverdue(x.Status, x.PlannedReturnDate)),
            assetTypes
        });
    }

    [HttpGet("overdue")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetOverdue(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var query = db.HrAssetAssignments.AsNoTracking()
            .Where(x =>
                x.Status == HrAssetAssignmentStatus.Assigned &&
                x.PlannedReturnDate.HasValue &&
                x.PlannedReturnDate.Value < now);

        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);

        var rows = await query.OrderBy(x => x.PlannedReturnDate).ToListAsync(cancellationToken);
        return Ok(await ToDtosAsync(rows, cancellationToken));
    }

    [HttpGet("analysis/{personnelId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> AnalyzePersonnel(
        Guid personnelId,
        CancellationToken cancellationToken)
    {
        var personnel = await db.Personnel.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == personnelId, cancellationToken);

        if (personnel is null)
            return NotFound(new { message = "Personel bulunamadı." });

        var rows = await db.HrAssetAssignments.AsNoTracking()
            .Where(x => x.PersonnelId == personnelId)
            .OrderByDescending(x => x.AssignmentDate)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var overdueCount = rows.Count(x =>
            x.Status == HrAssetAssignmentStatus.Assigned &&
            x.PlannedReturnDate.HasValue &&
            x.PlannedReturnDate.Value < now);
        var lostCount = rows.Count(x => x.Status == HrAssetAssignmentStatus.Lost);
        var damagedCount = rows.Count(x => x.Status == HrAssetAssignmentStatus.Damaged);

        var riskScore = lostCount * 30 + damagedCount * 15 + overdueCount * 10;
        var riskLevel = riskScore >= 40 ? "High" : riskScore >= 15 ? "Medium" : "Low";

        var findings = new List<string>();
        if (overdueCount > 0) findings.Add($"{overdueCount} zimmette iade süresi geçmiş.");
        if (lostCount > 0) findings.Add($"{lostCount} zimmet kayıp olarak işaretlenmiş.");
        if (damagedCount > 0) findings.Add($"{damagedCount} zimmet hasarlı olarak işaretlenmiş.");

        return Ok(new
        {
            personnelId,
            fullName = $"{personnel.FirstName} {personnel.LastName}".Trim(),
            totalAssignmentCount = rows.Count,
            activeAssignmentCount = rows.Count(x => x.Status == HrAssetAssignmentStatus.Assigned),
            returnedCount = rows.Count(x => x.Status == HrAssetAssignmentStatus.Returned),
            lostCount,
            damagedCount,
            overdueCount,
            riskLevel,
            riskScore,
            summary = findings.Count == 0
                ? "Personelin zimmet durumu düzenli."
                : string.Join(" ", findings),
            findings,
            recommendations = overdueCount > 0
                ? new[] { "Süresi geçen zimmetlerin iadesi takip edilmelidir." }
                : Array.Empty<string>(),
            assets = await ToDtosAsync(rows, cancellationToken)
        });
    }

    private async Task<IReadOnlyList<object>> ToDtosAsync(
        IReadOnlyList<HrAssetAssignment> rows,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        return rows.Select(x => (object)new
        {
            x.Id,
            x.CompanyId,
            x.PersonnelId,
            x.ProjectId,
            x.AssetType,
            x.AssetCode,
            x.AssetName,
            x.SerialNumber,
            x.AssignmentDate,
            x.PlannedReturnDate,
            x.ActualReturnDate,
            x.ConditionAtAssignment,
            x.ConditionAtReturn,
            x.DocumentPath,
            Status = (int)x.Status,
            StatusName = AssetStatusName(x.Status),
            x.IsActive,
            IsOverdue = x.Status == HrAssetAssignmentStatus.Assigned &&
                        x.PlannedReturnDate.HasValue &&
                        x.PlannedReturnDate.Value < now,
            OverdueDays = x.Status == HrAssetAssignmentStatus.Assigned &&
                          x.PlannedReturnDate.HasValue &&
                          x.PlannedReturnDate.Value < now
                ? (int)(now - x.PlannedReturnDate.Value).TotalDays
                : (int?)null,
            x.Notes,
            x.CreatedAtUtc
        }).ToList();
    }

    private static string AssetStatusName(HrAssetAssignmentStatus status) => status switch
    {
        HrAssetAssignmentStatus.Assigned => "Zimmetli",
        HrAssetAssignmentStatus.Returned => "İade Edildi",
        HrAssetAssignmentStatus.Lost => "Kayıp",
        HrAssetAssignmentStatus.Damaged => "Hasarlı",
        HrAssetAssignmentStatus.Cancelled => "İptal Edildi",
        _ => status.ToString()
    };

    private static DateTime? ToUtcDate(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc) : null;
}

public sealed record SaveAssetAssignmentRequest(
    Guid CompanyId,
    Guid PersonnelId,
    Guid? ProjectId,
    string AssetType,
    string AssetCode,
    string AssetName,
    string? SerialNumber,
    DateTime AssignmentDate,
    DateTime? PlannedReturnDate,
    string? ConditionAtAssignment,
    string? DocumentPath,
    string? Notes);

public sealed record UpdateAssetAssignmentRequest(
    Guid PersonnelId,
    Guid? ProjectId,
    string AssetType,
    string AssetCode,
    string AssetName,
    string? SerialNumber,
    DateTime AssignmentDate,
    DateTime? PlannedReturnDate,
    DateTime? ActualReturnDate,
    string? ConditionAtAssignment,
    string? ConditionAtReturn,
    string? DocumentPath,
    int Status,
    string? Notes);

public sealed record ReturnAssetRequest(
    DateTime? ReturnDate,
    string? ConditionAtReturn,
    string? DocumentPath,
    string? Notes);

public sealed record MarkLostRequest(
    DateTime? EventDate,
    string Reason,
    string? DocumentPath);

public sealed record MarkDamagedRequest(
    DateTime? EventDate,
    string DamageDescription,
    string? DocumentPath);

public sealed record ChangeAssetProjectRequest(
    Guid? ProjectId,
    string? Notes);

public sealed record TransferAssetPersonnelRequest(
    Guid NewPersonnelId,
    Guid? NewProjectId,
    DateTime TransferDate,
    string? ConditionAtTransfer,
    string? Notes);

public sealed record CancelAssetRequest(string Reason);
