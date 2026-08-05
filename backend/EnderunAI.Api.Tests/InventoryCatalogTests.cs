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
/// Stok kartı kataloğu: yeni alanlar, süzgeçler ve stok değeri.
///
/// Stok değeri ağırlıklı ortalama maliyetten hesaplanır; son alış
/// fiyatından hesaplansaydı eski stok bugünkü fiyatla değerlenir ve
/// bilanço şişerdi. Bu ayrım burada sabitleniyor.
/// </summary>
[Collection("Integration")]
public sealed class InventoryCatalogTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid WarehouseAId, Guid WarehouseBId,
        Guid KabloId, Guid BoruId, Guid SupplierId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var supplier = new CurrentAccount
        {
            CompanyId = company.Id,
            Code = $"TED-{suffix}",
            Title = $"Tercihli Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(supplier);

        var warehouseA = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DPA-{suffix}",
            Name = "Merkez Depo",
            Type = WarehouseType.Central
        };

        var warehouseB = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DPB-{suffix}",
            Name = "Saha Deposu",
            Type = WarehouseType.Site
        };

        db.Warehouses.AddRange(warehouseA, warehouseB);

        // Kablo: 10 adet × 100 TRY = 1.000 TRY değer, minimum 5 (normal)
        var kablo = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"KBL-{suffix}",
            Name = "Enerji Kablosu",
            Category = "Elektrik",
            Unit = "Metre",
            MinimumStock = 5m,
            AverageUnitCost = 100m
        };

        // Boru: 2 adet × 50 TRY = 100 TRY değer, minimum 10 (kritik)
        var boru = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"BRU-{suffix}",
            Name = "Çelik Boru",
            Category = "Mekanik",
            Unit = "Metre",
            MinimumStock = 10m,
            AverageUnitCost = 50m
        };

        db.InventoryItems.AddRange(kablo, boru);
        await db.SaveChangesAsync();

        db.WarehouseStocks.AddRange(
            new WarehouseStock
            {
                WarehouseId = warehouseA.Id,
                InventoryItemId = kablo.Id,
                Quantity = 10m
            },
            new WarehouseStock
            {
                WarehouseId = warehouseB.Id,
                InventoryItemId = boru.Id,
                Quantity = 2m
            });

        await db.SaveChangesAsync();

        return new Context(
            company.Id, warehouseA.Id, warehouseB.Id,
            kablo.Id, boru.Id, supplier.Id);
    }

    [Fact]
    public async Task Items_ReportStockValueFromAverageCost()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var items = await client.GetFromJsonAsync<JsonElement>(
            $"/api/inventory/items?companyId={context.CompanyId}");

        var kablo = items.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == context.KabloId);

        Assert.Equal(10m, kablo.GetProperty("totalStock").GetDecimal());
        Assert.Equal(1_000m, kablo.GetProperty("stockValue").GetDecimal());
    }

    [Fact]
    public async Task Items_FilterByCategory()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var items = await client.GetFromJsonAsync<JsonElement>(
            $"/api/inventory/items?companyId={context.CompanyId}&category=Mekanik");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(context.BoruId,
            items.EnumerateArray().Single().GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Items_FilterByWarehouse()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var items = await client.GetFromJsonAsync<JsonElement>(
            $"/api/inventory/items?companyId={context.CompanyId}" +
            $"&warehouseId={context.WarehouseAId}");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(context.KabloId,
            items.EnumerateArray().Single().GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Items_FilterCriticalOnly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var items = await client.GetFromJsonAsync<JsonElement>(
            $"/api/inventory/items?companyId={context.CompanyId}&criticalOnly=true");

        // Boru 2 < 10 kritik; kablo 10 > 5 değil.
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(context.BoruId,
            items.EnumerateArray().Single().GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Categories_ReturnsDistinctValues()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var categories = await client.GetFromJsonAsync<List<string>>(
            $"/api/inventory/categories?companyId={context.CompanyId}");

        Assert.Equal(["Elektrik", "Mekanik"], categories);
    }

    [Fact]
    public async Task ItemDetail_IncludesWarehouseBreakdownAndNewFields()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var update = await client.PutAsJsonAsync(
            $"/api/inventory/items/{context.KabloId}",
            new
            {
                name = "Enerji Kablosu",
                category = "Elektrik",
                brand = (string?)null,
                model = (string?)null,
                unit = "Metre",
                barcode = (string?)null,
                minimumStock = 5m,
                maximumStock = (decimal?)null,
                type = 0,
                isActive = true,
                preferredSupplierCurrentAccountId = context.SupplierId,
                vatRate = 20m,
                description = "NYY 5x10 mm²"
            });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/inventory/items/{context.KabloId}");

        Assert.Equal(20m, detail.GetProperty("vatRate").GetDecimal());
        Assert.Equal("NYY 5x10 mm²", detail.GetProperty("description").GetString());
        Assert.Equal(context.SupplierId,
            detail.GetProperty("preferredSupplierCurrentAccountId").GetGuid());
        Assert.Contains("Tercihli Tedarikçi",
            detail.GetProperty("preferredSupplierTitle").GetString());

        Assert.Equal(1_000m, detail.GetProperty("stockValue").GetDecimal());

        var warehouses = detail.GetProperty("warehouses");
        Assert.Equal(1, warehouses.GetArrayLength());
        Assert.Equal(10m,
            warehouses.EnumerateArray().Single().GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task ItemUpdate_RejectsInvalidVatRate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync(
            $"/api/inventory/items/{context.KabloId}",
            new
            {
                name = "Enerji Kablosu",
                category = "Elektrik",
                brand = (string?)null,
                model = (string?)null,
                unit = "Metre",
                barcode = (string?)null,
                minimumStock = 5m,
                maximumStock = (decimal?)null,
                type = 0,
                isActive = true,
                vatRate = 120m
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Warehouse_CanBeCreatedAndCannotBeClosedWhileHoldingStock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var branchId = await db.Branches
            .Where(x => x.CompanyId == context.CompanyId)
            .Select(x => x.Id)
            .FirstAsync();

        var create = await client.PostAsJsonAsync("/api/warehouses", new
        {
            companyId = context.CompanyId,
            branchId,
            projectId = (Guid?)null,
            projectSiteId = (Guid?)null,
            code = $"YENI-{suffix}",
            name = "Yeni Depo",
            type = 0,
            address = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        // Stok tutan depo kapatılamaz: kapatılırsa o stok defterde
        // görünmeden kalırdı.
        var close = await client.PutAsJsonAsync(
            $"/api/warehouses/{context.WarehouseAId}",
            new
            {
                branchId,
                projectId = (Guid?)null,
                projectSiteId = (Guid?)null,
                name = "Merkez Depo",
                type = 0,
                address = (string?)null,
                isActive = false
            });

        Assert.Equal(HttpStatusCode.BadRequest, close.StatusCode);
        Assert.Contains("stok varken",
            await close.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Warehouse_RejectsDuplicateCode()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var branchId = await db.Branches
            .Where(x => x.CompanyId == context.CompanyId)
            .Select(x => x.Id)
            .FirstAsync();

        var response = await client.PostAsJsonAsync("/api/warehouses", new
        {
            companyId = context.CompanyId,
            branchId,
            projectId = (Guid?)null,
            projectSiteId = (Guid?)null,
            code = $"DPA-{suffix}",
            name = "Kopya Depo",
            type = 0,
            address = (string?)null
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
