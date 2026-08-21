using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// MASRAF MERKEZİ SEÇENEKLERİ — TEK KAYNAK.
///
/// NEDEN VAR: masraf merkezi bu sistemde ayrı bir varlık değil; iki
/// alana bölünmüş — `ProjectId` (proje/şantiye) ve `CostCenterCode`
/// (merkez ofis, şubenin muhasebe kodu). Her ekran bu ikisini kendi
/// içinde ayrı ayrı topluyordu ve sonuç kullanıcıda şuydu: çek
/// ekranında proje seçicisine bakıp "Merkez"i orada arıyor,
/// bulamıyordu — çünkü Merkez İKİNCİ bir alandaydı.
///
/// Burada ikisi TEK LİSTE olarak birleşiyor: Merkez en üstte,
/// projeler altında. Ekranlar tek seçim yapıyor, seçim sunucuya
/// `projectId` ya da `costCenterCode` olarak çözülüyor.
/// </summary>
[ApiController]
[Authorize]
[Route("api/masraf-merkezleri")]
public sealed class CostCentersController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Seçenekler. `Kind` 0 = Merkez, 1 = Proje.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetOptions(
        [FromQuery] Guid? companyId,
        /// <summary>
        /// Mevcut kayıtta seçili olan proje. Kapalı/tamamlanmış olsa
        /// bile listeye KATILIR — aksi halde eski bir çeki açan
        /// kullanıcı kendi kaydının merkezini boş görür ve kaydederken
        /// farkında olmadan değiştirir.
        /// </summary>
        [FromQuery] Guid? includeProjectId,
        CancellationToken cancellationToken)
    {
        var branchQuery = db.Branches.AsNoTracking();

        if (companyId.HasValue)
            branchQuery = branchQuery.Where(x => x.CompanyId == companyId.Value);

        /*
         * MERKEZ ŞUBEDEN GELİYOR. Kod boşsa şube kodu kullanılıyor —
         * `Branch.CostCenterCode` yorumundaki kural: "Boşsa şube kodu
         * kullanılır". İki yerde farklı davranmamak için aynı kural.
         */
        var centers = await branchQuery
            .OrderByDescending(x => x.IsHeadOffice)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                Kind = 0,
                Code = x.CostCenterCode ?? x.Code,
                Label = x.IsHeadOffice ? "Merkez" : x.Name,
                ProjectId = (Guid?)null,
                IsClosed = false
            })
            .ToListAsync(cancellationToken);

        var projectQuery = db.Projects.AsNoTracking();

        if (companyId.HasValue)
            projectQuery = projectQuery.Where(x => x.CompanyId == companyId.Value);

        /*
         * KAPALI PROJE LİSTEDE YOK — ama mevcut kayıtta seçiliyse VAR.
         * Tamamlanmış projeye yeni çek işlenmemeli; eski çekin projesi
         * de kaybolmamalı. İki kural aynı sorguda.
         */
        projectQuery = projectQuery.Where(x =>
            (x.Status != ProjectStatus.Completed &&
             x.Status != ProjectStatus.Cancelled &&
             !x.IsArchived)
            || (includeProjectId != null && x.Id == includeProjectId.Value));

        var projects = await projectQuery
            .OrderBy(x => x.Code)
            .Select(x => new
            {
                Kind = 1,
                Code = x.Code,
                Label = $"{x.Code} — {x.Name}",
                ProjectId = (Guid?)x.Id,
                IsClosed = x.Status == ProjectStatus.Completed ||
                           x.Status == ProjectStatus.Cancelled ||
                           x.IsArchived
            })
            .ToListAsync(cancellationToken);

        // Merkez ÖNCE: ekran listenin başında ve ayrık gösteriyor.
        return Ok(centers.Concat(projects));
    }
}
