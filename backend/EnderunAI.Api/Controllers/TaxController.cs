using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Tax;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Vergi yükü YÖNETİM GÖRÜNÜMÜ.
///
/// Bu uçlar beyanname üretmez; müşavirin beyanıyla mutabakat için
/// defterdeki rakamı ve ileriye dönük tahmini verir. Tahminler yanıtta
/// varsayımlarıyla birlikte döner ki ekran onları gizlemeden gösterebilsin.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tax")]
public sealed class TaxController(
    ITaxLedgerService taxLedger,
    IVatAccrualService vatAccrual) : ControllerBase
{
    [HttpGet("overview")]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] Guid companyId,
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await taxLedger.GetOverviewAsync(
                companyId, year ?? DateTime.UtcNow.Year, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Dönem sonu KDV tahakkuk fişi. Aynı dönem iki kez
    /// muhasebeleştirilemez.
    /// </summary>
    [HttpPost("vat-accrual")]
    [RequirePermission(PermissionCatalog.Keys.AccountingManage)]
    public async Task<IActionResult> AccrueVat(
        VatAccrualRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await vatAccrual.AccrueAsync(
                request.CompanyId, request.Year, request.Month, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Müşavir mutabakatı: hesaplanan tutarlar ile kesilen tahakkuk
    /// fişleri yan yana. Fark sıfır olmalı.
    /// </summary>
    [HttpGet("vat-reconciliation")]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetVatReconciliation(
        [FromQuery] Guid companyId,
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await vatAccrual.ReconcileAsync(
                companyId, year ?? DateTime.UtcNow.Year, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}

public sealed record VatAccrualRequest(Guid CompanyId, int Year, int Month);
