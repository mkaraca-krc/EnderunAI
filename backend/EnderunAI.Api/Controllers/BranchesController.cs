using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/branches")]
public sealed class BranchesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.CompaniesView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken)
    {
        var query = db.Branches.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        var items = await query
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.Code,
                x.Name,
                x.Phone,
                x.Email,
                x.Address,
                x.IsHeadOffice,
                // Boşsa şube kodu masraf merkezi kodu olarak kullanılır.
                CostCenterCode = x.CostCenterCode ?? x.Code,
                x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.CompaniesManage)]
    public async Task<IActionResult> Create(
        CreateBranchRequest request,
        CancellationToken cancellationToken)
    {
        var companyExists = await db.Companies
            .AnyAsync(x => x.Id == request.CompanyId && x.IsActive, cancellationToken);

        if (!companyExists)
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });

        var code = request.Code.Trim().ToUpperInvariant();

        var duplicate = await db.Branches.AnyAsync(
            x => x.CompanyId == request.CompanyId && x.Code == code,
            cancellationToken);

        if (duplicate)
            return Conflict(new { message = "Bu şube kodu şirket içinde zaten kullanılıyor." });

        if (request.IsHeadOffice)
        {
            var existingHeadOffice = await db.Branches
                .Where(x => x.CompanyId == request.CompanyId && x.IsHeadOffice)
                .ToListAsync(cancellationToken);

            foreach (var branch in existingHeadOffice)
            {
                branch.IsHeadOffice = false;
                branch.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        var entity = new Branch
        {
            CompanyId = request.CompanyId,
            Code = code,
            Name = request.Name.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Address = request.Address?.Trim(),
            IsHeadOffice = request.IsHeadOffice
        };

        db.Branches.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(entity);
    }
}
