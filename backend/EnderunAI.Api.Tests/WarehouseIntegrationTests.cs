using System.Net;
using System.Net.Http.Json;
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

[Collection("Integration")]
public sealed class WarehouseIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task PostingGoodsReceipt_AutomaticallyCreatesStockAndUpdatesAverageCost()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
        var project = await TestDataFactory.CreateProjectAsync(db, suffix + "P");

        var supplier = new CurrentAccount
        {
            CompanyId = company.Id,
            Code = $"TED-{suffix}",
            Title = $"Test Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(supplier);

        var item = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"MLZ-{suffix}",
            Name = $"Test Malzeme {suffix}",
            Unit = "adet",
            MinimumStock = 0
        };
        db.InventoryItems.Add(item);

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DEPO-{suffix}",
            Name = $"Test Depo {suffix}",
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(warehouse);

        var purchaseRequest = new PurchaseRequest
        {
            CompanyId = company.Id,
            ProjectId = project.Id,
            RequestNumber = $"PR-{suffix}",
            RequestDate = DateTime.UtcNow.Date,
            RequestedByName = "Test Kullanıcı",
            Priority = PurchaseRequestPriority.Normal,
            Status = PurchaseRequestStatus.Approved
        };
        db.PurchaseRequests.Add(purchaseRequest);
        await db.SaveChangesAsync();

        var rfq = new EnderunAI.Api.Models.Rfq.Rfq
        {
            CompanyId = company.Id,
            PurchaseRequestId = purchaseRequest.Id,
            RfqNumber = $"RFQ-{suffix}",
            Title = "Test RFQ",
            IssueDate = DateTime.UtcNow.Date,
            Currency = "USD"
        };
        db.Rfqs.Add(rfq);
        await db.SaveChangesAsync();

        var purchaseOrder = new PurchaseOrderEntity
        {
            CompanyId = company.Id,
            ProjectId = project.Id,
            RfqId = rfq.Id,
            SupplierCurrentAccountId = supplier.Id,
            OrderNumber = $"PO-{suffix}",
            OrderDate = DateTime.UtcNow.Date,
            Status = PurchaseOrderStatus.Approved,
            Currency = "USD",
            ExchangeRate = 40m // 1 USD = 40 TRY (test sabiti)
        };

        var orderItem = new PurchaseOrderItem
        {
            LineNumber = 1,
            MaterialDescription = item.Name,
            Quantity = 100,
            Unit = "adet",
            UnitPrice = 2m,
            NetUnitPrice = 2m, // 2 USD * 40 = 80 TRY beklenen birim maliyet
            TotalPrice = 200m
        };
        purchaseOrder.Items.Add(orderItem);
        db.PurchaseOrders.Add(purchaseOrder);
        await db.SaveChangesAsync();

        var receipt = new GoodsReceipt
        {
            CompanyId = company.Id,
            PurchaseOrderId = purchaseOrder.Id,
            WarehouseId = warehouse.Id,
            ReceiptNumber = $"GR-{suffix}",
            ReceiptDate = DateTime.UtcNow.Date,
            Status = GoodsReceiptStatus.Draft,
            ReceivedByName = "Test Kullanıcı"
        };

        var receiptItem = new GoodsReceiptItem
        {
            PurchaseOrderItemId = orderItem.Id,
            InventoryItemId = item.Id,
            LineNumber = 1,
            MaterialDescription = item.Name,
            OrderedQuantity = 100,
            DeliveredQuantity = 40,
            AcceptedQuantity = 40,
            Unit = "adet"
        };
        receipt.Items.Add(receiptItem);
        db.GoodsReceipts.Add(receipt);
        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var postResponse = await client.PostAsync($"/api/goods-receipts/{receipt.Id}/post", null);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stock = await verifyDb.WarehouseStocks.SingleAsync(x =>
            x.WarehouseId == warehouse.Id && x.InventoryItemId == item.Id);
        Assert.Equal(40, stock.Quantity);

        var updatedItem = await verifyDb.InventoryItems.SingleAsync(x => x.Id == item.Id);
        // İlk mal kabul: ortalama maliyet = 2 USD * 40 kur = 80 TRY
        Assert.Equal(80m, updatedItem.AverageUnitCost);

        // Son alış fiyatı ortalamadan ayrı tutulur; ilk kabulde ikisi de
        // aynı çıkar ama alanın gerçekten dolduğu burada sabitleniyor.
        Assert.Equal(80m, updatedItem.LastPurchasePrice);
        Assert.Equal(receipt.ReceiptDate, updatedItem.LastPurchaseDate);

        var movement = await verifyDb.StockMovements.SingleAsync(x =>
            x.InventoryItemId == item.Id && x.Type == StockMovementType.Receipt);
        Assert.Equal(receipt.Id, movement.GoodsReceiptId);
        Assert.Equal(80m, movement.UnitCost);
        Assert.Equal(40 * 80m, movement.TotalCost);
    }

    [Fact]
    public async Task Issue_WithProjectAndSite_CreatesFrozenCostAndProjectCostTransaction()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
        var project = await TestDataFactory.CreateProjectAsync(db, suffix + "P");

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DEPO-{suffix}",
            Name = $"Test Depo {suffix}",
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(warehouse);

        var item = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"MLZ-{suffix}",
            Name = $"Test Malzeme {suffix}",
            Unit = "adet",
            AverageUnitCost = 25m
        };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        db.WarehouseStocks.Add(new WarehouseStock
        {
            WarehouseId = warehouse.Id,
            InventoryItemId = item.Id,
            Quantity = 100
        });

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"STE-{suffix}",
            Name = $"Test Şantiye {suffix}"
        };
        db.ProjectSites.Add(site);
        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = warehouse.Id,
            inventoryItemId = item.Id,
            projectId = project.Id,
            projectSiteId = site.Id,
            quantity = 10,
            referenceNumber = (string?)null,
            movementDate = DateTime.UtcNow.Date,
            description = "Entegrasyon testi sarfı"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(25m, payload.GetProperty("unitCost").GetDecimal());
        Assert.Equal(250m, payload.GetProperty("totalCost").GetDecimal());

        // Sarftan SONRA ortalama maliyeti değiştirelim — hareketin dondurulmuş
        // maliyetinin değişmemesi gerekiyor.
        using var mutateScope = fixture.Factory.Services.CreateScope();
        var mutateDb = mutateScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trackedItem = await mutateDb.InventoryItems.SingleAsync(x => x.Id == item.Id);
        trackedItem.AverageUnitCost = 999m;
        await mutateDb.SaveChangesAsync();

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var movement = await verifyDb.StockMovements.SingleAsync(x =>
            x.InventoryItemId == item.Id && x.Type == StockMovementType.Issue);
        Assert.Equal(25m, movement.UnitCost);
        Assert.Equal(250m, movement.TotalCost);
        Assert.Equal(site.Id, movement.ProjectSiteId);

        var costTransaction = await verifyDb.ProjectCostTransactions.SingleAsync(x =>
            x.ReferenceType == "StockMovement" && x.ReferenceId == movement.Id);
        Assert.Equal(project.Id, costTransaction.ProjectId);
        Assert.Equal(site.Id, costTransaction.ProjectSiteId);
        Assert.Equal(250m, costTransaction.Amount);
        Assert.Equal(ProjectCostType.Material, costTransaction.CostType);
    }

    [Fact]
    public async Task Issue_WithoutProject_DoesNotCreateProjectCostTransaction()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DEPO-{suffix}",
            Name = $"Test Depo {suffix}",
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(warehouse);

        var item = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"MLZ-{suffix}",
            Name = $"Test Malzeme {suffix}",
            Unit = "adet",
            AverageUnitCost = 10m
        };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        db.WarehouseStocks.Add(new WarehouseStock
        {
            WarehouseId = warehouse.Id,
            InventoryItemId = item.Id,
            Quantity = 50
        });
        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = warehouse.Id,
            inventoryItemId = item.Id,
            projectId = (Guid?)null,
            projectSiteId = (Guid?)null,
            quantity = 5,
            referenceNumber = (string?)null,
            movementDate = DateTime.UtcNow.Date,
            description = "Genel/merkez sarfı"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var hasCostTransaction = await verifyDb.ProjectCostTransactions.AnyAsync(x =>
            x.Description != null && x.Description.Contains(item.Name));
        Assert.False(hasCostTransaction);
    }

    [Fact]
    public async Task Adjustment_ComputesSignedDeltaAndUpdatesStock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DEPO-{suffix}",
            Name = $"Test Depo {suffix}",
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(warehouse);

        var item = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"MLZ-{suffix}",
            Name = $"Test Malzeme {suffix}",
            Unit = "adet"
        };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        db.WarehouseStocks.Add(new WarehouseStock
        {
            WarehouseId = warehouse.Id,
            InventoryItemId = item.Id,
            Quantity = 60
        });
        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/inventory/adjustments", new
        {
            warehouseId = warehouse.Id,
            inventoryItemId = item.Id,
            countedQuantity = 55,
            projectId = (Guid?)null,
            movementDate = DateTime.UtcNow.Date,
            description = "Sayım testi"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(-5m, payload.GetProperty("delta").GetDecimal());
        Assert.Equal(55m, payload.GetProperty("newQuantity").GetDecimal());

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stock = await verifyDb.WarehouseStocks.SingleAsync(x =>
            x.WarehouseId == warehouse.Id && x.InventoryItemId == item.Id);
        Assert.Equal(55m, stock.Quantity);

        var movement = await verifyDb.StockMovements.SingleAsync(x =>
            x.InventoryItemId == item.Id && x.Type == StockMovementType.Adjustment);
        Assert.Equal(-5m, movement.Quantity);
    }
}
