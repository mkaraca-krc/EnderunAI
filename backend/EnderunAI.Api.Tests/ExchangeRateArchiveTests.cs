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
/// Kur arşivinin okuma ve tazeleme davranışı.
///
/// Buradaki asıl güvence "kur uydurma yok" kuralı: arşivde kayıt yoksa
/// servis null döner ve dövizli işlem reddedilir. Yaklaşık bir kur
/// üretmek, yanlış kurla kesilmiş bir fişten geri dönmek zorunda
/// kalmak demektir.
/// </summary>
[Collection("Integration")]
public sealed class ExchangeRateArchiveTests(DatabaseFixture fixture)
{
    /// <summary>
    /// TCMB istemcisinin yerine geçen sahte kaynak. Ağ yok: testler
    /// TCMB ayakta olmasa da aynı sonucu verir.
    /// </summary>
    private sealed class FakeTcmbClient : ITcmbRateClient
    {
        private readonly Dictionary<DateTime, TcmbBulletin> bulletins = new();
        private readonly HashSet<DateTime> failures = [];

        public List<DateTime> RequestedDates { get; } = [];

        public void AddBulletin(DateTime date, decimal usdBuying, decimal eurBuying)
        {
            bulletins[date.Date] = new TcmbBulletin(
                DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
                $"TEST/{date:yyyyMMdd}",
                [
                    new TcmbRateRow("USD", 1, usdBuying, usdBuying + 0.1m, null, null),
                    new TcmbRateRow("EUR", 1, eurBuying, eurBuying + 0.1m, null, null),
                    // İzlenmeyen para birimi: arşive girmemeli.
                    new TcmbRateRow("AUD", 1, 33.1m, 33.2m, null, null)
                ]);
        }

        public void AddFailure(DateTime date) => failures.Add(date.Date);

        public Task<(TcmbBulletin? Bulletin, string? Error)> GetBulletinAsync(
            DateTime date, CancellationToken cancellationToken = default)
        {
            RequestedDates.Add(date.Date);

            if (failures.Contains(date.Date))
                return Task.FromResult<(TcmbBulletin?, string?)>((null, "TCMB erişilemedi."));

            return Task.FromResult<(TcmbBulletin?, string?)>(
                bulletins.TryGetValue(date.Date, out var bulletin)
                    ? (bulletin, null)
                    : (null, null));
        }
    }

    private static ExchangeRateService CreateService(AppDbContext db, ITcmbRateClient client)
        => new(db, client, NullLogger<ExchangeRateService>.Instance);

    private static async Task SeedRateAsync(
        AppDbContext db, DateTime date, string currency, decimal buying)
    {
        db.ExchangeRates.Add(new ExchangeRate
        {
            RateDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            CurrencyCode = currency,
            Unit = 1,
            ForexBuying = buying,
            ForexSelling = buying + 0.1m,
            Source = "TCMB"
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_TurkishLira_AlwaysReturnsOne()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = CreateService(db, new FakeTcmbClient());

        var lookup = await service.GetAsync("TRY", new DateTime(2019, 1, 1));

        Assert.NotNull(lookup);
        Assert.Equal(1m, lookup.ForexBuying);
        Assert.Equal(0, lookup.DaysBack);
    }

    [Fact]
    public async Task Get_WithoutArchive_ReturnsNull_NeverInventsRate()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = CreateService(db, new FakeTcmbClient());

        // Hiç kur olmayan bir para birimi: yaklaşık değer üretilmemeli.
        var lookup = await service.GetAsync("CHF", new DateTime(2019, 3, 15));

        Assert.Null(lookup);
    }

    [Fact]
    public async Task Get_OnWeekend_FallsBackToPreviousBulletin()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = CreateService(db, new FakeTcmbClient());

        // 2019-03-08 Cuma bülteni var; 09-10 hafta sonu, 11 Pazartesi yok.
        var friday = new DateTime(2019, 3, 8);
        await SeedRateAsync(db, friday, "GBP", 7.1234m);

        var sunday = await service.GetAsync("GBP", new DateTime(2019, 3, 10));

        Assert.NotNull(sunday);
        Assert.Equal(7.1234m, sunday.ForexBuying);
        Assert.Equal(friday.Date, sunday.EffectiveDate.Date);
        Assert.Equal(2, sunday.DaysBack);
    }

    [Fact]
    public async Task Get_BeforeFirstBulletin_ReturnsNull()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = CreateService(db, new FakeTcmbClient());

        await SeedRateAsync(db, new DateTime(2019, 5, 20), "GBP", 7.5m);

