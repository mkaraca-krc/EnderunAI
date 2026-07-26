using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/supplier-performance")]
[Authorize]
public sealed class SupplierPerformanceController(
    SupplierPerformanceDbContext db,
    ISupplierPerformanceService service) : ControllerBase
{
    public sealed record QualityRecordRequest(
        Guid CompanyId,
        Guid SupplierCurrentAccountId,
        Guid? PurchaseOrderId,
        Guid? GoodsReceiptId,
        Guid? MaterialId,
        SupplierQualityEventType EventType,
        decimal Quantity,
        decimal ImpactScore,
        string? Description,
        DateTime? EventDateUtc);

    public sealed record ManualEvaluationRequest(
        Guid CompanyId,
        Guid SupplierCurrentAccountId,
        decimal CommunicationScore,
        decimal FinancialScore,
        decimal QualityScore,
        decimal TechnicalScore,
        string? Comment);

    [HttpPost("calculate/{supplierId:guid}")]
    public async Task<ActionResult> Calculate(
        Guid supplierId,
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? periodStartUtc,
        [FromQuery] DateTime? periodEndUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CalculateAsync(companyId, supplierId, periodStartUtc, periodEndUtc, true, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("suppliers/{supplierId:guid}/latest")]
    public async Task<ActionResult> Latest(Guid supplierId, [FromQuery] Guid companyId, CancellationToken cancellationToken)
    {
        var result = await db.Snapshots.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SupplierCurrentAccountId == supplierId)
            .OrderByDescending(x => x.PeriodEndUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("suppliers/{supplierId:guid}/history")]
    public async Task<ActionResult> History(Guid supplierId, [FromQuery] Guid companyId, CancellationToken cancellationToken)
    {
        var result = await db.Snapshots.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SupplierCurrentAccountId == supplierId)
            .OrderByDescending(x => x.PeriodEndUtc)
            .Take(24)
            .ToListAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("ranking")]
    public async Task<ActionResult> Ranking([FromQuery] Guid companyId, [FromQuery] int take = 20, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var latestIds = await db.Snapshots.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => x.SupplierCurrentAccountId)
            .Select(x => x.OrderByDescending(y => y.PeriodEndUtc).Select(y => y.Id).First())
            .ToListAsync(cancellationToken);

        var result = await db.Snapshots.AsNoTracking()
            .Where(x => latestIds.Contains(x.Id))
            .OrderByDescending(x => x.OverallScore)
            .Take(take)
            .ToListAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("quality-records")]
    public async Task<ActionResult> AddQualityRecord(QualityRecordRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity < 0 || request.ImpactScore is < 0 or > 100)
            return BadRequest("Miktar veya etki puanı geçersizdir.");

        var entity = new SupplierQualityRecord
        {
            CompanyId = request.CompanyId,
            SupplierCurrentAccountId = request.SupplierCurrentAccountId,
            PurchaseOrderId = request.PurchaseOrderId,
            GoodsReceiptId = request.GoodsReceiptId,
            MaterialId = request.MaterialId,
            EventType = request.EventType,
            Quantity = request.Quantity,
            ImpactScore = request.ImpactScore,
            Description = request.Description?.Trim(),
            CreatedByUserId = GetUserId(),
            CreatedByName = GetUserName(),
            EventDateUtc = request.EventDateUtc ?? DateTime.UtcNow
        };
        db.QualityRecords.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpPost("manual-evaluations")]
    public async Task<ActionResult> AddManualEvaluation(ManualEvaluationRequest request, CancellationToken cancellationToken)
    {
        var scores = new[] { request.CommunicationScore, request.FinancialScore, request.QualityScore, request.TechnicalScore };
        if (scores.Any(x => x is < 0 or > 100))
            return BadRequest("Tüm değerlendirme puanları 0 ile 100 arasında olmalıdır.");

        var entity = new SupplierManualEvaluation
        {
            CompanyId = request.CompanyId,
            SupplierCurrentAccountId = request.SupplierCurrentAccountId,
            CommunicationScore = request.CommunicationScore,
            FinancialScore = request.FinancialScore,
            QualityScore = request.QualityScore,
            TechnicalScore = request.TechnicalScore,
            Comment = request.Comment?.Trim(),
            EvaluatedByUserId = GetUserId(),
            EvaluatedByName = GetUserName()
        };
        db.ManualEvaluations.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private string GetUserName() => User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "Bilinmeyen kullanıcı";
}
