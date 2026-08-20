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
/// Alış/satış iadesi ve kesinleşmiş fatura iptali.
///
/// İadenin can alıcı noktası ters fişin orijinali TAM kapatması ve
/// stoğun girdiği fiyatla çıkmasıdır: biri tutmazsa cari bakiyesinde
/// kalıntı borç kalır ya da kalan stoğun maliyeti bozulur.
/// </summary>
[Collection("Integration")]
public sealed class InvoiceReturnTests(DatabaseFixture fixture)
{
    private sealed record TestContext(
        Guid CompanyId,
        Guid ProjectId,
        Guid SupplierId,
        Guid CustomerId,
        Guid WarehouseId,
        Guid InventoryItemId);

    private static async Task SeedChartOfAccountsAsync(AppDbContext db, Guid companyId)
    {
        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = companyId, Code = "320", Name = "Satıcılar",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "120", Name = "Alıcılar",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "191.01.03", Name = "İndirilecek KDV",
                Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "391.09", Name = "Hesaplanan KDV",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "600.03", Name = "Yurtiçi Satışlar",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "610.01", Name = "Satış İadesi",
                Nature = AccountingAccountNature.Debit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "153", Name = "Ticari Mallar",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "740", Name = "Hizmet Üretim Maliyeti",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            // 150 ve 379 S6b ile zorunlu: stok hesabını kartın
            // KATEGORİSİ belirliyor ve varsayılan sarf (150). Mal kabul
            // ayrıca 379.01 GR/IR hesabını kullanıyor. Hesap yoksa uç
            // haklı olarak duruyor.
            new AccountingAccount
            {
                CompanyId = companyId, Code = "150", Name = "İlk Madde ve Malzeme",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "379", Name = "Diğer Borç ve Gider Karşılıkları",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = false
            });

        await db.SaveChangesAsync();

