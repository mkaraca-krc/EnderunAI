using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Market;
using EnderunAI.Api.Services.Market;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Alım fırsatı / maliyet riski eşiği (E4).
///
/// Geçiş mantığının kendisi <see cref="CommodityThresholdCrossingTests"/>
/// içinde veritabanısız test ediliyor; buradaki güvence kalıcılık
/// tarafı: eşik doğrulaması, tetiklenmelerin İDEMPOTENT yazılması ve
/// "görüldü" akışı.
///
/// ARŞİV ORTAK: commodity_prices şirketten bağımsız TEK bir tablodur ve
/// trend penceresi bugüne göre hesaplanır. Bu yüzden testler kendi
/// serisini kurmadan önce penceredeki bakır kayıtlarını temizler;
/// aksi hâlde başka paketlerin seed'i seriye karışır ve geçiş sayısı
/// rastgeleleşir. Koleksiyon içindeki testler seri koştuğu için bu
/// temizlik güvenlidir.
/// </summary>
[Collection("Integration")]
public sealed class CommodityAlertTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Bakır arşivini temizler ve verilen fiyat serisini bugünden
    /// geriye doğru günlük olarak yazar (son eleman = bugün).
    /// </summary>
    private async Task SeedSeriesAsync(params decimal[] pricesOldestFirst)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.CommodityPrices
            .Where(x => x.Commodity == Commodity.Copper)
            .ToListAsync();

        db.CommodityPrices.RemoveRange(existing);
        await db.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;

        for (var i = 0; i < pricesOldestFirst.Length; i++)
        {
            var offset = pricesOldestFirst.Length - 1 - i;

            db.CommodityPrices.Add(new CommodityPrice
            {
                Commodity = Commodity.Copper,
                SourceKind = CommodityPriceSourceKind.Comex,
                SourceSymbol = "HG=F",
                PriceDate = DateTime.SpecifyKind(
                    today.AddDays(-offset), DateTimeKind.Utc),
                PriceUsdPerTon = pricesOldestFirst[i],
                UsdRate = 30m,
                PriceTryPerTon = pricesOldestFirst[i] * 30m
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreateCompanyAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        return company.Id;
    }

    /// <summary>
    /// Eşiği kaydedip değerlendirdiğimizde geçiş tetiklenme olarak
    /// yazılmalı ve bekleyen uyarı olarak dönmeli.
    /// </summary>
    [Fact]
    public async Task SavingThreshold_EvaluatesImmediately()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // 9.500 → 9.200 → 8.800: son gün 9.000 eşiğinin altına iniyor
        await SeedSeriesAsync(9_500m, 9_200m, 8_800m);

        var response = await client.PutAsJsonAsync(
            "/api/market/commodities/copper/alert",
            new
            {
                companyId,
                buyBelowUsdPerTon = 9_000m,
                alertAboveUsdPerTon = 11_000m,
                isEnabled = true,
                notes = "Test eşiği"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(9_000m, status.GetProperty("buyBelowUsdPerTon").GetDecimal());
        Assert.Equal(8_800m, status.GetProperty("latestPriceUsdPerTon").GetDecimal());

        // Şu an alım bölgesindeyiz
        Assert.Equal(
            (int)CommodityAlertDirection.BuyOpportunity,
            status.GetProperty("currentState").GetInt32());

        var pending = status.GetProperty("pendingTriggers").EnumerateArray().ToList();
        var trigger = Assert.Single(pending);

        Assert.Equal(8_800m, trigger.GetProperty("priceUsdPerTon").GetDecimal());
        Assert.Equal(9_000m, trigger.GetProperty("thresholdUsdPerTon").GetDecimal());
    }

    /// <summary>
    /// BU PAKETİN ASIL GÜVENCESİ: değerlendirme birden fazla kez
    /// koşsa da aynı geçiş ikinci kez yazılmamalı. Gecelik iş her gün
    /// çalışıyor; idempotens olmasa uyarılar her turda çoğalırdı.
    /// </summary>
    [Fact]
    public async Task Evaluate_IsIdempotent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        await SeedSeriesAsync(9_500m, 9_200m, 8_800m);

        using var scope = fixture.Factory.Services.CreateScope();
        var alerts = scope.ServiceProvider.GetRequiredService<CommodityAlertService>();

        await alerts.SaveThresholdAsync(
            companyId, Commodity.Copper, 9_000m, null, true, null, default);

        var first = await alerts.EvaluateAsync(companyId, Commodity.Copper, default);
        var second = await alerts.EvaluateAsync(companyId, Commodity.Copper, default);
        var third = await alerts.EvaluateAsync(companyId, Commodity.Copper, default);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(0, third);

        var status = await alerts.GetStatusAsync(companyId, Commodity.Copper, default);
        Assert.Single(status.PendingTriggers);
    }

    /// <summary>
    /// Görüldü işaretlenen uyarı bekleyenlerden düşmeli — brifing
    /// aynı maddeyi her sabah tekrarlamasın.
    /// </summary>
    [Fact]
    public async Task Acknowledge_RemovesFromPending()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await SeedSeriesAsync(9_500m, 8_800m);

        await client.PutAsJsonAsync(
            "/api/market/commodities/copper/alert",
            new
            {
                companyId,
                buyBelowUsdPerTon = 9_000m,
                alertAboveUsdPerTon = (decimal?)null,
                isEnabled = true,
                notes = (string?)null
            });

        var before = await client.GetFromJsonAsync<JsonElement>(
            $"/api/market/commodities/copper/alert?companyId={companyId}");

        var triggerId = before.GetProperty("pendingTriggers").EnumerateArray()
            .Single().GetProperty("id").GetGuid();

        var ack = await client.PostAsync(
            $"/api/market/commodities/alerts/{triggerId}/acknowledge", null);
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);

        var after = await client.GetFromJsonAsync<JsonElement>(
            $"/api/market/commodities/copper/alert?companyId={companyId}");

        Assert.Empty(after.GetProperty("pendingTriggers").EnumerateArray());
    }

    /// <summary>
    /// Alım eşiği risk eşiğinden büyükse iki uyarı aynı anda tetiklenir
    /// ve hiçbiri anlam taşımaz; kayıt reddedilmeli.
    /// </summary>
    [Fact]
    public async Task InvertedThresholds_AreRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync(
            "/api/market/commodities/copper/alert",
            new
            {
                companyId,
                buyBelowUsdPerTon = 11_000m,
                alertAboveUsdPerTon = 9_000m,
                isEnabled = true,
                notes = (string?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Eşik kapalıysa değerlendirme hiçbir şey yazmamalı — kullanıcı
    /// uyarıyı bilinçli olarak susturmuştur.
    /// </summary>
    [Fact]
    public async Task DisabledThreshold_ProducesNoTriggers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        await SeedSeriesAsync(9_500m, 8_800m);

        using var scope = fixture.Factory.Services.CreateScope();
        var alerts = scope.ServiceProvider.GetRequiredService<CommodityAlertService>();

        await alerts.SaveThresholdAsync(
            companyId, Commodity.Copper, 9_000m, null,
            isEnabled: false, notes: null, cancellationToken: default);

        Assert.Equal(0, await alerts.EvaluateAsync(companyId, Commodity.Copper, default));

        var status = await alerts.GetStatusAsync(companyId, Commodity.Copper, default);
        Assert.False(status.IsEnabled);
        Assert.Null(status.CurrentState);
    }

    /// <summary>
    /// Eşik hiç tanımlanmamışsa varsayılan bir eşik uydurulmamalı:
    /// "bizim için ucuz"un ne olduğunu yalnızca şirket bilir.
    /// </summary>
    [Fact]
    public async Task WithoutThreshold_StatusIsEmptyAndNoSignals()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        await SeedSeriesAsync(9_500m, 100m);

        using var scope = fixture.Factory.Services.CreateScope();
        var alerts = scope.ServiceProvider.GetRequiredService<CommodityAlertService>();

        var status = await alerts.GetStatusAsync(companyId, Commodity.Copper, default);

        Assert.Null(status.BuyBelowUsdPerTon);
        Assert.False(status.IsEnabled);
        Assert.Null(status.CurrentState);
        Assert.Empty(status.PendingTriggers);

        Assert.Equal(0, await alerts.EvaluateAsync(companyId, Commodity.Copper, default));
    }
}
