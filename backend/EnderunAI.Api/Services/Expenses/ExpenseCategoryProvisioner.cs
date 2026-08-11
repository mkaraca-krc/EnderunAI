using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Expenses;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Expenses;

/// <summary>
/// Bir şirketin gider kategorilerinin var olduğunu garanti eder.
///
/// NEDEN AÇILIŞ SEED'İ YETMİYOR: seeder yalnızca uygulama başlarken
/// koşuyor. Sonradan açılan bir şirket bir sonraki yeniden başlatmaya
/// kadar kategorisiz kalırdı ve o şirkette gider kaydı hiç
/// açılamazdı. Bu yüzden kategori listesi okunurken de tamamlanıyor.
///
/// ADD-ONLY ve İDEMPOTENT: yalnızca eksik KOD eklenir. Kullanıcının
/// düzelttiği ad/sıra/aktiflik geri alınmaz, silinmiş kategori
/// diriltilmez — silme bilinçli bir karardır.
/// </summary>
public static class ExpenseCategoryProvisioner
{
    public static async Task<bool> EnsureAsync(
        AppDbContext db, Guid companyId, CancellationToken cancellationToken)
    {
        // Silinmişler de sayılıyor: silinen bir kategori yeniden
        // eklenmemeli, üstelik benzersiz kod indeksi de onu dışlar.
        var known = await db.ExpenseCategories
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);

        var existing = known.ToHashSet(StringComparer.Ordinal);
        var added = false;

        foreach (var definition in ExpenseCategoryCatalog.Defaults)
        {
            if (existing.Contains(definition.Code))
                continue;

            db.ExpenseCategories.Add(new ExpenseCategory
            {
                CompanyId = companyId,
                Code = definition.Code,
                Name = definition.Name,
                SortOrder = definition.SortOrder,
                IsSystem = true,
                IsAutomaticOnly = definition.AutomaticOnly,
                IsActive = true
            });

            added = true;
        }

        if (added)
            await db.SaveChangesAsync(cancellationToken);

        return added;
    }
}