        // 379.01 alt hesabını üretimdeki tohum açsın.
        await GoodsReceivedNotInvoicedAccountSeed.SeedAsync(db);
    }

    private async Task<TestContext> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        await SeedChartOfAccountsAsync(db, project.CompanyId);

        var supplier = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TED-{suffix}",
            Title = $"Test Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        var customer = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"MUS-{suffix}",
            Title = $"Test Müşteri {suffix}",
            Roles = CurrentAccountRoles.Customer,
            Status = CurrentAccountStatus.Approved
        };

        db.CurrentAccounts.AddRange(supplier, customer);

        var branchId = await db.Branches
            .Where(x => x.CompanyId == project.CompanyId)
            .Select(x => x.Id)
            .FirstAsync();

        var warehouse = new Warehouse
        {
            CompanyId = project.CompanyId,
            BranchId = branchId,
            ProjectId = project.Id,
            Code = $"DP-{suffix}",
            Name = $"Test Deposu {suffix}",
            Type = WarehouseType.Site,
            IsActive = true
        };

        var inventoryItem = new InventoryItem
        {
            CompanyId = project.CompanyId,
            Code = $"MLZ-{suffix}",
            Name = $"Test Malzeme {suffix}",
            Unit = "adet",
            IsActive = true
        };

        db.Warehouses.Add(warehouse);
        db.InventoryItems.Add(inventoryItem);

        await db.SaveChangesAsync();

        return new TestContext(
            project.CompanyId, project.Id, supplier.Id, customer.Id,
            warehouse.Id, inventoryItem.Id);
    }

    /// <summary>
    /// Onaylı, stok kartlı alış faturası kurar ve mal depoya girer.
    /// </summary>
    private async Task<(Guid InvoiceId, Guid ItemId)> CreateApprovedPurchaseAsync(
        HttpClient client, TestContext context, string suffix,
        decimal quantity = 100m, decimal unitPrice = 50m)
    {
        var created = await client.PostAsJsonAsync("/api/supplier-invoices", new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            projectId = (Guid?)context.ProjectId,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = $"ALS-{suffix}",
            invoiceDate = DateTime.UtcNow.Date,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            warehouseId = context.WarehouseId,
            items = new[]
            {
                new
                {
                    description = "Test malzeme",
                    quantity,
                    unit = "adet",
                    unitPrice,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    inventoryItemId = context.InventoryItemId
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var payload = await created.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = payload.GetProperty("id").GetGuid();
        var itemId = payload.GetProperty("items").EnumerateArray().First()
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/supplier-invoices/{invoiceId}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/supplier-invoices/{invoiceId}/approve", null)).StatusCode);

        return (invoiceId, itemId);
    }

    private async Task<(decimal Quantity, decimal AverageCost)> LoadStockAsync(
        TestContext context)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var quantity = await db.WarehouseStocks
            .Where(x => x.InventoryItemId == context.InventoryItemId)
            .SumAsync(x => (decimal?)x.Quantity) ?? 0m;

        var averageCost = await db.InventoryItems
            .Where(x => x.Id == context.InventoryItemId)
            .Select(x => x.AverageUnitCost)
            .SingleAsync();

        return (quantity, averageCost);
    }

    /// <summary>
    /// Tam iade: ters fiş orijinalin aynası olmalı (aynı hesaplar, ters
    /// yön), stok girdiği fiyatla çıkmalı ve proje maliyeti eksiyle
    /// dengelenmeli.
    /// </summary>
    [Fact]
    public async Task PurchaseReturn_Full_ReversesVoucherStockAndProjectCost()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (invoiceId, itemId) = await CreateApprovedPurchaseAsync(client, context, suffix);

        var beforeStock = await LoadStockAsync(context);
        Assert.Equal(100m, beforeStock.Quantity);
        Assert.Equal(50m, beforeStock.AverageCost);

        var returnResponse = await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{invoiceId}/returns",
            new
            {
                invoiceNumber = $"IADE-{suffix}",
                invoiceDate = DateTime.UtcNow.Date,
                items = new[] { new { originalItemId = itemId, quantity = 100m } }
            });

        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);

        var returnInvoice = await returnResponse.Content.ReadFromJsonAsync<JsonElement>();
        var returnInvoiceId = returnInvoice.GetProperty("id").GetGuid();

        Assert.True(returnInvoice.GetProperty("isReturn").GetBoolean());
        Assert.Equal(invoiceId, returnInvoice.GetProperty("originalInvoiceId").GetGuid());
        Assert.Equal(6_000m, returnInvoice.GetProperty("grandTotal").GetDecimal());

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/supplier-invoices/{returnInvoiceId}/submit", null))
            .StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/supplier-invoices/{returnInvoiceId}/approve", null))
            .StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var originalVoucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.SourceModule == "SupplierInvoice" &&
                              x.SourceEntityId == invoiceId);

        var returnVoucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.SourceModule == "SupplierInvoice" &&
                              x.SourceEntityId == returnInvoiceId);

        Assert.Equal(returnVoucher.TotalDebit, returnVoucher.TotalCredit);
        Assert.Equal(originalVoucher.TotalDebit, returnVoucher.TotalDebit);

        // Ayna kontrolü: her hesabın borcu iadede alacağa dönmüş olmalı.
        foreach (var line in originalVoucher.Lines)
        {
            var mirrored = returnVoucher.Lines
                .Where(x => x.AccountingAccountId == line.AccountingAccountId)
                .ToList();

            Assert.NotEmpty(mirrored);
            Assert.Equal(line.DebitAmount, mirrored.Sum(x => x.CreditAmount));
            Assert.Equal(line.CreditAmount, mirrored.Sum(x => x.DebitAmount));
        }

        var afterStock = await LoadStockAsync(context);
        Assert.Equal(0m, afterStock.Quantity);

        var movement = await db.StockMovements
            .SingleAsync(x => x.Type == StockMovementType.Return &&
                              x.InventoryItemId == context.InventoryItemId);

        Assert.Equal(100m, movement.Quantity);
        Assert.Equal(50m, movement.UnitCost);

        // Proje maliyeti eksiyle dengelenmeli; net etki sıfır.
        var projectCost = await db.ProjectCostTransactions
            .Where(x => x.ReferenceType == "SupplierInvoice" &&
                        (x.ReferenceId == invoiceId || x.ReferenceId == returnInvoiceId))
            .SumAsync(x => x.Amount);

        Assert.Equal(0m, projectCost);
    }

    /// <summary>
    /// Kısmi iade: kalan miktar iade edilebilir olarak durur, ikinci kez
    /// tamamı iade edilemez.
    /// </summary>
    [Fact]
    public async Task PurchaseReturn_Partial_TracksRemainingReturnableQuantity()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (invoiceId, itemId) = await CreateApprovedPurchaseAsync(client, context, suffix);

        var firstReturn = await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{invoiceId}/returns",
            new
            {
                invoiceNumber = $"IADE1-{suffix}",
                invoiceDate = DateTime.UtcNow.Date,
                items = new[] { new { originalItemId = itemId, quantity = 30m } }
            });

        Assert.Equal(HttpStatusCode.OK, firstReturn.StatusCode);

        var firstReturnId = (await firstReturn.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsync($"/api/supplier-invoices/{firstReturnId}/submit", null);
        await client.PostAsync($"/api/supplier-invoices/{firstReturnId}/approve", null);

        var afterFirst = await LoadStockAsync(context);
        Assert.Equal(70m, afterFirst.Quantity);

        var original = await client.GetFromJsonAsync<JsonElement>(
            $"/api/supplier-invoices/{invoiceId}");

        var returnable = original.GetProperty("returnableItems").EnumerateArray().Single();

        Assert.Equal(100m, returnable.GetProperty("invoicedQuantity").GetDecimal());
        Assert.Equal(30m, returnable.GetProperty("returnedQuantity").GetDecimal());
        Assert.Equal(70m, returnable.GetProperty("returnableQuantity").GetDecimal());

        // Kalanın üstünde iade istenemez.
        var tooMuch = await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{invoiceId}/returns",
            new
            {
                invoiceNumber = $"IADE2-{suffix}",
                invoiceDate = DateTime.UtcNow.Date,
                items = new[] { new { originalItemId = itemId, quantity = 71m } }
            });

        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Contains("en fazla", await tooMuch.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// İade, malın GİRDİĞİ fiyatla çıkar. Arada pahalı alım yapılmışsa
    /// ucuz malın iadesi kalan stoğun ortalamasını yükseltmemeli.
    /// </summary>
    [Fact]
    public async Task PurchaseReturn_UsesOriginalCost_NotCurrentAverage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // 100 adet × 50 TL
        var (firstInvoiceId, firstItemId) =
            await CreateApprovedPurchaseAsync(client, context, $"{suffix}a");

        // 100 adet × 150 TL → ortalama 100 TL
        await CreateApprovedPurchaseAsync(client, context, $"{suffix}b", unitPrice: 150m);

        var beforeReturn = await LoadStockAsync(context);
        Assert.Equal(200m, beforeReturn.Quantity);
        Assert.Equal(100m, beforeReturn.AverageCost);

        // İlk faturadan 100 adet iade: 50 TL ile çıkmalı.
        var returnResponse = await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{firstInvoiceId}/returns",
            new
            {
                invoiceNumber = $"IADE-{suffix}",
                invoiceDate = DateTime.UtcNow.Date,
                items = new[] { new { originalItemId = firstItemId, quantity = 100m } }
            });

        var returnId = (await returnResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsync($"/api/supplier-invoices/{returnId}/submit", null);
        await client.PostAsync($"/api/supplier-invoices/{returnId}/approve", null);

        var afterReturn = await LoadStockAsync(context);

        Assert.Equal(100m, afterReturn.Quantity);
        // (200×100 − 100×50) / 100 = 150 → kalan mal gerçekten 150'lik mal.
        Assert.Equal(150m, afterReturn.AverageCost);
    }

    /// <summary>
    /// Depoda o kadar mal yoksa iade edilemez; stok negatife düşerse
    /// sayım da maliyet de anlamını yitirir.
    /// </summary>
    [Fact]
    public async Task PurchaseReturn_WithoutEnoughStock_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (invoiceId, itemId) = await CreateApprovedPurchaseAsync(client, context, suffix);

        // Malzeme şantiyede kullanıldı: depodan çıktı.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stock = await db.WarehouseStocks
                .SingleAsync(x => x.InventoryItemId == context.InventoryItemId);

            stock.Quantity = 10m;
            await db.SaveChangesAsync();
        }

        var returnResponse = await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{invoiceId}/returns",
            new
            {
                invoiceNumber = $"IADE-{suffix}",
                invoiceDate = DateTime.UtcNow.Date,
                items = new[] { new { originalItemId = itemId, quantity = 100m } }
            });

        var returnId = (await returnResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsync($"/api/supplier-invoices/{returnId}/submit", null);

        var approve = await client.PostAsync(
            $"/api/supplier-invoices/{returnId}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Contains("iade edilemez", await approve.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Onaylanmamış faturadan iade yapılamaz: tersine çevrilecek bir
    /// kayıt yoktur.
    /// </summary>
    [Fact]
    public async Task PurchaseReturn_FromUnapprovedInvoice_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync("/api/supplier-invoices", new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            projectId = (Guid?)context.ProjectId,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = $"TSL-{suffix}",
            invoiceDate = DateTime.UtcNow.Date,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            items = new[]
            {
                new
                {
                    description = "Hizmet",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 1_000m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null
                }
            }
        });

        var payload = await created.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = payload.GetProperty("id").GetGuid();
        var itemId = payload.GetProperty("items").EnumerateArray().First()
            .GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{invoiceId}/returns",
            new
            {
                invoiceNumber = $"IADE-{suffix}",
                invoiceDate = DateTime.UtcNow.Date,
                items = new[] { new { originalItemId = itemId, quantity = 1m } }
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("onaylanmış faturadan", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Satış iadesi 600'ü değil 610 Satıştan İadeler'i borçlandırır;
    /// brüt satış rakamı bozulmamalı.
    /// </summary>
    [Fact]
    public async Task SalesReturn_PostsToSalesReturnAccount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync("/api/sales-invoices", new
        {
            companyId = context.CompanyId,
            customerCurrentAccountId = context.CustomerId,
            projectId = (Guid?)context.ProjectId,
            officialInvoiceNumber = $"SAT-{suffix}",
            invoiceDate = DateTime.UtcNow.Date,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            withholdingAmount = 0m,
            description = (string?)null,
            notes = (string?)null,
            items = new[]
            {
                new
                {
                    description = "Malzeme satışı",
                    quantity = 10m,
                    unit = "adet",
                    unitPrice = 1_000m,
                    vatRate = 20m
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var payload = await created.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = payload.GetProperty("id").GetGuid();
        var itemId = payload.GetProperty("items").EnumerateArray().First()
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/sales-invoices/{invoiceId}/post", null)).StatusCode);

        var returnResponse = await client.PostAsJsonAsync(
            $"/api/sales-invoices/{invoiceId}/returns",
            new
            {
                invoiceNumber = $"SIADE-{suffix}",
                invoiceDate = DateTime.UtcNow.Date,
                items = new[] { new { originalItemId = itemId, quantity = 4m } }
            });

        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);

        var returnInvoice = await returnResponse.Content.ReadFromJsonAsync<JsonElement>();
        var returnInvoiceId = returnInvoice.GetProperty("id").GetGuid();

        Assert.True(returnInvoice.GetProperty("isReturn").GetBoolean());
        Assert.Equal(4_800m, returnInvoice.GetProperty("grandTotal").GetDecimal());

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/sales-invoices/{returnInvoiceId}/post", null))
            .StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.SourceModule == "SalesInvoice" &&
                              x.SourceEntityId == returnInvoiceId);

        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);

        var returnLine = voucher.Lines.Single(x => x.AccountingAccount.Code == "610.01");
        var receivableLine = voucher.Lines.Single(x => x.AccountingAccount.Code == "120");

        Assert.Equal(4_000m, returnLine.DebitAmount);
        Assert.Equal(4_800m, receivableLine.CreditAmount);
        Assert.DoesNotContain(voucher.Lines, x => x.AccountingAccount.Code == "600.03");
    }

    /// <summary>
    /// Kesinleşmiş fatura iptali: fiş silinmez, ters kaydı üretilir ve
    /// stok geri çıkar.
    /// </summary>
    [Fact]
    public async Task CancelApprovedPurchaseInvoice_PostsReversalAndRemovesStock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (invoiceId, _) = await CreateApprovedPurchaseAsync(client, context, suffix);

        var response = await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{invoiceId}/cancel",
            new { reason = "Fatura yanlış cariye kesilmiş" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SupplierInvoices.SingleAsync(x => x.Id == invoiceId);

        Assert.Equal(SupplierInvoiceStatus.Cancelled, invoice.Status);
        Assert.NotNull(invoice.ReversalVoucherId);
        Assert.Equal("Fatura yanlış cariye kesilmiş", invoice.CancellationReason);

        var vouchers = await db.AccountingVouchers
            .Include(x => x.Lines)
            .Where(x => x.SourceModule == "SupplierInvoice" && x.SourceEntityId == invoiceId)
            .ToListAsync();

        // Orijinal fiş SİLİNMEZ; ikisi de defterde durur ve net etki sıfır.
        Assert.Equal(2, vouchers.Count);
        Assert.All(vouchers, x => Assert.Equal(AccountingVoucherStatus.Posted, x.Status));

        var netByAccount = vouchers
            .SelectMany(x => x.Lines)
            .GroupBy(x => x.AccountingAccountId)
            .Select(g => g.Sum(x => x.DebitAmount - x.CreditAmount));

        Assert.All(netByAccount, net => Assert.Equal(0m, net));

        var stock = await LoadStockAsync(context);
        Assert.Equal(0m, stock.Quantity);
    }

    /// <summary>
    /// Gerekçesiz iptal reddedilir: ters fişte ve denetim izinde gerekçe
    /// görünmeli.
    /// </summary>
    [Fact]
    public async Task CancelApprovedInvoice_WithoutReason_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (invoiceId, _) = await CreateApprovedPurchaseAsync(client, context, suffix);

        var response = await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{invoiceId}/cancel",
            new { reason = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("gerekçe zorunludur", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// İadesi olan fatura iptal edilemez: iade dayanaksız kalırdı.
    /// </summary>
    [Fact]
    public async Task CancelApprovedInvoice_WithReturn_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (invoiceId, itemId) = await CreateApprovedPurchaseAsync(client, context, suffix);

        await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{invoiceId}/returns",
            new
            {
                invoiceNumber = $"IADE-{suffix}",
                invoiceDate = DateTime.UtcNow.Date,
                items = new[] { new { originalItemId = itemId, quantity = 10m } }
            });

        var response = await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{invoiceId}/cancel",
            new { reason = "Yanlış girildi" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("iade faturası var", await response.Content.ReadAsStringAsync());
    }
}
