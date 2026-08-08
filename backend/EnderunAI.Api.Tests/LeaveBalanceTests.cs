using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Yıllık izin bakiyesi (H7).
///
/// Onaylanan kararlar:
/// - DEVİR SINIRSIZ. Hak edilen izin zaman aşımına uğramaz; bakiye tüm
///   hak edişten tüm kullanımın düşülmesidir.
/// - Hak ediş her HİZMET YILI dolduğunda doğar; ilk yılını
///   doldurmayanın hakkı yoktur.
/// - Bakiyeyi aşan talep ENGELLENMEZ, uyarılır.
///
/// Kademe tablosu burada tekrar yazılmadı; çıkış tazminatında
/// kullanılan kuralın aynısı okunuyor. İki ayrı kural, aynı personel
/// için ekranda ve çıkışta farklı rakam üretirdi.
/// </summary>
public sealed class LeaveBalanceTests
{
    private static readonly Guid Person =
        Guid.Parse("11111111-0000-0000-0000-000000000001");

    private static readonly DateOnly AsOf = new(2026, 6, 30);

    private static LeaveBalance Balance(
        int serviceYears,
        decimal used = 0m,
        decimal pending = 0m,
        int extraDays = 0)
    {
        var start = AsOf.AddDays(-(serviceYears * 365 + extraDays));

        return LeaveBalanceCalculator.Calculate(
            new LeaveBalanceInput(
                Person, "PRS-001", "Ali Veli",
                start.ToDateTime(TimeOnly.MinValue),
                used, pending),
            AsOf);
    }

    // ---------- Hak ediş ----------

    /// <summary>İlk yılını doldurmayanın yıllık izin hakkı yok.</summary>
    [Fact]
    public void BeforeFirstYear_NoEntitlement()
    {
        var balance = Balance(serviceYears: 0, extraDays: 200);

        Assert.Equal(0, balance.EntitlementDays);
        Assert.Equal(0, balance.ServiceYears);
        Assert.Contains("İlk hizmet yılı dolmadı", balance.Note!);
    }

    [Fact]
    public void FirstYearComplete_Gives14Days()
    {
        Assert.Equal(14, Balance(serviceYears: 1).EntitlementDays);
    }

    /// <summary>
    /// DEVİR: üç yıl çalışan, hiç kullanmamışsa 42 günü birikmiştir.
    /// </summary>
    [Fact]
    public void UnusedLeave_AccumulatesAcrossYears()
    {
        var balance = Balance(serviceYears: 3);

        Assert.Equal(42, balance.EntitlementDays);
        Assert.Equal(42m, balance.RemainingDays);
    }

    /// <summary>
    /// Kademe atlaması: 6. yılını dolduranın o yılki hakkı 20 gün.
    /// </summary>
    [Fact]
    public void SixthYear_MovesToTheTwentyDayTier()
    {
        var balance = Balance(serviceYears: 6);

        Assert.Equal(20, balance.CurrentTierDays);
        // 5×14 + 1×20 = 90
        Assert.Equal(90, balance.EntitlementDays);
    }

    [Fact]
    public void SixteenthYear_MovesToTheTwentySixDayTier()
    {
        Assert.Equal(26, Balance(serviceYears: 16).CurrentTierDays);
    }

    // ---------- Kullanım ve bakiye ----------

    [Fact]
    public void UsedDays_ReduceTheBalance()
    {
        var balance = Balance(serviceYears: 3, used: 10m);

        Assert.Equal(32m, balance.RemainingDays);
        Assert.Equal(32m, balance.AvailableDays);
    }

    /// <summary>
    /// Onay bekleyen talep bakiyeden düşmez ama KULLANILABİLİRden
    /// düşer: aynı gün iki kez vaat edilmemeli.
    /// </summary>
    [Fact]
    public void PendingDays_ReduceAvailableButNotRemaining()
    {
        var balance = Balance(serviceYears: 3, used: 10m, pending: 5m);

        Assert.Equal(32m, balance.RemainingDays);
        Assert.Equal(27m, balance.AvailableDays);
    }

