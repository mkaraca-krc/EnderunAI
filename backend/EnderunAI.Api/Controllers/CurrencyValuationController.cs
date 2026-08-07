using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>Değerleme fişi kesme isteği.</summary>
/// <param name="CompanyId">Şirket.</param>
/// <param name="ValuationDate">Değerleme tarihi.</param>
public sealed record PostCurrencyValuationRequest(
    Guid CompanyId,
    DateTime ValuationDate);

/// <summary>Değerleme turu iptal isteği.</summary>
/// <param name="Reason">İptal gerekçesi.</param>
public sealed record ReverseCurrencyValuationRequest(string? Reason);

/// <summary>
/// Dönem sonu kur değerlemesi (646/656).
///
/// Önizleme herkese değil, muhasebeyi görene açıktır; fiş kesmek ve
/// iptal etmek ayrı bir yetki ister — bu fişler doğrudan kâr/zarara
/// yazıyor.
/// </summary>
[ApiController]
[Authorize]
[Route("api/accounting/currency-valuation")]
public sealed class CurrencyValuationController(
    CurrencyValuationService valuationService) : ControllerBase
{
    /// <summary>
    /// Kesilecek fişin önizlemesi. Hiçbir kayıt yazmaz.
    /// </summary>
    [HttpGet("preview")]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> Preview(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? valuationDate,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçilmedi." });

        var date = valuationDate ?? DateTime.UtcNow.Date;

        var preview = await valuationService.PreviewAsync(
            companyId, date, cancellationToken);

        return Ok(preview);
    }

    /// <summary>
    /// Değerleme fişini keser ve kesinleştirir.
    /// </summary>
    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.AccountingManage)]
    public async Task<IActionResult> Post(
        [FromBody] PostCurrencyValuationRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.CompanyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçilmedi." });

        try
        {
            var run = await valuationService.PostAsync(
                request.CompanyId,
                request.ValuationDate,
                CurrentUserId(),
                cancellationToken);

            return Ok(new
            {
                run.Id,
                run.ValuationDate,
                run.PostedDifference,
                run.AccountingVoucherId,
                lineCount = run.Lines.Count
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Değerleme turunu ters kayıtla iptal eder.
    /// </summary>
    [HttpPost("{id:guid}/reverse")]
    [RequirePermission(PermissionCatalog.Keys.AccountingManage)]
    public async Task<IActionResult> Reverse(
        Guid id,
        [FromBody] ReverseCurrencyValuationRequest? request,
        [FromServices] IAccountingIntegrationService integration,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await valuationService.ReverseAsync(
                id,
                request?.Reason ?? "Kur değerlemesi iptali",
                integration,
                cancellationToken);

            return Ok(new
            {
                run.Id,
                run.ReversalVoucherId,
                run.ReversedAtUtc
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid? CurrentUserId()
    {
        var raw = User.FindFirst("sub")?.Value
            ?? User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
