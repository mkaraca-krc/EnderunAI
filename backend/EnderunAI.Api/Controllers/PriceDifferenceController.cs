using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/price-difference-profiles")]
public sealed class PriceDifferenceProfilesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        var query = db.PriceDifferenceProfiles
            .AsNoTracking()
            .Include(x => x.Coefficient)
            .AsQueryable();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await db.PriceDifferenceProfiles
            .AsNoTracking()
            .Include(x => x.Coefficient)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return item is null
            ? NotFound(new { message = "Fiyat farkı profili bulunamadı." })
            : Ok(ToDto(item));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        SavePriceDifferenceProfileRequest request,
        CancellationToken cancellationToken)
    {
        var duplicate = await db.PriceDifferenceProfiles.AnyAsync(
            x => x.ProjectId == request.ProjectId && x.ProfileName == request.ProfileName,
            cancellationToken);
        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu proje için aynı isimde bir fiyat farkı profili zaten var."
            });
        }

        var item = new PriceDifferenceProfile
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            ProfileName = request.ProfileName.Trim(),
            CalculationType = request.CalculationType,
            BaseYear = request.BaseYear,
            BaseMonth = request.BaseMonth,
            CurrencyCode = request.CurrencyCode,
            IsDefault = request.IsDefault,
            IsVatIncluded = request.IsVatIncluded,
            FormulaName = request.FormulaName,
            Notes = request.Notes,
            Coefficient = new PriceDifferenceCoefficient
            {
                A = request.A,
                B1 = request.B1,
                B2 = request.B2,
                B3 = request.B3,
                B4 = request.B4,
                B5 = request.B5,
                C = request.C
            }
        };

        db.PriceDifferenceProfiles.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(item));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        SavePriceDifferenceProfileRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.PriceDifferenceProfiles
            .Include(x => x.Coefficient)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Fiyat farkı profili bulunamadı." });

        var duplicate = await db.PriceDifferenceProfiles.AnyAsync(
            x => x.Id != id &&
                 x.ProjectId == item.ProjectId &&
                 x.ProfileName == request.ProfileName,
            cancellationToken);
        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu proje için aynı isimde bir fiyat farkı profili zaten var."
            });
        }

        item.ProfileName = request.ProfileName.Trim();
        item.CalculationType = request.CalculationType;
        item.BaseYear = request.BaseYear;
        item.BaseMonth = request.BaseMonth;
        item.CurrencyCode = request.CurrencyCode;
        item.IsDefault = request.IsDefault;
        item.IsVatIncluded = request.IsVatIncluded;
        item.FormulaName = request.FormulaName;
        item.Notes = request.Notes;
        item.UpdatedAtUtc = DateTime.UtcNow;

        if (item.Coefficient is null)
        {
            item.Coefficient = new PriceDifferenceCoefficient
            {
                PriceDifferenceProfileId = item.Id
            };
        }

        item.Coefficient.A = request.A;
        item.Coefficient.B1 = request.B1;
        item.Coefficient.B2 = request.B2;
        item.Coefficient.B3 = request.B3;
        item.Coefficient.B4 = request.B4;
        item.Coefficient.B5 = request.B5;
        item.Coefficient.C = request.C;
        item.Coefficient.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(item));
    }

    private static object ToDto(PriceDifferenceProfile item) => new
    {
        item.Id,
        item.CompanyId,
        item.ProjectId,
        item.ProfileName,
        item.CalculationType,
        item.BaseYear,
        item.BaseMonth,
        item.CurrencyCode,
        item.IsDefault,
        item.IsVatIncluded,
        item.FormulaName,
        item.Notes,
        Coefficient = item.Coefficient is null
            ? null
            : new
            {
                item.Coefficient.Id,
                item.Coefficient.A,
                item.Coefficient.B1,
                item.Coefficient.B2,
                item.Coefficient.B3,
                item.Coefficient.B4,
                item.Coefficient.B5,
                item.Coefficient.C,
                Total = item.Coefficient.A +
                        item.Coefficient.B1 +
                        item.Coefficient.B2 +
                        item.Coefficient.B3 +
                        item.Coefficient.B4 +
                        item.Coefficient.B5 +
                        item.Coefficient.C
            }
    };
}

