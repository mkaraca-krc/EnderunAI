using EnderunAI.Api.Contracts.Pricing;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/pricing")]
public sealed class PricingController : ControllerBase
{
    [HttpPost("calculate-offer")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public IActionResult CalculateOffer(CalculateOfferPriceRequest request)
    {
        if (request.ListPrice < 0)
            return BadRequest(new { message = "Liste fiyatı negatif olamaz." });

        var rates = new[]
        {
            request.DiscountRate,
            request.FreightRate,
            request.WasteRate,
            request.FinanceRate,
            request.GeneralExpenseRate,
            request.ProfitRate
        };

        if (rates.Any(x => x < 0 || x > 100))
            return BadRequest(new { message = "Oranlar 0 ile 100 arasında olmalıdır." });

        var netPurchasePrice =
            request.ListPrice * (1 - request.DiscountRate / 100m);

        var freightAmount = netPurchasePrice * request.FreightRate / 100m;
        var wasteAmount = netPurchasePrice * request.WasteRate / 100m;
        var financeAmount = netPurchasePrice * request.FinanceRate / 100m;
        var generalExpenseAmount =
            netPurchasePrice * request.GeneralExpenseRate / 100m;

        var costPrice =
            netPurchasePrice +
            freightAmount +
            wasteAmount +
            financeAmount +
            generalExpenseAmount;

        var profitAmount = costPrice * request.ProfitRate / 100m;
        var salesPrice = costPrice + profitAmount;

        return Ok(new CalculateOfferPriceResponse(
            decimal.Round(request.ListPrice, 4),
            decimal.Round(request.DiscountRate, 4),
            decimal.Round(netPurchasePrice, 4),
            decimal.Round(freightAmount, 4),
            decimal.Round(wasteAmount, 4),
            decimal.Round(financeAmount, 4),
            decimal.Round(generalExpenseAmount, 4),
            decimal.Round(costPrice, 4),
            decimal.Round(profitAmount, 4),
            decimal.Round(salesPrice, 4)));
    }
}
