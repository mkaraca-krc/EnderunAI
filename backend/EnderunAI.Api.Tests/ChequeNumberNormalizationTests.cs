using System.Globalization;
using EnderunAI.Api.Models;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ÇEK NUMARASI NORMALİZASYONU — mükerrer engelinin dayandığı kural.
///
/// Bu testler olmadan kısıtın DELİNİP DELİNMEDİĞİ görünmez: normalizasyon
/// sessizce bozulursa aynı çek iki kez girilebilir ve hiçbir yerde hata
/// çıkmaz, yalnız defterde iki kayıt olur.
///
/// KÜLTÜR ZORLANIYOR: testlerin bir kısmı `tr-TR` altında koşuyor.
/// Zorlanmasaydı tuzak yakalanmazdı — koşu ortamı `en-US` ya da
/// invariant olduğu için `ToUpper()` ile `ToUpperInvariant()` aynı
/// sonucu verir ve test yeşil kalırdı.
/// </summary>
public sealed class ChequeNumberNormalizationTests
{
    /// <summary>Gövdeyi verilen kültür altında çalıştırır ve kültürü geri koyar.</summary>
    private static T InCulture<T>(string culture, Func<T> body)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// TÜRKÇE "i" TUZAĞI.
    ///
    /// tr-TR kültüründe `"i".ToUpper()` → "İ" (noktalı büyük I).
    /// Kültüre bağlı bir çevirim kullanılsaydı aynı çek numarası,
    /// kaydeden kullanıcının kültürüne göre İKİ FARKLI normalize değer
    /// üretir ve mükerrer kontrolü delinirdi.
    /// </summary>
    [Fact]
    public void Normalize_TurkceKulturdeDeAyniDegeriUretir()
    {
        const string chequeNumber = "Bi12345";

        var invariant = Cheque.NormalizeChequeNumber(chequeNumber);
        var turkish = InCulture("tr-TR", () => Cheque.NormalizeChequeNumber(chequeNumber));

        Assert.Equal(invariant, turkish);
        Assert.Equal("BI12345", turkish);

        // Tuzağın gerçekten var olduğunu da kaydediyoruz: kültüre bağlı
        // çevirim BAŞKA bir sonuç veriyor. Bu satır düşerse tuzak
        // ortadan kalkmış demektir ve test artık bir şey korumuyordur.
        var cultureDependent = InCulture("tr-TR", () => chequeNumber.ToUpper());
        Assert.NotEqual("BI12345", cultureDependent);
    }

    [Theory]
    [InlineData("12345", "12345")]
    [InlineData(" 12345 ", "12345")]
    [InlineData("12 345", "12345")]
    [InlineData("12\t345", "12345")]
    [InlineData("ab-12 345", "AB-12345")]
    public void Normalize_BosluklariAtarVeBuyutur(string input, string expected)
    {
        Assert.Equal(expected, Cheque.NormalizeChequeNumber(input));
    }

    /// <summary>
    /// BAŞTAKİ SIFIRLAR KORUNUR.
    ///
    /// "0012345" ile "12345" FARKLI çeklerdir. Sayıya çevirmek ya da
    /// `TrimStart('0')` uygulamak iki ayrı çeki tek çek sanmaya yol
    /// açar; ikincisi kaydedilemez ve kullanıcı nedenini anlamaz.
    /// </summary>
    [Fact]
    public void Normalize_BastakiSifirlariKorur()
    {
        var withZeros = Cheque.NormalizeChequeNumber("0012345");
        var withoutZeros = Cheque.NormalizeChequeNumber("12345");

        Assert.Equal("0012345", withZeros);
        Assert.NotEqual(withZeros, withoutZeros);
    }

    [Fact]
    public void Normalize_BosDegerBosDize()
    {
        Assert.Equal(string.Empty, Cheque.NormalizeChequeNumber(null));
        Assert.Equal(string.Empty, Cheque.NormalizeChequeNumber("   "));
    }
}
