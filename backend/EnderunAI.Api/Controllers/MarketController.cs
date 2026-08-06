using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Market;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Piyasa verisi: TCMB kurları (ve ilerleyen fazda emtia fiyatları).
/// Okuma finans görüntüleme iznine, elle tazeleme finans yönetimine bağlı.
/// </summary>
[ApiController]
[Authorize]
[Route("api/market")]
public sealed class MarketController(
    IExchangeRateService exchangeRates,
    ICommodityPriceService commodityPrices,
    ICopperExposureService copperExposure) : ControllerBase
{
    /// <summary>
    /// Açık projelerin bakır/kur etkisi. Tonajı bilinmeyen projeler de
    /// listede kalır — görünmezlik "risk yok" izlenimi verirdi.
    /// </summary>
    [HttpGet("copper-impact")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetCopperImpact(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken)
        => Ok(await copperExposure.GetPortfolioAsync(companyId, cancellationToken));

    [HttpGet("copper-impact/{projectId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetProjectCopperImpact(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var impact = await copperExposure.GetForProjectAsync(projectId, cancellationToken);

        return impact is null ? NotFound() : Ok(impact);
    }

    /// <summary>Projenin kalan bakır tonajını ve taban tarihini kaydeder.</summary>
    [HttpPut("copper-impact/{projectId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.FinanceManage)]
    public async Task<IActionResult> SaveCopperExposure(
        Guid projectId,
        [FromBody] CopperExposureInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var impact = await copperExposure.SaveExposureAsync(
                projectId, input, cancellationToken);

            return impact is null ? NotFound() : Ok(impact);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Bakır özeti ve trendi. <c>days</c> 7/30/90 olarak kullanılır.
    /// Kaynak etiketi yanıtta daima döner — COMEX ile LME'nin aynı şey
    /// olmadığı ekranda görünmek zorunda.
    /// </summary>
    [HttpGet("commodities/copper")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetCopper(
        [FromQuery] int days,
        CancellationToken cancellationToken)
    {
        var window = days is > 0 and <= 365 ? days : 30;

        return Ok(await commodityPrices.GetSummaryAsync(
            Models.Market.Commodity.Copper, window, cancellationToken));
    }

    [HttpPost("commodities/refresh")]
    [RequirePermission(PermissionCatalog.Keys.FinanceManage)]
    public async Task<IActionResult> RefreshCommodities(
        [FromQuery] int days,
        CancellationToken cancellationToken)
    {
        var window = days is > 0 and <= 365 ? days : 30;
        var result = await commodityPrices.RefreshAsync(window, cancellationToken);

        return Ok(result);
    }

    /// <summary>Belirli bir tarihe uygulanacak kur.</summary>
    [HttpGet("exchange-rates/lookup")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> Lookup(
        [FromQuery] string currency,
        [FromQuery] DateTime? date,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return BadRequest(new { message = "Para birimi zorunludur." });

        var target = date ?? DateTime.UtcNow.Date;
        var lookup = await exchangeRates.GetAsync(currency, target, cancellationToken);

        if (lookup is null)
        {
            return NotFound(new
            {
                message =
                    $"{currency.Trim().ToUpperInvariant()} için arşivde kur bulunamadı. " +
                    "Kur girilmeden dövizli işlem yapılamaz."
            });
        }

        return Ok(new
        {
            lookup.CurrencyCode,
            lookup.RequestedDate,
            lookup.EffectiveDate,
            lookup.ForexBuying,
            lookup.ForexSelling,
            lookup.Source,
            lookup.DaysBack
        });
    }

    /// <summary>Tarih aralığındaki kur arşivi — grafik ve mutabakat için.</summary>
    [HttpGet("exchange-rates")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetRange(
        [FromQuery] string currency,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return BadRequest(new { message = "Para birimi zorunludur." });

        var end = to ?? DateTime.UtcNow.Date;
        var start = from ?? end.AddDays(-30);

        var items = await exchangeRates.GetRangeAsync(
            currency, start, end, cancellationToken);

        return Ok(items.Select(x => new
        {
            x.RateDate,
            x.CurrencyCode,
            x.ForexBuying,
            x.ForexSelling,
            x.BanknoteBuying,
            x.BanknoteSelling,
            x.BulletinNumber,
            x.Source
        }));
    }

    /// <summary>Arşivin tazeliği — "güncellenemedi" uyarısının kaynağı.</summary>
    [HttpGet("exchange-rates/freshness")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetFreshness(CancellationToken cancellationToken)
        => Ok(await exchangeRates.GetFreshnessAsync(cancellationToken));

    /// <summary>Kurları şimdi güncelle. Gecelik iş beklenmeden tetiklenebilir.</summary>
    [HttpPost("exchange-rates/refresh")]
    [RequirePermission(PermissionCatalog.Keys.FinanceManage)]
    public async Task<IActionResult> Refresh(
        [FromQuery] int days,
        CancellationToken cancellationToken)
    {
        var window = days is > 0 and <= 365 ? days : 7;
        var today = DateTime.UtcNow.Date;

        var result = await exchangeRates.RefreshAsync(
            today.AddDays(-window), today, cancellationToken);

        return Ok(new
        {
            result.FetchedDays,
            result.AlreadyPresentDays,
            result.UnavailableDays,
            result.Message,
            Errors = result.Errors
        });
    }
}
