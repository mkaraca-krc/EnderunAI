using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.HumanResources;

/// <summary>
/// Elden ödemenin şantiye bazında dağılımı.
/// </summary>
/// <param name="BySite">Şantiye kimliği → tutar.</param>
/// <param name="Unassigned">Şantiyesi girilmemiş puantaj günlerine
/// düşen pay. Herhangi bir şantiyeye dağıtılmaz; dağıtmak uydurma
/// olurdu ve şantiye kârlılığını yanlış gösterirdi.</param>
public sealed record SiteExtraPaymentShares(
    IReadOnlyDictionary<Guid, decimal> BySite,
    decimal Unassigned)
{
    /// <summary>Projenin toplam elden işçilik payı.</summary>
    public decimal Total =>
        decimal.Round(BySite.Values.Sum() + Unassigned, 2);
}

/// <summary>
/// Elden ödemelerin projelere ve şantiyelere PUANTAJ GÜNÜNE ORANLA
/// dağıtımı.
///
/// NEDEN ORANLI: doğrudan aylık tutarın tamamı yazılsaydı, birden
/// fazla projede/şantiyede çalışan personelin elden ödemesi her birine
/// ayrı ayrı yüklenir ve toplam maliyet gerçekte ödenenin katı
/// çıkardı. Pay = o birimde çalışılan gün ÷ o ay çalışılan toplam gün.
///
/// NEDEN AYRI SERVİS: aynı dağıtım hem proje maliyet analizinde hem
/// şantiye işçilik kırılımında gerekiyor. İki yerde ayrı yazılırsa
/// bir gün ayrışır ve iki ekran aynı personel için farklı rakam
/// gösterir.
///
/// GİZLİLİK KARARI VERMEZ: çağıran, <c>extra_payment.view</c> iznini
/// doğrulamadan bu servisi çağırmamalıdır. Yetkisiz akışta hiç
/// çağrılmadığı için sorgu elden tablosuna uğramaz.
/// </summary>
public sealed class ExtraPaymentAllocationService(AppDbContext db)
{
    /// <summary>
    /// Projenin elden işçilik payı.
    /// </summary>
    public async Task<decimal> GetProjectShareAsync(
        Guid companyId, Guid projectId, CancellationToken cancellationToken)
    {
        var shares = await AllocateAsync(companyId, cancellationToken);

        return decimal.Round(
            shares.Where(x => x.ProjectId == projectId).Sum(x => x.Amount), 2);
    }

    /// <summary>
    /// Projenin şantiye bazında elden işçilik payı.
    /// </summary>
    public async Task<SiteExtraPaymentShares> GetSiteSharesAsync(
        Guid companyId, Guid projectId, CancellationToken cancellationToken)
    {
        var shares = (await AllocateAsync(companyId, cancellationToken))
            .Where(x => x.ProjectId == projectId)
            .ToList();

        // Şantiyesi olan ve olmayan paylar AYRI tutuluyor. Sözlükte
        // null anahtar kullanılamıyor; ayrıca şantiyesi girilmemiş
        // günü herhangi bir şantiyeye dağıtmak uydurma olurdu.
        var bySite = shares
            .Where(x => x.ProjectSiteId.HasValue)
            .GroupBy(x => x.ProjectSiteId!.Value)
            .ToDictionary(
                g => g.Key,
                g => decimal.Round(g.Sum(x => x.Amount), 2));

        var unassigned = decimal.Round(
            shares.Where(x => !x.ProjectSiteId.HasValue).Sum(x => x.Amount), 2);

        return new SiteExtraPaymentShares(bySite, unassigned);
    }

    /// <summary>
    /// Elden ödemeleri puantaj gününe oranla proje+şantiye kırılımına
    /// dağıtır.
    ///
    /// Yalnızca ONAYLI ve projeye yazılmış puantaj günleri sayılır;
    /// izin/rapor günü hiçbir projeye yüklenmez.
    /// </summary>
    private async Task<List<(Guid ProjectId, Guid? ProjectSiteId, decimal Amount)>>
        AllocateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var result = new List<(Guid, Guid?, decimal)>();

        var payments = await db.PersonnelExtraPayments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.PersonnelId,
                x.MonthlyAmount,
                x.EffectiveStartDate,
                x.EffectiveEndDate
            })
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
            return result;

        var personnelIds = payments.Select(x => x.PersonnelId).Distinct().ToList();

        var days = await db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.IsApproved &&
                        x.ProjectId != null &&
                        personnelIds.Contains(x.PersonnelId))
            .Select(x => new
            {
                x.PersonnelId,
                ProjectId = x.ProjectId!.Value,
                x.ProjectSiteId,
                x.WorkDate
            })
            .ToListAsync(cancellationToken);

        if (days.Count == 0)
            return result;

        foreach (var personMonth in days.GroupBy(x => new
        {
            x.PersonnelId,
            x.WorkDate.Year,
            x.WorkDate.Month
        }))
        {
            var monthDays = personMonth.Count();

            if (monthDays == 0)
                continue;

            var monthStart = new DateTime(personMonth.Key.Year, personMonth.Key.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var monthlyAmount = payments
                .Where(x => x.PersonnelId == personMonth.Key.PersonnelId &&
                            x.EffectiveStartDate.Date <= monthEnd &&
                            (x.EffectiveEndDate == null ||
                             x.EffectiveEndDate.Value.Date >= monthStart))
                .Sum(x => x.MonthlyAmount);

            if (monthlyAmount == 0m)
                continue;

            foreach (var unit in personMonth.GroupBy(x => new
            {
                x.ProjectId,
                x.ProjectSiteId
            }))
            {
                result.Add((
                    unit.Key.ProjectId,
                    unit.Key.ProjectSiteId,
                    monthlyAmount * unit.Count() / monthDays));
            }
        }

        return result;
    }
}
