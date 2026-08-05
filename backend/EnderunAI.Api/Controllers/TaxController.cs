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
    IVatAccrualService vatAccrual,
    ITaxObligationService taxObligations) : ControllerBase
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
    /// Vergi takvimi: verilen aralığa düşen yükümlülükler, ödenmişler
    /// dahil. Nakit akış bunlardan yalnızca ödenmemişleri gösterir.
    /// </summary>
    [HttpGet("calendar")]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetCalendar(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var start = from ?? DateTime.UtcNow.Date.AddDays(-90);
        var end = to ?? DateTime.UtcNow.Date.AddDays(180);

        try
        {
            return Ok(await taxObligations.GetObligationsAsync(
                companyId, start, end, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Vergi dönemini ödendi işaretler; nakit akıştan düşer.
    /// </summary>
    [HttpPost("payments")]
    [RequirePermission(PermissionCatalog.Keys.AccountingEdit)]
    public async Task<IActionResult> MarkPaid(
        MarkTaxPaidRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(TaxObligationKind), request.Kind))
            return BadRequest(new { message = "Geçersiz vergi türü." });

        try
        {
            return Ok(await taxObligations.MarkPaidAsync(
                request.CompanyId,
                (TaxObligationKind)request.Kind,
                request.PeriodYear,
                request.PeriodNumber,
                request.Amount,
                request.PaidAt,
                request.Note,
                cancellationToken));
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

    /// <summary>Yanlış işaretlenen ödemeyi geri alır.</summary>
    [HttpDelete("payments")]
    [RequirePermission(PermissionCatalog.Keys.AccountingEdit)]
    public async Task<IActionResult> UndoPayment(
        [FromQuery] Guid companyId,
        [FromQuery] int kind,
        [FromQuery] int periodYear,
        [FromQuery] int periodNumber,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(TaxObligationKind), kind))
            return BadRequest(new { message = "Geçersiz vergi türü." });

        try
        {
            await taxObligations.UndoPaymentAsync(
                companyId, (TaxObligationKind)kind, periodYear, periodNumber,
                cancellationToken);

            return NoContent();
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
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

/// <summary>
/// Vergi dönemi ödendi işaretleme. Tutar boş bırakılırsa tahmini tutar
/// kullanılır; gerçekte farklı ödendiyse elle girilir.
/// </summary>
public sealed record MarkTaxPaidRequest(
    Guid CompanyId,
    int Kind,
    int PeriodYear,
    int PeriodNumber,
    decimal? Amount = null,
    DateTime? PaidAt = null,
    string? Note = null);
