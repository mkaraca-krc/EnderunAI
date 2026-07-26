using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/procurement-dashboard")]
[Authorize]
public sealed class ProcurementDashboardController(IProcurementDashboardService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProcurementDashboardSnapshot>> Get(
        [FromQuery] Guid companyId,
        [FromQuery] int months = 12,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            return BadRequest("Şirket bilgisi zorunludur.");

        return Ok(await service.GetAsync(companyId, months, cancellationToken));
    }

    [HttpGet("counters")]
    public async Task<ActionResult> Counters(
        [FromQuery] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            return BadRequest("Şirket bilgisi zorunludur.");

        var snapshot = await service.GetAsync(companyId, 1, cancellationToken);
        return Ok(new
        {
            snapshot.GeneratedAtUtc,
            snapshot.Counters,
            snapshot.Financial,
            snapshot.Approvals
        });
    }

    [HttpGet("trends")]
    public async Task<ActionResult> Trends(
        [FromQuery] Guid companyId,
        [FromQuery] int months = 12,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            return BadRequest("Şirket bilgisi zorunludur.");

        var snapshot = await service.GetAsync(companyId, months, cancellationToken);
        return Ok(new
        {
            snapshot.GeneratedAtUtc,
            snapshot.MonthlyTrend,
            snapshot.OrderStatusDistribution
        });
    }

    [HttpGet("suppliers")]
    public async Task<ActionResult> Suppliers(
        [FromQuery] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            return BadRequest("Şirket bilgisi zorunludur.");

        var snapshot = await service.GetAsync(companyId, 1, cancellationToken);
        return Ok(snapshot.TopSuppliers);
    }

    [HttpGet("projects")]
    public async Task<ActionResult> Projects(
        [FromQuery] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            return BadRequest("Şirket bilgisi zorunludur.");

        var snapshot = await service.GetAsync(companyId, 1, cancellationToken);
        return Ok(new
        {
            snapshot.TopProjects,
            snapshot.Budgets
        });
    }
}
