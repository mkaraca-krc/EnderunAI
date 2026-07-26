using EnderunAI.Api.Data;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/rfq-invitations")]
public sealed class RfqInvitationsController(
    IRfqInvitationService service,
    RfqInvitationDbContext db) : ControllerBase
{
    public sealed record SendRequest(IReadOnlyList<RfqInviteRecipient> Recipients, string PortalBaseUrl, bool SingleUse = false);
    public sealed record ReminderRequest(string PortalBaseUrl, int HoursBeforeDeadline = 24);

    [Authorize]
    [HttpPost("rfqs/{rfqId:guid}/send")]
    public async Task<ActionResult> Send(Guid rfqId, SendRequest request, CancellationToken cancellationToken)
    {
        if (request.Recipients.Count == 0) return BadRequest("En az bir tedarikçi seçilmelidir.");
        if (!Uri.TryCreate(request.PortalBaseUrl, UriKind.Absolute, out _)) return BadRequest("Portal adresi geçersiz.");
        return Ok(await service.SendAsync(rfqId, request.Recipients, request.PortalBaseUrl, request.SingleUse, cancellationToken));
    }

    [Authorize]
    [HttpPost("{invitationId:guid}/resend")]
    public async Task<ActionResult> Resend(Guid invitationId, [FromQuery] string portalBaseUrl, CancellationToken cancellationToken) =>
        Ok(await service.ResendAsync(invitationId, portalBaseUrl, cancellationToken));

    [Authorize]
    [HttpGet("rfqs/{rfqId:guid}")]
    public async Task<ActionResult> History(Guid rfqId, CancellationToken cancellationToken)
    {
        var rows = await db.Invitations.AsNoTracking()
            .Where(x => x.RfqId == rfqId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return Ok(rows);
    }

    [AllowAnonymous]
    [HttpGet("access/{token}")]
    public async Task<ActionResult> Access(string token, CancellationToken cancellationToken)
    {
        try
        {
            var invitation = await service.ValidateAsync(token, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), cancellationToken);
            return Ok(new { invitation.Id, invitation.RfqId, invitation.SupplierCurrentAccountId, invitation.ExpiresAtUtc, invitation.Status });
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [Authorize]
    [HttpPost("reminders")]
    public async Task<ActionResult> Reminders(ReminderRequest request, CancellationToken cancellationToken) =>
        Ok(new { sent = await service.SendDueRemindersAsync(request.PortalBaseUrl, request.HoursBeforeDeadline, cancellationToken) });
}
