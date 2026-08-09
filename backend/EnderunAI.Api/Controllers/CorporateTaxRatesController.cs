using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record SaveCorporateTaxRateRequest(
    Guid CompanyId,
    int Year,
    decimal Rate,
    string? Note);

/// <summary>
/// Yıl bazlı kurumlar vergisi oranı.
///
/// Oran daha önce hiçbir uçtan girilemiyordu: alan ayarlanabilir
/// görünüyor ama hesap her zaman koda gömülü %25'e düşüyordu. Oran
/// mevzuatla değiştiği için yıl bazlı tutuluyor ve varsayılanı yok —
/// girilmemiş yılda vergi tahmini üretilmez, ekran bunu söyler.
/// </summary>
[ApiController]
[Authorize]
[Route("api/kurumlar-vergisi-oranlari")]
public sealed class CorporateTaxRatesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid companyId,
        CancellationToken cancellationToken)
    {
        var rows = await db.CompanyCorporateTaxRates
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.Year)
            .Select(x => new { x.Id, x.CompanyId, x.Year, x.Rate, x.Note })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    /// <summary>
    /// Yılın oranını yazar. Aynı yıl için ikinci kayıt açılmaz;
    /// mevcut oran güncellenir.
    /// </summary>
    [HttpPut]
    [RequirePermission(PermissionCatalog.Keys.CompanySettingsEdit)]
    public async Task<IActionResult> Save(
        SaveCorporateTaxRateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Year is < 2000 or > 2100)
            return BadRequest(new { message = "Geçersiz yıl." });

        // Negatif oran anlamsız; %100 üstü oran veri girişi hatasıdır.
        if (request.Rate < 0m || request.Rate > 100m)
            return BadRequest(new { message = "Oran 0 ile 100 arasında olmalıdır." });

        if (!await db.Companies.AnyAsync(
                x => x.Id == request.CompanyId, cancellationToken))
        {
            return NotFound(new { message = "Şirket bulunamadı." });
        }

        var entity = await db.CompanyCorporateTaxRates.SingleOrDefaultAsync(
            x => x.CompanyId == request.CompanyId && x.Year == request.Year,
            cancellationToken);

        if (entity is null)
        {
            entity = new CompanyCorporateTaxRate
            {
                CompanyId = request.CompanyId,
                Year = request.Year
            };

            db.CompanyCorporateTaxRates.Add(entity);
        }

        entity.Rate = request.Rate;
        entity.Note = string.IsNullOrWhiteSpace(request.Note)
            ? null
            : request.Note.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = $"{request.Year} kurumlar vergisi oranı kaydedildi.",
            entity.Id,
            entity.Year,
            entity.Rate
        });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.CompanySettingsEdit)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.CompanyCorporateTaxRates
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Oran kaydı bulunamadı." });

        db.CompanyCorporateTaxRates.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Oran kaydı silindi." });
    }
}
