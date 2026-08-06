using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Market;
using EnderunAI.Api.Services.Market;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Bakır fiyat arşivi: kaynak ayrıştırma, birim çevrimi ve TL karşılığı.
///
/// Asıl güvence birim: COMEX USD/lb kote eder, sistem USD/ton üzerinden
/// çalışır. Çevrim atlanırsa fiyat 2.204 kat sapar ve kâr etkisi ekranı
/// sessizce çöpe döner. LME kaynağında da birim varsayılmaz, beyan
/// edilir ve akla yatkınlık kontrolünden geçer.
/// </summary>
[Collection("Integration")]
public sealed class CommodityPriceTests(DatabaseFixture fixture)
{
    /// <summary>Yahoo chart yanıtının küçültülmüş hâli — canlı yapıyla aynı.</summary>
    private const string YahooJson = """
        {
          "chart": {
            "result": [
              {
                "meta": { "currency": "USD", "symbol": "HG=F" },
                "timestamp": [1785729600, 1785816000, 1785902400],
                "indicators": {
                  "quote": [
                    { "close": [6.5139999, 6.6185002, 6.7030000] }
                  ]
                }
              }
            ]
          }
        }
        """;

    private sealed class FakeSource(
        IReadOnlyList<CommodityQuote> quotes,
        string? error = null,
        CommodityPriceSourceKind kind = CommodityPriceSourceKind.Comex)
        : ICommodityPriceSource
    {
        public CommodityPriceSourceKind Kind => kind;
        public string Symbol => "HG=F";
        public string DisplayName => "COMEX bakır vadeli (LME değil)";

        public Task<CommodityFetchResult> GetDailyPricesAsync(
            int days, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommodityFetchResult(quotes, error));
    }

    private static CommodityPriceService CreateService(
        AppDbContext db, ICommodityPriceSource source)
        => new(
            db,
            source,
            new ExchangeRateService(db, new NoopTcmbClient(), NullLogger<ExchangeRateService>.Instance),
            NullLogger<CommodityPriceService>.Instance);

    private sealed class NoopTcmbClient : ITcmbRateClient
    {
        public Task<(TcmbBulletin? Bulletin, string? Error)> GetBulletinAsync(
            DateTime date, CancellationToken cancellationToken = default)
            => Task.FromResult<(TcmbBulletin?, string?)>((null, null));
    }

