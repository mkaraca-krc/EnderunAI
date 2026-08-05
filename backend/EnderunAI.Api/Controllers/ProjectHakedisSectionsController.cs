using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <param name="ContractType">Yalnızca KARMA projede anlamlı; boşsa
/// projenin sözleşme tipi geçerlidir.</param>
public sealed record HakedisSectionRequest(
    Guid? Id,
    int Order,
    string Name,
    string? Code,
    bool IsActive,
    ProjectContractType? ContractType = null);

public sealed record ReplaceHakedisSectionsRequest(
    IReadOnlyCollection<HakedisSectionRequest> Sections);

/// <summary>
/// Projenin imalat bölümleri (NATURA'da 12 bölüm). Bölümler koda
/// gömülmedi: her projenin kırılımı farklı olur. NATURA listesi yalnızca
/// şablon olarak sunulur.
/// </summary>
[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/hakedis-sections")]
public sealed class ProjectHakedisSectionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> List(
        Guid projectId, CancellationToken cancellationToken) =>
        Ok(await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Order)
            .Select(x => new
            {
                x.Id,
                x.Order,
                x.Name,
                x.Code,
                x.IsActive,
                ContractType = x.ContractType == null ? (int?)null : (int)x.ContractType
            })
            .ToListAsync(cancellationToken));

    /// <summary>
    /// Hazır kısım şablonları. Eski uç NATURA listesini düz dizi olarak
    /// döndürüyordu; mevcut çağıranlar bozulmasın diye o davranış
    /// korunuyor, şablon seti ayrı uçtan geliyor.
    /// </summary>
    [HttpGet("/api/hakedis-section-template")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public IActionResult Template() =>
        Ok(HakedisSectionTemplate.Natura
            .Select((name, index) => new { order = index + 1, name })
            .ToList());

    /// <summary>
    /// Tüm hazır şablonlar (konut, endüstriyel, otel, hastane, AVM).
    /// Tek tıkla uygulanır; sonra serbestçe düzenlenir. Şablon seçmeden
    /// boş başlamak da mümkündür — bu uç yalnızca öneri sunar.
    /// </summary>
    [HttpGet("/api/hakedis-section-templates")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public IActionResult Templates() =>
        Ok(HakedisSectionTemplate.All.Select(template => new
        {
            template.Key,
            template.Name,
            template.Description,
            SectionCount = template.Sections.Count,
            Sections = template.Sections
                .Select((name, index) => new { Order = index + 1, Name = name })
                .ToList()
        }));

    /// <summary>
    /// Bölüm listesini topluca değiştirir. Id verilen satır güncellenir,
    /// verilmeyen eklenir, listede olmayan pasife çekilir — silinmez,
    /// çünkü geçmiş hakedişlerin satırları o bölüme bağlı olabilir.
    /// </summary>
    [HttpPut]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    public async Task<IActionResult> Replace(
        Guid projectId,
        ReplaceHakedisSectionsRequest request,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        if (request.Sections.Any(x => string.IsNullOrWhiteSpace(x.Name)))
            return BadRequest(new { message = "Bölüm adı boş olamaz." });

        var existing = await db.ProjectHakedisSections
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        var keptIds = new HashSet<Guid>();

        foreach (var item in request.Sections)
        {
            var current = item.Id is Guid id
                ? existing.SingleOrDefault(x => x.Id == id)
                : null;

            if (current is null)
            {
                db.ProjectHakedisSections.Add(new ProjectHakedisSection
                {
                    ProjectId = projectId,
                    Order = item.Order,
                    Name = item.Name.Trim(),
                    Code = string.IsNullOrWhiteSpace(item.Code) ? null : item.Code.Trim(),
                    IsActive = item.IsActive,
                    ContractType = item.ContractType
                });
                continue;
            }

            current.Order = item.Order;
            current.Name = item.Name.Trim();
            current.Code = string.IsNullOrWhiteSpace(item.Code) ? null : item.Code.Trim();
            current.IsActive = item.IsActive;
            current.ContractType = item.ContractType;
            current.UpdatedAtUtc = DateTime.UtcNow;

            keptIds.Add(current.Id);
        }

        // Listeden çıkarılan bölümler pasife çekilir; geçmiş hakedişlerin
        // icmali bozulmasın.
        foreach (var orphan in existing.Where(x => !keptIds.Contains(x.Id) && x.IsActive))
        {
            orphan.IsActive = false;
            orphan.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Bölümler kaydedildi." });
    }
}
