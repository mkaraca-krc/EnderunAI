using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Projects;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Maliyet sınıfının KAYNAKTAN türetilmesi ve icmal kısmı bağlantısı.
///
/// Sınıf yanlış türetilirse icmal karşılaştırması sessizce yanılır:
/// taşeron işçiliği genel gidere düşerse işçilik bileşeni olduğundan
/// ucuz, GG&K olduğundan pahalı görünür.
/// </summary>
[Collection("Integration")]
public sealed class ProjectCostClassificationTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("740.03.11", ProjectCostClass.SubcontractorLabor)]
    [InlineData("740.03.11.01", ProjectCostClass.SubcontractorLabor)]
    [InlineData("740.01.01", ProjectCostClass.Labor)]
    [InlineData("770.01.02", ProjectCostClass.Labor)]
    [InlineData("740.03.10", ProjectCostClass.Overhead)]
    [InlineData("770.03.10", ProjectCostClass.Overhead)]
    [InlineData("740.04.01", ProjectCostClass.Overhead)]
    public void ExpenseAccount_MapsToExpectedClass(string code, ProjectCostClass expected)
    {
        Assert.Equal(expected, ProjectCostClassifier.ForExpenseAccount(code));
    }

    /// <summary>
    /// Hesabı bilinmeyen gider genel gider sayılır: bilinmeyeni malzeme
    /// ya da işçilik saymak karşılaştırmayı sessizce yanıltırdı.
    /// </summary>
    [Fact]
    public void UnknownExpenseAccount_FallsBackToOverhead()
    {
        Assert.Equal(ProjectCostClass.Overhead, ProjectCostClassifier.ForExpenseAccount(null));
        Assert.Equal(ProjectCostClass.Overhead, ProjectCostClassifier.ForExpenseAccount("  "));
        Assert.Equal(ProjectCostClass.Overhead, ProjectCostClassifier.ForExpenseAccount("320"));
    }

    /// <summary>ALIŞ faturası hesabına bakılmaksızın malzemedir.</summary>
    [Fact]
    public void StockInvoice_IsAlwaysMaterial()
    {
        Assert.Equal(
            ProjectCostClass.Material,
            ProjectCostClassifier.ForSupplierInvoice(SupplierInvoiceType.Stock, "740.03.11"));
    }

    // ELLE MALİYET TÜRÜ EŞLEMESİNİN TESTİ KALDIRILDI: eşlemenin tek
    // çağıranı, elle maliyet kaydı ucuydu ve o uç kapandı (tek kaynak
    // ilkesi — elle girilen maliyet artık gider kaydından geçiyor).
    // Kullanılmayan kodun testini tutmak, ölü kodu canlı sanmaktır;
    // gider kategorisi → maliyet sınıfı eşlemesi ise
    // ProjectRealizedCostTests içinde sınanıyor.

    private sealed record TestContext(
        Guid CompanyId,
        Guid ProjectId,
        Guid SectionId,
        Guid OtherProjectSectionId,
        Guid WarehouseId,
        Guid InventoryItemId,
        Guid SupplierId);

    private async Task<TestContext> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var otherProject = await TestDataFactory.CreateProjectAsync(db, $"{suffix}x");

        var section = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Order = 1,
            Name = "Kolon Kablo",
            IsActive = true
        };

        var otherSection = new ProjectHakedisSection
        {
            ProjectId = otherProject.Id,
            Order = 1,
            Name = "Başka Projenin Kısmı",
            IsActive = true
        };

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
            Name = $"Depo {suffix}",
            Type = WarehouseType.Site,
            IsActive = true
        };

        var item = new InventoryItem
        {
            CompanyId = project.CompanyId,
            Code = $"MLZ-{suffix}",
            Name = $"Kablo {suffix}",
            Unit = "mt",
            IsActive = true,
            AverageUnitCost = 25m
        };

        var supplier = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TED-{suffix}",
            Title = $"Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        db.ProjectHakedisSections.AddRange(section, otherSection);
        db.Warehouses.Add(warehouse);
        db.InventoryItems.Add(item);
        db.CurrentAccounts.Add(supplier);
        await db.SaveChangesAsync();

        db.WarehouseStocks.Add(new WarehouseStock
        {
            WarehouseId = warehouse.Id,
            InventoryItemId = item.Id,
            Quantity = 1_000m
        });
        await db.SaveChangesAsync();

        return new TestContext(
            project.CompanyId, project.Id, section.Id, otherSection.Id,
            warehouse.Id, item.Id, supplier.Id);
    }

    /// <summary>
    /// Depo sarfı malzeme olarak sınıflanır ve seçilen kısım hem stok
    /// hareketine hem maliyet kaydına işlenir.
    /// </summary>
    [Fact]
    public async Task StockIssue_ClassifiesAsMaterialAndCarriesSection()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = context.WarehouseId,
            inventoryItemId = context.InventoryItemId,
            projectId = context.ProjectId,
            projectSiteId = (Guid?)null,
            quantity = 40m,
            referenceNumber = (string?)null,
            movementDate = DateTime.UtcNow.Date,
            description = "Kolon kablosu çekimi",
            projectHakedisSectionId = context.SectionId
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cost = await db.ProjectCostTransactions
            .SingleAsync(x => x.ProjectId == context.ProjectId);

        Assert.Equal(ProjectCostClass.Material, cost.CostClass);
        Assert.Equal(context.SectionId, cost.ProjectHakedisSectionId);
        Assert.Equal(1_000m, cost.Amount);

        var movement = await db.StockMovements
            .SingleAsync(x => x.InventoryItemId == context.InventoryItemId &&
                              x.Type == StockMovementType.Issue);

        Assert.Equal(context.SectionId, movement.ProjectHakedisSectionId);
    }

    /// <summary>
    /// Kısım opsiyoneldir: seçilmezse sarf kaydedilir ve maliyet proje
    /// geneline yazılır. Zorunlu olsaydı saha, kısmını bilmediği bir
    /// sarfı hiç kaydedemezdi.
    /// </summary>
    [Fact]
    public async Task StockIssue_WithoutSection_IsAccepted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = context.WarehouseId,
            inventoryItemId = context.InventoryItemId,
            projectId = context.ProjectId,
            projectSiteId = (Guid?)null,
            quantity = 10m,
            referenceNumber = (string?)null,
            movementDate = DateTime.UtcNow.Date,
            description = "Genel sarf"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cost = await db.ProjectCostTransactions
            .SingleAsync(x => x.ProjectId == context.ProjectId);

        Assert.Null(cost.ProjectHakedisSectionId);
        Assert.Equal(ProjectCostClass.Material, cost.CostClass);
    }

    /// <summary>
    /// Başka projenin kısmına yazılan sarf iki projenin de analizini
    /// bozar; reddedilmeli.
    /// </summary>
    [Fact]
    public async Task StockIssue_WithForeignSection_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = context.WarehouseId,
            inventoryItemId = context.InventoryItemId,
            projectId = context.ProjectId,
            projectSiteId = (Guid?)null,
            quantity = 5m,
            referenceNumber = (string?)null,
            movementDate = DateTime.UtcNow.Date,
            description = "Yanlış kısım",
            projectHakedisSectionId = context.OtherProjectSectionId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("bu projeye ait değil", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Gider faturasında kalemler farklı sınıflara düşerse sınıf başına
    /// ayrı maliyet kaydı yazılır. Tek kayıt yazılsaydı taşeron
    /// işçiliğiyle nakliye aynı sınıfta toplanırdı.
    /// </summary>
    [Fact]
    public async Task ExpenseInvoice_SplitsCostRowsPerClass()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid subcontractorAccountId;
        Guid transportAccountId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var accounts = new[]
            {
                new AccountingAccount
                {
                    CompanyId = context.CompanyId, Code = "740.03.11",
                    Name = "DIŞARDAN SAĞLANAN İŞÇİLİKLER",
                    Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
                },
                new AccountingAccount
                {
                    CompanyId = context.CompanyId, Code = "740.03.10",
                    Name = "NAKLİYE GİDERLERİ",
                    Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
                },
                new AccountingAccount
                {
                    CompanyId = context.CompanyId, Code = "320", Name = "Satıcılar",
                    Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
                },
                new AccountingAccount
                {
                    CompanyId = context.CompanyId, Code = "191.01.03", Name = "İndirilecek KDV",
                    Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
                }
            };

            db.AccountingAccounts.AddRange(accounts);
            await db.SaveChangesAsync();

            subcontractorAccountId = accounts[0].Id;
            transportAccountId = accounts[1].Id;
        }

        var created = await client.PostAsJsonAsync("/api/supplier-invoices", new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            projectId = (Guid?)context.ProjectId,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = $"GDR-{suffix}",
            invoiceDate = DateTime.UtcNow.Date,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            invoiceType = 1,
            items = new[]
            {
                new
                {
                    description = "Taşeron işçilik",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 60_000m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    expenseAccountId = subcontractorAccountId
                },
                new
                {
                    description = "Nakliye",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 40_000m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    expenseAccountId = transportAccountId
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var invoiceId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/supplier-invoices/{invoiceId}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/supplier-invoices/{invoiceId}/approve", null)).StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var costs = await verifyDb.ProjectCostTransactions
            .Where(x => x.ReferenceType == "SupplierInvoice" && x.ReferenceId == invoiceId)
            .ToListAsync();

        Assert.Equal(2, costs.Count);
        Assert.Equal(60_000m,
            costs.Single(x => x.CostClass == ProjectCostClass.SubcontractorLabor).Amount);
        Assert.Equal(40_000m,
            costs.Single(x => x.CostClass == ProjectCostClass.Overhead).Amount);
    }

    /// <summary>
    /// Çok sınıflı faturanın iptalinde TÜM sınıflar dengelenmeli;
    /// yalnız ilki dengelenirse kalan maliyet projede asılı kalır.
    /// </summary>
    [Fact]
    public async Task CancellingMultiClassInvoice_OffsetsEveryClass()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid subcontractorAccountId;
        Guid transportAccountId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var accounts = new[]
            {
                new AccountingAccount
                {
                    CompanyId = context.CompanyId, Code = "740.03.11",
                    Name = "DIŞARDAN SAĞLANAN İŞÇİLİKLER",
                    Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
                },
                new AccountingAccount
                {
                    CompanyId = context.CompanyId, Code = "740.03.10",
                    Name = "NAKLİYE GİDERLERİ",
                    Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
                },
                new AccountingAccount
                {
                    CompanyId = context.CompanyId, Code = "320", Name = "Satıcılar",
                    Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
                },
                new AccountingAccount
                {
                    CompanyId = context.CompanyId, Code = "191.01.03", Name = "İndirilecek KDV",
                    Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
                }
            };

            db.AccountingAccounts.AddRange(accounts);
            await db.SaveChangesAsync();

            subcontractorAccountId = accounts[0].Id;
            transportAccountId = accounts[1].Id;
        }

        var created = await client.PostAsJsonAsync("/api/supplier-invoices", new
        {
            companyId = context.CompanyId,
            supplierCurrentAccountId = context.SupplierId,
            projectId = (Guid?)context.ProjectId,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = $"GDR2-{suffix}",
            invoiceDate = DateTime.UtcNow.Date,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            invoiceType = 1,
            items = new[]
            {
                new
                {
                    description = "Taşeron işçilik",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 10_000m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    expenseAccountId = subcontractorAccountId
                },
                new
                {
                    description = "Nakliye",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 5_000m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    expenseAccountId = transportAccountId
                }
            }
        });

        var invoiceId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsync($"/api/supplier-invoices/{invoiceId}/submit", null);
        await client.PostAsync($"/api/supplier-invoices/{invoiceId}/approve", null);

        var cancel = await client.PostAsJsonAsync(
            $"/api/supplier-invoices/{invoiceId}/cancel",
            new { reason = "Yanlış cariye kesilmiş" });

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var netByClass = await verifyDb.ProjectCostTransactions
            .Where(x => x.ReferenceType == "SupplierInvoice" && x.ReferenceId == invoiceId)
            .GroupBy(x => x.CostClass)
            .Select(g => new { g.Key, Net = g.Sum(x => x.Amount) })
            .ToListAsync();

        Assert.Equal(2, netByClass.Count);
        Assert.All(netByClass, x => Assert.Equal(0m, x.Net));
    }
}
