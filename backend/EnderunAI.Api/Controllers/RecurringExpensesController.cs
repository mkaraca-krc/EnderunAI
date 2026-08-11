using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record SaveRecurringExpenseRequest(
    Guid CompanyId,
    ExpenseCenterType CenterType,
    Guid CenterId,
    Guid ExpenseCategoryId,
    string Description,
    decimal EstimatedAmount,
    ExpensePaymentMethod PaymentMethod,
    Guid? SupplierCurrentAccountId,
    int StartYear,
    int StartMonth,
    int? EndYear,
    int? EndMonth,
    int PaymentDay);

public sealed record ConfirmRecurringPeriodRequest(
    int Year,
    int Month,
    decimal ActualAmount,
    ExpenseDocumentType DocumentType,
    string? DocumentNumber);

/// <summary>
/// Aylık tekrar eden gider şablonları ve dönem onayı.
///
/// Şablon TAHMİNİ taşır; ay gelince gerçekleşen girilip onaylanır ve
/// o dönemin tahmini kalemi düşer (R5). Bu kural
/// <see cref="RecurringExpenseService"/> içinde tek yerde duruyor.
/// </summary>
[ApiController]
[Authorize]
[Route("api/expenses/tekrarlayan")]
public sealed class RecurringExpensesController(
    AppDbContext db,
    ExpenseEntryService entries,
    RecurringExpenseService recurring,
    ExpenseCenterResolver centers,
    IExtraPaymentVisibilityService extraPaymentVisibility) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.ExpenseView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid companyId,
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var canSeeCash = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        var query = db.RecurringExpenseTemplates
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        // Elden ödenen şablon da elden kalemdir: yetkisizde hiç
        // görünmez, aksi halde tutarı şablondan okunurdu.
        var hiddenCount = canSeeCash
            ? 0
            : await query.CountAsync(
                x => x.PaymentMethod == ExpensePaymentMethod.Cash, cancellationToken);

        if (!canSeeCash)
            query = query.Where(x => x.PaymentMethod != ExpensePaymentMethod.Cash);

        var templates = await query
            .OrderBy(x => x.Description)
            .Select(x => new
            {
                id = x.Id,
                description = x.Description,
                estimatedAmount = x.EstimatedAmount,
                categoryId = x.ExpenseCategoryId,
                categoryName = x.ExpenseCategory.Name,
                centerType = x.CenterType.ToString(),
                centerName =
                    x.ProjectSiteId != null ? x.Project!.Name + " — " + x.ProjectSite!.Name
                    : x.ProjectId != null ? x.Project!.Name
                    : x.Branch!.Name,
                paymentMethod = x.PaymentMethod.ToString(),
                startYear = x.StartYear,
                startMonth = x.StartMonth,
                endYear = x.EndYear,
                endMonth = x.EndMonth,
                paymentDay = x.PaymentDay,
                isStopped = x.IsStopped
            })
            .ToListAsync(cancellationToken);

        // Bir dönem soruldu mu, o ayın durumu da dönüyor: ekran
        // "hangi ay hâlâ gerçekleşen bekliyor" diye gösterebilsin.
        object? periods = null;

        if (year is int y && month is int m && m is >= 1 and <= 12)
        {
            var probe = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);

            var visibleIds = templates.Select(x => x.id).ToHashSet();

            var states = await recurring.GetPeriodStatesAsync(
                companyId, probe, probe, cancellationToken);

            periods = states
                .Where(x => visibleIds.Contains(x.TemplateId))
                .Select(x => new
                {
                    templateId = x.TemplateId,
                    year = x.Year,
                    month = x.Month,
                    dueDate = x.DueDate,
                    estimatedAmount = x.EstimatedAmount,
                    actualEntryId = x.ActualEntryId,
                    actualAmount = x.ActualAmount,
                    isConfirmed = x.ActualEntryId != null
                })
                .ToList();
        }

        return Ok(new
        {
            templates,
            periods,
            hiddenCount,
            hiddenNote = hiddenCount > 0
                ? "Elden ödenen tekrarlayan giderler gösterilmiyor."
                : null
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> Create(
        [FromBody] SaveRecurringExpenseRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request, cancellationToken);

        if (validation.Error is string error)
            return BadRequest(new { message = error });

        if (request.PaymentMethod == ExpensePaymentMethod.Cash &&
            !await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken))
            return Forbid();

        var template = new RecurringExpenseTemplate
        {
            CompanyId = request.CompanyId,
            ExpenseCategoryId = request.ExpenseCategoryId,
            Description = request.Description.Trim(),
            EstimatedAmount = decimal.Round(request.EstimatedAmount, 2),
            PaymentMethod = request.PaymentMethod,
            SupplierCurrentAccountId = request.SupplierCurrentAccountId,
            StartYear = request.StartYear,
            StartMonth = request.StartMonth,
            EndYear = request.EndYear,
            EndMonth = request.EndMonth,
            PaymentDay = Math.Clamp(request.PaymentDay, 1, 31)
        };

        ApplyCenter(template, validation.Center!);

        db.RecurringExpenseTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = template.Id });
    }

    /// <summary>
    /// Şablonu durdurur. SİLMEZ: bu şablondan doğmuş gerçekleşen
    /// kayıtlar kaynaklarını kaybetmemeli.
    /// </summary>
    [HttpPost("{id:guid}/durdur")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> Stop(
        Guid id, CancellationToken cancellationToken)
    {
        var template = await db.RecurringExpenseTemplates
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (template is null)
            return NotFound(new { message = "Şablon bulunamadı." });

        if (template.PaymentMethod == ExpensePaymentMethod.Cash &&
            !await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken))
            return Forbid();

        template.IsStopped = true;
        template.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id });
    }

    /// <summary>
    /// Dönemin gerçekleşenini onaylar: tahmini düşer, yerine gerçek
    /// gider kaydı geçer.
    /// </summary>
    [HttpPost("{id:guid}/gerceklesen")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> Confirm(
        Guid id,
        [FromBody] ConfirmRecurringPeriodRequest request,
        CancellationToken cancellationToken)
    {
        var method = await db.RecurringExpenseTemplates
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => (ExpensePaymentMethod?)x.PaymentMethod)
            .SingleOrDefaultAsync(cancellationToken);

        if (method is null)
            return NotFound(new { message = "Şablon bulunamadı." });

        if (method == ExpensePaymentMethod.Cash &&
            !await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken))
            return Forbid();

        var (error, entryId) = await recurring.ConfirmPeriodAsync(
            id, request.Year, request.Month, request.ActualAmount,
            request.DocumentType, request.DocumentNumber, cancellationToken);

        if (error is not null)
            return BadRequest(new { message = error });

        return Ok(new { entryId });
    }

    private async Task<ExpenseValidationResult> ValidateAsync(
        SaveRecurringExpenseRequest request, CancellationToken cancellationToken)
    {
        if (request.StartMonth is < 1 or > 12)
            return new ExpenseValidationResult("Ay 1-12 aralığında olmalıdır.", null);

        if (request.EndMonth is int endMonth && endMonth is < 1 or > 12)
            return new ExpenseValidationResult("Bitiş ayı 1-12 aralığında olmalıdır.", null);

        if ((request.EndYear is null) != (request.EndMonth is null))
            return new ExpenseValidationResult(
                "Bitiş dönemi için yıl ve ay birlikte verilmelidir.", null);

        // Doğrulama gider kaydıyla AYNI servisten: merkez, kategori ve
        // otomatik kategori yasağı tek kuralda.
        var input = new ExpenseEntryInput(
            request.CompanyId, request.CenterType, request.CenterId,
            request.ExpenseCategoryId,
            new DateTime(request.StartYear, Math.Clamp(request.StartMonth, 1, 12), 1,
                0, 0, 0, DateTimeKind.Utc),
            request.EstimatedAmount, request.Description ?? string.Empty,
            request.PaymentMethod, ExpenseDocumentType.None, null,
            request.SupplierCurrentAccountId);

        return await entries.ValidateAsync(input, cancellationToken);
    }

    private static void ApplyCenter(
        RecurringExpenseTemplate template, ExpenseCenterRef center)
    {
        template.CenterType = center.Type;
        template.BranchId = null;
        template.ProjectId = null;
        template.ProjectSiteId = null;

        switch (center.Type)
        {
            case ExpenseCenterType.Branch:
                template.BranchId = center.Id;
                break;

            case ExpenseCenterType.Project:
                template.ProjectId = center.Id;
                break;

            case ExpenseCenterType.ProjectSite:
                template.ProjectSiteId = center.Id;
                template.ProjectId = center.ParentProjectId;
                break;
        }
    }
}
