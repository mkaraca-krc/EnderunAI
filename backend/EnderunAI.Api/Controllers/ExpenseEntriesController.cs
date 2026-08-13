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
    Guid? SupplierCurrentAccountId,
    Guid? PartnerAccountId,
    Guid? CreditCardId,
    /// <summary>
    /// Gider bir ARACA aitse aracın kimliği. Araç kartındaki masraf
    /// dökümü bu bağdan okunur; ayrı bir araç masraf tablosu yok.
    /// </summary>
    Guid? VehicleId = null);

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
    PartnerAccountService partners,
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
        {
            var fromUtc = ExpenseEntryService.AsUtcDate(start);
            query = query.Where(x => x.ExpenseDate >= fromUtc);
        }

        if (to is DateTime end)
        {
            var toUtc = ExpenseEntryService.AsUtcDate(end);
            query = query.Where(x => x.ExpenseDate <= toUtc);
        }

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
        // Maske TEK YÜKLEMDEN geliyor (ExpenseEntryService):
        // elden, şahıs carisi, şahıs kartı ve belgesiz kart
        // harcaması. Faturalı şirket kartı harcaması sıradan bir
        // giderdir, gizlenmez.
        var hiddenCount = canSeeCash
            ? 0
            : await query.CountAsync(
                ExpenseEntryService.IsMaskedExpense, cancellationToken);

        if (!canSeeCash)
            query = query.Where(ExpenseEntryService.IsVisibleExpense);

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
                partnerName = x.PartnerAccount != null ? x.PartnerAccount.FullName : null,
                cardName = x.CreditCard != null ? x.CreditCard.Name : null,
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
            ExpenseDate = ExpenseEntryService.AsUtcDate(input.ExpenseDate),
            Amount = decimal.Round(input.Amount, 2),
            Description = input.Description.Trim(),
            PaymentMethod = input.PaymentMethod,
            DocumentType = input.DocumentType,
            DocumentNumber = string.IsNullOrWhiteSpace(input.DocumentNumber)
                ? null
                : input.DocumentNumber.Trim(),
            SupplierCurrentAccountId = input.SupplierCurrentAccountId,
            PartnerAccountId = await ResolvePartnerAsync(input, cancellationToken),
            CreditCardId = input.PaymentMethod == ExpensePaymentMethod.CreditCard
                ? input.CreditCardId
                : null,
            VehicleId = input.VehicleId
        };

        ExpenseEntryService.ApplyCenter(entry, validation.Center!);

        db.ExpenseEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        // Mahsup gider kaydından DOĞAR: elle girilseydi gider
        // merkezinde görünmeyen bir kalem bakiyeyi düşürürdü.
        await partners.SyncSettlementAsync(entry, cancellationToken);
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
        entry.ExpenseDate = ExpenseEntryService.AsUtcDate(input.ExpenseDate);
        entry.Amount = decimal.Round(input.Amount, 2);
        entry.Description = input.Description.Trim();
        entry.PaymentMethod = input.PaymentMethod;
        entry.DocumentType = input.DocumentType;
        entry.DocumentNumber = string.IsNullOrWhiteSpace(input.DocumentNumber)
            ? null
            : input.DocumentNumber.Trim();
        entry.SupplierCurrentAccountId = input.SupplierCurrentAccountId;
        entry.PartnerAccountId = await ResolvePartnerAsync(input, cancellationToken);
        entry.CreditCardId = input.PaymentMethod == ExpensePaymentMethod.CreditCard
            ? input.CreditCardId
            : null;
        entry.VehicleId = input.VehicleId;
        entry.UpdatedAtUtc = DateTime.UtcNow;

        ExpenseEntryService.ApplyCenter(entry, validation.Center!);

        // Ödeme şekli değiştiyse mahsup da takip eder: banka'ya
        // çevrilen bir gider şahsın borcunu düşürmeye devam edemez.
        await partners.SyncSettlementAsync(entry, cancellationToken);

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

        // Sahipsiz mahsup satırı bakiyeyi olduğundan düşük gösterirdi.
        await partners.RemoveSettlementAsync(entry.Id, cancellationToken);

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
        // Banka ve kart ödemeleri sıradan giderdir. Kart harcamasının
        // maskesi kalemin KENDİSİNDE (şahıs kartı / belgesiz) çözülüyor;
        // yazma kapısını kartın tamamına kapamak, faturalı şirket kartı
        // harcamasını da yetkili olmayan kullanıcıya yasaklardı.
        if (method is ExpensePaymentMethod.Bank or ExpensePaymentMethod.CreditCard)
            return false;

        return !await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken);
    }


    /// <summary>
    /// Kaydın hangi şahsın carisine yazılacağı.
    ///
    /// ŞAHIS KARTI: kişi kartın kendisinden geliyor, kullanıcı ayrıca
    /// seçmiyor — seçseydi kartın sahibi ile mahsubun sahibi
    /// ayrışabilir ve bakiye yanlış kişide birikirdi.
    /// </summary>
    private async Task<Guid?> ResolvePartnerAsync(
        ExpenseEntryInput input, CancellationToken cancellationToken)
    {
        if (input.PaymentMethod == ExpensePaymentMethod.PartnerAccount)
            return input.PartnerAccountId;

        if (input.PaymentMethod != ExpensePaymentMethod.CreditCard ||
            input.CreditCardId is not Guid cardId)
            return null;

        return await db.CreditCards
            .AsNoTracking()
            .Where(x => x.Id == cardId &&
                        x.Ownership == Models.FinancialInstruments
                            .CreditCardOwnership.Personal)
            .Select(x => x.PartnerAccountId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static ExpenseEntryInput ToInput(SaveExpenseEntryRequest request) =>
        new(request.CompanyId,
            request.CenterType,
            request.CenterId,
            request.ExpenseCategoryId,
            ExpenseEntryService.AsUtcDate(request.ExpenseDate),
            request.Amount,
            request.Description ?? string.Empty,
            request.PaymentMethod,
            request.DocumentType,
            request.DocumentNumber,
            request.SupplierCurrentAccountId,
            request.PartnerAccountId,
            request.CreditCardId,
            request.VehicleId);
}
