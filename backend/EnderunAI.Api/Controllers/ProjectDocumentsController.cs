using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Proje Dosya Merkezi: her proje kendi klasörlenmiş, sürümlenen dosya
/// arşivine sahiptir. Fiziksel dosyalar
/// /var/www/enderun-data/project-files/{projectId}/{klasör}/{storedFileName}
/// altında tutulur — her sürüm kendi dosyasında kalır, üzerine yazma
/// yapılmaz (eski sürümler her zaman indirilebilir).
/// </summary>
[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/documents")]
public sealed class ProjectDocumentsController(
    AppDbContext db,
    ICurrentDataScopeService dataScope,
    ICurrentUserService currentUser) : ControllerBase
{
    private const string StorageRoot = "/var/www/enderun-data/project-files";
    private const long MaxTotalUploadBytes = 100 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".dwg", ".dxf", ".xlsx", ".docx", ".jpg", ".jpeg", ".png", ".zip", ".rar"
        };

    public static readonly string[] SuggestedFolders =
    [
        "Keşif", "Çizimler", "Şartnameler", "Sözleşme", "Saha Fotoğrafları", "Hakediş Ekleri", "Diğer"
    ];

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.DocumentsView)]
    public async Task<IActionResult> GetAll(
        Guid projectId,
        [FromQuery] Guid? siteId,
        [FromQuery] string? folder,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessProjectAsync(projectId, cancellationToken))
            return NotFound(new { message = "Proje bulunamadı." });

        var query = db.ProjectDocuments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.IsCurrentVersion);

        if (siteId.HasValue)
            query = query.Where(x => x.ProjectSiteId == siteId.Value);

        if (!string.IsNullOrWhiteSpace(folder))
            query = query.Where(x => x.Folder == folder);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => EF.Functions.ILike(x.FileName, $"%{term}%"));
        }

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.ProjectSiteId,
                SiteName = x.ProjectSite != null ? x.ProjectSite.Name : null,
                x.Folder,
                x.FileName,
                x.SizeBytes,
                x.ContentType,
                x.Extension,
                x.UploadedByUserId,
                UploadedByName = x.UploadedByUser.FullName,
                x.Description,
                x.VersionNumber,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("folders")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsView)]
    public async Task<IActionResult> GetFolders(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessProjectAsync(projectId, cancellationToken))
            return NotFound(new { message = "Proje bulunamadı." });

        var usedFolders = await db.ProjectDocuments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.Folder)
            .Distinct()
            .ToListAsync(cancellationToken);

        var all = SuggestedFolders
            .Union(usedFolders, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(all);
    }

    [HttpGet("{documentId:guid}/versions")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsView)]
    public async Task<IActionResult> GetVersions(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessProjectAsync(projectId, cancellationToken))
            return NotFound(new { message = "Proje bulunamadı." });

        var doc = await db.ProjectDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == documentId && x.ProjectId == projectId, cancellationToken);

        if (doc is null)
            return NotFound(new { message = "Dosya bulunamadı." });

        var versions = await db.ProjectDocuments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.ProjectSiteId == doc.ProjectSiteId &&
                        x.Folder == doc.Folder &&
                        x.FileName == doc.FileName)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new
            {
                x.Id,
                x.VersionNumber,
                x.IsCurrentVersion,
                x.SizeBytes,
                x.UploadedByUserId,
                UploadedByName = x.UploadedByUser.FullName,
                x.CreatedAtUtc,
                x.Description
            })
            .ToListAsync(cancellationToken);

        return Ok(versions);
    }

    [HttpGet("{documentId:guid}/download")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsView)]
    public async Task<IActionResult> Download(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessProjectAsync(projectId, cancellationToken))
            return NotFound(new { message = "Proje bulunamadı." });

        var doc = await db.ProjectDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == documentId && x.ProjectId == projectId, cancellationToken);

        if (doc is null)
            return NotFound(new { message = "Dosya bulunamadı." });

        var fullPath = Path.Combine(
            StorageRoot, projectId.ToString(), SanitizeFolder(doc.Folder), doc.StoredFileName);

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "Fiziksel dosya bulunamadı." });

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, doc.ContentType, doc.FileName);
    }

    [HttpPost]
    [RequestSizeLimit(MaxTotalUploadBytes)]
    [RequirePermission(PermissionCatalog.Keys.DocumentsCreate)]
    public async Task<IActionResult> Upload(
        Guid projectId,
        [FromForm] UploadProjectDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessProjectAsync(projectId, cancellationToken))
            return NotFound(new { message = "Proje bulunamadı." });

        if (request.Files is null || request.Files.Count == 0)
            return BadRequest(new { message = "En az bir dosya seçilmelidir." });

        if (string.IsNullOrWhiteSpace(request.Folder))
            return BadRequest(new { message = "Klasör seçilmelidir." });

        if (request.ProjectSiteId.HasValue)
        {
            var siteExists = await db.ProjectSites.AsNoTracking()
                .AnyAsync(
                    x => x.Id == request.ProjectSiteId.Value && x.ProjectId == projectId,
                    cancellationToken);

            if (!siteExists)
                return BadRequest(new { message = "Seçilen şantiye bu projeye ait değil." });
        }

        var folder = request.Folder.Trim();
        var sanitizedFolder = SanitizeFolder(folder);
        var directory = Path.Combine(StorageRoot, projectId.ToString(), sanitizedFolder);
        Directory.CreateDirectory(directory);

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        var uploaded = new List<object>();

        foreach (var file in request.Files)
        {
            if (file.Length == 0)
                continue;

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                return BadRequest(new
                {
                    message = $"'{extension}' dosya türüne izin verilmiyor. " +
                        "İzin verilen türler: pdf, dwg, dxf, xlsx, docx, jpg, png, zip, rar."
                });
            }

            var originalName = Path.GetFileName(file.FileName);

            var existingVersions = await db.ProjectDocuments
                .Where(x => x.ProjectId == projectId &&
                            x.ProjectSiteId == request.ProjectSiteId &&
                            x.Folder == folder &&
                            x.FileName == originalName &&
                            x.IsCurrentVersion)
                .ToListAsync(cancellationToken);

            var nextVersion = 1;
            if (existingVersions.Count > 0)
            {
                nextVersion = existingVersions.Max(x => x.VersionNumber) + 1;
                foreach (var previous in existingVersions)
                    previous.IsCurrentVersion = false;
            }

            var storedFileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(directory, storedFileName);

            await using (var stream = new FileStream(
                fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            var document = new ProjectDocument
            {
                ProjectId = projectId,
                ProjectSiteId = request.ProjectSiteId,
                Folder = folder,
                FileName = originalName,
                StoredFileName = storedFileName,
                SizeBytes = file.Length,
                ContentType = ResolveContentType(extension),
                Extension = extension,
                UploadedByUserId = currentUser.UserId ?? Guid.Empty,
                Description = description,
                VersionNumber = nextVersion,
                IsCurrentVersion = true
            };

            db.ProjectDocuments.Add(document);
            uploaded.Add(new { document.FileName, document.VersionNumber });
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = $"{uploaded.Count} dosya yüklendi.",
            files = uploaded
        });
    }

    [HttpPatch("{documentId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsEdit)]
    public async Task<IActionResult> UpdateDescription(
        Guid projectId,
        Guid documentId,
        UpdateProjectDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessProjectAsync(projectId, cancellationToken))
            return NotFound(new { message = "Proje bulunamadı." });

        var doc = await db.ProjectDocuments
            .SingleOrDefaultAsync(x => x.Id == documentId && x.ProjectId == projectId, cancellationToken);

        if (doc is null)
            return NotFound(new { message = "Dosya bulunamadı." });

        doc.Description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Açıklama güncellendi." });
    }

    [HttpDelete("{documentId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.DocumentsDelete)]
    public async Task<IActionResult> Delete(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessProjectAsync(projectId, cancellationToken))
            return NotFound(new { message = "Proje bulunamadı." });

        var doc = await db.ProjectDocuments
            .SingleOrDefaultAsync(x => x.Id == documentId && x.ProjectId == projectId, cancellationToken);

        if (doc is null)
            return NotFound(new { message = "Dosya bulunamadı." });

        var wasCurrent = doc.IsCurrentVersion;

        doc.IsDeleted = true;
        doc.IsActive = false;
        doc.DeletedAtUtc = DateTime.UtcNow;
        doc.DeletedByUserId = currentUser.UserId;
        doc.IsCurrentVersion = false;

        if (wasCurrent)
        {
            var previous = await db.ProjectDocuments
                .Where(x => x.ProjectId == projectId &&
                            x.ProjectSiteId == doc.ProjectSiteId &&
                            x.Folder == doc.Folder &&
                            x.FileName == doc.FileName &&
                            x.Id != doc.Id)
                .OrderByDescending(x => x.VersionNumber)
                .FirstOrDefaultAsync(cancellationToken);

            if (previous is not null)
                previous.IsCurrentVersion = true;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Dosya silindi." });
    }

    private async Task<bool> CanAccessProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.CompanyId, x.BranchId })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return false;

        var siteIds = await db.ProjectSites
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var scope = await dataScope.GetAsync(cancellationToken);
        return scope is not null &&
            scope.CanAccessProjectDocuments(project.CompanyId, project.BranchId, projectId, siteIds);
    }

    private static string SanitizeFolder(string folder)
    {
        var trimmed = folder.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return "Diger";

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var cleaned = new string(trimmed
            .Where(character => !invalidCharacters.Contains(character) && character != '.')
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "Diger" : cleaned;
    }

    private static string ResolveContentType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".zip" => "application/zip",
            ".rar" => "application/vnd.rar",
            ".dwg" => "application/acad",
            ".dxf" => "application/dxf",
            _ => "application/octet-stream"
        };
}
