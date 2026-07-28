using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/finance")]
public sealed class FinanceDashboardController(
    IFinanceDashboardService service)
    : ControllerBase
{
    [HttpGet("dashboard")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> Dashboard(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? hierarchyNodeId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetDashboardAsync(
                companyId,
                projectId,
                hierarchyNodeId,
                cancellationToken));
        }
        catch (Exception exception)
            when (exception is ArgumentException or KeyNotFoundException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("cari-summary")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> CurrentAccountSummary(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? hierarchyNodeId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetCurrentAccountSummaryAsync(
                companyId,
                projectId,
                hierarchyNodeId,
                cancellationToken));
        }
        catch (Exception exception)
            when (exception is ArgumentException or KeyNotFoundException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("projects-summary")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> ProjectsSummary(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? hierarchyNodeId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetProjectsSummaryAsync(
                companyId,
                projectId,
                hierarchyNodeId,
                cancellationToken));
        }
        catch (Exception exception)
            when (exception is ArgumentException or KeyNotFoundException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("cash-flow")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> CashFlow(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? hierarchyNodeId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetCashFlowAsync(
                companyId,
                projectId,
                hierarchyNodeId,
                cancellationToken));
        }
        catch (Exception exception)
            when (exception is ArgumentException or KeyNotFoundException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("suppliers-summary")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> SuppliersSummary(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? hierarchyNodeId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetSuppliersSummaryAsync(
                companyId,
                projectId,
                hierarchyNodeId,
                cancellationToken));
        }
        catch (Exception exception)
            when (exception is ArgumentException or KeyNotFoundException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
