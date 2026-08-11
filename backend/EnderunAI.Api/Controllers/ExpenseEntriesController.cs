using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record SaveExpenseEntryRequest(
    Guid CompanyId,
    ExpenseCenterType CenterType,
    Guid CenterId,
    Guid ExpenseCategoryId,
    DateTime ExpenseDate,
    decimal Amount,
    string Description,
    ExpensePaymentMethod PaymentMethod,
    ExpenseDocumentType DocumentType,
    string? DocumentNumber,
    Guid? SupplierCurrentAccountId);

/// <summary>
/// Elle girilen gider kayıtları.
///
/// ELDEN İZOLASYONU: elden ödenen kalemler <c>extra_payment.view</c>
/// olmayan kullanıcıya HİÇ GELMEZ ve toplam yalnızca görünen
/// kalemlerden hesaplanır. Tam toplamı verip satırı gizlemek,
/// gizlenen tutarı çıkarımla ele verirdi — o yüzden "toplam eksi
/// gizli" yaklaşımı bilinçle kullanılmıyor. Gizlenen kalem varsa
/// yanıt bunu ayrıca söyler.
/// </summary>
[ApiController]
[Authorize]
[Route("api/expenses/kayitlar")]
public sealed class ExpenseEntriesController(
    AppDbContext db,
    ExpenseEntryService service,
    IExtraPaymentVisibilityService extraPaymentVisibility) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.ExpenseView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] ExpenseCenterType? centerType,
        [FromQuery] Guid? centerId,
        [FromQuery] Guid? categoryId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var canSeeCash = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        var query = db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (from is DateTime start)
            query = query.Where(x => x.ExpenseDate >= start.Date);

        if (to is DateTime end)
            query = query.Where(x => x.ExpenseDate <= end.Date);

        if (categoryId is Guid category)
            query = query.Where(x => x.ExpenseCategoryId == category);

        if (centerType is ExpenseCenterType type && centerId is Guid center)
            query = type switch
            {
                ExpenseCenterType.Branch => query.Where(x => x.BranchId == center),
                ExpenseCenterType.ProjectSite =>
                    query.Where(x => x.ProjectSiteId == center),
                // Proje merkezi seçilince şantiyeleri de kapsanır:
                // "bu projeye ne harcadık" sorusunun cevabı şantiye
                // giderlerini dışarıda bırakamaz.
                _ => query.Where(x => x.ProjectId == center)
            };

        // Gizlenen kalem SAYISI toplam için değil, kullanıcıya "eksik
        // bakıyorsun" diyebilmek için okunuyor; tutarı taşımıyor.
        var hiddenCount = canSeeCash
            ? 0
            : await query.CountAsync(
                x => x.PaymentMethod == ExpensePaymentMethod.Cash,
                cancellationToken);

        if (!canSeeCash)
            query = query.Where(x => x.PaymentMethod != ExpensePaymentMethod.Cash);

        var rows = await query
            .OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                id = x.Id,
                expenseDate = x.ExpenseDate,
                amount = x.Amount,
                description = x.Description,
                categoryId = x.ExpenseCategoryId,
                categoryName = x.ExpenseCategory.Name,
                centerType = x.CenterType.ToString(),
                centerName =
                    x.ProjectSiteId != null ? x.Project!.Name + " — " + x.ProjectSite!.Name
                    : x.ProjectId != null ? x.Project!.Name
                    : x.Branch!.Name,
                paymentMethod = x.PaymentMethod.ToString(),
                documentType = x.DocumentType.ToString(),
                documentNumber = x.DocumentNumber,
                supplierName = x.SupplierCurrentAccount != null
                    ? x.SupplierCurrentAccount.Title
                    : null,
                isRecurring = x.RecurringTemplateId != null
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            items = rows,
            // TOPLAM = yalnız görünen kalemler.
            total = rows.Sum(x => x.amount),
            hiddenCount,
            hiddenNote = hiddenCount > 0
                ? "Elden ödenen kalemler gösterilmiyor; toplam yalnızca " +
                  "görünen kalemleri kapsıyor."
                : null
        });
    }

    /// <summary>
    /// Kaydı yazmadan önce "bu gider zaten girilmiş olabilir"
    /// uyarısını sorar. Ayrı uç: ekran kullanıcıya kaydetmeden önce
    /// gösteriyor, kayıt akışını kesmiyor.
    /// </summary>
    [HttpPost("benzer-kayitlar")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> PossibleDuplicates(
        [FromBody] SaveExpenseEntryRequest request,
        CancellationToken cancellationToken)
    {
        var input = ToInput(request);

        var hints = await service.FindPossibleDuplicatesAsync(
            input, null, cancellationToken);

        return Ok(hints.Select(x => new
        {
            id = x.Id,
            expenseDate = x.ExpenseDate,
            amount = x.Amount,
            description = x.Description
        }));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> Create(
        [FromBody] SaveExpenseEntryRequest request,
        CancellationToken cancellationToken)
    {
        var input = ToInput(request);

        var validation = await service.ValidateAsync(input, cancellationToken);

        if (validation.Error is string error)
            return BadRequest(new { message = error });

        if (await CashWriteForbiddenAsync(input.PaymentMethod, cancellationToken))
            return Forbid();

        var entry = new ExpenseEntry
        {
            CompanyId = input.CompanyId,
            ExpenseCategoryId = input.ExpenseCategoryId,
            ExpenseDate = input.ExpenseDate.Date,
            Amount = decimal.Round(input.Amount, 2),
            Description = input.Description.Trim(),
            PaymentMethod = input.PaymentMethod,
            DocumentType = input.DocumentType,
            DocumentNumber = string.IsNullOrWhiteSpace(input.DocumentNumber)
                ? null
                : input.DocumentNumber.Trim(),
            SupplierCurrentAccountId = input.SupplierCurrentAccountId
        };

        ExpenseEntryService.ApplyCenter(entry, validation.Center!);

        db.ExpenseEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = entry.Id });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] SaveExpenseEntryRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await db.ExpenseEntries
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entry is null)
            return NotFound(new { message = "Gider kaydı bulunamadı." });

        // Görünmeyen bir kaydı düzeltmek de görmektir.
        if (await CashWriteForbiddenAsync(entry.PaymentMethod, cancellationToken))
            return Forbid();

        var input = ToInput(request);

        var validation = await service.ValidateAsync(input, cancellationToken);

        if (validation.Error is string error)
            return BadRequest(new { message = error });

        if (await CashWriteForbiddenAsync(input.PaymentMethod, cancellationToken))
            return Forbid();

        entry.ExpenseCategoryId = input.ExpenseCategoryId;
        entry.ExpenseDate = input.ExpenseDate.Date;
        entry.Amount = decimal.Round(input.Amount, 2);
        entry.Description = input.Description.Trim();
        entry.PaymentMethod = input.PaymentMethod;
        entry.DocumentType = input.DocumentType;
        entry.DocumentNumber = string.IsNullOrWhiteSpace(input.DocumentNumber)
            ? null
            : input.DocumentNumber.Trim();
        entry.SupplierCurrentAccountId = input.SupplierCurrentAccountId;
        entry.UpdatedAtUtc = DateTime.UtcNow;

        ExpenseEntryService.ApplyCenter(entry, validation.Center!);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = entry.Id });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken)
    {
        var entry = await db.ExpenseEntries
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entry is null)
            return NotFound(new { message = "Gider kaydı bulunamadı." });

        if (await CashWriteForbiddenAsync(entry.PaymentMethod, cancellationToken))
            return Forbid();

        db.ExpenseEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id });
    }

    /// <summary>
    /// Elden kalemi yazmak/silmek de <c>extra_payment.view</c>
    /// istiyor. Yalnız okuma maskelenseydi, yetkisiz kullanıcı
    /// göremediği bir kaydı silebilir ya da bir gideri elden
    /// işaretleyip kendi görüşünden kaçırabilirdi.
    /// </summary>
    private async Task<bool> CashWriteForbiddenAsync(
        ExpensePaymentMethod method, CancellationToken cancellationToken)
    {
        if (method != ExpensePaymentMethod.Cash)
            return false;

        return !await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken);
    }

    private static ExpenseEntryInput ToInput(SaveExpenseEntryRequest request) =>
        new(request.CompanyId,
            request.CenterType,
            request.CenterId,
            request.ExpenseCategoryId,
            request.ExpenseDate,
            request.Amount,
            request.Description ?? string.Empty,
            request.PaymentMethod,
            request.DocumentType,
            request.DocumentNumber,
            request.SupplierCurrentAccountId);
}
