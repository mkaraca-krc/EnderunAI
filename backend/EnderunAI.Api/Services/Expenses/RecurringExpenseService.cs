using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Expenses;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Expenses;

/// <summary>
/// Bir şablonun belirli bir dönemdeki hali.
/// <see cref="ActualEntryId"/> doluysa o dönem GERÇEKLEŞMİŞTİR ve
/// tahmini artık sayılmaz.
/// </summary>
public sealed record RecurringPeriodState(
    Guid TemplateId,
    int Year,
    int Month,
    DateTime DueDate,
    decimal EstimatedAmount,
    Guid? ActualEntryId,
    decimal? ActualAmount);

/// <summary>
/// Tekrarlayan gider şablonlarının dönem hesabı.
///
/// R5 ÇİFT SAYIM KORUMASININ MERKEZİ: bir dönem için tahmini ve
/// gerçekleşen asla birlikte sayılmaz. Karar tek yerde veriliyor;
/// rapor da nakit akış da bu servisten okuyor, kendi kuralını
/// yazmıyor. İki yere kopyalansaydı biri "gerçekleşen varsa
/// tahminiyi düş" derken diğeri toplamayı sürdürür ve fark ancak
/// ay sonunda fark edilirdi.
/// </summary>
public sealed class RecurringExpenseService(AppDbContext db)
{
    /// <summary>
    /// Şablonların verilen ay aralığındaki durumu. Aralık, ayın ilk
    /// gününe göre kapsayıcıdır.
    /// </summary>
    public async Task<List<RecurringPeriodState>> GetPeriodStatesAsync(
        Guid companyId, DateTime from, DateTime to,
        CancellationToken cancellationToken)
    {
        var templates = await db.RecurringExpenseTemplates
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsStopped)
            .ToListAsync(cancellationToken);

        if (templates.Count == 0)
            return [];

        var templateIds = templates.Select(x => x.Id).ToList();

        // Bu şablonlardan doğmuş gerçekleşen kayıtlar. Dönemi kaydın
        // kendisi taşıyor; ayrı bir "kapandı" bayrağı tutulsaydı
        // bayrak ile kayıt arasında tutarsızlık doğabilirdi.
        var actuals = await db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.RecurringTemplateId != null &&
                        templateIds.Contains(x.RecurringTemplateId!.Value) &&
                        x.PeriodYear != null && x.PeriodMonth != null)
            .Select(x => new
            {
                TemplateId = x.RecurringTemplateId!.Value,
                Year = x.PeriodYear!.Value,
                Month = x.PeriodMonth!.Value,
                x.Id,
                x.Amount
            })
            .ToListAsync(cancellationToken);

        var actualByPeriod = actuals.ToDictionary(
            x => (x.TemplateId, x.Year, x.Month),
            x => (x.Id, x.Amount));

        var first = new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var last = new DateTime(to.Year, to.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = new List<RecurringPeriodState>();

        foreach (var template in templates)
        {
            var start = new DateTime(
                template.StartYear, template.StartMonth, 1, 0, 0, 0, DateTimeKind.Utc);

            var end = template.EndYear is int endYear && template.EndMonth is int endMonth
                ? new DateTime(endYear, endMonth, 1, 0, 0, 0, DateTimeKind.Utc)
                : (DateTime?)null;

            for (var cursor = first; cursor <= last; cursor = cursor.AddMonths(1))
            {
                if (cursor < start)
                    continue;

                if (end is DateTime stop && cursor > stop)
                    break;

                var day = Math.Min(
                    template.PaymentDay,
                    DateTime.DaysInMonth(cursor.Year, cursor.Month));

                var key = (template.Id, cursor.Year, cursor.Month);

                var actual = actualByPeriod.TryGetValue(key, out var hit)
                    ? hit
                    : default;

                result.Add(new RecurringPeriodState(
                    template.Id,
                    cursor.Year,
                    cursor.Month,
                    new DateTime(cursor.Year, cursor.Month, day, 0, 0, 0, DateTimeKind.Utc),
                    template.EstimatedAmount,
                    actual.Id == Guid.Empty ? null : actual.Id,
                    actual.Id == Guid.Empty ? null : actual.Amount));
            }
        }

        return result;
    }

    /// <summary>
    /// Bir dönemin gerçekleşenini kaydeder. AYNI DÖNEM İKİNCİ KEZ
    /// KAPANMAZ: ikinci onay aynı ayı iki kez saydırırdı.
    /// </summary>
    public async Task<(string? Error, Guid? EntryId)> ConfirmPeriodAsync(
        Guid templateId, int year, int month, decimal actualAmount,
        ExpenseDocumentType documentType, string? documentNumber,
        CancellationToken cancellationToken)
    {
        var template = await db.RecurringExpenseTemplates
            .SingleOrDefaultAsync(x => x.Id == templateId, cancellationToken);

        if (template is null)
            return ("Tekrarlayan gider şablonu bulunamadı.", null);

        if (actualAmount <= 0m)
            return ("Gerçekleşen tutar sıfırdan büyük olmalıdır.", null);

        if (month is < 1 or > 12)
            return ("Ay 1-12 aralığında olmalıdır.", null);

        var start = new DateTime(template.StartYear, template.StartMonth, 1,
            0, 0, 0, DateTimeKind.Utc);

        var period = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);

        if (period < start)
            return ("Bu dönem şablonun başlangıcından önce.", null);

        if (template.EndYear is int endYear && template.EndMonth is int endMonth &&
            period > new DateTime(endYear, endMonth, 1, 0, 0, 0, DateTimeKind.Utc))
            return ("Bu dönem şablonun bitişinden sonra.", null);

        var alreadyConfirmed = await db.ExpenseEntries
            .AnyAsync(x => x.RecurringTemplateId == templateId &&
                           x.PeriodYear == year && x.PeriodMonth == month,
                cancellationToken);

        if (alreadyConfirmed)
            return ("Bu dönem zaten kesinleşmiş; düzeltme gider kaydından yapılır.", null);

        var day = Math.Min(template.PaymentDay, DateTime.DaysInMonth(year, month));

        var entry = new ExpenseEntry
        {
            CompanyId = template.CompanyId,
            CenterType = template.CenterType,
            BranchId = template.BranchId,
            ProjectId = template.ProjectId,
            ProjectSiteId = template.ProjectSiteId,
            ExpenseCategoryId = template.ExpenseCategoryId,
            ExpenseDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc),
            Amount = decimal.Round(actualAmount, 2),
            Description = template.Description,
            PaymentMethod = template.PaymentMethod,
            DocumentType = documentType,
            DocumentNumber = string.IsNullOrWhiteSpace(documentNumber)
                ? null
                : documentNumber.Trim(),
            SupplierCurrentAccountId = template.SupplierCurrentAccountId,
            RecurringTemplateId = template.Id,
            PeriodYear = year,
            PeriodMonth = month
        };

        db.ExpenseEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return (null, entry.Id);
    }
}