    /// <summary>
    /// Avans izin verilmişse bakiye negatife düşer ve not bunu söyler.
    /// </summary>
    [Fact]
    public void OverusedLeave_ShowsNegativeBalanceWithANote()
    {
        var balance = Balance(serviceYears: 1, used: 20m);

        Assert.Equal(-6m, balance.RemainingDays);
        Assert.Contains("aşıyor", balance.Note!);
    }

    // ---------- Sonraki hak ediş ----------

    [Fact]
    public void NextAccrual_IsTheEndOfTheCurrentServiceYear()
    {
        var balance = Balance(serviceYears: 3);

        Assert.Equal(AsOf.AddDays(365), balance.NextAccrualDate);
        Assert.Equal(14, balance.NextAccrualDays);
    }

    /// <summary>
    /// Kademe sınırı 1826 gün: tam 5 yıl (1825 gün) hâlâ 14 gün, 5
    /// yıldan FAZLASI 20 gün. Sonraki hak ediş duyurusu bu sınırı
    /// doğru okumalı.
    /// </summary>
    [Fact]
    public void NextAccrual_AnnouncesTheTierChange()
    {
        // 4. yılı dolmuş kişinin bir sonraki hak edişi 5. yılın sonunda
        // (1825 gün) ve o gün hâlâ 14 günlük kademede.
        Assert.Equal(14, Balance(serviceYears: 4).NextAccrualDays);

        // 5. yılı dolmuş kişinin bir sonraki hak edişi 2190 günde ve
        // orası 20 günlük kademe.
        Assert.Equal(20, Balance(serviceYears: 5).NextAccrualDays);
    }

    // ---------- Eksik veri ----------

    /// <summary>
    /// İşe giriş tarihi yoksa hak ediş hesaplanamaz; sıfır göstermek
    /// yerine nedeni söyleniyor.
    /// </summary>
    [Fact]
    public void MissingEmploymentStartDate_IsExplained()
    {
        var balance = LeaveBalanceCalculator.Calculate(
            new LeaveBalanceInput(Person, "PRS-001", "Ali Veli", null, 0m, 0m),
            AsOf);

        Assert.Equal(0, balance.EntitlementDays);
        Assert.Null(balance.NextAccrualDate);
        Assert.Contains("İşe giriş tarihi girilmemiş", balance.Note!);
    }

    [Fact]
    public void FutureEmploymentStartDate_IsExplained()
    {
        var balance = LeaveBalanceCalculator.Calculate(
            new LeaveBalanceInput(
                Person, "PRS-001", "Ali Veli",
                AsOf.AddDays(30).ToDateTime(TimeOnly.MinValue), 0m, 0m),
            AsOf);

        Assert.Equal(0, balance.EntitlementDays);
        Assert.Contains("ileri bir tarih", balance.Note!);
    }

    // ---------- Aşım uyarısı ----------

    [Fact]
    public void RequestWithinBalance_ProducesNoWarning()
    {
        Assert.Null(LeaveBalanceCalculator.DescribeOverdraft(
            Balance(serviceYears: 3), requestedDays: 10m));
    }

    [Fact]
    public void RequestBeyondBalance_IsWarnedWithTheGap()
    {
        var warning = LeaveBalanceCalculator.DescribeOverdraft(
            Balance(serviceYears: 1, used: 10m), requestedDays: 10m);

        Assert.NotNull(warning);
        Assert.Contains("6", warning!);
    }

    /// <summary>
    /// Hakkı hiç doğmamışsa talebin tamamı avans izindir; mesaj bunu
    /// ayrıca söylüyor.
    /// </summary>
    [Fact]
    public void RequestWithoutAnyEntitlement_IsCalledAdvanceLeave()
    {
        var warning = LeaveBalanceCalculator.DescribeOverdraft(
            Balance(serviceYears: 0, extraDays: 100), requestedDays: 3m);

        Assert.Contains("avans izin", warning!);
    }

    [Fact]
    public void ZeroDayRequest_IsNotWarned()
    {
        Assert.Null(LeaveBalanceCalculator.DescribeOverdraft(
            Balance(serviceYears: 0), requestedDays: 0m));
    }
}
