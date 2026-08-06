using System.Globalization;
using EnderunAI.Api.Services.Market;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// TCMB kur bülteni ayrıştırma. Ağ yok, veritabanı yok — bülten metni
/// sabit, dolayısıyla TCMB erişilemese de bu testler çalışır.
///
/// Asıl güvence kültür tuzağı: TCMB ondalık ayırıcı olarak nokta
/// kullanıyor. Sunucu kültürü Türkçe olduğunda kültüre duyarlı bir
/// ayrıştırma "47.4881" değerini 474881 diye okur ve tüm dövizli
/// fişler on binlerce kat şişer. O yüzden testler Türkçe kültür
/// altında da koşturuluyor.
/// </summary>
public sealed class TcmbRateParserTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Tarih_Date Tarih="05.08.2026" Date="08/05/2026" Bulten_No="2026/144">
          <Currency CrossOrder="0" Kod="USD" CurrencyCode="USD">
            <Unit>1</Unit>
            <Isim>ABD DOLARI</Isim>
            <ForexBuying>47.4881</ForexBuying>
            <ForexSelling>47.5736</ForexSelling>
            <BanknoteBuying>47.4548</BanknoteBuying>
            <BanknoteSelling>47.6450</BanknoteSelling>
          </Currency>
          <Currency CrossOrder="9" Kod="EUR" CurrencyCode="EUR">
            <Unit>1</Unit>
            <Isim>EURO</Isim>
            <ForexBuying>55.1234</ForexBuying>
            <ForexSelling>55.2456</ForexSelling>
            <BanknoteBuying>55.0854</BanknoteBuying>
            <BanknoteSelling>55.3284</BanknoteSelling>
          </Currency>
          <Currency CrossOrder="11" Kod="JPY" CurrencyCode="JPY">
            <Unit>100</Unit>
            <Isim>JAPON YENI</Isim>
            <ForexBuying>32.1400</ForexBuying>
            <ForexSelling>32.3600</ForexSelling>
            <BanknoteBuying/>
            <BanknoteSelling/>
          </Currency>
          <Currency CrossOrder="20" Kod="XDR" CurrencyCode="XDR">
            <Unit>1</Unit>
            <Isim>SDR</Isim>
            <ForexBuying/>
            <ForexSelling/>
            <CrossRateOther>1.3</CrossRateOther>
          </Currency>
        </Tarih_Date>
        """;

    [Fact]
    public void Parse_ReadsBulletinDateAndNumber()
    {
        var bulletin = TcmbRateParser.Parse(SampleXml);

        Assert.NotNull(bulletin);
        Assert.Equal(new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc), bulletin.RateDate);
        Assert.Equal("2026/144", bulletin.BulletinNumber);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Parse_DecimalSeparator_IsCultureIndependent(string culture)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var bulletin = TcmbRateParser.Parse(SampleXml);
            var usd = bulletin!.Rows.Single(x => x.CurrencyCode == "USD");

            Assert.Equal(47.4881m, usd.ForexBuying);
            Assert.Equal(47.5736m, usd.ForexSelling);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Parse_HundredUnitQuotation_IsReducedToSingleUnit()
    {
        // JPY 100 birim üzerinden kote edilir. Bire indirgenmezse yen
        // cinsi bir fatura 100 kat şişerdi.
        var bulletin = TcmbRateParser.Parse(SampleXml);
        var jpy = bulletin!.Rows.Single(x => x.CurrencyCode == "JPY");

        Assert.Equal(0.3214m, jpy.ForexBuying);
        Assert.Equal(0.3236m, jpy.ForexSelling);
    }

    [Fact]
    public void Parse_EmptyBanknoteFields_BecomeNull()
    {
        var bulletin = TcmbRateParser.Parse(SampleXml);
        var jpy = bulletin!.Rows.Single(x => x.CurrencyCode == "JPY");

        Assert.Null(jpy.BanknoteBuying);
        Assert.Null(jpy.BanknoteSelling);
    }

    [Fact]
    public void Parse_RowWithoutForexBuying_IsSkipped()
    {
        // XDR'de döviz alış yok; muhasebenin esas kuru o olduğu için
        // satır arşive hiç girmemeli — 0 ile kaydedilirse dövizli fiş
        // sıfır TL'ye kesilir.
        var bulletin = TcmbRateParser.Parse(SampleXml);

        Assert.DoesNotContain(bulletin!.Rows, x => x.CurrencyCode == "XDR");
        Assert.Equal(3, bulletin.Rows.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bu xml değil")]
    [InlineData("<html><body>404</body></html>")]
    public void Parse_InvalidInput_ReturnsNull(string input)
    {
        // Yarım veya alakasız içerik arşive girmemeli.
        Assert.Null(TcmbRateParser.Parse(input));
    }

    [Fact]
    public void Parse_BulletinWithNoUsableRow_ReturnsNull()
    {
        const string xml = """
            <Tarih_Date Tarih="05.08.2026" Bulten_No="2026/144">
              <Currency Kod="XDR" CurrencyCode="XDR"><Unit>1</Unit></Currency>
            </Tarih_Date>
            """;

        Assert.Null(TcmbRateParser.Parse(xml));
    }

    [Fact]
    public void BuildHistoricalPath_MatchesTcmbLayout()
    {
        var path = TcmbRateParser.BuildHistoricalPath(new DateTime(2026, 8, 4));

        Assert.Equal("kurlar/202608/04082026.xml", path);
    }
}
