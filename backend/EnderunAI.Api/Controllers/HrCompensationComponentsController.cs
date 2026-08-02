using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hr/compensation-components")]
public sealed class HrCompensationComponentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? personnelId,
        [FromQuery] Guid? projectId,
        [FromQuery] bool? isActive,
        [FromQuery] DateTime? effectiveDate,
        CancellationToken cancellationToken)
    {
        var query = db.HrCompensationComponents.AsNoTracking().AsQueryable();

        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (personnelId.HasValue) query = query.Where(x => x.PersonnelId == personnelId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);

        if (effectiveDate.HasValue)
        {
            var date = DateTime.SpecifyKind(effectiveDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(x =>
                x.EffectiveStartDate <= date &&
                (!x.EffectiveEndDate.HasValue || x.EffectiveEndDate.Value >= date));
        }

        var items = await query
            .OrderByDescending(x => x.EffectiveStartDate)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.HrCompensationComponents.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return item is null
            ? NotFound(new { message = "Ek ücret kaydı bulunamadı." })
            : Ok(ToDto(item));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollCreate)]
    public async Task<IActionResult> Create(
        SaveCompensationComponentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Kod ve ad zorunludur." });

        var item = new HrCompensationComponent
        {
            CompanyId = request.CompanyId,
            PersonnelId = request.PersonnelId,
            ProjectId = request.ProjectId
        };

        Apply(item, request);

        db.HrCompensationComponents.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(item));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        SaveCompensationComponentRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.HrCompensationComponents.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Ek ücret kaydı bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Kod ve ad zorunludur." });

        Apply(item, request);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.HrCompensationComponents.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Ek ücret kaydı bulunamadı." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Ek ücret kaydı silindi." });
    }

    [HttpGet("summary")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid personnelId,
        [FromQuery] DateTime effectiveDate,
        CancellationToken cancellationToken)
    {
        var date = DateTime.SpecifyKind(effectiveDate.Date, DateTimeKind.Utc);

        var rows = await db.HrCompensationComponents.AsNoTracking()
            .Where(x =>
                x.PersonnelId == personnelId &&
                x.IsActive &&
                x.EffectiveStartDate <= date &&
                (!x.EffectiveEndDate.HasValue || x.EffectiveEndDate.Value >= date))
            .ToListAsync(cancellationToken);

        // CalculationType: 0=Monthly fixed, 1=Daily, 2=Hourly (matches convention used elsewhere)
        return Ok(new
        {
            personnelId,
            effectiveDate = date,
            componentCount = rows.Count,
            monthlyFixedAmount = rows.Where(x => x.CalculationType == 0).Sum(x => x.Amount),
            dailyAmount = rows.Where(x => x.CalculationType == 1).Sum(x => x.Amount),
            hourlyAmount = rows.Where(x => x.CalculationType == 2).Sum(x => x.Amount),
            payrollIncludedAmount = rows.Where(x => x.IncludeInPayroll).Sum(x => x.Amount),
            projectCostIncludedAmount = rows.Where(x => x.IncludeInProjectCost).Sum(x => x.Amount),
            currencyCode = rows.FirstOrDefault()?.CurrencyCode ?? "TRY"
        });
    }

    private static void Apply(HrCompensationComponent item, SaveCompensationComponentRequest request)
    {
        item.Code = request.Code.Trim();
        item.Name = request.Name.Trim();
        item.ComponentType = request.ComponentType;
        item.CalculationType = request.CalculationType;
        item.PaymentMethod = request.PaymentMethod;
        item.Amount = request.Amount;
        item.CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? "TRY" : request.CurrencyCode.Trim().ToUpperInvariant();
        item.EffectiveStartDate = DateTime.SpecifyKind(request.EffectiveStartDate.Date, DateTimeKind.Utc);
        item.EffectiveEndDate = request.EffectiveEndDate.HasValue
            ? DateTime.SpecifyKind(request.EffectiveEndDate.Value.Date, DateTimeKind.Utc)
            : null;
        item.IsAttendanceBased = request.IsAttendanceBased;
        item.IncludeInPayroll = request.IncludeInPayroll;
        item.IncludeInSgkBase = request.IncludeInSgkBase;
        item.IncludeInIncomeTaxBase = request.IncludeInIncomeTaxBase;
        item.IncludeInStampTaxBase = request.IncludeInStampTaxBase;
        item.IncludeInProjectCost = request.IncludeInProjectCost;
        item.IncludeInProgressPaymentCost = request.IncludeInProgressPaymentCost;
        item.IsActive = request.IsActive;
        item.Description = request.Description?.Trim();
    }

    private static readonly string[] ComponentTypeNames =
    {
        "Prim", "İkramiye", "Yol Yardımı", "Yemek Yardımı", "Konaklama",
        "Vardiya Farkı", "Tazminat", "Kesinti", "Diğer"
    };

    private static readonly string[] CalculationTypeNames =
        { "Aylık Sabit", "Günlük", "Saatlik", "Yüzdesel", "Tek Seferlik" };

    private static readonly string[] PaymentMethodNames =
        { "Bordro ile", "Nakit", "Banka Transferi", "Diğer" };

    private static object ToDto(HrCompensationComponent x) => new
    {
        x.Id,
        x.CompanyId,
        x.PersonnelId,
        x.ProjectId,
        x.Code,
        x.Name,
        x.ComponentType,
        ComponentTypeName = NameOf(ComponentTypeNames, x.ComponentType),
        x.CalculationType,
        CalculationTypeName = NameOf(CalculationTypeNames, x.CalculationType),
        x.PaymentMethod,
        PaymentMethodName = NameOf(PaymentMethodNames, x.PaymentMethod),
        x.Amount,
        x.CurrencyCode,
        x.EffectiveStartDate,
        x.EffectiveEndDate,
        x.IsAttendanceBased,
        x.IncludeInPayroll,
        x.IncludeInSgkBase,
        x.IncludeInIncomeTaxBase,
        x.IncludeInStampTaxBase,
        x.IncludeInProjectCost,
        x.IncludeInProgressPaymentCost,
        x.IsActive,
        x.Description,
        x.CreatedAtUtc
    };

    private static string NameOf(string[] names, int index) =>
        index >= 0 && index < names.Length ? names[index] : "Diğer";
}

public sealed record SaveCompensationComponentRequest(
    Guid CompanyId,
    Guid PersonnelId,
    Guid? ProjectId,
    string Code,
    string Name,
    int ComponentType,
    int CalculationType,
    int PaymentMethod,
    decimal Amount,
    string CurrencyCode,
    DateTime EffectiveStartDate,
    DateTime? EffectiveEndDate,
    bool IsAttendanceBased,
    bool IncludeInPayroll,
    bool IncludeInSgkBase,
    bool IncludeInIncomeTaxBase,
    bool IncludeInStampTaxBase,
    bool IncludeInProjectCost,
    bool IncludeInProgressPaymentCost,
    bool IsActive,
    string? Description);
