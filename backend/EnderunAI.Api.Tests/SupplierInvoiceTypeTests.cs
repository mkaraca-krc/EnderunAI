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
/// Alış (stok) ve gider faturasının ayrışması.
///
/// İki güvence: alış faturası stoğu DOĞRU DEPOYA girer ve ağırlıklı
/// ortalama maliyeti günceller; gider faturası kalem bazında SEÇİLEN
/// hesaba ve DOĞRU MASRAF MERKEZİNE yazılır. Mal kabule bağlı faturada
/// stok bir daha girmez — çift sayma imkânsız olmalı.
/// </summary>
[Collection("Integration")]
public sealed class SupplierInvoiceTypeTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId,
        Guid ProjectId,
        Guid SupplierId,
        Guid MerkezWarehouseId,
        Guid SantiyeWarehouseId,
        Guid KabloItemId,
        Guid ElektrikAccountId,
        Guid KirtasiyeAccountId,
        Guid PayableAccountId,
        string MerkezCostCenter,
        string ProjectCode);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var company = await db.Companies.SingleAsync(x => x.Id == project.CompanyId);

        var branch = await db.Branches.SingleAsync(
            x => x.CompanyId == company.Id && x.IsHeadOffice);
        branch.CostCenterCode = $"MRK-{suffix}";

        var supplier = new CurrentAccount
        {
            CompanyId = company.Id,
            Code = $"TED-{suffix}",
            Title = $"Test Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(supplier);

        // Hesap planı: fişin ihtiyaç duyduğu asgari hesaplar.
        AccountingAccount Account(string code, string name, AccountingAccountNature nature) =>
            new()
            {
                CompanyId = company.Id,
                Code = code,
                Name = name,
                Nature = nature,
                Level = 3,
                IsPostingAllowed = true
            };

        var stock = Account("153", "Ticari Mallar", AccountingAccountNature.Debit);

        /*
         * 150 ve 379 S6b ile ZORUNLU oldu.
         *
         * Kartın kategorisi hangi stok hesabına yazılacağını
         * belirliyor ve VARSAYILAN SARF (150) — kategorisiz kartlar da
         * oraya düşüyor. Mal kabul ayrıca 379.01 "faturası gelmemiş
         * mal alımları" hesabını kullanıyor.
         *
         * Hesap yoksa uç HAKLI OLARAK duruyor: stokun muhasebesiz
         * girmesindense işlem durmalı.
         */
        var sarf = Account("150", "İlk Madde ve Malzeme", AccountingAccountNature.Debit);
        var grir = Account("379", "Diğer Borç ve Gider Karşılıkları", AccountingAccountNature.Credit);
        grir.IsPostingAllowed = false;
        var vatIn = Account("191", "İndirilecek KDV", AccountingAccountNature.Debit);
        var payable = Account("320", "Satıcılar", AccountingAccountNature.Credit);
        var cost = Account("740", "Hizmet Üretim Maliyeti", AccountingAccountNature.Debit);
        var elektrik = Account("770.03.10", "Elektrik Su Doğalgaz", AccountingAccountNature.Debit);
        var kirtasiye = Account("770.03.03", "Kırtasiye", AccountingAccountNature.Debit);

        db.AccountingAccounts.AddRange(
            stock, sarf, grir, vatIn, payable, cost, elektrik, kirtasiye);

        var merkezWarehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"MRKD-{suffix}",
            Name = "Merkez Depo",
            Type = WarehouseType.Central
        };

        var santiyeWarehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            ProjectId = project.Id,
            Code = $"SNTD-{suffix}",
            Name = "Şantiye Deposu",
            Type = WarehouseType.Site
        };

        db.Warehouses.AddRange(merkezWarehouse, santiyeWarehouse);

        var kablo = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"KBL-{suffix}",
            Name = "Enerji Kablosu",
            Unit = "Metre"
        };
        db.InventoryItems.Add(kablo);

        await db.SaveChangesAsync();

        db.CompanyFinanceSettings.Add(new CompanyFinanceSettings
        {
            CompanyId = company.Id,
            InventoryAccountId = stock.Id,
            VatInAccountId = vatIn.Id,
            PayablesAccountId = payable.Id,
            ExpenseAccountId = cost.Id
        });

        await db.SaveChangesAsync();

        // 379.01 alt hesabını üretimdeki tohum açsın: testler de
        // gerçek şirketle aynı yoldan geçsin.
        await GoodsReceivedNotInvoicedAccountSeed.SeedAsync(db);

        return new Context(
            company.Id, project.Id, supplier.Id,
            merkezWarehouse.Id, santiyeWarehouse.Id, kablo.Id,
            elektrik.Id, kirtasiye.Id, payable.Id,
            branch.CostCenterCode!, project.Code);
    }

    private static object StockInvoice(
        Context context, string suffix, Guid warehouseId,
        Guid? goodsReceiptId = null) => new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            projectId = (Guid?)context.ProjectId,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId,
            invoiceNumber = $"ALS-{suffix}",
            invoiceDate = DateTime.UtcNow,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            invoiceType = 0,
            warehouseId = (Guid?)warehouseId,
            costCenterCode = (string?)null,
            items = new[]
            {
                new
                {
                    description = "Enerji kablosu",
                    quantity = 100m,
                    unit = "Metre",
                    unitPrice = 400m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    inventoryItemId = (Guid?)context.KabloItemId,
                    warehouseId = (Guid?)null,
                    expenseAccountId = (Guid?)null,
                    costCenterCode = (string?)null
                }
            }
        };

    private async Task<Guid> CreateAndApproveAsync(HttpClient client, object payload)
    {
        var created = await client.PostAsJsonAsync("/api/supplier-invoices", payload);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/supplier-invoices/{id}/submit", null)).StatusCode);

        var approved = await client.PostAsync($"/api/supplier-invoices/{id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        return id;
    }

    [Fact]
    public async Task StockInvoice_PostsToSelectedWarehouseAndUpdatesAverageCost()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await CreateAndApproveAsync(
            client, StockInvoice(context, suffix, context.SantiyeWarehouseId));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stock = await db.WarehouseStocks.SingleAsync(x =>
            x.InventoryItemId == context.KabloItemId &&
            x.WarehouseId == context.SantiyeWarehouseId);

        Assert.Equal(100m, stock.Quantity);

        var item = await db.InventoryItems.SingleAsync(x => x.Id == context.KabloItemId);

        // KDV hariç birim fiyat maliyete girer.
        Assert.Equal(400m, item.AverageUnitCost);
        Assert.Equal(400m, item.LastPurchasePrice);

        var movement = await db.StockMovements.SingleAsync(x =>
            x.InventoryItemId == context.KabloItemId);

        Assert.Equal(StockMovementType.Receipt, movement.Type);
        Assert.Equal(100m, movement.Quantity);
        Assert.Equal(40_000m, movement.TotalCost);
    }

    /// <summary>
    /// Mal kabule bağlı faturada stok BİR DAHA girmez: mal kabul zaten
    /// girmiştir, tekrar girilse miktar ve maliyet ikiye katlanırdı.
    /// </summary>
    [Fact]
    public async Task StockInvoice_WithGoodsReceipt_DoesNotPostStockAgain()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid goodsReceiptId;
        Guid purchaseOrderId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Sipariş zinciri talep → RFQ → sipariş şeklinde; testin
            // konusu bu değil, yalnızca mal kabul bağının kurulabilmesi
            // için asgari zincir açılıyor.
            var request = new PurchaseRequest
            {
                CompanyId = context.CompanyId,
                ProjectId = context.ProjectId,
                RequestNumber = $"TLP-{suffix}",
                RequestDate = DateTime.UtcNow,
                RequestedByName = "Test"
            };
            db.PurchaseRequests.Add(request);
            await db.SaveChangesAsync();

            var rfq = new Models.Rfq.Rfq
            {
                CompanyId = context.CompanyId,
                PurchaseRequestId = request.Id,
                RfqNumber = $"RFQ-{suffix}",
                Title = "Test",
                IssueDate = DateTime.UtcNow
            };
            db.Rfqs.Add(rfq);
            await db.SaveChangesAsync();

            var order = new Models.PurchaseOrder.PurchaseOrder
            {
                CompanyId = context.CompanyId,
                ProjectId = context.ProjectId,
                RfqId = rfq.Id,
                SupplierCurrentAccountId = context.SupplierId,
                OrderNumber = $"SIP-{suffix}",
                OrderDate = DateTime.UtcNow,
                Currency = "TRY",
                ExchangeRate = 1m
            };
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync();

            var receipt = new Models.GoodsReceipt.GoodsReceipt
            {
                CompanyId = context.CompanyId,
                PurchaseOrderId = order.Id,
                WarehouseId = context.SantiyeWarehouseId,
                ReceiptNumber = $"MK-{suffix}",
                ReceiptDate = DateTime.UtcNow,
                ReceivedByName = "Test"
            };
            db.GoodsReceipts.Add(receipt);
            await db.SaveChangesAsync();

            goodsReceiptId = receipt.Id;
            purchaseOrderId = order.Id;
        }

        var payload = new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            projectId = (Guid?)context.ProjectId,
            purchaseOrderId = (Guid?)purchaseOrderId,
            goodsReceiptId = (Guid?)goodsReceiptId,
            invoiceNumber = $"ALS-GR-{suffix}",
            invoiceDate = DateTime.UtcNow,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            invoiceType = 0,
            warehouseId = (Guid?)null,
            costCenterCode = (string?)null,
            items = new[]
            {
                new
                {
                    description = "Enerji kablosu",
                    quantity = 100m,
                    unit = "Metre",
                    unitPrice = 400m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    inventoryItemId = (Guid?)context.KabloItemId,
                    warehouseId = (Guid?)null,
                    expenseAccountId = (Guid?)null,
                    costCenterCode = (string?)null
                }
            }
        };

        await CreateAndApproveAsync(client, payload);

        using var verify = fixture.Factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var stockRows = await verifyDb.WarehouseStocks
            .CountAsync(x => x.InventoryItemId == context.KabloItemId);

        Assert.Equal(0, stockRows);

        var movements = await verifyDb.StockMovements
            .CountAsync(x => x.InventoryItemId == context.KabloItemId);

        Assert.Equal(0, movements);
    }

    [Fact]
    public async Task ExpenseInvoice_PostsToSelectedAccountsWithCostCenters()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var payload = new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            // Merkez gideri: projesi yok.
            projectId = (Guid?)null,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = $"GDR-{suffix}",
            invoiceDate = DateTime.UtcNow,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            invoiceType = 1,
            warehouseId = (Guid?)null,
            costCenterCode = context.MerkezCostCenter,
            items = new[]
            {
                new
                {
                    description = "Ofis elektriği",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 1_000m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    inventoryItemId = (Guid?)null,
                    warehouseId = (Guid?)null,
                    expenseAccountId = (Guid?)context.ElektrikAccountId,
                    costCenterCode = (string?)null
                },
                new
                {
                    description = "Kırtasiye",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 500m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    inventoryItemId = (Guid?)null,
                    warehouseId = (Guid?)null,
                    expenseAccountId = (Guid?)context.KirtasiyeAccountId,
                    costCenterCode = (string?)null
                }
            }
        };

        var invoiceId = await CreateAndApproveAsync(client, payload);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SupplierInvoices
            .SingleAsync(x => x.Id == invoiceId);

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == invoice.AccountingVoucherId);

        var elektrik = voucher.Lines.Single(x =>
            x.AccountingAccount.Code == "770.03.10");
        Assert.Equal(1_000m, elektrik.DebitAmount);
        Assert.Equal(context.MerkezCostCenter, elektrik.CostCenterCode);

        var kirtasiye = voucher.Lines.Single(x =>
            x.AccountingAccount.Code == "770.03.03");
        Assert.Equal(500m, kirtasiye.DebitAmount);

        // Merkez gideri projeye yazılmaz.
        Assert.Null(elektrik.ProjectId);

        var payable = voucher.Lines.Single(x => x.AccountingAccount.Code == "320");
        Assert.Equal(1_800m, payable.CreditAmount);
        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);

        // Projesiz gider proje maliyetine düşmez.
        var costRows = await db.ProjectCostTransactions
            .CountAsync(x => x.ReferenceId == invoiceId);

        Assert.Equal(0, costRows);
    }

    /// <summary>
    /// Aynı hesap + aynı masraf merkezine düşen kalemler tek satırda
    /// birleşir: 40 kalemlik kırtasiye faturası deftere 40 satır
    /// yazmamalı.
    /// </summary>
    [Fact]
    public async Task ExpenseInvoice_MergesLinesWithSameAccountAndCostCenter()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        object Line(decimal price, string description) => new
        {
            description,
            quantity = 1m,
            unit = "adet",
            unitPrice = price,
            vatRate = 20m,
            purchaseOrderItemId = (Guid?)null,
            inventoryItemId = (Guid?)null,
            warehouseId = (Guid?)null,
            expenseAccountId = (Guid?)context.KirtasiyeAccountId,
            costCenterCode = (string?)null
        };

        var payload = new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            projectId = (Guid?)null,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = $"GDR-M-{suffix}",
            invoiceDate = DateTime.UtcNow,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            invoiceType = 1,
            warehouseId = (Guid?)null,
            costCenterCode = context.MerkezCostCenter,
            items = new[]
            {
                Line(100m, "Kalem"),
                Line(200m, "Defter"),
                Line(300m, "Klasör")
            }
        };

        var invoiceId = await CreateAndApproveAsync(client, payload);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SupplierInvoices.SingleAsync(x => x.Id == invoiceId);

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == invoice.AccountingVoucherId);

        var expenseLines = voucher.Lines
            .Where(x => x.AccountingAccount.Code == "770.03.03")
            .ToList();

        Assert.Single(expenseLines);
        Assert.Equal(600m, expenseLines[0].DebitAmount);
    }

    [Fact]
    public async Task ExpenseInvoice_RejectsNonExpenseAccount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/supplier-invoices", new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            projectId = (Guid?)null,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = $"GDR-X-{suffix}",
            invoiceDate = DateTime.UtcNow,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            invoiceType = 1,
            warehouseId = (Guid?)null,
            costCenterCode = context.MerkezCostCenter,
            items = new[]
            {
                new
                {
                    description = "Yanlış hesap",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 100m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    inventoryItemId = (Guid?)null,
                    warehouseId = (Guid?)null,
                    // 320 Satıcılar bir gider hesabı değil.
                    expenseAccountId = (Guid?)context.PayableAccountId,
                    costCenterCode = (string?)null
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("gider/maliyet hesabı değil",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StockInvoice_RequiresInventoryItemOnEveryLine()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/supplier-invoices", new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            projectId = (Guid?)context.ProjectId,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = $"ALS-X-{suffix}",
            invoiceDate = DateTime.UtcNow,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            invoiceType = 0,
            warehouseId = (Guid?)context.MerkezWarehouseId,
            costCenterCode = (string?)null,
            items = new[]
            {
                new
                {
                    description = "Serbest metin kalemi",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 100m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    inventoryItemId = (Guid?)null,
                    warehouseId = (Guid?)null,
                    expenseAccountId = (Guid?)null,
                    costCenterCode = (string?)null
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("stok kartı seçilmelidir",
            await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// GERİYE UYUM: depo da stok kartı da verilmeyen alış faturası
    /// eskisi gibi düz maliyet faturası olarak işlenir. Stok kartı
    /// koşulsuz zorunlu tutulsaydı, hizmet/nakliye gibi depoya
    /// uğramayan alışlar ve yeni ekran çıkana kadarki mevcut girişler
    /// tamamen bloke olurdu.
    /// </summary>
    [Fact]
    public async Task StockInvoice_WithoutWarehouseOrItems_BehavesAsPlainCostInvoice()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var invoiceId = await CreateAndApproveAsync(client, new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            projectId = (Guid?)context.ProjectId,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = $"DUZ-{suffix}",
            invoiceDate = DateTime.UtcNow,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            items = new[]
            {
                new
                {
                    description = "Nakliye hizmeti",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 2_000m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null
                }
            }
        });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Stok hareketi oluşmaz.
        var movements = await db.StockMovements
            .CountAsync(x => x.CompanyId == context.CompanyId);
        Assert.Equal(0, movements);

        var invoice = await db.SupplierInvoices.SingleAsync(x => x.Id == invoiceId);

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == invoice.AccountingVoucherId);

        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        Assert.Equal(2_400m, voucher.TotalDebit);
    }

    /// <summary>
    /// Şantiye deposuna giren malzemenin masraf merkezi o şantiyenin
    /// projesidir — depo seçimi masraf merkezini kendiliğinden belirler.
    /// </summary>
    [Fact]
    public async Task StockInvoice_UsesWarehouseProjectAsCostCenter()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var invoiceId = await CreateAndApproveAsync(
            client, StockInvoice(context, suffix, context.SantiyeWarehouseId));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SupplierInvoices.SingleAsync(x => x.Id == invoiceId);

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == invoice.AccountingVoucherId);

        /*
         * 153 DEĞİL 150 (S6b).
         *
         * Stok hesabını artık kartın KATEGORİSİ belirliyor ve
         * varsayılan SARF — kategorisiz "Enerji Kablosu" kartı da
         * oraya düşüyor. Önceden hepsi finans ayarındaki tek
         * `InventoryAccountId` (153) hesabına gidiyordu; kullanıcı
         * kararı gereği taahhüt malzemesi ticari mal sayılmıyor.
         */
        var stockLine = voucher.Lines.Single(x => x.AccountingAccount.Code == "150");

        Assert.Equal(40_000m, stockLine.DebitAmount);
        Assert.Equal(context.ProjectCode, stockLine.CostCenterCode);
    }

    /// <summary>
    /// GERİYE UYUM: tip belirtilmeyen istek alış sayılır ve mevcut
    /// akış bozulmaz.
    /// </summary>
    [Fact]
    public async Task InvoiceWithoutTypeDefaultsToStock()
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
            invoiceNumber = $"ESK-{suffix}",
            invoiceDate = DateTime.UtcNow,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            warehouseId = (Guid?)context.MerkezWarehouseId,
            items = new[]
            {
                new
                {
                    description = "Kablo",
                    quantity = 10m,
                    unit = "Metre",
                    unitPrice = 100m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    inventoryItemId = (Guid?)context.KabloItemId
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var detail = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, detail.GetProperty("invoiceType").GetInt32());
        Assert.Equal("Alış (Stok)", detail.GetProperty("invoiceTypeName").GetString());
    }
}
