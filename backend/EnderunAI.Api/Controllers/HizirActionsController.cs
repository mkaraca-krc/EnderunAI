using System.Security.Claims;
using EnderunAI.Api.Contracts;
using EnderunAI.Api.Services.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/hizir/actions")]
[Authorize]
public sealed class HizirActionsController(IHizirActionService actionService) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<ActionResult<HizirActionPreview>> Preview(
        [FromBody] HizirActionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await actionService.PreviewAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("execute")]
    public async Task<ActionResult<HizirActionResult>> Execute(
        [FromBody] HizirActionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            Guid? userId = null;
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");
            if (Guid.TryParse(rawUserId, out var parsedUserId))
                userId = parsedUserId;

            return Ok(await actionService.ExecuteAsync(request, userId, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}