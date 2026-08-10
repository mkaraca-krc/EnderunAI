using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Vade bazlı nakit akışı: önümüzdeki 30/60/90 günde beklenen
/// tahsilatlar ve ödemeler, mevcut kasa/banka bakiyesiyle birlikte.
/// </summary>
/// <summary>Tekrarlayan tahmini gider isteği.</summary>
public sealed record SaveEstimatedExpenseRequest(
    Guid CompanyId,
    string Description,
    decimal Amount,
    int StartYear,
    int StartMonth,
    int RecurrenceCount,
    int PaymentDay,
    Guid? ProjectId);

[ApiController]
[Authorize]
[Route("api/cash-flow")]
public sealed class CashFlowController(
    ICashFlowService service,
    ICashFlowProjectionService projection,
    AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid companyId,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        return Ok(await service.GetAsync(companyId, projectId, cancellationToken));
    }

    /// <summary>
    /// Likidite takvimi: TARİH BAZLI yürüyen bakiye, finansman açığı
    /// ve aylık özet.
    ///
    /// AYRI VE DAR İZİN (cashflow.view): tablo bordro çıkışını ELDEN
    /// DAHİL tam tutarla taşıyor. finance.view'e bırakılsaydı Teknik
    /// Ofis ve Teknik Koordinatör de görürdü — ikisinde de ek ödeme
    /// yetkisi yok ve elden toplamı buradan sızardı. Kapı dar
    /// tutulduğu için tablo İÇERİDE tek ve eksiksiz: kalem gizleme
    /// yok, iki kullanıcı aynı bakiyeyi okuyor.
    /// </summary>
    [HttpGet("projeksiyon")]
    [RequirePermission(PermissionCatalog.Keys.CashFlowView)]
    public async Task<IActionResult> GetProjection(
        [FromQuery] Guid companyId,
        [FromQuery] int? months,
        [FromQuery] DateTime? targetDate,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        return Ok(await projection.GetAsync(
            companyId, months ?? 6, targetDate, cancellationToken));
    }

    // ---------------- Tahmini gider (gider merkezi stopgap) ----------------

    /// <summary>
    /// Tekrarlayan tahmini giderler. Aynı dar kapıda: bu satırlar
    /// likidite tablosunu doğrudan etkiliyor.
    /// </summary>
    [HttpGet("tahmini-giderler")]
    [RequirePermission(PermissionCatalog.Keys.CashFlowView)]
    public async Task<IActionResult> GetEstimatedExpenses(
        [FromQuery] Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await db.CashFlowEstimatedExpenses
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.StartYear).ThenBy(x => x.StartMonth)
            .Select(x => new
            {
                x.Id, x.Description, x.Amount, x.StartYear, x.StartMonth,
                x.RecurrenceCount, x.PaymentDay, x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost("tahmini-giderler")]
    [RequirePermission(PermissionCatalog.Keys.CashFlowView)]
    public async Task<IActionResult> CreateEstimatedExpense(
        SaveEstimatedExpenseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { message = "Gider açıklaması zorunludur." });

        if (request.Amount <= 0m)
            return BadRequest(new { message = "Tutar sıfırdan büyük olmalıdır." });

        if (request.StartMonth is < 1 or > 12)
            return BadRequest(new { message = "Geçersiz başlangıç ayı." });

        if (request.PaymentDay is < 1 or > 31)
            return BadRequest(new { message = "Ödeme günü 1-31 arasında olmalıdır." });

        // Süresiz tekrar yok: gözden geçirilmeyen bir varsayıma
        // dönüşürdü. Üst sınır en uzun ufkun (12 ay) iki katı.
        if (request.RecurrenceCount is < 1 or > 24)
        {
            return BadRequest(new
            {
                message = "Tekrar sayısı 1-24 ay arasında olmalıdır."
            });
        }

        var expense = new CashFlowEstimatedExpense
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            Description = request.Description.Trim(),
            Amount = request.Amount,
            StartYear = request.StartYear,
            StartMonth = request.StartMonth,
            RecurrenceCount = request.RecurrenceCount,
            PaymentDay = request.PaymentDay
        };

        db.CashFlowEstimatedExpenses.Add(expense);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            expense.Id,
            message = "Tahmini gider eklendi; takvimde tahmini olarak görünecek."
        });
    }

    [HttpDelete("tahmini-giderler/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.CashFlowView)]
    public async Task<IActionResult> DeleteEstimatedExpense(
        Guid id, CancellationToken cancellationToken)
    {
        var expense = await db.CashFlowEstimatedExpenses
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (expense is null)
            return NotFound(new { message = "Tahmini gider bulunamadı." });

        db.CashFlowEstimatedExpenses.Remove(expense);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Tahmini gider kaldırıldı." });
    }
}
