using System.Security.Claims;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/procurement-documents")]
[Authorize]
public sealed class ProcurementDocumentsController(
    ProcurementDocumentDbContext db,
    IWebHostEnvironment environment) : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".xlsx", ".xls", ".csv", ".doc", ".docx", ".jpg", ".jpeg", ".png", ".webp"
    };

    public sealed record CreateRevisionRequest(
        Guid CompanyId,
        ProcurementDocumentType DocumentType,
        Guid DocumentId,
        string Action,
        string? Reason,
        JsonElement Snapshot);

    public sealed record CreateCommentRequest(
        Guid CompanyId,
        ProcurementDocumentType DocumentType,
        Guid DocumentId,
        string Comment);

    [HttpGet("{documentType}/{documentId:guid}/timeline")]
    public async Task<ActionResult> Timeline(
        ProcurementDocumentType documentType,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var revisions = await db.Revisions.AsNoTracking()
            .Where(x => x.DocumentType == documentType && x.DocumentId == documentId)
            .OrderByDescending(x => x.RevisionNumber)
            .ToListAsync(cancellationToken);

        var attachments = await db.Attachments.AsNoTracking()
            .Where(x => x.DocumentType == documentType && x.DocumentId == documentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var comments = await db.Comments.AsNoTracking()
            .Where(x => x.DocumentType == documentType && x.DocumentId == documentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(new { revisions, attachments, comments });
    }

    [HttpPost("revisions")]
    public async Task<ActionResult> CreateRevision(
        CreateRevisionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DocumentId == Guid.Empty)
            return BadRequest("Belge kimliği zorunludur.");
        if (string.IsNullOrWhiteSpace(request.Action))
            return BadRequest("Revizyon işlemi zorunludur.");

        var latestRevision = await db.Revisions
            .Where(x => x.DocumentType == request.DocumentType && x.DocumentId == request.DocumentId)
            .MaxAsync(x => (int?)x.RevisionNumber, cancellationToken) ?? 0;

        var actor = GetActor();
        var entity = new ProcurementDocumentRevision
        {
            CompanyId = request.CompanyId,
            DocumentType = request.DocumentType,
            DocumentId = request.DocumentId,
            RevisionNumber = latestRevision + 1,
            Action = request.Action.Trim(),
            Reason = request.Reason?.Trim(),
            SnapshotJson = request.Snapshot.ValueKind == JsonValueKind.Undefined
                ? "{}"
                : request.Snapshot.GetRawText(),
            CreatedByUserId = actor.UserId,
            CreatedByName = actor.Name,
            IpAddress = actor.IpAddress
        };

        db.Revisions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpPost("comments")]
    public async Task<ActionResult> AddComment(
        CreateCommentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Comment))
            return BadRequest("Açıklama boş olamaz.");

        var actor = GetActor();
        var entity = new ProcurementDocumentComment
        {
            CompanyId = request.CompanyId,
            DocumentType = request.DocumentType,
            DocumentId = request.DocumentId,
            Comment = request.Comment.Trim(),
            CreatedByUserId = actor.UserId,
            CreatedByName = actor.Name,
            IpAddress = actor.IpAddress
        };

        db.Comments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpPost("{documentType}/{documentId:guid}/attachments")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult> UploadAttachment(
        ProcurementDocumentType documentType,
        Guid documentId,
        [FromForm] Guid companyId,
        [FromForm] string? description,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return BadRequest("Dosya boş olamaz.");
        if (file.Length > 25_000_000)
            return BadRequest("Dosya boyutu 25 MB sınırını aşamaz.");

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            return BadRequest("Bu dosya türüne izin verilmiyor.");

        var root = Path.Combine(environment.ContentRootPath, "uploads", "procurement", documentType.ToString(), documentId.ToString("N"));
        Directory.CreateDirectory(root);

        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var physicalPath = Path.Combine(root, storedFileName);
        await using (var stream = System.IO.File.Create(physicalPath))
            await file.CopyToAsync(stream, cancellationToken);

        var actor = GetActor();
        var entity = new ProcurementDocumentAttachment
        {
            CompanyId = companyId,
            DocumentType = documentType,
            DocumentId = documentId,
            FileName = Path.GetFileName(file.FileName),
            StoredFileName = storedFileName,
            FilePath = Path.GetRelativePath(environment.ContentRootPath, physicalPath).Replace('\\', '/'),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FileSize = file.Length,
            Description = description?.Trim(),
            UploadedByUserId = actor.UserId,
            UploadedByName = actor.Name
        };

        db.Attachments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpGet("attachments/{id:guid}/download")]
    public async Task<ActionResult> DownloadAttachment(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        var physicalPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, entity.FilePath));
        var allowedRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "uploads", "procurement"));
        if (!physicalPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Geçersiz dosya yolu.");
        if (!System.IO.File.Exists(physicalPath))
            return NotFound("Dosya fiziksel olarak bulunamadı.");

        return PhysicalFile(physicalPath, entity.ContentType, entity.FileName);
    }

    [HttpDelete("attachments/{id:guid}")]
    public async Task<ActionResult> DeleteAttachment(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private (Guid? UserId, string Name, string? IpAddress) GetActor()
    {
        Guid? userId = null;
        var rawId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(rawId, out var parsed))
            userId = parsed;

        var name = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "Bilinmeyen kullanıcı";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        return (userId, name, ip);
    }
}
