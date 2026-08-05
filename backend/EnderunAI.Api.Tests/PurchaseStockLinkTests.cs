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
/// Satın alma talebinde seçilen stok kartının kaydedilmesi.
///
/// Malzeme talebi ekranı kullanıcıya stok kartı seçtiriyor ve seçimi
/// zorunlu tutuyordu, ama <c>PurchaseRequestItem</c> modelinde
/// <c>InventoryItemId</c> özelliği yoktu (veritabanında kolon vardı) —
/// seçim sessizce kayboluyordu. Bu testler bağın gerçekten yazıldığını
/// ve talep katalog dışı malzeme için hâlâ açılabildiğini sabitliyor.
/// </summary>
[Collection("Integration")]
public sealed class PurchaseStockLinkTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId, Guid InventoryItemId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var item = new InventoryItem
        {
            CompanyId = project.CompanyId,
            Code = $"MLZ-{suffix}",
            Name = $"Test Malzeme {suffix}",
            Unit = "Adet"
        };

        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        return new Context(project.CompanyId, project.Id, item.Id);
    }

    private static object BuildRequest(Context context, Guid? inventoryItemId) => new
    {
        companyId = context.CompanyId,
        projectId = context.ProjectId,
        requestType = 1,
        requestDate = DateTime.UtcNow,
        neededByDate = (DateTime?)null,
        requestedByName = "Test Talep Eden",
        description = "Stok kartı bağı testi",
        priority = 1,
        items = new[]
        {
            new
            {
                inventoryItemId,
                materialDescription = "Test kalemi",
                quantity = 5m,
                unit = "Adet",
                requestedDeliveryDate = (DateTime?)null,
                notes = (string?)null
            }
        }
    };

    [Fact]
    public async Task PurchaseRequest_PersistsSelectedInventoryItem()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            BuildRequest(context, context.InventoryItemId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var line = await db.PurchaseRequestItems
            .AsNoTracking()
            .Include(x => x.PurchaseRequest)
            .SingleAsync(x => x.PurchaseRequest.CompanyId == context.CompanyId);

        Assert.Equal(context.InventoryItemId, line.InventoryItemId);
    }

    [Fact]
    public async Task PurchaseRequest_DetailReturnsInventoryItemCodeAndName()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            BuildRequest(context, context.InventoryItemId));

        var payload = await created.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = payload.GetProperty("id").GetGuid();

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-requests/{requestId}");

        var line = detail.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(context.InventoryItemId,
            line.GetProperty("inventoryItemId").GetGuid());
        Assert.Contains($"MLZ-{suffix}".ToUpperInvariant(),
            line.GetProperty("inventoryItemCode").GetString()!.ToUpperInvariant());
        Assert.Contains("Test Malzeme",
            line.GetProperty("inventoryItemName").GetString()!);
    }

    /// <summary>
    /// Katalog dışı malzeme: kart seçilmeden de talep açılabilmeli.
    /// Zorunlu tutulsaydı, kartı henüz tanımlanmamış bir malzeme için
    /// talep hiç girilemez ve süreç kilitlenirdi.
    /// </summary>
    [Fact]
    public async Task PurchaseRequest_AllowsFreeTextWithoutInventoryItem()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            BuildRequest(context, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var line = await db.PurchaseRequestItems
            .AsNoTracking()
            .Include(x => x.PurchaseRequest)
            .SingleAsync(x => x.PurchaseRequest.CompanyId == context.CompanyId);

        Assert.Null(line.InventoryItemId);
        Assert.Equal("Test kalemi", line.MaterialDescription);
    }
}