    /// <summary>
    /// Kuru o güne kesin olarak yazar. Testler paylaşılan bir
    /// veritabanında koştuğu için "varsa dokunma" yeterli değil:
    /// başka bir testin aynı güne yazdığı kur, buradaki beklenen
    /// TL hesabını sessizce bozar.
    /// </summary>
    private static async Task SetRateAsync(
        AppDbContext db, DateTime date, decimal buying)
    {
        var utc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        var existing = await db.ExchangeRates
            .SingleOrDefaultAsync(x => x.RateDate == utc && x.CurrencyCode == "USD");

        if (existing is not null)
        {
            existing.ForexBuying = buying;
            existing.ForexSelling = buying + 0.1m;
        }
        else
        {
            db.ExchangeRates.Add(new ExchangeRate
            {
                RateDate = utc,
                CurrencyCode = "USD",
                Unit = 1,
                ForexBuying = buying,
                ForexSelling = buying + 0.1m,
                Source = "TCMB"
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public void YahooParser_ReadsDailyCloses()
    {
        var quotes = YahooChartParser.Parse(YahooJson);

        Assert.Equal(3, quotes.Count);
        Assert.Equal(new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc), quotes[0].PriceDate);
        Assert.Equal(6.7030000m, quotes[2].Price);
        Assert.Equal("USD", YahooChartParser.ReadCurrency(YahooJson));
    }

    [Fact]
    public void YahooParser_SkipsNullCloses()
    {
        // Tatil günlerinde kapanış null gelir; sıfır fiyat arşive
        // girerse trend ve eşik uyarısı bozulur.
        const string json = """
            {
              "chart": {
                "result": [
                  {
                    "meta": { "currency": "USD" },
                    "timestamp": [1785729600, 1785816000],
                    "indicators": { "quote": [ { "close": [null, 6.61] } ] }
                  }
                ]
              }
            }
            """;

        var quotes = YahooChartParser.Parse(json);

        Assert.Single(quotes);
        Assert.Equal(6.61m, quotes[0].Price);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bu json değil")]
    [InlineData("{\"chart\":{\"result\":[]}}")]
    [InlineData("{\"chart\":{\"error\":\"Not Found\"}}")]
    public void YahooParser_InvalidPayload_ReturnsEmpty(string json)
    {
        Assert.Empty(YahooChartParser.Parse(json));
    }

    [Fact]
    public void PoundToTonConversion_MatchesKnownValue()
    {
        // 6,721 USD/lb x 2204,6226 = 14.817,27 USD/ton
        var usdPerTon = decimal.Round(
            6.721m * YahooComexCopperSource.PoundsPerMetricTon, 2);

        Assert.Equal(14_817.27m, usdPerTon);
    }

    [Fact]
    public void LmeParser_WrongUnit_IsRejectedInsteadOfStored()
    {
        // Değer USD/lb olduğu hâlde ton diye beyan edilirse sonuç
        // akla yatkın bandın çok altında kalır; sessizce kaydedilmemeli.
        const string json = """
            {"success":true,"rates":{"2026-08-05":{"USDLME-XCU":4.15}}}
            """;

        var result = MetalPriceApiLmeSource.ParseTimeframe(json, "ton");

        Assert.Empty(result.Quotes);
        Assert.Contains("METAL_API_UNIT", result.Error);
    }

    [Fact]
    public void LmeParser_DeclaredPoundUnit_IsConverted()
    {
        const string json = """
            {"success":true,"rates":{"2026-08-05":{"USDLME-XCU":4.15}}}
            """;

        var result = MetalPriceApiLmeSource.ParseTimeframe(json, "lb");

        var quote = Assert.Single(result.Quotes);
        Assert.Null(result.Error);
        Assert.Equal(
            decimal.Round(4.15m * YahooComexCopperSource.PoundsPerMetricTon, 2),
            quote.Price);
    }

    [Fact]
    public void LmeParser_ApiError_IsSurfaced()
    {
        const string json = """
            {"success":false,"error":{"statusCode":102,"message":"invalid access key"}}
            """;

        var result = MetalPriceApiLmeSource.ParseTimeframe(json, "ton");

        Assert.Empty(result.Quotes);
        Assert.Contains("invalid access key", result.Error);
    }

    [Fact]
    public async Task Refresh_ComputesLiraUsingSameDayRate()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var day = new DateTime(2017, 4, 10, 0, 0, 0, DateTimeKind.Utc);
        await SetRateAsync(db, day, 3.6500m);

        var service = CreateService(
            db, new FakeSource([new CommodityQuote(day, 10_000m)]));

        var result = await service.RefreshAsync(30);

        Assert.Equal(1, result.StoredDays);

        var stored = await db.CommodityPrices.AsNoTracking()
            .SingleAsync(x => x.PriceDate == day);

        Assert.Equal(10_000m, stored.PriceUsdPerTon);
        Assert.Equal(3.6500m, stored.UsdRate);
        Assert.Equal(36_500m, stored.PriceTryPerTon);
    }

    [Fact]
    public async Task Refresh_WithoutRate_LeavesLiraEmptyInsteadOfGuessing()
    {
        // Kur yoksa bugünkü kurla geçmiş fiyat çarpılmaz: o sayı ne
        // emtia ne kur hareketini gösterir.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var day = new DateTime(2001, 6, 12, 0, 0, 0, DateTimeKind.Utc);

        var service = CreateService(
            db, new FakeSource([new CommodityQuote(day, 9_500m)]));

        await service.RefreshAsync(30);

        var stored = await db.CommodityPrices.AsNoTracking()
            .SingleAsync(x => x.PriceDate == day);

        Assert.Equal(9_500m, stored.PriceUsdPerTon);
        Assert.Null(stored.UsdRate);
        Assert.Null(stored.PriceTryPerTon);
    }

    [Fact]
    public async Task Refresh_SameDayPriceMoves_UpdatesInsteadOfDuplicating()
    {
        // Borsa kapanmadan çekilen bar gün içinde değişir; arşivde
        // sabahın fiyatı donmamalı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var day = new DateTime(2016, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        await SetRateAsync(db, day, 2.9800m);

        var first = CreateService(db, new FakeSource([new CommodityQuote(day, 8_000m)]));
        await first.RefreshAsync(30);

        var second = CreateService(db, new FakeSource([new CommodityQuote(day, 8_150m)]));
        var result = await second.RefreshAsync(30);

        Assert.Equal(0, result.StoredDays);
        Assert.Equal(1, result.UpdatedDays);

        var rows = await db.CommodityPrices.AsNoTracking()
            .Where(x => x.PriceDate == day)
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal(8_150m, rows[0].PriceUsdPerTon);
        Assert.Equal(decimal.Round(8_150m * 2.98m, 2), rows[0].PriceTryPerTon);
    }

    [Fact]
    public async Task Refresh_SourceDown_KeepsArchiveAndReportsError()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var day = new DateTime(2015, 3, 11, 0, 0, 0, DateTimeKind.Utc);
        await SetRateAsync(db, day, 2.6100m);

        await CreateService(db, new FakeSource([new CommodityQuote(day, 5_900m)]))
            .RefreshAsync(30);

        var down = CreateService(db, new FakeSource([], "COMEX bakır: bağlantı yok."));
        var result = await down.RefreshAsync(30);

        Assert.Equal(0, result.StoredDays);
        Assert.Single(result.Errors);

        // Arşivdeki fiyat yerinde durmalı.
        Assert.True(await db.CommodityPrices.AnyAsync(
            x => x.PriceDate == day && x.PriceUsdPerTon == 5_900m));
    }

    [Fact]
    public async Task Summary_SeparatesUsdAndLiraChange()
    {
        // TL değişimi hem bakırı hem kuru içerir; ikisini karıştırmak
        // "bakır mı pahalandı, lira mı değer kaybetti" sorusunu
        // cevapsız bırakır.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Bu testin kendine ait günleri; başka testlerin yazdığı
        // tarihlerle (bugün ve dün) çakışmaması gerekiyor.
        var start = DateTime.UtcNow.Date.AddDays(-25);
        var end = DateTime.UtcNow.Date.AddDays(-20);

        // Var olan satırları temizle: özet penceresi son 30 günü kapsıyor.
        var window = DateTime.UtcNow.Date.AddDays(-30);
        var existing = await db.CommodityPrices
            .Where(x => x.PriceDate >= window)
            .ToListAsync();
        db.CommodityPrices.RemoveRange(existing);
        await db.SaveChangesAsync();

        await SetRateAsync(db, start, 40m);
        await SetRateAsync(db, end, 44m);

        var service = CreateService(db, new FakeSource(
        [
            new CommodityQuote(DateTime.SpecifyKind(start, DateTimeKind.Utc), 10_000m),
            new CommodityQuote(DateTime.SpecifyKind(end, DateTimeKind.Utc), 10_500m)
        ]));

        await service.RefreshAsync(30);

        var summary = await service.GetSummaryAsync(Commodity.Copper, 30);

        // Bakır USD bazında %5, TL bazında (10.500x44)/(10.000x40) − 1 = %15,5
        Assert.Equal(5m, summary.ChangePercentUsd);
        Assert.Equal(15.50m, summary.ChangePercentTry);
        Assert.Equal(10_500m, summary.LatestUsdPerTon);
        Assert.False(summary.IsLme);
        Assert.Contains("LME değil", summary.SourceLabel);
    }

    [Fact]
    public async Task Summary_EmptyArchive_ReturnsNullsNotZero()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var window = DateTime.UtcNow.Date.AddDays(-400);
        var existing = await db.CommodityPrices
            .Where(x => x.PriceDate >= window)
            .ToListAsync();
        db.CommodityPrices.RemoveRange(existing);
        await db.SaveChangesAsync();

        var service = CreateService(db, new FakeSource([]));
        var summary = await service.GetSummaryAsync(Commodity.Copper, 30);

        Assert.Null(summary.LatestUsdPerTon);
        Assert.Null(summary.ChangePercentUsd);
        Assert.True(summary.IsStale);
        Assert.NotNull(summary.Warning);
    }
}
