using EnderunAI.Api.Contracts.OfferCosting;
using EnderunAI.Api.Services.Costing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/offer-costing")]
public sealed class OfferCostingController(
    ICostEngine costEngine) : ControllerBase
{
    [HttpPost("estimate-position")]
    public async Task<IActionResult> EstimatePosition(
        EstimatePositionCostRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await costEngine.EstimatePositionAsync(
                    request,
                    cancellationToken);

            return Ok(result);
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
            return BadRequest(new
            {
                message = exception.Message
            });
        }
    }
}
