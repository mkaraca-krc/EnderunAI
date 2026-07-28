using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Contracts.HumanResources;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hr/workforce")]
public sealed class HrWorkforceController(IHrApprovalService service)
    : ControllerBase
{
    [HttpGet("leaves")]
    public async Task<IActionResult> GetLeaves(
        [FromQuery] Guid? companyId, [FromQuery] Guid? personnelId,
        [FromQuery] Guid? projectId, [FromQuery] int? leaveType,
        [FromQuery] int? status, [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate, CancellationToken cancellationToken) =>
        Ok(await service.GetLeavesAsync(
            companyId, personnelId, projectId, leaveType, status,
            startDate, endDate, cancellationToken));

    [HttpPost("leaves")]
    public Task<IActionResult> CreateLeave(
        CreateHrLeaveRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.CreateLeaveAsync(
                request, CurrentUserId(), cancellationToken)));

    [HttpPut("leaves/{id:guid}")]
    public Task<IActionResult> UpdateLeave(
        Guid id, UpdateHrLeaveRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.UpdateLeaveAsync(
                id, request, CurrentUserId(), cancellationToken)));

    [HttpPost("leaves/{id:guid}/approve")]
    public Task<IActionResult> ApproveLeave(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.ApproveLeaveAsync(
                id, CurrentUserId(), cancellationToken)));

    [HttpPost("leaves/{id:guid}/reject")]
    public Task<IActionResult> RejectLeave(
        Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.RejectLeaveAsync(
                id, request.Reason, CurrentUserId(), cancellationToken)));

    [HttpDelete("leaves/{id:guid}")]
    public Task<IActionResult> DeleteLeave(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await service.DeleteLeaveAsync(id, CurrentUserId(), cancellationToken);
            return Ok(new { message = "İzin talebi silindi." });
        });

    [HttpGet("overtimes")]
    public async Task<IActionResult> GetOvertimes(
        [FromQuery] Guid? companyId, [FromQuery] Guid? personnelId,
        [FromQuery] Guid? projectId, [FromQuery] int? status,
        [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken) =>
        Ok(await service.GetOvertimesAsync(
            companyId, personnelId, projectId, status,
            startDate, endDate, cancellationToken));

    [HttpPost("overtimes")]
    public Task<IActionResult> CreateOvertime(
        CreateHrOvertimeRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.CreateOvertimeAsync(
                request, CurrentUserId(), cancellationToken)));

    [HttpPut("overtimes/{id:guid}")]
    public Task<IActionResult> UpdateOvertime(
        Guid id, UpdateHrOvertimeRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.UpdateOvertimeAsync(
                id, request, CurrentUserId(), cancellationToken)));

    [HttpPost("overtimes/{id:guid}/approve")]
    public Task<IActionResult> ApproveOvertime(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.ApproveOvertimeAsync(
                id, CurrentUserId(), cancellationToken)));

    [HttpPost("overtimes/{id:guid}/reject")]
    public Task<IActionResult> RejectOvertime(
        Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.RejectOvertimeAsync(
                id, request.Reason, CurrentUserId(), cancellationToken)));

    [HttpDelete("overtimes/{id:guid}")]
    public Task<IActionResult> DeleteOvertime(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await service.DeleteOvertimeAsync(id, CurrentUserId(), cancellationToken);
            return Ok(new { message = "Fazla mesai kaydı silindi." });
        });

    [HttpGet("advances")]
    public async Task<IActionResult> GetAdvances(
        [FromQuery] Guid? companyId, [FromQuery] Guid? personnelId,
        [FromQuery] Guid? projectId, [FromQuery] int? status,
        [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken) =>
        Ok(await service.GetAdvancesAsync(
            companyId, personnelId, projectId, status,
            startDate, endDate, cancellationToken));

    [HttpPost("advances")]
    public Task<IActionResult> CreateAdvance(
        CreateHrAdvanceRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.CreateAdvanceAsync(
                request, CurrentUserId(), cancellationToken)));

    [HttpPut("advances/{id:guid}")]
    public Task<IActionResult> UpdateAdvance(
        Guid id, UpdateHrAdvanceRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.UpdateAdvanceAsync(
                id, request, CurrentUserId(), cancellationToken)));

    [HttpPost("advances/{id:guid}/approve")]
    public Task<IActionResult> ApproveAdvance(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.ApproveAdvanceAsync(
                id, CurrentUserId(), cancellationToken)));

    [HttpPost("advances/{id:guid}/reject")]
    public Task<IActionResult> RejectAdvance(
        Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.RejectAdvanceAsync(
                id, request.Reason, CurrentUserId(), cancellationToken)));

    [HttpPost("advances/{id:guid}/paid")]
    public Task<IActionResult> MarkAdvancePaid(
        Guid id, MarkAdvancePaidRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.MarkAdvancePaidAsync(
                id, request.PaymentReference, CurrentUserId(), cancellationToken)));

    [HttpDelete("advances/{id:guid}")]
    public Task<IActionResult> DeleteAdvance(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await service.DeleteAdvanceAsync(id, CurrentUserId(), cancellationToken);
            return Ok(new { message = "Avans talebi silindi." });
        });

    private Guid? CurrentUserId()
    {
        var value =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static async Task<IActionResult> ExecuteAsync(
        Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException exception)
        {
            return new NotFoundObjectResult(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return new BadRequestObjectResult(new { message = exception.Message });
        }
        catch (DbUpdateException)
        {
            return new ConflictObjectResult(new
            {
                message = "Kayıt veritabanı kısıtları nedeniyle tamamlanamadı."
            });
        }
    }
}
