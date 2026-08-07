using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hakedis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Controllers;

/// <summary>İşverenden teslim alınan mal/hizmet kaydı.</summary>
public sealed record BarterReceiptRequest(
    Guid ProjectId,
    Guid? ProjectSiteId,
    DateTime EntryDate,
    decimal Amount,
    string Description,
    string? Notes);

/// <summary>
/// Barter defteri. Hakedişten kesilen barter, işverenden mal/hizmet
/// (daire, dükkân vb.) alacağımızdır; teslim alındıkça bakiye düşer.
///
/// Kesinti kayıtlarını hakediş kendisi yazar; buradan yalnızca teslim
/// alma girilir.
/// </summary>
[ApiController]
[Authorize]
[Route("api/barter-ledger")]
public sealed class BarterLedgerController(AppDbContext db) : ControllerBase
{
    /// <summary>Projenin barter hareketleri ve açık bakiyesi.</summary>
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken)
    {
        var entries = await db.BarterLedgerEntries
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.EntryDate)
            .Select(x => new
            {
                x.Id,
                x.ProjectSiteId,
                ProjectSiteName = x.ProjectSite != null ? x.ProjectSite.Name : null,
                x.ProgressPaymentId,
                ProgressPaymentNumber = x.ProgressPayment != null
                    ? x.ProgressPayment.ProgressPaymentNumber
                    : null,
                EntryType = (int)x.EntryType,
                x.EntryDate,
                x.Amount,
                x.Description,
                x.Notes
            })
            .ToListAsync(cancellationToken);

        var deducted = entries
            .Where(x => x.EntryType == (int)BarterEntryType.Deduction)
            .Sum(x => x.Amount);

        var received = entries
            .Where(x => x.EntryType == (int)BarterEntryType.Receipt)
            .Sum(x => x.Amount);

        return Ok(new
        {
            projectId,
            totalDeducted = deducted,
            totalReceived = received,
            // İşverenden alınmayı bekleyen mal/hizmet tutarı.
            openBalance = HakedisCalculationService.CalculateBarterBalance(
                deducted, received),
            entries
        });
    }

    /// <summary>
    /// İşverenden teslim alınan mal/hizmeti kaydeder; barter bakiyesi
    /// bu kadar azalır.
    /// </summary>
    [HttpPost("receipts")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    public async Task<IActionResult> AddReceipt(
        BarterReceiptRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Amount <= 0m)
            return BadRequest(new { message = "Teslim alma tutarı sıfırdan büyük olmalıdır." });

        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { message = "Açıklama zorunludur." });

        var projectExists = await db.Projects
            .AnyAsync(x => x.Id == request.ProjectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var deducted = await db.BarterLedgerEntries
            .Where(x => x.ProjectId == request.ProjectId &&
                        x.EntryType == BarterEntryType.Deduction)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var received = await db.BarterLedgerEntries
            .Where(x => x.ProjectId == request.ProjectId &&
                        x.EntryType == BarterEntryType.Receipt)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var openBalance = HakedisCalculationService.CalculateBarterBalance(
            deducted, received);

        // Alacağımızdan fazlasını teslim almış görünmek bakiyeyi negatife
        // düşürürdü; kayıt hatalıdır.
        if (request.Amount > openBalance)
        {
            return Conflict(new
            {
                message = $"Teslim alma tutarı ({TurkishFormat.Amount(request.Amount)}) açık barter " +
                          $"bakiyesini ({TurkishFormat.Amount(openBalance)}) aşamaz."
            });
        }

        db.BarterLedgerEntries.Add(new BarterLedgerEntry
        {
            ProjectId = request.ProjectId,
            ProjectSiteId = request.ProjectSiteId,
            EntryType = BarterEntryType.Receipt,
            EntryDate = DateTime.SpecifyKind(request.EntryDate.Date, DateTimeKind.Utc),
            Amount = request.Amount,
            Description = request.Description.Trim(),
            Notes = request.Notes?.Trim()
        });

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Barter teslim alma kaydedildi." });
    }
}
