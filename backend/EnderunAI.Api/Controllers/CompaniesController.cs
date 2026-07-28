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
[Route("api/companies")]
public sealed class CompaniesController(
    AppDbContext db,
    ICurrentDataScopeService dataScope) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var scope = await dataScope.GetAsync(cancellationToken);
        if (scope is null)
            return Unauthorized();

        var items = await scope.Apply(db.Companies)
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.TradeName,
                x.TaxOffice,
                x.TaxNumber,
                x.Phone,
                x.Email,
                x.Website,
                x.Address,
                x.IsActive,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var scope = await dataScope.GetAsync(cancellationToken);
        if (scope is null)
            return Unauthorized();

        var item = await scope.Apply(db.Companies)
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.TradeName,
                x.TaxOffice,
                x.TaxNumber,
                x.Phone,
                x.Email,
                x.Website,
                x.Address,
                x.IsActive,
                BranchCount = x.Branches.Count,
                ProjectCount = x.Projects.Count
            })
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? NotFound(new { message = "Şirket bulunamadı." })
            : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await dataScope.GetAsync(cancellationToken);
        if (scope is null)
            return Unauthorized();
        if (!scope.HasGlobalAccess)
            return Forbid();

        var code = request.Code.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Şirket kodu ve adı zorunludur." });

        if (await db.Companies.AnyAsync(x => x.Code == code, cancellationToken))
            return Conflict(new { message = "Bu şirket kodu zaten kullanılıyor." });

        var company = new Company
        {
            Code = code,
            Name = request.Name.Trim(),
            TradeName = request.TradeName?.Trim(),
            TaxOffice = request.TaxOffice?.Trim(),
            TaxNumber = request.TaxNumber?.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Website = request.Website?.Trim(),
            Address = request.Address?.Trim()
        };

        db.Companies.Add(company);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = company.Id }, company);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await dataScope.GetAsync(cancellationToken);
        if (scope is null)
            return Unauthorized();

        var company = await scope.Apply(db.Companies)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (company is null)
            return NotFound(new { message = "Şirket bulunamadı." });

        company.Name = request.Name.Trim();
        company.TradeName = request.TradeName?.Trim();
        company.TaxOffice = request.TaxOffice?.Trim();
        company.TaxNumber = request.TaxNumber?.Trim();
        company.Phone = request.Phone?.Trim();
        company.Email = request.Email?.Trim();
        company.Website = request.Website?.Trim();
        company.Address = request.Address?.Trim();
        company.IsActive = request.IsActive;
        company.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(company);
    }
}
