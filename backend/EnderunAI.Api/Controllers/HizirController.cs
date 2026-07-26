using EnderunAI.Api.Contracts;
using EnderunAI.Api.Services.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/hizir")]
[Authorize]
public sealed class HizirController(
    IHizirChatService chatService,
    IHizirDashboardAggregator dashboardAggregator) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<HizirDashboardSnapshot>> Dashboard(
        CancellationToken cancellationToken)
    {
        return Ok(await dashboardAggregator.GetSnapshotAsync(cancellationToken));
    }

    [HttpPost("chat")]
    public async Task<ActionResult<HizirChatResponse>> Chat(
        [FromBody] HizirChatRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await chatService.ReplyAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
    }
}
