using EnderunAI.Api.Services.Isg;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Sağlık raporu, eğitim ve sertifikanın ortak geçerlilik hesabı.
/// Sınır günler kritik: bir gün kayması "süresi dolmuş belgeyle
/// çalıştırma" demektir.
/// </summary>
public sealed class IsgValidityCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    [Fact]
    public void NoExpiryDate_IsTreatedAsIndefinite()
    {
        // Bitiş tarihi yoksa tahmini bir tarih üretilmez.
        Assert.Equal(
            IsgValidityStatus.NoExpiry,
            IsgValidityCalculator.Evaluate(null, Today));

        Assert.Null(IsgValidityCalculator.DaysRemaining(null, Today));
    }

    [Fact]
    public void ExpiryToday_IsStillValid()
    {
        // Bitiş günü dahil geçerlidir.
        Assert.Equal(
            IsgValidityStatus.ExpiringSoon,
            IsgValidityCalculator.Evaluate(Today, Today));

        Assert.Equal(0, IsgValidityCalculator.DaysRemaining(Today, Today));
    }

    [Fact]
    public void ExpiredYesterday_IsExpired()
    {
        Assert.Equal(
            IsgValidityStatus.Expired,
            IsgValidityCalculator.Evaluate(Today.AddDays(-1), Today));

        Assert.Equal(-1, IsgValidityCalculator.DaysRemaining(Today.AddDays(-1), Today));
    }

    [Fact]
    public void ExactlyThirtyDaysLeft_IsExpiringSoon()
    {
        Assert.Equal(
            IsgValidityStatus.ExpiringSoon,
            IsgValidityCalculator.Evaluate(Today.AddDays(30), Today));
    }

    [Fact]
    public void ThirtyOneDaysLeft_IsStillValid()
    {
        // Uyarı eşiğinin dışı: 31 gün kala henüz uyarı çıkmaz.
        Assert.Equal(
            IsgValidityStatus.Valid,
            IsgValidityCalculator.Evaluate(Today.AddDays(31), Today));
    }

    [Fact]
    public void WarningThreshold_IsThirtyDays()
    {
        // Eşik panel ve brifingde ortak; sabit tek yerde.
        Assert.Equal(30, IsgValidityCalculator.WarningDays);
    }

    [Theory]
    [InlineData(IsgValidityStatus.Valid, "Geçerli", "green")]
    [InlineData(IsgValidityStatus.ExpiringSoon, "Süresi doluyor", "yellow")]
    [InlineData(IsgValidityStatus.Expired, "Süresi doldu", "red")]
    [InlineData(IsgValidityStatus.NoExpiry, "Süresiz", "gray")]
    public void StatusNameAndColor_AreConsistent(
        IsgValidityStatus status, string expectedName, string expectedColor)
    {
        Assert.Equal(expectedName, IsgValidityCalculator.StatusName(status));
        Assert.Equal(expectedColor, IsgValidityCalculator.StatusColor(status));
    }
}
