using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Services.Schedule;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Çalışma haftası kademeleri (H3).
///
/// Korunan fikir: ofis kadrosuna cumartesi yazmak doğrudan yanlış gün
/// ve yanlış mesai, dolayısıyla yanlış bordro demektir. Şirket geneli
/// tek bir ayar bu ayrımı yapamaz.
///
/// Kademeler personel–DEPARTMAN bağı üzerinden kurulamadı; bu kod
/// tabanında personelin departmanı yok. Gerçekte var olan ve aranan
/// ayrımı karşılayan eksen görev yeri: merkez ile şantiye.
/// </summary>
public sealed class WorkWeekResolverTests
{
    private const int Unassigned = 0;
    private const int HeadOffice = 1;
    private const int Site = 2;

    private const int Company = (int)WorkWeekDays.MondayToSaturday;
    private const int Office = (int)WorkWeekDays.MondayToFriday;
    private const int Everyday = (int)WorkWeekDays.AllDays;

    // ---------- Kademe sırası ----------

    [Fact]
    public void PersonnelSetting_WinsOverEverything()
    {
        var result = WorkWeekResolver.Resolve(
            personnelWorkWeek: Everyday,
            workLocationType: HeadOffice,
            headOfficeWorkWeek: Office,
            companyWorkWeek: Company);

        Assert.Equal(WorkWeekDays.AllDays, result.Days);
        Assert.Equal("Personel", result.Source);
    }

    /// <summary>Merkez kadrosu şirket varsayılanını ezer.</summary>
    [Fact]
    public void HeadOfficeSetting_WinsOverCompany()
    {
        var result = WorkWeekResolver.Resolve(
            personnelWorkWeek: null,
            workLocationType: HeadOffice,
            headOfficeWorkWeek: Office,
            companyWorkWeek: Company);

        Assert.Equal(WorkWeekDays.MondayToFriday, result.Days);
        Assert.Equal("Merkez kadrosu", result.Source);
    }

    /// <summary>
    /// Merkez ayarı ŞANTİYE personeline uygulanmaz: cumartesi
    /// çalışılan sahaya ofis takvimi yazmak günü eksiltirdi.
    /// </summary>
    [Fact]
    public void HeadOfficeSetting_DoesNotLeakToSiteStaff()
    {
        var result = WorkWeekResolver.Resolve(
            personnelWorkWeek: null,
            workLocationType: Site,
            headOfficeWorkWeek: Office,
            companyWorkWeek: Company);

        Assert.Equal(WorkWeekDays.MondayToSaturday, result.Days);
        Assert.Equal("Şirket", result.Source);
    }

    [Fact]
    public void UnassignedStaff_FallsBackToCompany()
    {
        var result = WorkWeekResolver.Resolve(
            personnelWorkWeek: null,
            workLocationType: Unassigned,
            headOfficeWorkWeek: Office,
            companyWorkWeek: Company);

        Assert.Equal(WorkWeekDays.MondayToSaturday, result.Days);
        Assert.Equal("Şirket", result.Source);
    }

    [Fact]
    public void NothingDefined_FallsBackToSiteDefault()
    {
        var result = WorkWeekResolver.Resolve(null, Site, null, null);

        Assert.Equal(WorkWeekDays.MondayToSaturday, result.Days);
        Assert.Equal("Varsayılan", result.Source);
    }

    // ---------- Bozuk değerler ----------

    /// <summary>
    /// Çalışılan günü olmayan maske TANIM SAYILMAZ; süre hesabını
    /// sonsuz döngüye sokardı, kademe atlanır.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public void UnusableValue_IsSkipped(int broken)
    {
        var result = WorkWeekResolver.Resolve(
            personnelWorkWeek: broken,
            workLocationType: Site,
            headOfficeWorkWeek: null,
            companyWorkWeek: Company);

        Assert.Equal(WorkWeekDays.MondayToSaturday, result.Days);
        Assert.Equal("Şirket", result.Source);
    }

    [Fact]
    public void BrokenHeadOfficeValue_FallsThroughToCompany()
    {
        var result = WorkWeekResolver.Resolve(
            personnelWorkWeek: null,
            workLocationType: HeadOffice,
            headOfficeWorkWeek: 0,
            companyWorkWeek: Company);

        Assert.Equal("Şirket", result.Source);
    }

    // ---------- Kaynağın açıklanması ----------

    /// <summary>
    /// Kaynak ekranda yazılır: kullanıcı neyi değiştireceğini bilmeli.
    /// </summary>
    [Fact]
    public void EveryResolution_ExplainsItsSource()
    {
        var results = new[]
        {
            WorkWeekResolver.Resolve(Everyday, Site, null, Company),
            WorkWeekResolver.Resolve(null, HeadOffice, Office, Company),
            WorkWeekResolver.Resolve(null, Site, null, Company),
            WorkWeekResolver.Resolve(null, Site, null, null)
        };

        Assert.All(results, x => Assert.False(string.IsNullOrWhiteSpace(x.Source)));
        Assert.All(results, x => Assert.False(string.IsNullOrWhiteSpace(x.Description)));
    }

    // ---------- Etiketler ----------

    [Fact]
    public void CommonWeeks_HaveReadableNames()
    {
        Assert.Equal("Pazartesi–Cumartesi",
            WorkWeekResolver.Describe(WorkWeekDays.MondayToSaturday));
        Assert.Equal("Pazartesi–Cuma",
            WorkWeekResolver.Describe(WorkWeekDays.MondayToFriday));
        Assert.Equal("Her gün (takvim günü)",
            WorkWeekResolver.Describe(WorkWeekDays.AllDays));
    }

    [Fact]
    public void UnusualWeek_IsListedDayByDay()
    {
        var days = WorkWeekDays.Monday | WorkWeekDays.Wednesday | WorkWeekDays.Friday;

        Assert.Equal("Pzt, Çar, Cum", WorkWeekResolver.Describe(days));
    }
}
