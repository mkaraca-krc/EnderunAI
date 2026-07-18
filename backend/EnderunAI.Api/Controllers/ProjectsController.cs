using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public sealed class ProjectsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken)
    {
        var query = db.Projects.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.BranchId,
                BranchName = x.Branch.Name,
                x.EmployerCurrentAccountId,
                EmployerName = x.EmployerCurrentAccount.Title,
                x.Code,
                x.Name,
                x.ContractNumber,
                x.ContractAmount,
                x.CurrencyCode,
                x.Status,
                x.HealthStatus,
                WarehouseCount = x.Warehouses.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.BranchId,
                BranchName = x.Branch.Name,
                x.EmployerCurrentAccountId,
                EmployerName = x.EmployerCurrentAccount.Title,
                x.Code,
                x.Name,
                x.ContractNumber,
                x.ContractDate,
                x.ContractAmount,
                x.CurrencyCode,
                x.VatRate,
                x.WithholdingRate,
                x.PlannedStartDate,
                x.PlannedEndDate,
                x.City,
                x.District,
                x.Address,
                x.Status,
                x.HealthStatus,
                Warehouses = x.Warehouses.Select(w => new
                {
                    w.Id,
                    w.Code,
                    w.Name,
                    w.Type,
                    w.IsActive
                })
            })
            .SingleOrDefaultAsync(cancellationToken);

        return project is null
            ? NotFound(new { message = "Proje bulunamadı." })
            : Ok(project);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var company = await db.Companies
            .SingleOrDefaultAsync(
                x => x.Id == request.CompanyId && x.IsActive,
                cancellationToken);

        if (company is null)
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });

        var branch = await db.Branches
            .SingleOrDefaultAsync(
                x => x.Id == request.BranchId &&
                     x.CompanyId == request.CompanyId &&
                     x.IsActive,
                cancellationToken);

        if (branch is null)
            return BadRequest(new { message = "Seçilen şube bu şirkete ait değil veya pasif." });

        var employer = await db.CurrentAccounts
            .SingleOrDefaultAsync(
                x => x.Id == request.EmployerCurrentAccountId &&
                     x.CompanyId == request.CompanyId,
                cancellationToken);

        if (employer is null)
            return BadRequest(new { message = "İşveren cari kartı bulunamadı." });

        if (employer.Status != CurrentAccountStatus.Approved)
            return BadRequest(new { message = "Proje yalnızca onaylanmış cari kart ile açılabilir." });

        if (!employer.Roles.HasFlag(CurrentAccountRoles.Customer))
            return BadRequest(new { message = "Seçilen cari kartın müşteri rolü bulunmuyor." });

        var code = request.Code.Trim().ToUpperInvariant();

        if (await db.Projects.AnyAsync(
                x => x.CompanyId == request.CompanyId && x.Code == code,
                cancellationToken))
        {
            return Conflict(new { message = "Bu proje kodu zaten kullanılıyor." });
        }

        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken);

        try
        {
            var project = new Project
            {
                CompanyId = request.CompanyId,
                BranchId = request.BranchId,
                EmployerCurrentAccountId = request.EmployerCurrentAccountId,
                Code = code,
                Name = request.Name.Trim(),
                ContractNumber = request.ContractNumber?.Trim(),
                ContractDate = request.ContractDate,
                ContractAmount = request.ContractAmount,
                CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                    ? "TRY"
                    : request.CurrencyCode.Trim().ToUpperInvariant(),
                VatRate = request.VatRate,
                WithholdingRate = request.WithholdingRate?.Trim(),
                PlannedStartDate = request.PlannedStartDate,
                PlannedEndDate = request.PlannedEndDate,
                City = request.City?.Trim(),
                District = request.District?.Trim(),
                Address = request.Address?.Trim(),
                Status = ProjectStatus.Active,
                HealthStatus = ProjectHealthStatus.Green
            };

            db.Projects.Add(project);
            await db.SaveChangesAsync(cancellationToken);

            var warehouse = new Warehouse
            {
                CompanyId = project.CompanyId,
                BranchId = project.BranchId,
                ProjectId = project.Id,
                Code = $"{project.Code}-DEPO",
                Name = $"{project.Name} Şantiye Deposu",
                Type = WarehouseType.Site,
                Address = project.Address
            };

            db.Warehouses.Add(warehouse);
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return Ok(new
            {
                message = "Proje ve şantiye deposu oluşturuldu.",
                project = new
                {
                    project.Id,
                    project.Code,
                    project.Name,
                    project.Status
                },
                warehouse = new
                {
                    warehouse.Id,
                    warehouse.Code,
                    warehouse.Name,
                    warehouse.Type
                }
            });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
