using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Fleet;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
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
    IVehicleService vehicles,
    IVehicleExpenseService vehicleExpenses,
    IExtraPaymentVisibilityService extraPaymentVisibility) : ControllerBase
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

    /// <summary>
    /// TARİHLİ TEKİL MASRAF için önerilen gider merkezi: masraf
    /// tarihinde araç neredeydi. ÖNERİdir — kayıt normal gider ucundan
    /// açılır ve kullanıcı merkezi değiştirebilir.
    /// </summary>
    [HttpGet("{id:guid}/expense-center")]
    [RequirePermission(PermissionCatalog.Keys.VehicleView)]
    public Task<IActionResult> SuggestCenter(
        Guid id, [FromQuery] DateTime date, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var suggestion = await vehicleExpenses.SuggestCenterAsync(
                id, date, cancellationToken);

            return suggestion is null
                ? Ok(new
                {
                    suggestion = (object?)null,
                    message =
                        "Araç bir projeye atanmamış ve şirketin merkez şubesi " +
                        "tanımlı değil; gider merkezini elle seçin."
                })
                : Ok(new { suggestion });
        });

    /// <summary>
    /// DÖNEMSEL MASRAF ÖNİZLEMESİ (kira, sigorta, kasko, MTV): gün
    /// oranına göre dağıtım. Hiçbir şey yazmaz.
    /// </summary>
    [HttpGet("{id:guid}/periodic-cost/preview")]
    [RequirePermission(PermissionCatalog.Keys.VehicleView)]
    public Task<IActionResult> PreviewPeriodic(
        Guid id,
        [FromQuery] DateTime periodStart,
        [FromQuery] DateTime periodEnd,
        [FromQuery] decimal amount,
        CancellationToken cancellationToken) =>
        RunAsync(async () => Ok(await vehicleExpenses.PreviewPeriodicAsync(
            id, periodStart, periodEnd, amount, cancellationToken)));

    /// <summary>
    /// Dönemsel masrafı yazar: her pay için AYRI gider kaydı açılır.
    /// Tek kayıt açıp payları başka bir tabloda tutmak, gider merkezi
    /// raporunun okumadığı ikinci bir defter demek olurdu.
    /// </summary>
    [HttpPost("{id:guid}/periodic-cost")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public Task<IActionResult> CreatePeriodic(
        Guid id,
        VehiclePeriodicCostRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(async () => Ok(await vehicleExpenses.CreatePeriodicAsync(
            id, request, cancellationToken)));

    /// <summary>
    /// Araç masraf dökümü — gider kayıtlarının FİLTRELENMİŞ görünümü.
    /// Ayrı bir toplama kaynağı değil: aynı satırlar gider merkezi
    /// raporunda da bir kez sayılıyor.
    /// </summary>
    [HttpGet("{id:guid}/expenses")]
    [RequirePermission(PermissionCatalog.Keys.VehicleView)]
    public async Task<IActionResult> GetExpenses(
        Guid id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var query = db.ExpenseEntries.AsNoTracking().Where(x => x.VehicleId == id);

        if (from.HasValue)
        {
            var start = Services.Fleet.VehicleService.AsUtcDate(from.Value);
            query = query.Where(x => x.ExpenseDate >= start);
        }

        if (to.HasValue)
        {
            var end = Services.Fleet.VehicleService.AsUtcDate(to.Value);
            query = query.Where(x => x.ExpenseDate <= end);
        }

        // ELDEN İZOLASYONU: maskeli kalemler yetkisiz kullanıcıya HİÇ
        // gelmez ve toplam yalnız görünenlerden oluşur.
        //
        // Hem yetki kapısı hem yüklem gider modülünün kendi
        // parçalarından okunuyor (IExtraPaymentVisibilityService +
        // IsVisibleExpense). Burada yeniden yazılsaydı iki maske
        // zamanla ayrışır ve biri delinirdi — gider listesi gizlerken
        // araç kartı gösterirdi.
        var canSeeCash = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        var hiddenCount = canSeeCash
            ? 0
            : await query.CountAsync(
                Services.Expenses.ExpenseEntryService.IsMaskedExpense,
                cancellationToken);

        if (!canSeeCash)
        {
            query = query.Where(
                Services.Expenses.ExpenseEntryService.IsVisibleExpense);
        }

        var items = await query
            .OrderByDescending(x => x.ExpenseDate)
            .Select(x => new
            {
                x.Id,
                x.ExpenseDate,
                x.Amount,
                x.Description,
                CategoryName = x.ExpenseCategory.Name,
                CenterType = (int)x.CenterType,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                x.BranchId,
                BranchName = x.Branch != null ? x.Branch.Name : null,
                PaymentMethod = (int)x.PaymentMethod
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            items,
            total = items.Sum(x => x.Amount),

            // Gizlenen kalem SAYISI söyleniyor, tutarı değil: toplamın
            // neden eksik göründüğü anlaşılsın ama tutar sızmasın.
            hiddenCount
        });
    }

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
