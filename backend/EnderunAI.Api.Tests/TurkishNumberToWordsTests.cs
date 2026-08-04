using EnderunAI.Api.Services.Hakedis;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Hakediş çıktısındaki "yazı ile" satırı. Türkçe okumanın iki tuzağı
/// var: "biryüz" ve "birbin" denmez, ama "birmilyon" denir.
/// </summary>
public sealed class TurkishNumberToWordsTests
{
    [Theory]
    [InlineData(0, "sıfır TL")]
    [InlineData(1, "bir TL")]
    [InlineData(9, "dokuz TL")]
    [InlineData(10, "on TL")]
    [InlineData(11, "onbir TL")]
    [InlineData(90, "doksan TL")]
    [InlineData(99, "doksandokuz TL")]
    public void SmallNumbers_AreReadCorrectly(decimal amount, string expected)
    {
        Assert.Equal(expected, TurkishNumberToWords.Convert(amount));
    }

    /// <summary>"biryüz" değil "yüz"; ama "ikiyüz" doğru.</summary>
    [Theory]
    [InlineData(100, "yüz TL")]
    [InlineData(101, "yüzbir TL")]
    [InlineData(200, "ikiyüz TL")]
    [InlineData(999, "dokuzyüzdoksandokuz TL")]
    public void Hundreds_DropTheLeadingOne(decimal amount, string expected)
    {
        Assert.Equal(expected, TurkishNumberToWords.Convert(amount));
    }

    /// <summary>"birbin" değil "bin"; ama "ikibin" doğru.</summary>
    [Theory]
    [InlineData(1_000, "bin TL")]
    [InlineData(1_001, "binbir TL")]
    [InlineData(2_000, "ikibin TL")]
    [InlineData(11_000, "onbirbin TL")]
    public void Thousands_DropTheLeadingOne(decimal amount, string expected)
    {
        Assert.Equal(expected, TurkishNumberToWords.Convert(amount));
    }

    /// <summary>Milyonda "bir" düşmez.</summary>
    [Theory]
    [InlineData(1_000_000, "birmilyon TL")]
    [InlineData(2_000_000, "ikimilyon TL")]
    [InlineData(1_000_000_000, "birmilyar TL")]
    public void MillionsKeepTheLeadingOne(decimal amount, string expected)
    {
        Assert.Equal(expected, TurkishNumberToWords.Convert(amount));
    }

    /// <summary>Kuruş ayrı okunur.</summary>
    [Theory]
    [InlineData(33_058.43, "otuzüçbinellisekiz TL kırküç Kr")]
    [InlineData(0.05, "sıfır TL beş Kr")]
    [InlineData(1.50, "bir TL elli Kr")]
    public void Cents_AreReadSeparately(decimal amount, string expected)
    {
        Assert.Equal(expected, TurkishNumberToWords.Convert(amount));
    }

    /// <summary>Kuruş yoksa yazılmaz.</summary>
    [Fact]
    public void ZeroCents_AreOmitted()
    {
        Assert.Equal("beşyüz TL", TurkishNumberToWords.Convert(500.00m));
    }

    /// <summary>Aradaki boş gruplar atlanır: 1.000.005 → birmilyonbeş.</summary>
    [Fact]
    public void EmptyGroups_AreSkipped()
    {
        Assert.Equal("birmilyonbeş TL", TurkishNumberToWords.Convert(1_000_005m));
    }

    /// <summary>NATURA ölçeğinde bir tutar.</summary>
    [Fact]
    public void LargeProgressPaymentAmount_IsReadable()
    {
        Assert.Equal(
            "birmilyonikiyüzotuzdörtbinbeşyüzaltmışyedi TL seksendokuz Kr",
            TurkishNumberToWords.Convert(1_234_567.89m));
    }

    [Fact]
    public void NegativeAmount_IsPrefixed()
    {
        Assert.Equal("eksi bin TL", TurkishNumberToWords.Convert(-1_000m));
    }

    /// <summary>Yuvarlama: üçüncü hane kuruşa yuvarlanır.</summary>
    [Fact]
    public void AmountIsRoundedToCents()
    {
        Assert.Equal("bir TL yetmişsekiz Kr", TurkishNumberToWords.Convert(1.775m));
    }
}
