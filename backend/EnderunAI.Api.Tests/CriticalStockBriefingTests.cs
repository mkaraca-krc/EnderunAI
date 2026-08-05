using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Kritik stok brifingi.
///
/// İki hata düzeltildi: sayım <c>Take(5)</c>'ten SONRA yapıldığı için
/// 30 malzeme kritikken de "5 malzeme" yazıyordu, ve bağlantı var
/// olmayan bir sayfaya (/stok) gidiyordu.
/// </summary>
[Collection("Integration")]
public sealed class CriticalStockBriefingTests(DatabaseFixture fixture)
{
    /// <summary>Hepsi minimumun altında yedi kalem kurar.</summary>
    private async Task<Guid> SeedCriticalItemsAsync(string suffix, int count)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DP-{suffix}",
            Name = "Test Deposu",
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(warehouse);

        var items = new List<InventoryItem>();

        for (var index = 0; index < count; index++)
        {
            var item = new InventoryItem
            {
                CompanyId = company.Id,
                Code = $"KRT{index:00}-{suffix}",
                Name = $"Kritik Malzeme {index:00} {suffix}",
                Unit = "Adet",
                MinimumStock = 100m
            };

            items.Add(item);
        }

        db.InventoryItems.AddRange(items);
        await db.SaveChangesAsync();

        foreach (var item in items)
        {
            db.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = warehouse.Id,
                InventoryItemId = item.Id,
                Quantity = 1m
            });
        }

        await db.SaveChangesAsync();

        return company.Id;
    }

    [Fact]
    public async Task Briefing_ReportsTotalCountNotJustFirstFive()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await SeedCriticalItemsAsync(suffix, 7);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var briefing = await client.GetFromJsonAsync<JsonElement>("/api/hizir/briefing");

        var item = briefing.GetProperty("items").EnumerateArray()
            .Single(x => (x.GetProperty("title").GetString() ?? "")
                .Contains("minimum stok seviyesinin altında"));

        var title = item.GetProperty("title").GetString()!;

        // En az 7 kritik kalem var; "5 malzeme" yazamaz.
        Assert.DoesNotContain("5 malzeme minimum", title);

        var count = int.Parse(title.Split(' ')[0]);
        Assert.True(count >= 7, $"Kritik kalem sayısı en az 7 olmalıydı, {count} yazdı.");

        // Ad listesi yine 5 ile sınırlı ama kalanın varlığı söyleniyor.
        var detail = item.GetProperty("detail").GetString() ?? "";
        Assert.Contains("kalem daha", detail);
    }

    [Fact]
    public async Task Briefing_LinksToExistingStockPage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await SeedCriticalItemsAsync(suffix, 2);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var briefing = await client.GetFromJsonAsync<JsonElement>("/api/hizir/briefing");

        var item = briefing.GetProperty("items").EnumerateArray()
            .Single(x => (x.GetProperty("title").GetString() ?? "")
                .Contains("minimum stok seviyesinin altında"));

        // /stok diye bir sayfa yok; bağlantı 404 veriyordu.
        Assert.Equal("/depo-stok", item.GetProperty("targetPath").GetString());
    }
}