        // İleriye doğru tahmin yapılmaz: arşivdeki ilk günden önceki bir
        // tarih için kur yoktur.
        var lookup = await service.GetAsync("GBP", new DateTime(2019, 5, 1));

        Assert.Null(lookup);
    }

    [Fact]
    public async Task Refresh_StoresOnlyTrackedCurrencies_AndSkipsWeekends()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var client = new FakeTcmbClient();
        // 2018-06-04 Pazartesi ... 06-08 Cuma; 06-09/10 hafta sonu.
        client.AddBulletin(new DateTime(2018, 6, 4), 4.6512m, 5.4321m);
        client.AddBulletin(new DateTime(2018, 6, 5), 4.6789m, 5.4567m);

        var service = CreateService(db, client);

        var result = await service.RefreshAsync(
            new DateTime(2018, 6, 4), new DateTime(2018, 6, 10));

        Assert.Equal(2, result.FetchedDays);
        Assert.Empty(result.Errors);

        // Hafta sonuna hiç istek atılmamalı.
        Assert.DoesNotContain(new DateTime(2018, 6, 9), client.RequestedDates);
        Assert.DoesNotContain(new DateTime(2018, 6, 10), client.RequestedDates);

        var stored = await db.ExchangeRates
            .AsNoTracking()
            .Where(x => x.RateDate >= new DateTime(2018, 6, 4, 0, 0, 0, DateTimeKind.Utc)
                        && x.RateDate <= new DateTime(2018, 6, 10, 0, 0, 0, DateTimeKind.Utc))
            .ToListAsync();

        Assert.Equal(4, stored.Count);
        Assert.All(stored, x => Assert.Contains(x.CurrencyCode, new[] { "USD", "EUR" }));
        Assert.DoesNotContain(stored, x => x.CurrencyCode == "AUD");

        var usd = stored.Single(
            x => x.CurrencyCode == "USD"
                 && x.RateDate == new DateTime(2018, 6, 4, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(4.6512m, usd.ForexBuying);
    }

    [Fact]
    public async Task Refresh_RunTwice_DoesNotDuplicate()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var client = new FakeTcmbClient();
        client.AddBulletin(new DateTime(2018, 7, 2), 4.55m, 5.31m);

        var service = CreateService(db, client);

        var first = await service.RefreshAsync(
            new DateTime(2018, 7, 2), new DateTime(2018, 7, 2));
        var second = await service.RefreshAsync(
            new DateTime(2018, 7, 2), new DateTime(2018, 7, 2));

        Assert.Equal(1, first.FetchedDays);
        Assert.Equal(0, second.FetchedDays);
        Assert.Equal(1, second.AlreadyPresentDays);

        var count = await db.ExchangeRates
            .CountAsync(
                x => x.RateDate == new DateTime(2018, 7, 2, 0, 0, 0, DateTimeKind.Utc)
                     && x.CurrencyCode == "USD");

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Refresh_WhenSourceFails_ReportsErrorWithoutThrowing()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var client = new FakeTcmbClient();
        client.AddFailure(new DateTime(2018, 8, 6));
        client.AddBulletin(new DateTime(2018, 8, 7), 4.71m, 5.45m);

        var service = CreateService(db, client);

        var result = await service.RefreshAsync(
            new DateTime(2018, 8, 6), new DateTime(2018, 8, 7));

        // Bir gün patlarsa iş durmaz, kalan günler çekilir.
        Assert.Equal(1, result.FetchedDays);
        Assert.Single(result.Errors);

        Assert.False(await db.ExchangeRates.AnyAsync(
            x => x.RateDate == new DateTime(2018, 8, 6, 0, 0, 0, DateTimeKind.Utc)));
        Assert.True(await db.ExchangeRates.AnyAsync(
            x => x.RateDate == new DateTime(2018, 8, 7, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Freshness_WhenArchiveIsOld_WarnsInsteadOfPretendingCurrent()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = CreateService(db, new FakeTcmbClient());

        // Arşivdeki en yeni USD kaydı bugünden çok eskiyse uyarı çıkmalı;
        // testler paylaşılan veritabanında çalıştığı için bu koşul ancak
        // gerçekten güncel kur yokken sağlanır.
        var freshness = await service.GetFreshnessAsync();

        if (freshness.LatestRateDate is null)
        {
            Assert.True(freshness.IsStale);
            Assert.NotNull(freshness.Warning);
            return;
        }

        var days = (DateTime.UtcNow.Date - freshness.LatestRateDate.Value.Date).Days;
        Assert.Equal(days, freshness.DaysSinceLatest);
        Assert.Equal(days >= 4, freshness.IsStale);
        Assert.Equal(freshness.IsStale, freshness.Warning is not null);
    }
}
