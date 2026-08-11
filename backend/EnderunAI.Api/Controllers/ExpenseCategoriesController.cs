using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record SaveExpenseCategoryRequest(
    Guid CompanyId,
    string Name,
    int SortOrder,
    bool IsActive);

/// <summary>
/// Gider kategorisi kataloğu ve gider merkezi listesi — gider
/// merkezinin iki ekseni. Kategori parametrik olduğu için katalog
/// yönetimi <c>expense.manage</c>, okuma <c>expense.view</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/expenses")]
public sealed class ExpenseCategoriesController(
    AppDbContext db,
    ExpenseCenterResolver centers) : ControllerBase
{
    [HttpGet("kategoriler")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseView)]
    public async Task<IActionResult> ListCategories(
        [FromQuery] Guid companyId,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        // Sonradan açılan şirkette liste boş dönmesin.
        await ExpenseCategoryProvisioner.EnsureAsync(db, companyId, cancellationToken);

        var rows = await db.ExpenseCategories
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new
            {
                id = x.Id,
                code = x.Code,
                name = x.Name,
                sortOrder = x.SortOrder,
                isSystem = x.IsSystem,
                // Otomatik kategoriler elle giriş listesinde
                // gösterilmez; ekran bu bayrağa göre filtreliyor.
                isAutomaticOnly = x.IsAutomaticOnly,
                isActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("merkezler")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseView)]
    public async Task<IActionResult> ListCenters(
        [FromQuery] Guid companyId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var rows = await centers.ListAsync(companyId, cancellationToken);

        return Ok(rows.Select(x => new
        {
            type = x.Type.ToString(),
            id = x.Id,
            name = x.Name,
            parentProjectId = x.ParentProjectId,
            isHeadOffice = x.IsHeadOffice
        }));
    }

    [HttpPost("kategoriler")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> CreateCategory(
        [FromBody] SaveExpenseCategoryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var name = (request.Name ?? string.Empty).Trim();

        if (name.Length == 0)
            return BadRequest(new { message = "Kategori adı zorunludur." });

        var code = TurkishSlug(name);

        if (code.Length == 0)
            return BadRequest(new { message = "Kategori adından geçerli bir kod üretilemedi." });

        var exists = await db.ExpenseCategories
            .AnyAsync(x => x.CompanyId == request.CompanyId && x.Code == code,
                cancellationToken);

        if (exists)
            return Conflict(new { message = "Bu adla bir kategori zaten var." });

        var category = new ExpenseCategory
        {
            CompanyId = request.CompanyId,
            Code = code,
            Name = name,
            SortOrder = request.SortOrder,
            IsSystem = false,
            IsAutomaticOnly = false,
            IsActive = request.IsActive
        };

        db.ExpenseCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = category.Id, code = category.Code });
    }

    /// <summary>
    /// Kategori düzeltme. Sistem kategorisinde AD ve SIRA değişir,
    /// KOD değişmez: otomatik kalemler koda bağlı, kod değişirse
    /// satın alma ve görev masrafı kategorisiz kalır.
    /// </summary>
    [HttpPut("kategoriler/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> UpdateCategory(
        Guid id,
        [FromBody] SaveExpenseCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await db.ExpenseCategories
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (category is null)
            return NotFound(new { message = "Kategori bulunamadı." });

        var name = (request.Name ?? string.Empty).Trim();

        if (name.Length == 0)
            return BadRequest(new { message = "Kategori adı zorunludur." });

        category.Name = name;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        category.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = category.Id });
    }

    /// <summary>
    /// Kategori silme. Sistem kategorisi SİLİNMEZ — pasife alınır.
    /// Silinseydi ona bağlı geçmiş kayıtlar kategorisiz kalır ve
    /// otomatik akış her açılışta kategoriyi yeniden arardı.
    /// </summary>
    [HttpDelete("kategoriler/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> DeleteCategory(
        Guid id, CancellationToken cancellationToken)
    {
        var category = await db.ExpenseCategories
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (category is null)
            return NotFound(new { message = "Kategori bulunamadı." });

        if (category.IsSystem)
            return BadRequest(new
            {
                message = "Kurulumla gelen kategori silinemez; pasife alınabilir."
            });

        db.ExpenseCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id });
    }

    /// <summary>
    /// Ad → kod. Türkçe karakterler sadeleştirilir, boşluk tire olur.
    /// Kod makine tarafı anahtar olduğu için sadeleştirme şart:
    /// "Araç/Yakıt" ile "arac-yakit" aynı kaydı göstermeli.
    /// </summary>
    private static string TurkishSlug(string value)
    {
        var normalized = Services.Engineering.TurkishSearch.Normalize(value);

        var builder = new System.Text.StringBuilder(normalized.Length);
        var lastWasDash = false;

        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
