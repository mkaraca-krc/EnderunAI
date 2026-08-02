using EnderunAI.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/warehouses")]
public sealed class WarehousesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        var query = db.Warehouses.AsNoTracking().Where(x => x.IsActive);

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        var warehouses = await query
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.ProjectId,
                x.ProjectSiteId,
                x.Code,
                x.Name,
                x.Type
            })
            .ToListAsync(cancellationToken);

        return Ok(warehouses);
    }
}
