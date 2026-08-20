using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using PurchaseOrderEntity = EnderunAI.Api.Models.PurchaseOrder.PurchaseOrder;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Mal kabulde red gerekçesi ve otomatik alış iadesi belgesi (S2).
///
/// Asıl güvenceler:
/// - Reddedilen/hasarlı miktar için GEREKÇE ZORUNLU. Gerekçesiz red
///   tedarikçiyle mutabakatta savunulamaz ve kalite geçmişini
///   "sebebi bilinmeyen redler"le doldurur.
/// - İade belgesi mal kabul kesinleşirken OTOMATİK doğar; elle açma
///   adımı unutulduğunda reddedilen mal kayıtsız kalırdı.
/// - Stoğa YALNIZ kabul edilen girer.
/// - Reddedilen miktar siparişte AÇIK kalır: tedarikçi eksiği yeniden
///   gönderebilmeli.
/// - Red ve hasar AYRI satır: farklı gerekçeler, kalite analizinde
///   ayrı sayılmalı.
/// </summary>
[Collection("Integration")]
public sealed class PurchaseReturnTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId,
        Guid ProjectId,
        Guid SupplierId,
        Guid ReceiptId,
        Guid ReceiptItemId,
        Guid OrderId,
        Guid InventoryItemId);

    /// <summary>
    /// 500 sipariş → 480 teslim edilmiş bir mal kabul taslağı kurar.
    /// Kabul/red/hasar dağılımı testte belirlenir.
    /// </summary>
    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        // Mal kabul artık muhasebe fişi kesiyor (S6b): stok hesapları
        // ve 379.01 GR/IR hesabı olmadan kesinleşemez.
        await TestDataFactory.EnsureStockAccountsAsync(db, project.CompanyId);

        var supplier = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TED-{suffix}",
            Title = $"Test Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(supplier);

        var branchId = await db.Branches
            .Where(x => x.CompanyId == project.CompanyId)
            .Select(x => x.Id)
            .FirstAsync();

        var inventoryItem = new InventoryItem
        {
            CompanyId = project.CompanyId,
            Code = $"STK-{suffix}",
            Name = "Çimento",
            Unit = "adet"
        };
        db.InventoryItems.Add(inventoryItem);

        var warehouse = new Warehouse
        {
            CompanyId = project.CompanyId,
            BranchId = branchId,
            Code = $"DEP-{suffix}",
            Name = "Test Depo",
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(warehouse);

        var purchaseRequest = new PurchaseRequest
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            RequestNumber = $"PR-{suffix}",
            RequestDate = DateTime.UtcNow.Date,
            RequestedByName = "Test",
            Priority = PurchaseRequestPriority.Normal,
            Status = PurchaseRequestStatus.Approved
        };
        db.PurchaseRequests.Add(purchaseRequest);
        await db.SaveChangesAsync();

        var rfq = new EnderunAI.Api.Models.Rfq.Rfq
        {
            CompanyId = project.CompanyId,
            PurchaseRequestId = purchaseRequest.Id,
            RfqNumber = $"RFQ-{suffix}",
            Title = "Test RFQ",
            IssueDate = DateTime.UtcNow.Date,
            Currency = "TRY"
        };
        db.Rfqs.Add(rfq);
        await db.SaveChangesAsync();

        var purchaseOrder = new PurchaseOrderEntity
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            RfqId = rfq.Id,
            SupplierCurrentAccountId = supplier.Id,
            OrderNumber = $"PO-{suffix}",
            OrderDate = DateTime.UtcNow.Date,
            Status = PurchaseOrderStatus.Approved,
            Currency = "TRY",
            ExchangeRate = 1m
        };

        var orderItem = new PurchaseOrderItem
        {
            LineNumber = 1,
            MaterialDescription = "Çimento",
            Quantity = 500m,
            Unit = "adet",
            UnitPrice = 10m,
            NetUnitPrice = 10m,
            TotalPrice = 5000m
        };

        purchaseOrder.Items.Add(orderItem);
        db.PurchaseOrders.Add(purchaseOrder);
        await db.SaveChangesAsync();

        var receipt = new GoodsReceipt
        {
            CompanyId = project.CompanyId,
            PurchaseOrderId = purchaseOrder.Id,
            WarehouseId = warehouse.Id,
            ReceiptNumber = $"GR-{suffix}",
            ReceiptDate = DateTime.UtcNow.Date,
            Status = GoodsReceiptStatus.Draft,
            ReceivedByName = "Depo Sorumlusu"
        };

        var receiptItem = new GoodsReceiptItem
        {
            PurchaseOrderItemId = orderItem.Id,
            InventoryItemId = inventoryItem.Id,
            LineNumber = 1,
            MaterialDescription = "Çimento",
            OrderedQuantity = 500m,
            DeliveredQuantity = 480m,
            AcceptedQuantity = 480m,
            Unit = "adet"
        };

        receipt.Items.Add(receiptItem);
        db.GoodsReceipts.Add(receipt);
        await db.SaveChangesAsync();

        return new Context(
            project.CompanyId, project.Id, supplier.Id,
            receipt.Id, receiptItem.Id, purchaseOrder.Id, inventoryItem.Id);
    }

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <summary>Kabul/red/hasar dağılımını taslağa yazar.</summary>
    private static async Task<HttpResponseMessage> SetQuantitiesAsync(
        HttpClient client,
        Context context,
        decimal accepted,
        decimal rejected,
        decimal damaged,
        string? reason) =>
        await client.PutAsJsonAsync(
            $"/api/goods-receipts/{context.ReceiptId}/draft",
            new
            {
                items = new[]
                {
                    new
                    {
                        id = context.ReceiptItemId,
                        inventoryItemId = context.InventoryItemId,
                        deliveredQuantity = 480m,
                        acceptedQuantity = accepted,
                        rejectedQuantity = rejected,
                        damagedQuantity = damaged,
                        lotNumber = (string?)null,
                        serialNumber = (string?)null,
                        productionDate = (DateTime?)null,
                        expiryDate = (DateTime?)null,
                        warrantyEndDate = (DateTime?)null,
                        shelfLocation = (string?)null,
                        notes = (string?)null,
                        rejectionReason = reason
                    }
                }
            });

    // ---------- Gerekçe zorunluluğu ----------

    /// <summary>
    /// Reddedilen miktar varsa gerekçesiz kesinleştirilemez.
    /// </summary>
    [Fact]
    public async Task Post_WithRejectionButNoReason_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        Assert.Equal(
            HttpStatusCode.OK,
            (await SetQuantitiesAsync(client, context, 470m, 10m, 0m, null))
            .StatusCode);

        var post = await client.PostAsync(
            $"/api/goods-receipts/{context.ReceiptId}/post", null);

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);

        var body = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("gerekçe", body.GetProperty("message").GetString()!);

        // Reddedildiği için stoğa da hiçbir şey girmemeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.WarehouseStocks.AnyAsync(
            x => x.InventoryItemId == context.InventoryItemId));
    }

    /// <summary>Hasarlı miktar da gerekçe ister.</summary>
    [Fact]
    public async Task Post_WithDamageButNoReason_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetQuantitiesAsync(client, context, 475m, 0m, 5m, null);

        var post = await client.PostAsync(
            $"/api/goods-receipts/{context.ReceiptId}/post", null);

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
    }

    /// <summary>
    /// Tamamı kabul edilen kabulde gerekçe istenmez — gereksiz alan
    /// doldurtmak kullanıcıyı boş yere yavaşlatırdı.
    /// </summary>
    [Fact]
    public async Task Post_WithFullAcceptance_NeedsNoReason()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetQuantitiesAsync(client, context, 480m, 0m, 0m, null);

        var post = await client.PostAsync(
            $"/api/goods-receipts/{context.ReceiptId}/post", null);

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // İade yok.
        Assert.False(await db.PurchaseReturns.AnyAsync(
            x => x.GoodsReceiptId == context.ReceiptId));
    }

    // ---------- Kısmi kabul ve iade belgesi ----------

    /// <summary>
    /// Kullanıcının anlattığı senaryo: 500 sipariş / 480 geldi / 470
    /// kabul → 470 stok, 10 iade belgesi, 20 eksik açık.
    /// </summary>
    [Fact]
    public async Task Post_PartialAcceptance_CreatesReturnAndLeavesOrderOpen()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetQuantitiesAsync(
            client, context, 470m, 10m, 0m, "Torbalar yırtık, nem almış.");

        var post = await client.PostAsync(
            $"/api/goods-receipts/{context.ReceiptId}/post", null);

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 470 stoğa girdi.
        var stock = await db.WarehouseStocks
            .SingleAsync(x => x.InventoryItemId == context.InventoryItemId);

        Assert.Equal(470m, stock.Quantity);

        // Sipariş kalemi 470 teslim almış sayılır; 30 açık kalır
        // (20 hiç gelmedi + 10 reddedildi).
        var orderItem = await db.PurchaseOrderItems
            .SingleAsync(x => x.PurchaseOrderId == context.OrderId);

        Assert.Equal(470m, orderItem.ReceivedQuantity);
        Assert.Equal(30m, orderItem.Quantity - orderItem.ReceivedQuantity);

        var order = await db.PurchaseOrders
            .SingleAsync(x => x.Id == context.OrderId);

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);

        // 10 adet için iade belgesi doğdu.
        var purchaseReturn = await db.PurchaseReturns
            .Include(x => x.Items)
            .SingleAsync(x => x.GoodsReceiptId == context.ReceiptId);

        Assert.Equal(PurchaseReturnStatus.Draft, purchaseReturn.Status);
        Assert.Equal(context.SupplierId, purchaseReturn.SupplierCurrentAccountId);
        Assert.False(string.IsNullOrWhiteSpace(purchaseReturn.ReturnNumber));

        var line = Assert.Single(purchaseReturn.Items);

        Assert.Equal(10m, line.Quantity);
        Assert.Equal(PurchaseReturnReasonKind.Rejected, line.ReasonKind);
        Assert.Contains("yırtık", line.Reason);

        // Bedel siparişteki birim fiyattan: 10 x 10 = 100
        Assert.Equal(100m, line.LineTotal);
        Assert.Equal(100m, purchaseReturn.TotalAmount);
    }

    /// <summary>
    /// Red ve hasar AYRI satır olur: ikisi farklı gerekçedir ve
    /// tedarikçi kalite analizinde ayrı sayılmalı.
    /// </summary>
    [Fact]
    public async Task Post_RejectedAndDamaged_ProduceSeparateLines()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetQuantitiesAsync(
            client, context, 460m, 12m, 8m, "Bir kısmı yanlış ürün, bir kısmı kırık.");

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync(
                $"/api/goods-receipts/{context.ReceiptId}/post", null)).StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var purchaseReturn = await db.PurchaseReturns
            .Include(x => x.Items)
            .SingleAsync(x => x.GoodsReceiptId == context.ReceiptId);

        Assert.Equal(2, purchaseReturn.Items.Count);

        var rejected = purchaseReturn.Items
            .Single(x => x.ReasonKind == PurchaseReturnReasonKind.Rejected);
        var damaged = purchaseReturn.Items
            .Single(x => x.ReasonKind == PurchaseReturnReasonKind.Damaged);

        Assert.Equal(12m, rejected.Quantity);
        Assert.Equal(8m, damaged.Quantity);
        Assert.Equal(200m, purchaseReturn.TotalAmount);
    }

    // ---------- İade belgesi uçları ----------

    /// <summary>
    /// Belge mal kabulden erişilebilir ve kalemleriyle okunabilir.
    /// </summary>
    [Fact]
    public async Task Return_IsReachableFromReceipt()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetQuantitiesAsync(client, context, 470m, 10m, 0m, "Şartnameye uymuyor");
        await client.PostAsync($"/api/goods-receipts/{context.ReceiptId}/post", null);

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-returns?goodsReceiptId={context.ReceiptId}");

        var row = Assert.Single(list.EnumerateArray().ToList());

        Assert.Equal("Taslak", row.GetProperty("statusName").GetString());
        Assert.Equal(100m, row.GetProperty("totalAmount").GetDecimal());

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-returns/{row.GetProperty("id").GetGuid()}");

        var items = detail.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal("Reddedildi", items[0].GetProperty("reasonKindName").GetString());
        Assert.Equal("Şartnameye uymuyor", items[0].GetProperty("reason").GetString());
    }

    /// <summary>
    /// Bekleyen iade listesi yalnız kapanmamış belgeleri getirir —
    /// takip ekranı bundan besleniyor.
    /// </summary>
    [Fact]
    public async Task OpenOnly_ListsUnclosedReturns()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetQuantitiesAsync(client, context, 470m, 10m, 0m, "Red");
        await client.PostAsync($"/api/goods-receipts/{context.ReceiptId}/post", null);

        var open = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-returns?companyId={context.CompanyId}&openOnly=true");

        var row = Assert.Single(open.EnumerateArray().ToList());
        var returnId = row.GetProperty("id").GetGuid();

        // Gönder → hâlâ açık.
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                $"/api/purchase-returns/{returnId}/durum",
                new { status = (int)PurchaseReturnStatus.Sent, note = (string?)null }))
            .StatusCode);

        var stillOpen = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-returns?companyId={context.CompanyId}&openOnly=true");

        Assert.Single(stillOpen.EnumerateArray().ToList());

        // Kapat → listeden düşer.
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                $"/api/purchase-returns/{returnId}/durum",
                new { status = (int)PurchaseReturnStatus.Completed, note = (string?)null }))
            .StatusCode);

        var closed = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-returns?companyId={context.CompanyId}&openOnly=true");

        Assert.Empty(closed.EnumerateArray());
    }

    /// <summary>
    /// Taslaktan doğrudan kapatılamaz; belge önce tedarikçiye
    /// gönderilmeli.
    /// </summary>
    [Fact]
    public async Task Return_CannotSkipSentStep()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetQuantitiesAsync(client, context, 470m, 10m, 0m, "Red");
        await client.PostAsync($"/api/goods-receipts/{context.ReceiptId}/post", null);

        var returnId = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/purchase-returns?goodsReceiptId={context.ReceiptId}"))
            .EnumerateArray().Single().GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/purchase-returns/{returnId}/durum",
            new { status = (int)PurchaseReturnStatus.Completed, note = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// İptal gerekçe ister: reddedilmiş mal sessizce kaybolmamalı.
    /// </summary>
    [Fact]
    public async Task Return_CancellationRequiresReason()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetQuantitiesAsync(client, context, 470m, 10m, 0m, "Red");
        await client.PostAsync($"/api/goods-receipts/{context.ReceiptId}/post", null);

        var returnId = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/purchase-returns?goodsReceiptId={context.ReceiptId}"))
            .EnumerateArray().Single().GetProperty("id").GetGuid();

        var without = await client.PostAsJsonAsync(
            $"/api/purchase-returns/{returnId}/durum",
            new { status = (int)PurchaseReturnStatus.Cancelled, note = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, without.StatusCode);

        var with = await client.PostAsJsonAsync(
            $"/api/purchase-returns/{returnId}/durum",
            new
            {
                status = (int)PurchaseReturnStatus.Cancelled,
                note = "Tedarikçi yerinde değiştirdi, iade edilmedi."
            });

        Assert.Equal(HttpStatusCode.OK, with.StatusCode);
    }

    /// <summary>
    /// Mal kabul detayı red gerekçesini geri veriyor — ekran kalem
    /// bazında göstermek zorunda.
    /// </summary>
    [Fact]
    public async Task ReceiptDetail_ExposesRejectionReason()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetQuantitiesAsync(
            client, context, 470m, 10m, 0m, "Etiketsiz geldi");

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/goods-receipts/{context.ReceiptId}");

        var item = detail.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(
            "Etiketsiz geldi", item.GetProperty("rejectionReason").GetString());
        Assert.Equal(470m, item.GetProperty("acceptedQuantity").GetDecimal());
        Assert.Equal(10m, item.GetProperty("rejectedQuantity").GetDecimal());
    }
}
