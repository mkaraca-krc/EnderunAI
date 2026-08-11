using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Expenses;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Expenses;

/// <summary>Gider kaydının doğrulanmış hali; kaydetmeye hazır.</summary>
public sealed record ExpenseEntryInput(
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
    Guid? PartnerAccountId);

/// <summary>Doğrulama sonucu — hata varsa Türkçe mesajla.</summary>
public sealed record ExpenseValidationResult(string? Error, ExpenseCenterRef? Center);

/// <summary>Aynı gider zaten girilmiş olabilir uyarısı.</summary>
public sealed record ExpenseDuplicateHint(
    Guid Id,
    DateTime ExpenseDate,
    decimal Amount,
    string Description);

/// <summary>
/// Elle gider kaydının kuralları.
///
/// İKİ KAPI: merkez var olmalı (ExpenseCenterResolver) ve kategori
/// elle girilebilir olmalı. İkincisi çift sayım korumasının
/// parçası — otomatik kategoriler (malzeme, işçilik, taşeron, yol)
/// kendi kaynaklarından akıyor, elle de girilebilseydi aynı gider
/// iki kez sayılırdı.
/// </summary>
public sealed class ExpenseEntryService(AppDbContext db, ExpenseCenterResolver centers)
{
    /// <summary>
    /// Tarihi UTC gününe sabitler.
    ///
    /// ZORUNLU: query string'den ya da Z taşımayan bir gövdeden gelen
    /// tarih Kind=Unspecified oluyor ve Npgsql bunu timestamptz
    /// kolonuna yazmayı/karşılaştırmayı reddediyor. Dönüşüm tek
    /// yerde; her çağıranın hatırlamasına bırakılırsa bir uçta
    /// çalışıp diğerinde 500 verir.
    /// </summary>
    public static DateTime AsUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    /// <summary>
    /// R4 UYARI EŞİĞİ: aynı merkez + kategori + ay içinde tutarı
    /// %5'ten yakın bir kayıt varsa kullanıcı uyarılır. SERT ENGEL
    /// DEĞİL — iki ayrı kira ödemesi ya da iki ayrı yakıt fişi meşru;
    /// engel olsaydı doğru kayıtlar da girilemezdi.
    /// </summary>
    public const decimal DuplicateAmountTolerance = 0.05m;

