using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Inventory;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DEPODAN ÇIKIŞ → MUHASEBE sözleşmeleri (S6c).
///
/// ÖLÇÜM: bu fazdan önce depo çıkışı ve sayım düzeltmesi HİÇ fiş
/// kesmiyordu — 740'ta yalnız 1 satır vardı (o da tedarikçi
/// faturasından) ve hiçbir proje maliyet kaydı stok hareketinden
/// doğmamıştı. Taahhüt işinde malzemenin çoğu satılmaz, projeye
/// gider; yani bu en sık kullanılan yoldu ve açık kaldığı sürece stok
/// ile muhasebe İLK çıkışta ayrışırdı.
/// </summary>
[Collection("Integration")]
public sealed class StockConsumptionAccountingTests(DatabaseFixture fixture)
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

    private sealed record Scene(
        Guid CompanyId, Guid WarehouseId, Guid ItemId, Guid ProjectId, string ProjectCode);

    private static async Task<Scene> BuildAsync(
        AppDbContext db, string suffix,
        decimal onHand = 100m, decimal unitCost = 30m,
        InventoryAccountingKind kind = InventoryAccountingKind.Consumable)
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
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(warehouse);

        var category = new InventoryCategory
        {
            Code = $"KAT-{suffix}",
            Name = $"Kategori {suffix}",
            AccountingKind = kind
        };
        db.InventoryCategories.Add(category);
        await db.SaveChangesAsync();

        var item = new InventoryItem
        {
            CompanyId = companyId,
            InventoryCategoryId = category.Id,
            Code = $"MLZ-{suffix}",
            Name = $"Malzeme {suffix}",
            Unit = "adet",
            AverageUnitCost = unitCost
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
        await db.SaveChangesAsync();

        return new Scene(companyId, warehouse.Id, item.Id, project.Id, project.Code);
    }

    private static async Task<Guid> AccountIdAsync(AppDbContext db, Guid companyId, string code) =>
        await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == code)
            .Select(x => x.Id).SingleAsync();

    /// <summary>
    /// PROJEYE ÇIKIŞ FİŞ KESER: borç 740.03.09, alacak 150.
    ///
    /// Fişsiz çıkış tam olarak bu fazdan önceki durumdu: mal
    /// gidiyordu, 150 hiç alacaklanmıyordu.
    /// </summary>
    [Fact]
    public async Task ProjeyeCikis_740Borc_150Alacak_YazarVeStokDuser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 100m, unitCost: 30m);

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, suffix, ["Genel Müdür"]);

        var response = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = scene.ItemId,
            quantity = 4m,
            projectId = scene.ProjectId,
            movementDate = DateTime.UtcNow.Date,
            description = "Şantiyeye sarf"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stock = await db.WarehouseStocks.AsNoTracking()
            .SingleAsync(x => x.WarehouseId == scene.WarehouseId);
        Assert.Equal(96m, stock.Quantity);

        var movement = await db.StockMovements.AsNoTracking()
            .SingleAsync(x => x.InventoryItemId == scene.ItemId);

        Assert.NotNull(movement.AccountingVoucherId);

        var lines = await db.AccountingVoucherLines.AsNoTracking()
            .Where(x => x.AccountingVoucherId == movement.AccountingVoucherId)
            .ToListAsync();

        // Alt hesap TERCİH EDİLİYOR: 740 ana hesabı da var ama malzeme
        // 740.03.09'a yazılmalı.
        var expenseId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.ProjectMaterialExpenseCode);
        var stockId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.ConsumableStockCode);

        var debit = lines.Single(x => x.AccountingAccountId == expenseId);
        Assert.Equal(120m, debit.DebitAmount);

        var credit = lines.Single(x => x.AccountingAccountId == stockId);
        Assert.Equal(120m, credit.CreditAmount);

        // PROJE ETİKETİ TAŞINIYOR — maliyet çıkışta doğuyor ve hangi
        // projede doğduğu bu satırın anlattığı şey.
        Assert.All(lines, x => Assert.Equal(scene.ProjectId, x.ProjectId));
    }

    /// <summary>
    /// TİCARİ MAL PROJEYE GİDERSE DE 740'a yazılır, 621'e değil —
    /// satılmamış, projede tüketilmiştir. Ayrılan yalnız ALACAK
    /// tarafıdır: 153.
    /// </summary>
    [Fact]
    public async Task TicariMalProjeyeCikarsa_740Borc_153Alacak()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(
            db, suffix, onHand: 50m, unitCost: 10m,
            kind: InventoryAccountingKind.TradeGood);

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, suffix, ["Genel Müdür"]);

        var response = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = scene.ItemId,
            quantity = 3m,
            projectId = scene.ProjectId,
            movementDate = DateTime.UtcNow.Date
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var movement = await db.StockMovements.AsNoTracking()
            .SingleAsync(x => x.InventoryItemId == scene.ItemId);

        var lines = await db.AccountingVoucherLines.AsNoTracking()
            .Where(x => x.AccountingVoucherId == movement.AccountingVoucherId)
            .ToListAsync();

        var expenseId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.ProjectMaterialExpenseCode);
        var tradeGoodId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.TradeGoodStockCode);
        var cogsId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.TradeGoodCostCode);

        Assert.Equal(30m, lines.Single(x => x.AccountingAccountId == expenseId).DebitAmount);
        Assert.Equal(30m, lines.Single(x => x.AccountingAccountId == tradeGoodId).CreditAmount);

        // 621 SATIŞ hesabıdır; projede tüketilen mal oraya yazılmamalı.
        Assert.DoesNotContain(lines, x => x.AccountingAccountId == cogsId);
    }

    /// <summary>
    /// PROJESİZ ÇIKIŞ 770'e yazılır (kullanıcı kararı).
    ///
    /// 740'a yazılsaydı hiç iş yapılmamışken üretim maliyeti doğar,
    /// proje kârlılık raporları ve hakediş kıyasları şişerdi.
    /// </summary>
    [Fact]
    public async Task ProjesizCikis_770eYazilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 20m, unitCost: 25m);

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, suffix, ["Genel Müdür"]);

        var response = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = scene.ItemId,
            quantity = 2m,
            movementDate = DateTime.UtcNow.Date,
            description = "Ofis sarfı"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var movement = await db.StockMovements.AsNoTracking()
            .SingleAsync(x => x.InventoryItemId == scene.ItemId);

        var lines = await db.AccountingVoucherLines.AsNoTracking()
            .Where(x => x.AccountingVoucherId == movement.AccountingVoucherId)
            .ToListAsync();

        var adminId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.GeneralAdminExpenseCode);
        var productionId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.ProjectMaterialExpenseCode);

        Assert.Equal(50m, lines.Single(x => x.AccountingAccountId == adminId).DebitAmount);
        Assert.DoesNotContain(lines, x => x.AccountingAccountId == productionId);

        // Projesiz çıkışta proje etiketi de olmamalı.
        Assert.All(lines, x => Assert.Null(x.ProjectId));
    }

    /// <summary>
    /// SAYIM NOKSANI 689.02'ye, FAZLASI 649.03'e yazılır ve yön doğru
    /// döner (kullanıcı kararı).
    /// </summary>
    [Fact]
    public async Task SayimFarki_NoksanVeFazla_DogruHesabaVeYoneYazilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 100m, unitCost: 20m);

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, suffix, ["Genel Müdür"]);

        // NOKSAN: 100 -> 90, 10 adet × 20 = 200 TL kayıp.
        var shortage = await client.PostAsJsonAsync("/api/inventory/adjustments", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = scene.ItemId,
            countedQuantity = 90m,
            movementDate = DateTime.UtcNow.Date,
            description = "Fire"
        });
        Assert.Equal(HttpStatusCode.OK, shortage.StatusCode);

        var shortageMovement = await db.StockMovements.AsNoTracking()
            .Where(x => x.InventoryItemId == scene.ItemId && x.Quantity < 0)
            .SingleAsync();

        var shortageLines = await db.AccountingVoucherLines.AsNoTracking()
            .Where(x => x.AccountingVoucherId == shortageMovement.AccountingVoucherId)
            .ToListAsync();

        var lossId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.InventoryShortageCode);
        var stockId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.ConsumableStockCode);

        Assert.Equal(200m, shortageLines.Single(x => x.AccountingAccountId == lossId).DebitAmount);
        Assert.Equal(200m, shortageLines.Single(x => x.AccountingAccountId == stockId).CreditAmount);

        // FAZLA: 90 -> 95, 5 adet × 20 = 100 TL. Yön TERS dönmeli.
        var surplus = await client.PostAsJsonAsync("/api/inventory/adjustments", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = scene.ItemId,
            countedQuantity = 95m,
            movementDate = DateTime.UtcNow.Date,
            description = "Sayım fazlası"
        });
        Assert.Equal(HttpStatusCode.OK, surplus.StatusCode);

        var surplusMovement = await db.StockMovements.AsNoTracking()
            .Where(x => x.InventoryItemId == scene.ItemId && x.Quantity > 0)
            .SingleAsync();

        var surplusLines = await db.AccountingVoucherLines.AsNoTracking()
            .Where(x => x.AccountingVoucherId == surplusMovement.AccountingVoucherId)
            .ToListAsync();

        var gainId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.InventorySurplusCode);

        Assert.Equal(100m, surplusLines.Single(x => x.AccountingAccountId == gainId).CreditAmount);
        Assert.Equal(100m, surplusLines.Single(x => x.AccountingAccountId == stockId).DebitAmount);
    }

    /// <summary>
    /// STOK VE MUHASEBE TAM KAPANIYOR: giriş, çıkış ve sayım farkından
    /// sonra mutabakat raporu SIFIR fark vermeli.
    ///
    /// Bu fazın asıl iddiası bu — tek tek hesap kontrolleri değil,
    /// üçü bir arada koştuğunda raporun tutması.
    /// </summary>
    [Fact]
    public async Task GirisCikisVeSayimSonrasi_MutabakatSifirFarkVerir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = scope.ServiceProvider
            .GetRequiredService<IStockAccountingConsistencyService>();

        // Depo BOŞ başlıyor: girişi de fişiyle birlikte biz yapacağız.
        var scene = await BuildAsync(db, suffix, onHand: 0m, unitCost: 50m);

        var stockAccountId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.ConsumableStockCode);
        var grirId = await AccountIdAsync(
            db, scene.CompanyId, InventoryAccountResolver.GoodsReceivedNotInvoicedCode);

        // GİRİŞ: 10 adet × 50 = 500 TL. Mal kabulün ürettiği fişin
        // aynısı (borç 150, alacak 379.01).
        var stock = await db.WarehouseStocks
            .SingleAsync(x => x.WarehouseId == scene.WarehouseId);
        stock.Quantity = 10m;

        var entry = new AccountingVoucher
        {
            CompanyId = scene.CompanyId,
            VoucherNumber = $"GIRIS-{suffix}",
            Status = AccountingVoucherStatus.Posted,
            VoucherDate = DateTime.UtcNow.Date,
            TotalDebit = 500m,
            TotalCredit = 500m
        };
        db.AccountingVouchers.Add(entry);
        await db.SaveChangesAsync();

        db.AccountingVoucherLines.AddRange(
            new AccountingVoucherLine
            {
                AccountingVoucherId = entry.Id,
                AccountingAccountId = stockAccountId,
                LineNumber = 1,
                DebitAmount = 500m,
                CreditAmount = 0m,
                CurrencyCode = "TRY",
                ExchangeRate = 1m
            },
            new AccountingVoucherLine
            {
                AccountingVoucherId = entry.Id,
                AccountingAccountId = grirId,
                LineNumber = 2,
                DebitAmount = 0m,
                CreditAmount = 500m,
                CurrencyCode = "TRY",
                ExchangeRate = 1m
            });
        await db.SaveChangesAsync();

        var before = await report.BuildAsync(scene.CompanyId, default);
        Assert.True(before.IsConsistent, "Giriş sonrası zaten tutmalıydı.");

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, suffix, ["Genel Müdür"]);

        // ÇIKIŞ: 4 adet projeye.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/inventory/issues", new
            {
                warehouseId = scene.WarehouseId,
                inventoryItemId = scene.ItemId,
                quantity = 4m,
                projectId = scene.ProjectId,
                movementDate = DateTime.UtcNow.Date
            })).StatusCode);

        // SAYIM NOKSANI: 6 -> 5.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/inventory/adjustments", new
            {
                warehouseId = scene.WarehouseId,
                inventoryItemId = scene.ItemId,
                countedQuantity = 5m,
                movementDate = DateTime.UtcNow.Date,
                description = "Fire"
            })).StatusCode);

        var after = await report.BuildAsync(scene.CompanyId, default);

        var line = after.Lines.Single(x =>
            x.StockAccountCode == InventoryAccountResolver.ConsumableStockCode);

        // Depoda 5 × 50 = 250 kaldı; mizan da 500 − 200 − 50 = 250.
        Assert.Equal(250m, line.StockValue);
        Assert.Equal(250m, line.AccountBalance);
        Assert.Equal(0m, line.Difference);
        Assert.True(after.IsConsistent,
            "Giriş + çıkış + sayım sonrası mutabakat SIFIR fark vermeliydi.");
    }

    /// <summary>
    /// ÇIKIŞ FİŞSİZ KESİNLEŞEMEZ: fiş üretimi stok düşüşüyle aynı
    /// transaction içinde ve kaydetmeden ÖNCE olmalı.
    ///
    /// Ayrı olsaydı fiş patladığında mal muhasebesiz giderdi — S6b'de
    /// mal kabulünde kapatılan deliğin çıkış tarafındaki eşi.
    /// </summary>
    [Fact]
    public void CikisVeSayim_FisiAyniTransactiondaKeser()
    {
        var code = File.ReadAllText(
            Path.Combine(ApiPath(), "Controllers", "InventoryController.cs"));

        foreach (var (name, poster) in new[]
        {
            ("çıkış", "consumptionPoster.PostIssueAsync"),
            ("sayım", "consumptionPoster.PostAdjustmentAsync")
        })
        {
            var posterAt = code.IndexOf(poster, StringComparison.Ordinal);
            Assert.True(posterAt > 0, $"{name} muhasebe fişi kesmiyor.");

            var transactionAt = code.LastIndexOf(
                "BeginTransactionAsync", posterAt, StringComparison.Ordinal);

            Assert.True(transactionAt > 0,
                $"{name} fişi transaction dışında kesiliyor.");

            var commitAt = code.IndexOf(
                "CommitAsync", posterAt, StringComparison.Ordinal);

            Assert.True(commitAt > posterAt,
                $"{name} fişi commit'ten SONRA kesiliyor.");
        }
    }

    /// <summary>
    /// ÇIKIŞ HESAP KODLARI TEK YERDE. Eşleme ikinci bir dosyaya
    /// kopyalanırsa aynı malzeme hangi uçtan çıktığına göre farklı
    /// hesaba yazılmaya başlar ve fark ancak envanterde görülür.
    ///
    /// KAPSAM DARALTMASI (dürüstlük notu): kural 689 ve 649'u
    /// kapsıyor; 740 ve 770'i KAPSAMIYOR.
    ///
    /// İkisi de stoka özgü değil ve meşru başka kullanıcıları var —
    /// ölçüldü: 770'i `SubcontractorInvoiceGenerator` (taşeron gider
    /// hesabı adayları), `ProjectCostClassifier` (maliyet sınıfı),
    /// `AccountingIntegrationService` ve `DatabaseSeeder` (hesap planı
    /// tohumu) kullanıyor; 740'ı da ilk ikisi. Bunları tekelleştirmek
    /// ilgisiz akışları kırardı.
    ///
    /// 689.02 ve 649.03 bu fazda AÇILDI ve yalnız stok sayım farkına
    /// ait; onlar tekelleştirilebiliyor ve ediliyor. Tekelleştirilemeyen
    /// kod için sahte güvence verilmiyor.
    /// </summary>
    [Fact]
    public void CikisHesapKodlari_YalnizCozumleyicideGecer()
    {
        var root = ApiPath();

        var allowed = new[]
        {
            Path.Combine("Services", "Inventory", "InventoryAccountResolver.cs"),
            Path.Combine("Data", "StockVarianceAccountSeed.cs")
        };

        var offenders = new List<string>();

        foreach (var folder in new[] { "Controllers", "Services", "Data" })
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (allowed.Contains(relative)) continue;

                var text = File.ReadAllText(file);
                text = Regex.Replace(text, @"/\*[\s\S]*?\*/", " ");
                text = Regex.Replace(text, @"//[^\n]*", " ");

                if (Regex.IsMatch(text, "\"(689|649)"))
                    offenders.Add(relative);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "689/649 hesap kodları çözümleyici dışında geçiyor: "
            + string.Join(", ", offenders)
            + ". Sayım farkı eşlemesi tek yerde kalmalı.");
    }
}
