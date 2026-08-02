using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Contracts.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hr/payroll")]
public sealed class HrPayrollController(IHrApprovalService service) : ControllerBase
{
    [HttpGet("records")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> GetPayrolls(
        [FromQuery] Guid? companyId, [FromQuery] Guid? personnelId,
        [FromQuery] int? year, [FromQuery] int? month,
        [FromQuery] int? status, CancellationToken cancellationToken) =>
        Ok(await service.GetPayrollsAsync(
            companyId, personnelId, year, month, status, cancellationToken));

    [HttpGet("records/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public Task<IActionResult> GetPayroll(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.GetPayrollAsync(id, cancellationToken)));

    [HttpGet("summary")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public Task<IActionResult> GetSummary(
        [FromQuery] Guid companyId, [FromQuery] int year, [FromQuery] int month,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.GetPayrollSummaryAsync(
                companyId, year, month, cancellationToken)));

    [HttpPost("records/calculate-company")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollCreate)]
    public Task<IActionResult> CalculateCompany(
        CalculateCompanyPayrollRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.CalculateCompanyPayrollAsync(
                request, CurrentUserId(), cancellationToken)));

    [HttpPost("records/{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollApprove)]
    public Task<IActionResult> Approve(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.ApprovePayrollAsync(
                id, CurrentUserId(), cancellationToken)));

    [HttpPost("records/{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollEdit)]
    public Task<IActionResult> Cancel(
        Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.CancelPayrollAsync(
                id, request.Reason, CurrentUserId(), cancellationToken)));

    [HttpPost("records/{id:guid}/paid")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollEdit)]
    public Task<IActionResult> MarkPaid(
        Guid id, MarkPayrollPaidRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
            Ok(await service.MarkPayrollPaidAsync(
                id, request, CurrentUserId(), cancellationToken)));

    [HttpDelete("records/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollDelete)]
    public Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await service.DeletePayrollAsync(id, CurrentUserId(), cancellationToken);
            return Ok(new { message = "Bordro kaydı silindi." });
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
