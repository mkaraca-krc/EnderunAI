using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Stok rezervasyonu sökümünün DAVRANIŞ-NÖTR olduğunun kanıtı.
///
/// Rezervasyon akışı hiç yazılmamıştı: `new StockReservation` kod
/// tabanında yoktu, denetleyicide yalnız GET vardı, canlıda sıfır
/// kayıt ve sıfır rezerve miktar vardı. "Kullanılabilir stok" her
/// zaman `Quantity - 0` yani toplam stoğun kendisiydi.
///
/// Bu testler sökümden SONRA kullanılabilir stok rakamının, kritik
/// stok kümesinin ve stok yeterlilik kontrollerinin aynı sonucu
/// verdiğini sabitler.
/// </summary>
[Collection("Integration")]
public sealed class StockReservationRemovalTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid WarehouseId, Guid ItemId, Guid OtherItemId);

    /// <summary>
    /// 100 birim stoklu bir malzeme (minimum 10 → kritik değil) ve
    /// 5 birim stoklu ikinci bir malzeme (minimum 10 → kritik).
    /// </summary>
    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(
            db, suffix);

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DP-{suffix}",
            Name = $"Test Depo {suffix}"
        };

        var item = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"MLZ-{suffix}",
            Name = "Bol Stoklu Malzeme",
            Unit = "Adet",
            MinimumStock = 10m
        };

        var otherItem = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"KRT-{suffix}",
            Name = "Kritik Malzeme",
            Unit = "Adet",
            MinimumStock = 10m
        };

        db.Warehouses.Add(warehouse);
        db.InventoryItems.AddRange(item, otherItem);
        await db.SaveChangesAsync();

        db.WarehouseStocks.AddRange(
            new WarehouseStock
            {
                WarehouseId = warehouse.Id,
                InventoryItemId = item.Id,
                Quantity = 100m
            },
            new WarehouseStock
            {
                WarehouseId = warehouse.Id,
                InventoryItemId = otherItem.Id,
                Quantity = 5m
            });

        await db.SaveChangesAsync();

        return new Context(company.Id, warehouse.Id, item.Id, otherItem.Id);
    }

    private Task<HttpClient> ClientAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <summary>
    /// Depo stok listesinde miktar toplam stoğun kendisi ve artık
    /// ayrı bir "rezerve" kovası yok.
    /// </summary>
    [Fact]
    public async Task WarehouseStocks_ReportQuantityWithoutReservedBucket()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientAsync();

        var response = await client.GetAsync(
            $"/api/inventory/warehouses/{context.WarehouseId}/stocks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("reservedQuantity", raw);
        Assert.DoesNotContain("availableQuantity", raw);

        var rows = JsonDocument.Parse(raw).RootElement;

        var row = rows.EnumerateArray().Single(
            x => x.GetProperty("inventoryItemId").GetGuid() == context.ItemId);

        Assert.Equal(100m, row.GetProperty("quantity").GetDecimal());
        Assert.False(row.GetProperty("isCritical").GetBoolean());
    }

    /// <summary>
    /// Kritik stok kümesi değişmiyor: rezerve miktar her zaman sıfır
    /// olduğu için eşik karşılaştırması zaten toplam stok üzerinden
    /// yürüyordu.
    /// </summary>
    [Fact]
    public async Task CriticalStockAlerts_MatchTotalQuantityThreshold()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientAsync();

        var response = await client.GetAsync(
            $"/api/inventory/critical-stock-alerts?companyId={context.CompanyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var alerts = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        var ids = alerts.EnumerateArray()
            .Select(x => x.GetProperty("inventoryItemId").GetGuid())
            .ToList();

        // 5 < 10 → kritik. 100 > 10 → değil.
        Assert.Contains(context.OtherItemId, ids);
        Assert.DoesNotContain(context.ItemId, ids);
    }

    /// <summary>
    /// Malzeme kartı toplamı stoğun kendisi; ayrı bir "kullanılabilir"
    /// alanı kalmadı çünkü ikisi hep aynı sayıydı.
    /// </summary>
    [Fact]
    public async Task InventoryItemDetail_HasNoSeparateAvailableStock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientAsync();

        var raw = await (await client.GetAsync(
            $"/api/inventory/items/{context.ItemId}")).Content.ReadAsStringAsync();

        var payload = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(100m, payload.GetProperty("totalStock").GetDecimal());
        Assert.DoesNotContain("availableStock", raw);
        Assert.DoesNotContain("reservedQuantity", raw);
    }

    /// <summary>
    /// Stok yeterlilik kontrolü aynı kararı veriyor: 100 birimden
    /// 120 çıkışa izin verilmiyor, 60 çıkışa veriliyor.
    /// </summary>
    [Fact]
    public async Task StockIssue_StillRejectsMoreThanQuantity()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientAsync();

        var tooMuch = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = context.WarehouseId,
            inventoryItemId = context.ItemId,
            quantity = 120m,
            movementDate = DateTime.UtcNow
        });

        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);

        var allowed = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = context.WarehouseId,
            inventoryItemId = context.ItemId,
            quantity = 60m,
            movementDate = DateTime.UtcNow
        });

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stock = await db.WarehouseStocks.AsNoTracking()
            .SingleAsync(x => x.WarehouseId == context.WarehouseId &&
                              x.InventoryItemId == context.ItemId);

        Assert.Equal(40m, stock.Quantity);
    }

    /// <summary>
    /// Rezervasyon ucu artık YOK. 404 bekleniyor: 401 gelseydi uç
    /// duruyor ama izin kapalı demek olurdu.
    /// </summary>
    [Fact]
    public async Task ReservationEndpoint_IsGone()
    {
        var client = await ClientAsync();

        var response = await client.GetAsync("/api/stock-reservations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