    public async Task<ExpenseValidationResult> ValidateAsync(
        ExpenseEntryInput input, CancellationToken cancellationToken)
    {
        if (input.CompanyId == Guid.Empty)
            return new ExpenseValidationResult("Şirket seçimi zorunludur.", null);

        if (input.Amount <= 0m)
            return new ExpenseValidationResult("Tutar sıfırdan büyük olmalıdır.", null);

        if (string.IsNullOrWhiteSpace(input.Description))
            return new ExpenseValidationResult("Açıklama zorunludur.", null);

        var center = await centers.ResolveAsync(
            input.CompanyId, input.CenterType, input.CenterId, cancellationToken);

        if (center is null)
            return new ExpenseValidationResult(
                "Gider merkezi bulunamadı ya da bu şirkete ait değil.", null);

        var category = await db.ExpenseCategories
            .AsNoTracking()
            .Where(x => x.Id == input.ExpenseCategoryId &&
                        x.CompanyId == input.CompanyId)
            .Select(x => new { x.Name, x.IsAutomaticOnly, x.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

        if (category is null)
            return new ExpenseValidationResult("Kategori bulunamadı.", null);

        if (!category.IsActive)
            return new ExpenseValidationResult(
                "Pasif kategoriye gider kaydedilemez.", null);

        // ÇİFT SAYIM KORUMASI: otomatik kategori elle girilemez.
        if (category.IsAutomaticOnly)
            return new ExpenseValidationResult(
                $"\"{category.Name}\" kategorisi satın alma, görevlendirme ve " +
                "puantaj kayıtlarından otomatik geliyor; elle girilirse aynı " +
                "gider iki kez sayılır. Bu kalem kaynağından düzeltilir.", null);

        // Faturasız gider bir şahsın carisinden mahsup ediliyorsa
        // sahibi ZORUNLU: sahibi belli olmayan bir mahsup hiçbir
        // bakiyeyi düşürmez, defteri sessizce şişirir.
        if (input.PaymentMethod == ExpensePaymentMethod.PartnerAccount)
        {
            if (input.PartnerAccountId is not Guid partnerId)
                return new ExpenseValidationResult(
                    "Şahıs carisinden mahsup için kişi seçilmelidir.", null);

            var partnerExists = await db.PartnerAccounts
                .AnyAsync(x => x.Id == partnerId && x.CompanyId == input.CompanyId,
                    cancellationToken);

            if (!partnerExists)
                return new ExpenseValidationResult("Şahıs carisi bulunamadı.", null);
        }

        if (input.SupplierCurrentAccountId is Guid supplierId)
        {
            var supplierExists = await db.CurrentAccounts
                .AnyAsync(x => x.Id == supplierId && x.CompanyId == input.CompanyId,
                    cancellationToken);

            if (!supplierExists)
                return new ExpenseValidationResult("Tedarikçi bulunamadı.", null);
        }

        return new ExpenseValidationResult(null, center);
    }

    /// <summary>
    /// Doğrulanmış girdiden kayıt gövdesi üretir. Merkez alanları
    /// TEK YERDE dolduruluyor: şantiye merkezinde projenin de
    /// yazılması rapor tarafında "proje altında topla" için şart ve
    /// çağıranın hatırlamasına bırakılmamalı.
    /// </summary>
    public static void ApplyCenter(ExpenseEntry entry, ExpenseCenterRef center)
    {
        entry.CenterType = center.Type;
        entry.BranchId = null;
        entry.ProjectId = null;
        entry.ProjectSiteId = null;

        switch (center.Type)
        {
            case ExpenseCenterType.Branch:
                entry.BranchId = center.Id;
                break;

            case ExpenseCenterType.Project:
                entry.ProjectId = center.Id;
                break;

            case ExpenseCenterType.ProjectSite:
                entry.ProjectSiteId = center.Id;
                entry.ProjectId = center.ParentProjectId;
                break;
        }
    }

    /// <summary>
    /// R4: aynı merkez + kategori + ay içinde yakın tutarlı kayıtlar.
    /// Kaydı ENGELLEMEZ, yalnızca kullanıcıya gösterilir.
    /// </summary>
    public async Task<List<ExpenseDuplicateHint>> FindPossibleDuplicatesAsync(
        ExpenseEntryInput input, Guid? excludeId, CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(
            input.ExpenseDate.Year, input.ExpenseDate.Month, 1,
            0, 0, 0, DateTimeKind.Utc);

        var monthEnd = monthStart.AddMonths(1);

        var lower = input.Amount * (1m - DuplicateAmountTolerance);
        var upper = input.Amount * (1m + DuplicateAmountTolerance);

        var query = db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.CompanyId == input.CompanyId &&
                        x.ExpenseCategoryId == input.ExpenseCategoryId &&
                        x.CenterType == input.CenterType &&
                        x.ExpenseDate >= monthStart && x.ExpenseDate < monthEnd &&
                        x.Amount >= lower && x.Amount <= upper);

        query = input.CenterType switch
        {
            ExpenseCenterType.Branch => query.Where(x => x.BranchId == input.CenterId),
            ExpenseCenterType.ProjectSite =>
                query.Where(x => x.ProjectSiteId == input.CenterId),
            _ => query.Where(x => x.ProjectId == input.CenterId &&
                                  x.ProjectSiteId == null)
        };

        if (excludeId is Guid id)
            query = query.Where(x => x.Id != id);

        return await query
            .OrderBy(x => x.ExpenseDate)
            .Select(x => new ExpenseDuplicateHint(
                x.Id, x.ExpenseDate, x.Amount, x.Description))
            .Take(5)
            .ToListAsync(cancellationToken);
    }
}
