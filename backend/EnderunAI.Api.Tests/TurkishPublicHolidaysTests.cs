using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Resmî tatiller (H3).
///
/// Korunan karar: dini bayram TARİHLERİ koda gömülmedi. Kayan ve
/// resmî ilana bağlı bir tarihi sistemin tahmin etmesi, puantajı ve
/// dolayısıyla bordroyu sessizce yanlış üretirdi. Bilinen şey bayramın
/// YAPISI: arife yarım gün + Ramazan'da 3, Kurban'da 4 tam gün.
/// Kullanıcı yalnızca ilk günü giriyor.
/// </summary>
public sealed class TurkishPublicHolidaysTests
{
    // ---------- Sabit tatiller ----------

    [Fact]
    public void FixedHolidays_CoverEveryNationalDay()
    {
        var days = TurkishPublicHolidays.Fixed(2026);

        Assert.Equal(8, days.Count);

        Assert.Contains(days, x => x.Date == new DateOnly(2026, 1, 1));
        Assert.Contains(days, x => x.Date == new DateOnly(2026, 4, 23));
        Assert.Contains(days, x => x.Date == new DateOnly(2026, 5, 1));
        Assert.Contains(days, x => x.Date == new DateOnly(2026, 5, 19));
        Assert.Contains(days, x => x.Date == new DateOnly(2026, 7, 15));
        Assert.Contains(days, x => x.Date == new DateOnly(2026, 8, 30));
        Assert.Contains(days, x => x.Date == new DateOnly(2026, 10, 28));
        Assert.Contains(days, x => x.Date == new DateOnly(2026, 10, 29));
    }

    /// <summary>
    /// 28 Ekim öğleden sonra başlar; tam gün sayılırsa ücrete esas gün
    /// bir fazla çıkar.
    /// </summary>
    [Fact]
    public void RepublicDayEve_IsHalfDay()
    {
        var days = TurkishPublicHolidays.Fixed(2026);

        Assert.True(days.Single(x => x.Date == new DateOnly(2026, 10, 28)).IsHalfDay);
        Assert.False(days.Single(x => x.Date == new DateOnly(2026, 10, 29)).IsHalfDay);
    }

    [Fact]
    public void FixedHolidays_FollowTheRequestedYear()
    {
        Assert.All(
            TurkishPublicHolidays.Fixed(2027),
            x => Assert.Equal(2027, x.Date.Year));
    }

    [Fact]
    public void AbsurdYear_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TurkishPublicHolidays.Fixed(1500));
    }

    /// <summary>
    /// Sabit listede dini bayram YOKTUR — tarihini sistem bilemez.
    /// </summary>
    [Fact]
    public void FixedHolidays_ContainNoReligiousHoliday()
    {
        Assert.DoesNotContain(
            TurkishPublicHolidays.Fixed(2026),
            x => x.Name.Contains("Ramazan") || x.Name.Contains("Kurban"));
    }

    // ---------- Dini bayramlar ----------

    /// <summary>Ramazan: arife + 3 tam gün.</summary>
    [Fact]
    public void Ramadan_IsEveningPlusThreeDays()
    {
        var days = TurkishPublicHolidays.Religious(
            ReligiousHolidayKind.Ramazan, new DateOnly(2026, 3, 20));

        Assert.Equal(4, days.Count);

        Assert.Equal(new DateOnly(2026, 3, 19), days[0].Date);
        Assert.True(days[0].IsHalfDay);
        Assert.Contains("arifesi", days[0].Name);

        Assert.Equal(new DateOnly(2026, 3, 20), days[1].Date);
        Assert.Equal(new DateOnly(2026, 3, 21), days[2].Date);
        Assert.Equal(new DateOnly(2026, 3, 22), days[3].Date);
        Assert.All(days.Skip(1), x => Assert.False(x.IsHalfDay));
    }

    /// <summary>Kurban: arife + 4 tam gün.</summary>
    [Fact]
    public void Sacrifice_IsEveningPlusFourDays()
    {
        var days = TurkishPublicHolidays.Religious(
            ReligiousHolidayKind.Kurban, new DateOnly(2026, 5, 27));

        Assert.Equal(5, days.Count);

        Assert.Equal(new DateOnly(2026, 5, 26), days[0].Date);
        Assert.True(days[0].IsHalfDay);
        Assert.Equal(new DateOnly(2026, 5, 30), days[^1].Date);
    }

    /// <summary>
    /// Bayram ay başına denk gelirse arife bir önceki aya taşar.
    /// </summary>
    [Fact]
    public void EveCrossesMonthBoundary()
    {
        var days = TurkishPublicHolidays.Religious(
            ReligiousHolidayKind.Ramazan, new DateOnly(2026, 4, 1));

        Assert.Equal(new DateOnly(2026, 3, 31), days[0].Date);
    }

    /// <summary>Yılbaşına denk gelen bayramda arife bir önceki yıla taşar.</summary>
    [Fact]
    public void EveCrossesYearBoundary()
    {
        var days = TurkishPublicHolidays.Religious(
            ReligiousHolidayKind.Kurban, new DateOnly(2026, 1, 1));

        Assert.Equal(new DateOnly(2025, 12, 31), days[0].Date);
    }

    [Fact]
    public void ReligiousDays_AreNamedByOrder()
    {
        var days = TurkishPublicHolidays.Religious(
            ReligiousHolidayKind.Ramazan, new DateOnly(2026, 3, 20));

        Assert.Contains("1. gün", days[1].Name);
        Assert.Contains("3. gün", days[3].Name);
    }
}
