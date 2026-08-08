using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// T.C. kimlik numarası doğrulaması (H1).
///
/// Korunan fikir: numara BOŞ bırakılabilir ama YANLIŞ girilemez.
/// Boş alan eksikliktir ve uyarıyla yönetilir; yanlış alan sessiz bir
/// hatadır ve ancak SGK bildirimi reddedildiğinde — aylar sonra —
/// ortaya çıkar.
///
/// Test numaraları algoritmaya uyacak şekilde üretilmiştir; gerçek
/// kişilere ait değildir.
/// </summary>
public sealed class TurkishIdentityNumberTests
{
    /// <summary>
    /// Algoritmaya uyan örnek üretir: ilk 9 haneden 10. ve 11. haneler
    /// hesaplanır. Testin kendi verisini kuralın kendisinden değil,
    /// kuralın TANIMINDAN üretmesi için ayrı yazıldı.
    /// </summary>
    private static string Build(string firstNine)
    {
        var digits = firstNine.Select(x => x - '0').ToArray();

        var odd = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var even = digits[1] + digits[3] + digits[5] + digits[7];

        var tenth = ((odd * 7 - even) % 10 + 10) % 10;
        var eleventh = (digits.Sum() + tenth) % 10;

        return firstNine + tenth + eleventh;
    }

    [Theory]
    [InlineData("100000000")]
    [InlineData("123456789")]
    [InlineData("987654321")]
    [InlineData("111111111")]
    [InlineData("246813579")]
    public void GeneratedNumbers_AreValid(string firstNine)
    {
        Assert.True(TurkishIdentityNumber.IsValid(Build(firstNine)));
    }

    /// <summary>
    /// Fark negatif olduğunda mod işlemi pozitife çekilmezse geçerli
    /// numara reddedilirdi — C#'ta (-3 % 10) == -3.
    /// </summary>
    [Fact]
    public void NumberWithNegativeChecksumDifference_IsStillValid()
    {
        // Tek haneler küçük, çift haneler büyük: 1×7 − 36 = −29.
        Assert.True(TurkishIdentityNumber.IsValid(Build("190909090")));
    }

    [Fact]
    public void WrongCheckDigit_IsRejected()
    {
        var valid = Build("123456789");
        var broken = valid[..10] + (valid[10] == '0' ? '1' : '0');

        Assert.True(TurkishIdentityNumber.IsValid(valid));
        Assert.False(TurkishIdentityNumber.IsValid(broken));
    }

    /// <summary>Hane sırası bozulunca sağlama tutmaz — yaygın yazım hatası.</summary>
    [Fact]
    public void TransposedDigits_AreRejected()
    {
        var valid = Build("123456789");
        var swapped = valid[1].ToString() + valid[0] + valid[2..];

        Assert.False(TurkishIdentityNumber.IsValid(swapped));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_IsNotValid(string? value)
    {
        Assert.False(TurkishIdentityNumber.IsValid(value));
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("123456789012")]
    public void WrongLength_IsRejected(string value)
    {
        Assert.False(TurkishIdentityNumber.IsValid(value));
    }

    [Fact]
    public void LeadingZero_IsRejected()
    {
        Assert.False(TurkishIdentityNumber.IsValid("01234567890"));
    }

    [Fact]
    public void NonDigits_AreRejected()
    {
        Assert.False(TurkishIdentityNumber.IsValid("1234567890A"));
        Assert.False(TurkishIdentityNumber.IsValid("12345 67890"));
    }

    // ---------- Boş bırakılabilirlik ----------

    /// <summary>
    /// Kayıt kimlik numarasız açılabilir; canlıda 4 personelde yok.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_IsAccepted_WhenOptional(string? value)
    {
        Assert.True(TurkishIdentityNumber.IsBlankOrValid(value));
        Assert.Null(TurkishIdentityNumber.Describe(value));
    }

    [Fact]
    public void InvalidValue_IsNotAcceptedEvenThoughFieldIsOptional()
    {
        Assert.False(TurkishIdentityNumber.IsBlankOrValid("11111111111"));
        Assert.NotNull(TurkishIdentityNumber.Describe("11111111111"));
    }

    // ---------- Gerekçe metni ----------

    [Fact]
    public void Describe_ExplainsLengthProblem()
    {
        Assert.Contains("11 haneli", TurkishIdentityNumber.Describe("123")!);
    }

    [Fact]
    public void Describe_ExplainsLeadingZero()
    {
        Assert.Contains("sıfırla", TurkishIdentityNumber.Describe("01234567890")!);
    }

    [Fact]
    public void Describe_ExplainsChecksumProblem()
    {
        var valid = Build("123456789");
        var broken = valid[..10] + (valid[10] == '0' ? '1' : '0');

        Assert.Contains("algoritma", TurkishIdentityNumber.Describe(broken)!);
    }

    [Fact]
    public void Describe_IsSilentForValidNumbers()
    {
        Assert.Null(TurkishIdentityNumber.Describe(Build("246813579")));
    }

    [Fact]
    public void SurroundingWhitespace_IsTolerated()
    {
        Assert.True(TurkishIdentityNumber.IsValid($"  {Build("123456789")}  "));
    }
}
