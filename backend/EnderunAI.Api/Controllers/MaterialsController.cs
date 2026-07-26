using EnderunAI.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/materials")]
public sealed class MaterialsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? companyId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = db.Materials.AsNoTracking().Where(x => x.IsActive);

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.Code.ToLower().Contains(term) ||
                x.Name.ToLower().Contains(term) ||
                (x.Brand != null && x.Brand.ToLower().Contains(term)));
        }

        var items = await query
            .OrderBy(x => x.Code)
            .Take(500)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.Code,
                x.Name,
                x.Unit,
                x.Category,
                x.Brand,
                x.Model,
                x.MinimumStock
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
