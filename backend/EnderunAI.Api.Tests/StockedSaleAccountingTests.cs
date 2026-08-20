using System.Net.Http.Json;
using System.Text.RegularExpressions;
using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Services.Inventory;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// STOKLU SATIŞ → MUHASEBE sözleşmeleri (S5).
///
/// ÖLÇÜM: bu fazdan önce 600 ve 621 hesaplarında SIFIR fiş satırı
/// vardı ve satış faturası kaleminde stok bağı hiç yoktu. Mal depodan
/// çıkıyor, 150/153 hiç alacaklanmıyordu — S6b'de kurulan mutabakat
/// raporu ilk satışta sapardı. Buradaki kurallar o deliğin bir daha
/// açılmamasını sağlıyor.
/// </summary>
[Collection("Integration")]
public sealed class StockedSaleAccountingTests(DatabaseFixture fixture)
{
    private static string ApiPath()
    {
        var dir = AppContext.BaseDirectory;

        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir, "EnderunAI.Api", "Controllers")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!, "EnderunAI.Api");
    }

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(ApiPath(), relative));

    private sealed record Scene(
        Guid CompanyId,
        Guid WarehouseId,
        Guid ItemId,
        Guid CustomerId,
        Guid StockAccountId,
        Guid CostAccountId);

    /// <summary>
    /// Stoklu satış faturası kesebilecek en küçük kurulum: depo, stok
    /// kartı (ağırlıklı ortalama maliyetli), müşteri ve hesap planı.
    /// </summary>
    private static async Task<Scene> BuildAsync(
        AppDbContext db, string suffix,
        decimal onHand = 10m, decimal unitCost = 40m,
        InventoryAccountingKind kind = InventoryAccountingKind.TradeGood)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var companyId = project.CompanyId;

        var branch = await db.Branches.FirstAsync(x => x.CompanyId == companyId);

        var warehouse = new Warehouse
        {
            CompanyId = companyId,
            BranchId = branch.Id,
            Code = $"DEPO-{suffix}",
            Name = $"Test Depo {suffix}",
            // Merkez DEĞİL: kullanıcı kararı gereği stoklu satış
            // faturası şantiye deposundan da yapılabilmeli.
            Type = WarehouseType.Site
        };
        db.Warehouses.Add(warehouse);

        var category = new InventoryCategory
        {
            Code = $"KAT-{suffix}",
            Name = $"Test Kategori {suffix}",
            AccountingKind = kind
        };
        db.InventoryCategories.Add(category);
        await db.SaveChangesAsync();

        var item = new InventoryItem
        {
            CompanyId = companyId,
            InventoryCategoryId = category.Id,
            Code = $"MLZ-{suffix}",
            Name = $"Test Malzeme {suffix}",
            Unit = "adet",
            AverageUnitCost = unitCost,
            SalesPrice = 100m,
            VatRate = 20m
        };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        db.WarehouseStocks.Add(new WarehouseStock
        {
            WarehouseId = warehouse.Id,
            InventoryItemId = item.Id,
            Quantity = onHand
        });

        await TestDataFactory.EnsureStockAccountsAsync(db, companyId);

        var revenue = new AccountingAccount
        {
            CompanyId = companyId,
            Code = $"600.{suffix}"[..12],
            Name = "Yurtiçi Satışlar",
            Nature = AccountingAccountNature.Credit,
            Level = 1,
            IsPostingAllowed = true
        };
        var vatOut = new AccountingAccount
        {
            CompanyId = companyId,
            Code = $"391.{suffix}"[..12],
            Name = "Hesaplanan KDV",
            Nature = AccountingAccountNature.Credit,
            Level = 1,
            IsPostingAllowed = true
        };
        var receivable = new AccountingAccount
        {
            CompanyId = companyId,
            Code = $"120.{suffix}"[..12],
            Name = "Alıcılar",
            Nature = AccountingAccountNature.Debit,
            Level = 1,
            IsPostingAllowed = true
        };
        db.AccountingAccounts.AddRange(revenue, vatOut, receivable);
        await db.SaveChangesAsync();

        db.CompanyFinanceSettings.Add(new CompanyFinanceSettings
        {
            CompanyId = companyId,
            SalesAccountId = revenue.Id,
            VatOutAccountId = vatOut.Id,
            ReceivablesAccountId = receivable.Id
        });
        await db.SaveChangesAsync();

        var stockCode = kind == InventoryAccountingKind.TradeGood
            ? InventoryAccountResolver.TradeGoodStockCode
            : InventoryAccountResolver.ConsumableStockCode;

        var stockAccountId = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == stockCode)
            .Select(x => x.Id).SingleAsync();

        var costAccountId = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId
                && x.Code == InventoryAccountResolver.TradeGoodCostCode)
            .Select(x => x.Id).SingleAsync();

        return new Scene(
            companyId, warehouse.Id, item.Id,
            project.EmployerCurrentAccountId!.Value,
            stockAccountId, costAccountId);
    }

    private static CreateSalesInvoiceRequest Request(
        Scene scene,
        decimal quantity = 2m,
        Guid? inventoryItemId = null,
        Guid? warehouseId = null,
        bool includeService = false)
    {
        var items = new List<SalesInvoiceItemRequest>
        {
            new("Satılan malzeme", quantity, "adet", 100m, 20m,
                inventoryItemId ?? scene.ItemId)
        };

        if (includeService)
        {
            // Stoksuz hizmet satırı: aynı faturada karışabilmeli.
            items.Add(new SalesInvoiceItemRequest(
                "Montaj işçiliği", 1m, "saat", 500m, 20m, null));
        }

        return new CreateSalesInvoiceRequest(
            scene.CompanyId,
            scene.CustomerId,
            null,
            $"GIB-{Guid.NewGuid():N}"[..16],
            DateTime.UtcNow.Date,
            null,
            "TRY",
            1m,
            0m,
            "Stoklu satış testi",
            null,
            items,
            warehouseId ?? scene.WarehouseId);
    }

    /// <summary>
    /// STOKLU SATIŞ FATURASI: mal depodan çıkar, 621 borçlanır,
    /// 153 alacaklanır.
    ///
    /// Bu fazdan önce fatura yalnız 120/600/391 yazıyordu; mal fiziken
    /// çıkmıyordu bile — kalemde stok bağı yoktu.
    /// </summary>
    [Fact]
    public async Task StokluFatura_MaliyetYazarVeStokDuser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = scope.ServiceProvider.GetRequiredService<ISalesInvoiceService>();

        var scene = await BuildAsync(db, suffix, onHand: 10m, unitCost: 40m);

        var created = await invoices.CreateAsync(Request(scene, quantity: 3m), default);

        // TASLAKTA MAL ÇIKMAZ: taslak bir plandır.
        var beforePost = await db.WarehouseStocks.AsNoTracking()
            .SingleAsync(x => x.WarehouseId == scene.WarehouseId);
        Assert.Equal(10m, beforePost.Quantity);

        await invoices.PostAsync(created.Id, default);

        var afterPost = await db.WarehouseStocks.AsNoTracking()
            .SingleAsync(x => x.WarehouseId == scene.WarehouseId);
        Assert.Equal(7m, afterPost.Quantity);

        var invoice = await db.SalesInvoices.AsNoTracking()
            .Include(x => x.Items)
            .SingleAsync(x => x.Id == created.Id);

        var line = invoice.Items.Single(x => x.InventoryItemId == scene.ItemId);
        Assert.Equal(40m, line.UnitCostAtSale);
        Assert.Equal(120m, line.LineCost);

        var voucherLines = await db.AccountingVoucherLines.AsNoTracking()
            .Where(x => x.AccountingVoucherId == invoice.AccountingVoucherId)
            .ToListAsync();

        var cost = voucherLines.Single(x => x.AccountingAccountId == scene.CostAccountId);
        Assert.Equal(120m, cost.DebitAmount);
        Assert.Equal(0m, cost.CreditAmount);

        var stock = voucherLines.Single(x => x.AccountingAccountId == scene.StockAccountId);
        Assert.Equal(120m, stock.CreditAmount);
        Assert.Equal(0m, stock.DebitAmount);
    }

    /// <summary>
    /// HİZMET SATIRI MALİYET ÜRETMEZ.
    ///
    /// Stoksuz satırda depodan çıkan bir mal yoktur; 621'e yazılsaydı
    /// hiç var olmayan bir malın maliyeti deftere girerdi.
    /// </summary>
    [Fact]
    public async Task HizmetSatiri_MaliyetUretmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = scope.ServiceProvider.GetRequiredService<ISalesInvoiceService>();

        var scene = await BuildAsync(db, suffix, onHand: 10m, unitCost: 40m);

        // Yalnız hizmet: stok kartı bağı yok, depo da yok.
        var request = new CreateSalesInvoiceRequest(
            scene.CompanyId, scene.CustomerId, null,
            $"GIB-{Guid.NewGuid():N}"[..16],
            DateTime.UtcNow.Date, null, "TRY", 1m, 0m,
            "Hizmet faturası", null,
            [new SalesInvoiceItemRequest("Danışmanlık", 1m, "saat", 1000m, 20m, null)],
            null);

        var created = await invoices.CreateAsync(request, default);
        await invoices.PostAsync(created.Id, default);

        var invoice = await db.SalesInvoices.AsNoTracking()
            .SingleAsync(x => x.Id == created.Id);

        var voucherLines = await db.AccountingVoucherLines.AsNoTracking()
            .Where(x => x.AccountingVoucherId == invoice.AccountingVoucherId)
            .ToListAsync();

        Assert.DoesNotContain(voucherLines, x => x.AccountingAccountId == scene.CostAccountId);
        Assert.DoesNotContain(voucherLines, x => x.AccountingAccountId == scene.StockAccountId);

        // Stok da hiç değişmemeli.
        var stock = await db.WarehouseStocks.AsNoTracking()
            .SingleAsync(x => x.WarehouseId == scene.WarehouseId);
        Assert.Equal(10m, stock.Quantity);
    }

    /// <summary>
    /// NEGATİF STOK YASAĞI SATIŞTA DA GEÇERLİ — olmayan mal satılamaz.
    ///
    /// Reddedilen satışta ne stok değişmeli ne fiş kesilmeli: yarım
    /// işlenmiş bir satış, hiç işlenmemişten beterdir.
    /// </summary>
    [Fact]
    public async Task YetersizStok_SatisiReddederVeHicbirSeyDegismez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = scope.ServiceProvider.GetRequiredService<ISalesInvoiceService>();

        var scene = await BuildAsync(db, suffix, onHand: 2m, unitCost: 40m);

        var created = await invoices.CreateAsync(Request(scene, quantity: 5m), default);

        await Assert.ThrowsAnyAsync<Exception>(
            () => invoices.PostAsync(created.Id, default));

        var stock = await db.WarehouseStocks.AsNoTracking()
            .SingleAsync(x => x.WarehouseId == scene.WarehouseId);
        Assert.Equal(2m, stock.Quantity);

        var invoice = await db.SalesInvoices.AsNoTracking()
            .SingleAsync(x => x.Id == created.Id);
        Assert.Null(invoice.AccountingVoucherId);
        Assert.Equal(SalesInvoiceStatus.Draft, invoice.Status);

        Assert.False(await db.StockMovements.AnyAsync(
            x => x.InventoryItemId == scene.ItemId));
    }

    /// <summary>
    /// DEPO SEÇİLMEDEN STOKLU FATURA KESİNLEŞMEZ.
    ///
    /// Malın nereden çıkacağı bilinmeden stok düşülemez; tahmin
    /// edilseydi yanlış depodan mal eksilirdi.
    /// </summary>
    [Fact]
    public async Task DeposuzStokluFatura_Kesinlesemez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = scope.ServiceProvider.GetRequiredService<ISalesInvoiceService>();

        var scene = await BuildAsync(db, suffix);

        var request = Request(scene) with { WarehouseId = null };
        var created = await invoices.CreateAsync(request, default);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => invoices.PostAsync(created.Id, default));

        Assert.Contains("depo", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MALİYET DONDURULUR: satıştan sonra kartın ortalaması değişse de
    /// faturanın maliyeti ve kârı değişmez.
    ///
    /// Dondurulmasaydı geçmiş bir satışın kârı, sonraki alımların
    /// fiyatına göre kendiliğinden değişirdi.
    /// </summary>
    [Fact]
    public async Task Maliyet_SatistaDondurulur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = scope.ServiceProvider.GetRequiredService<ISalesInvoiceService>();

        var scene = await BuildAsync(db, suffix, onHand: 10m, unitCost: 40m);

        var created = await invoices.CreateAsync(Request(scene, quantity: 2m), default);
        await invoices.PostAsync(created.Id, default);

        // Kartın ortalaması sonradan yükseliyor.
        var card = await db.InventoryItems.SingleAsync(x => x.Id == scene.ItemId);
        card.AverageUnitCost = 95m;
        await db.SaveChangesAsync();

        var line = await db.SalesInvoiceItems.AsNoTracking()
            .SingleAsync(x => x.SalesInvoiceId == created.Id
                && x.InventoryItemId == scene.ItemId);

        // Kart 95'e çıktı ama satır 40'ta kaldı.
        Assert.Equal(40m, line.UnitCostAtSale);
        Assert.Equal(80m, line.LineCost);
    }

    /// <summary>
    /// SATIR KÂRI MALİYET YETKİSİNE BAĞLI.
    ///
    /// Perakende satış ekranının maliyeti göstermemesi BİLİNÇLİ bir
    /// karardı (RetailSalesController'da gerekçesiyle duruyor): satış
    /// personeli maliyeti görmemeli. Satır kârı maliyeti ele verir,
    /// bu yüzden aynı kapıya bağlandı — `inventory.view`.
    ///
    /// Yeni bir izin anahtarı AÇILMADI: stok maliyetini bugün fiilen o
    /// izin koruyor ve fiyatlandırma ekranı da onu kullanıyor. İkinci
    /// bir anahtar iki ekranın zamanla ayrışmasına yol açardı.
    /// </summary>
    [Fact]
    public async Task SatirKari_MaliyetYetkisiOlmayanaGosterilmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid invoiceId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoices = scope.ServiceProvider.GetRequiredService<ISalesInvoiceService>();

            var scene = await BuildAsync(db, suffix, onHand: 10m, unitCost: 40m);

            var created = await invoices.CreateAsync(Request(scene, quantity: 2m), default);
            await invoices.PostAsync(created.Id, default);

            invoiceId = created.Id;
        }

        // Ön Muhasebe: faturayı görür (accounting.view) ama stok
        // maliyetini görmez (inventory.view yok).
        var masked = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, $"{suffix}m", ["Ön Muhasebe"]);

        var maskedResponse = await masked.GetFromJsonAsync<SalesInvoiceDetailResponse>(
            $"/api/sales-invoices/{invoiceId}");

        Assert.NotNull(maskedResponse);
        Assert.All(maskedResponse!.Items, x => Assert.Null(x.LineCost));
        Assert.All(maskedResponse.Items, x => Assert.Null(x.LineProfit));

        // Gizlendiği SAKLANMIYOR: kaç satırın maliyeti gizlendi, söyleniyor.
        Assert.Equal(1, maskedResponse.HiddenCostCount);

        // Genel Müdür ikisini de görür.
        var full = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, $"{suffix}f", ["Genel Müdür"]);

        var fullResponse = await full.GetFromJsonAsync<SalesInvoiceDetailResponse>(
            $"/api/sales-invoices/{invoiceId}");

        Assert.NotNull(fullResponse);
        var stocked = fullResponse!.Items.Single(x => x.InventoryItemId is not null);

        Assert.Equal(80m, stocked.LineCost);
        // Satır kârı = matrah (2 × 100) − maliyet (2 × 40).
        Assert.Equal(120m, stocked.LineProfit);
        Assert.Equal(0, fullResponse.HiddenCostCount);
    }

    /// <summary>
    /// PERAKENDE VE FATURA AYNI MALİYET MANTIĞINI KULLANIR.
    ///
    /// Kural iki belgeye ayrı yazılsaydı biri kategoriye göre ayırırken
    /// diğeri sabit hesaba yazabilir ve aynı malzeme hangi ekrandan
    /// satıldığına göre farklı hesaptan düşerdi.
    /// </summary>
    [Fact]
    public void SatisMaliyetiEslemesi_TekYerdeDurur()
    {
        var offenders = new List<string>();

        var root = ApiPath();
        var allowed = new[]
        {
            Path.Combine("Services", "Inventory", "SaleCostLineBuilder.cs"),
            Path.Combine("Services", "Inventory", "InventoryAccountResolver.cs")
        };

        foreach (var folder in new[] { "Controllers", "Services" })
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (allowed.Contains(relative)) continue;

                var code = File.ReadAllText(file);
                code = Regex.Replace(code, @"/\*[\s\S]*?\*/", " ");
                code = Regex.Replace(code, @"//[^\n]*", " ");

                // Yalnız SATIŞ maliyetiyle ilgili dosyalar: 621'i
                // string olarak taşıyan başka bir yer olmamalı.
                if (Regex.IsMatch(code, "\"621"))
                    offenders.Add(relative);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "621 hesap kodu çözümleyici dışında geçiyor: "
            + string.Join(", ", offenders)
            + ". Eşleme tek yerde kalmalı, yoksa aynı malzeme hangi "
            + "ekrandan satıldığına göre farklı hesaba yazılır.");
    }

    /// <summary>
    /// SATIŞTA STOK ÇIKIŞI TEK KAPIDAN.
    ///
    /// Perakende fişi ve satış faturası aynı çıkışçıyı kullanmalı;
    /// biri kendi döngüsünü yazarsa negatif stok yasağı ve maliyet
    /// dondurma kuralı zamanla ayrışır.
    /// </summary>
    [Fact]
    public void SatisYollari_AyniStokCikisKapisiniKullanir()
    {
        var retail = Source(Path.Combine("Services", "Retail", "RetailSaleService.cs"));
        var invoice = Source(Path.Combine("Services", "Accounting", "SalesInvoiceService.cs"));

        Assert.Contains("stockIssuer.IssueAsync", retail);
        Assert.Contains("stockIssuer.IssueAsync", invoice);

        // Kendi düşüş döngüsünü yazmamalılar.
        Assert.DoesNotMatch(@"stock\.Quantity\s*-=", retail);
        Assert.DoesNotMatch(@"stock\.Quantity\s*-=", invoice);
    }

    /// <summary>
    /// STOK VE FİŞ AYNI TRANSACTION'DA.
    ///
    /// Ayrı olsaydı stok çıkıp fiş kesilemediğinde mal muhasebesiz
    /// giderdi — S6b'de mal kabulünde kapatılan deliğin satış eşi.
    /// </summary>
    [Fact]
    public void StokVeFis_AyniTransactionda()
    {
        var code = Source(Path.Combine("Services", "Accounting", "SalesInvoiceService.cs"));

        var transactionAt = code.IndexOf(
            "await db.Database.BeginTransactionAsync", StringComparison.Ordinal);
        var issueAt = code.IndexOf("stockIssuer.IssueAsync", StringComparison.Ordinal);
        var voucherAt = code.IndexOf(
            "CreateSalesInvoiceVoucherAsync", StringComparison.Ordinal);

        Assert.True(transactionAt > 0, "Transaction bulunamadı.");
        Assert.True(issueAt > transactionAt,
            "Stok çıkışı transaction'ın İÇİNDE olmalı.");
        Assert.True(voucherAt > issueAt,
            "Fiş, stok çıkışıyla aynı transaction içinde ve sonrasında olmalı.");

        // Stok için AYRI bir transaction açılmamalı.
        var transactionCount = Regex.Matches(
            code, @"BeginTransactionAsync").Count;

        Assert.True(transactionCount <= 2,
            $"Kesinleştirmede beklenenden fazla transaction var ({transactionCount}); "
            + "stok ve fiş ayrı transaction'lara bölünmüş olabilir.");
    }
}