public sealed record SavePriceDifferenceProfileRequest(
    Guid CompanyId,
    Guid ProjectId,
    string ProfileName,
    PriceDifferenceCalculationType CalculationType,
    int BaseYear,
    int BaseMonth,
    string CurrencyCode,
    bool IsDefault,
    bool IsVatIncluded,
    string? FormulaName,
    string? Notes,
    decimal A,
    decimal B1,
    decimal B2,
    decimal B3,
    decimal B4,
    decimal B5,
    decimal C);

[ApiController]
[Authorize]
[Route("api/price-difference-indexes")]
public sealed class PriceDifferenceIndexesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? year,
        [FromQuery] string? sourceName,
        CancellationToken cancellationToken)
    {
        var query = db.PriceDifferenceIndexPeriods.AsNoTracking().AsQueryable();

        if (year.HasValue)
            query = query.Where(x => x.Year == year.Value);

        if (!string.IsNullOrWhiteSpace(sourceName))
            query = query.Where(x => x.SourceName == sourceName);

        var items = await query
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await db.PriceDifferenceIndexPeriods
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return item is null
            ? NotFound(new { message = "Endeks dönemi bulunamadı." })
            : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        SavePriceDifferenceIndexRequest request,
        CancellationToken cancellationToken)
    {
        var duplicate = await db.PriceDifferenceIndexPeriods.AnyAsync(
            x => x.Year == request.Year &&
                 x.Month == request.Month &&
                 x.SourceName == request.SourceName,
            cancellationToken);
        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu dönem ve kaynak için endeks kaydı zaten mevcut."
            });
        }

        var item = new PriceDifferenceIndexPeriod();
        Apply(item, request);

        db.PriceDifferenceIndexPeriods.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        SavePriceDifferenceIndexRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.PriceDifferenceIndexPeriods
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Endeks dönemi bulunamadı." });

        var duplicate = await db.PriceDifferenceIndexPeriods.AnyAsync(
            x => x.Id != id &&
                 x.Year == request.Year &&
                 x.Month == request.Month &&
                 x.SourceName == request.SourceName,
            cancellationToken);
        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu dönem ve kaynak için endeks kaydı zaten mevcut."
            });
        }

        Apply(item, request);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(item);
    }

    private static void Apply(
        PriceDifferenceIndexPeriod item,
        SavePriceDifferenceIndexRequest request)
    {
        item.Year = request.Year;
        item.Month = request.Month;
        item.SourceName = request.SourceName;
        item.PeriodLabel = request.PeriodLabel;
        item.LaborIndex = request.LaborIndex;
        item.FuelIndex = request.FuelIndex;
        item.MaterialIndex = request.MaterialIndex;
        item.MachineryIndex = request.MachineryIndex;
        item.CementIndex = request.CementIndex;
        item.OtherIndex = request.OtherIndex;
        item.CopperIndex = request.CopperIndex;
        item.SteelIndex = request.SteelIndex;
        item.ElectricityIndex = request.ElectricityIndex;
        item.UsdRate = request.UsdRate;
        item.EurRate = request.EurRate;
        item.Notes = request.Notes;
    }
}

public sealed record SavePriceDifferenceIndexRequest(
    int Year,
    int Month,
    string SourceName,
    string? PeriodLabel,
    decimal LaborIndex,
    decimal FuelIndex,
    decimal MaterialIndex,
    decimal MachineryIndex,
    decimal CementIndex,
    decimal OtherIndex,
    decimal CopperIndex,
    decimal SteelIndex,
    decimal ElectricityIndex,
    decimal UsdRate,
    decimal EurRate,
    string? Notes);
