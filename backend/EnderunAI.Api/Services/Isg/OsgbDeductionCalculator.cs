using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Isg;

/// <summary>Hakedişe önerilecek İSG kesintisi.</summary>
/// <param name="Amount">Kesinti tutarı.</param>
/// <param name="Description">Hakediş satırında görünecek açıklama.</param>
/// <param name="PersonCount">Kişi başı hesapta kullanılan çalışan
/// sayısı; sabit bedelde null.</param>
public sealed record OsgbDeductionSuggestion(
    decimal Amount,
    string Description,
    int? PersonCount);

/// <summary>
/// OSGB aylık bedelinin hakediş kesintisine çevrilmesi.
///
/// Static ve veritabanısız — bordro/hakediş motorlarıyla aynı desen:
/// hesap kuralı tek yerde durur ve test edilebilir.
///
/// İlke: hesaplanamayan durumda ÖNERİ ÜRETİLMEZ (null döner). Sözleşme
/// yoksa, dönem sözleşme dışındaysa veya kişi başı bedelde çalışan
/// yoksa uydurma tutar önerilmez — ön muhasebe boş satır görür ve
/// kendi girer.
/// </summary>
public static class OsgbDeductionCalculator
{
    /// <summary>
    /// Hakediş dönemine düşen kesintiyi hesaplar.
    /// </summary>
    /// <param name="contract">Aktif OSGB sözleşmesi; yoksa null.</param>
    /// <param name="periodDate">Hakediş dönemi (ay içinde herhangi bir gün).</param>
    /// <param name="activePersonCount">Kişi başı bedelde o dönemde
    /// projenin şantiyelerinde aktif atanmış personel sayısı.</param>
    public static OsgbDeductionSuggestion? Calculate(
        IsgOsgbContract? contract,
        DateOnly periodDate,
        int activePersonCount)
    {
        if (contract is null)
            return null;

        if (!CoversPeriod(contract, periodDate))
            return null;

        return contract.BillingType switch
        {
            OsgbBillingType.MonthlyFixed => BuildFixed(contract),
            OsgbBillingType.PerPerson => BuildPerPerson(contract, activePersonCount),
            _ => null
        };
    }

    /// <summary>
    /// Dönem sözleşme süresi içinde mi. Ay bazında bakılır: sözleşme
    /// ayın 20'sinde başlamışsa o ay için de kesinti vardır.
    /// </summary>
    public static bool CoversPeriod(IsgOsgbContract contract, DateOnly periodDate)
    {
        var period = FirstDayOfMonth(periodDate);

        if (FirstDayOfMonth(contract.StartDate) > period)
            return false;

        return contract.EndDate is not DateOnly end ||
               FirstDayOfMonth(end) >= period;
    }

    private static OsgbDeductionSuggestion? BuildFixed(IsgOsgbContract contract)
    {
        var amount = Round(contract.MonthlyFee);

        return amount <= 0m
            ? null
            : new OsgbDeductionSuggestion(
                amount,
                "İSG katılım payı (OSGB aylık hizmet bedeli)",
                null);
    }

    private static OsgbDeductionSuggestion? BuildPerPerson(
        IsgOsgbContract contract, int activePersonCount)
    {
        if (contract.PerPersonFee <= 0m || activePersonCount <= 0)
            return null;

        var amount = Round(contract.PerPersonFee * activePersonCount);

        return new OsgbDeductionSuggestion(
            amount,
            $"İSG katılım payı (OSGB kişi başı bedel × {activePersonCount} kişi)",
            activePersonCount);
    }

    private static DateOnly FirstDayOfMonth(DateOnly value) =>
        new(value.Year, value.Month, 1);

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
