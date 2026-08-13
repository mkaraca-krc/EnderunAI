using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Fleet;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Fleet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Araç (filo) kartları ve atamaları.
///
/// MASRAF UCU YOK: araç masrafı gider kaydının kendi ucundan girilir
/// (<c>/api/expenses</c>), yalnızca aracı işaretlenir. Buraya ayrı bir
/// masraf ucu açılsaydı aynı masraf iki yerden girilebilir ve iki kez
/// sayılırdı.
/// </summary>
[ApiController]
[Route("api/vehicles")]
[Authorize]
public sealed class VehiclesController(
    AppDbContext db,
    IVehicleService vehicles) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.VehicleView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] int? ownership,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = db.Vehicles.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (ownership.HasValue && Enum.IsDefined(typeof(VehicleOwnership), ownership.Value))
            query = query.Where(x => (int)x.Ownership == ownership.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            query = query.Where(x =>
                EF.Functions.ILike(x.PlateNumber, $"%{term}%") ||
                EF.Functions.ILike(x.Brand ?? string.Empty, $"%{term}%") ||
                EF.Functions.ILike(x.Model ?? string.Empty, $"%{term}%"));
        }

        var items = await query
            .OrderBy(x => x.PlateNumber)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.PlateNumber,
                Type = (int)x.Type,
                Ownership = (int)x.Ownership,
                x.Brand,
                x.Model,
                x.ModelYear,
                x.InspectionDueDate,
                x.InsuranceRenewalDate,
                x.CascoRenewalDate,
                x.MotorTaxDueDate,
                x.NextMaintenanceDate,

                // GÜNCEL KONUM: açık atama. Yoksa araç merkez havuzunda
                // ya da hiç atanmamıştır; ikisi ekranda ayrışsın diye
                // atamanın varlığı da dönüyor.
                CurrentAssignment = x.Assignments
                    .Where(a => a.EndDate == null)
                    .Select(a => new
                    {
                        a.Id,
                        a.ProjectId,
                        ProjectCode = a.Project != null ? a.Project.Code : null,
                        ProjectName = a.Project != null ? a.Project.Name : null,
                        a.StartDate,
                        DriverName = a.DriverPersonnel != null
                            ? a.DriverPersonnel.FullName
                            : null
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.VehicleView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.PlateNumber,
                Type = (int)x.Type,
                Ownership = (int)x.Ownership,
                x.Brand,
                x.Model,
                x.ChassisNumber,
                x.ModelYear,
                FuelType = x.FuelType != null ? (int?)x.FuelType : null,
                x.LessorCurrentAccountId,
                LessorTitle = x.LessorCurrentAccount != null
                    ? x.LessorCurrentAccount.Title
                    : null,
                x.RentAmount,
                RentPeriod = x.RentPeriod != null ? (int?)x.RentPeriod : null,
                x.RentDueDay,
                x.PurchaseDate,
                x.PurchaseCost,
                x.InspectionDueDate,
                x.InsuranceRenewalDate,
                x.CascoRenewalDate,
                x.MotorTaxDueDate,
                x.NextMaintenanceDate,
                x.Notes,

                Assignments = x.Assignments
                    .OrderByDescending(a => a.StartDate)
                    .Select(a => new
                    {
                        a.Id,
                        a.ProjectId,
                        ProjectCode = a.Project != null ? a.Project.Code : null,
                        ProjectName = a.Project != null ? a.Project.Name : null,
                        a.ProjectSiteId,
                        a.DriverPersonnelId,
                        DriverName = a.DriverPersonnel != null
                            ? a.DriverPersonnel.FullName
                            : null,
                        a.StartDate,
                        a.EndDate,
                        a.Notes
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return vehicle is null
            ? NotFound(new { message = "Araç bulunamadı." })
            : Ok(vehicle);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.VehicleManage)]
    public Task<IActionResult> Create(
        SaveVehicleRequest request, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var vehicle = await vehicles.CreateAsync(request, cancellationToken);

            return Ok(new { vehicle.Id, vehicle.PlateNumber });
        });

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.VehicleManage)]
    public Task<IActionResult> Update(
        Guid id, SaveVehicleRequest request, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var vehicle = await vehicles.UpdateAsync(id, request, cancellationToken);

            return Ok(new { vehicle.Id, vehicle.PlateNumber });
        });

    /// <summary>
    /// Aracı bir projeye ya da (proje boşsa) MERKEZ HAVUZUNA atar.
    /// Önceki açık atama kapatılır, silinmez.
    /// </summary>
    [HttpPost("{id:guid}/assignments")]
    [RequirePermission(PermissionCatalog.Keys.VehicleManage)]
    public Task<IActionResult> Assign(
        Guid id, AssignVehicleRequest request, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var assignment = await vehicles.AssignAsync(id, request, cancellationToken);

            return Ok(new
            {
                assignment.Id,
                assignment.VehicleId,
                assignment.ProjectId,
                assignment.StartDate
            });
        });

    private static async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException exception)
        {
            return new NotFoundObjectResult(new { message = exception.Message });
        }
        catch (FleetValidationException exception)
        {
            return new BadRequestObjectResult(new { message = exception.Message });
        }
    }
}
