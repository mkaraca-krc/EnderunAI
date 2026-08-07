using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.HumanResources;

/// <summary>
/// Bir personelin gerçek yevmiyesi.
/// </summary>
/// <param name="PersonnelId">Personel.</param>
/// <param name="AsOf">Hangi tarihe göre çözüldüğü.</param>
/// <param name="MonthlyGross">Resmî aylık brüt.</param>
/// <param name="OfficialDailyRate">Resmî günlük ücret.</param>
/// <param name="OfficialHourlyRate">Resmî saatlik ücret.</param>
/// <param name="DailyWorkHours">Günlük normal çalışma saati.</param>
/// <param name="ExtraMonthlyAmount">Aylık elden ödeme; yetki yoksa null.</param>
/// <param name="ExtraDailyRate">Elden ödemenin günlük payı.</param>
/// <param name="ActualDailyRate">Gerçek günlük ücret (resmî + elden).</param>
/// <param name="ActualHourlyRate">Gerçek saatlik ücret.</param>
/// <param name="ExtraPaymentHidden">Elden kısım gizlendi mi.</param>
public sealed record ActualDailyWage(
    Guid PersonnelId,
    DateTime AsOf,
    decimal MonthlyGross,
    decimal OfficialDailyRate,
    decimal OfficialHourlyRate,
    decimal DailyWorkHours,
    decimal? ExtraMonthlyAmount,
    decimal? ExtraDailyRate,
    decimal? ActualDailyRate,
    decimal? ActualHourlyRate,
    bool ExtraPaymentHidden);

/// <summary>
/// "Gerçek yevmiye": puantajın esas aldığı resmî günlük/saatlik ücrete
/// elden ödemenin günlük payını ekler.
///
/// SALT GÖSTERİM: bu rakam bordroya, SGK matrahına ve muhasebeye
/// GİRMEZ. Puantajdan üretilen resmî tutarlar değişmez; buradaki
/// rakam "bu adam gerçekte günlüğüne kaça çalışıyor" sorusunun
/// cevabıdır ve yalnızca yetkiliye gösterilir.
///
/// Elden ödemenin günlük payı aylık tutarın 30'a bölümüdür — resmî
/// günlük ücretin türetildiği kuralın aynısı. Farklı bir bölen
/// kullanmak iki rakamı kıyaslanamaz hâle getirirdi.
/// </summary>
public sealed class ActualDailyWageService(
    HrDbContext hrDb,
    AppDbContext appDb,
    SalaryTakeHomeService takeHome,
    IExtraPaymentVisibilityService extraPaymentVisibility)
{
    /// <summary>Aylık tutarın günlüğe çevrilmesinde kullanılan bölen.</summary>
    private const decimal MonthlyToDailyDivisor = 30m;

    /// <summary>Bordro ayarı yoksa yasal haftalık 45 saatin 6 güne bölümü.</summary>
    private const decimal DefaultDailyWorkHours = 7.5m;

    /// <summary>
    /// Personelin verilen tarihteki gerçek yevmiyesi.
    /// </summary>
    /// <returns>Ücret kartı yoksa null — rakam uydurulmaz.</returns>
    public async Task<ActualDailyWage?> ResolveAsync(
        Guid personnelId, DateTime asOf, CancellationToken cancellationToken)
    {
        var date = DateTime.SpecifyKind(asOf.Date, DateTimeKind.Utc);

        var card = await hrDb.SalaryDefinitions
            .AsNoTracking()
            .Where(x => x.PersonnelId == personnelId &&
                        x.EffectiveStartDate <= date &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= date))
            .OrderByDescending(x => x.EffectiveStartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (card is null)
            return null;

        var companyId = await appDb.Personnel
            .AsNoTracking()
            .Where(x => x.Id == personnelId)
            .Select(x => (Guid?)x.CompanyId)
            .SingleOrDefaultAsync(cancellationToken);

        var dailyWorkHours = await LoadDailyWorkHoursAsync(
            companyId, date.Year, cancellationToken);

        var monthlyGross = card.SalaryBasis == SalaryBasis.Net
            ? (card.GrossSalary > 0m ? card.GrossSalary : card.TargetNetSalary)
            : card.GrossSalary;

        // Kart neyi söylüyorsa o; boşsa bordroyla AYNI kuraldan türetilir.
        var officialDaily = card.DailyRate > 0m
            ? card.DailyRate
            : decimal.Round(monthlyGross / MonthlyToDailyDivisor, 2);

        var officialHourly = card.HourlyRate > 0m
            ? card.HourlyRate
            : decimal.Round(officialDaily / dailyWorkHours, 2);

        if (!await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken))
        {
            return new ActualDailyWage(
                personnelId, date, monthlyGross,
                officialDaily, officialHourly, dailyWorkHours,
                ExtraMonthlyAmount: null,
                ExtraDailyRate: null,
                ActualDailyRate: null,
                ActualHourlyRate: null,
                ExtraPaymentHidden: true);
        }

        var extras = await takeHome.LoadEffectiveExtraPaymentsAsync(
            [personnelId], date, cancellationToken);

        var extraMonthly = extras.GetValueOrDefault(personnelId, 0m);
        var extraDaily = decimal.Round(extraMonthly / MonthlyToDailyDivisor, 2);
        var actualDaily = decimal.Round(officialDaily + extraDaily, 2);

        return new ActualDailyWage(
            personnelId, date, monthlyGross,
            officialDaily, officialHourly, dailyWorkHours,
            ExtraMonthlyAmount: extraMonthly,
            ExtraDailyRate: extraDaily,
            ActualDailyRate: actualDaily,
            ActualHourlyRate: decimal.Round(actualDaily / dailyWorkHours, 2),
            ExtraPaymentHidden: false);
    }

    private async Task<decimal> LoadDailyWorkHoursAsync(
        Guid? companyId, int year, CancellationToken cancellationToken)
    {
        if (companyId is null)
            return DefaultDailyWorkHours;

        var hours = await appDb.CompanyPayrollSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId.Value && x.Year == year)
            .Select(x => (decimal?)x.DailyWorkHours)
            .SingleOrDefaultAsync(cancellationToken);

        return hours is > 0m ? hours.Value : DefaultDailyWorkHours;
    }
}
