using EnderunAI.Api.Contracts.Purchasing;
using EnderunAI.Api.Services.Purchasing.Automation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/purchase-automation")]
public sealed class PurchaseAutomationController(
    IPurchaseRequestGenerator generator)
    : ControllerBase
{
    [HttpPost("generate-from-offer/{offerId:guid}")]
    public async Task<IActionResult> GenerateFromOffer(
        Guid offerId,
        GeneratePurchaseRequestFromOfferRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await generator.GenerateFromOfferAsync(
                    offerId,
                    request,
                    cancellationToken);

            return Ok(new
            {
                message =
                    "Satın alma talebi teklif reçetelerinden otomatik oluşturuldu.",
                result.PurchaseRequestId,
                result.RequestNumber,
                result.OfferId,
                result.OfferNumber,
                result.SourceOfferItemCount,
                result.GeneratedMaterialCount,
                result.TotalQuantity
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new
            {
                message = exception.Message
            });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new
            {
                message = exception.Message
            });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new
            {
                message = exception.Message
            });
        }
    }
}
