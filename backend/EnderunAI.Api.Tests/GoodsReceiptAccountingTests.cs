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
/// MAL KABUL → MUHASEBE (GR/IR) sözleşmeleri.
///
/// Ölçüm: bu faz öncesinde 153 Ticari Mallar, 621 ve 600 hesaplarında
/// SIFIR fiş satırı vardı — stok hiç muhasebeye girmiyordu. Buradaki
/// kurallar o bağın bir daha kopmamasını sağlıyor.
/// </summary>
[Collection("Integration")]
public sealed class GoodsReceiptAccountingTests(DatabaseFixture fixture)
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

    /// <summary>
    /// MAL KABUL FİŞSİZ KESİNLEŞEMEZ.
    ///
    /// Fiş kesilmezse stok fiziken artar ama mali tabloda görünmez.
    /// Bu tam olarak bu fazdan önceki durumdu: depoda mal vardı,
    /// 150/153 sıfırdı.
    /// </summary>
    [Fact]
    public void MalKabul_FisKesmedenKesinlesemez()
    {
        var code = Source(Path.Combine("Services", "GoodsReceipts", "GoodsReceiptService.cs"));

        var postedAt = code.IndexOf(
            "receipt.Status = GoodsReceiptStatus.Posted;", StringComparison.Ordinal);

        Assert.True(postedAt > 0, "Kesinleşme satırı bulunamadı — desen değişmiş.");

        // Fiş üretimi kesinleşmeden ÖNCE olmalı: sonra olsaydı fiş
        // patladığında kabul "işlendi" kalır, stok muhasebesiz olurdu.
        var posterAt = code.IndexOf(
            "accountingPoster.PostAsync", StringComparison.Ordinal);

        Assert.True(posterAt > 0, "Mal kabul muhasebe fişi kesmiyor.");
        Assert.True(posterAt < postedAt,
            "Muhasebe fişi kesinleşmeden ÖNCE üretilmeli.");
    }

    /// <summary>
    /// MAL KABUL FİŞİ KDV YAZMAZ. Mal kabulde fatura yoktur; KDV
    /// yazılsaydı beyan edilecek vergi elde belge olmadan doğardı.
    /// </summary>
    [Fact]
    public void MalKabulFisi_KdvYazmaz()
    {
        var code = Source(Path.Combine(
            "Services", "Inventory", "GoodsReceiptAccountingPoster.cs"));

        code = Regex.Replace(code, @"/\*[\s\S]*?\*/", " ");
        code = Regex.Replace(code, @"//[^\n]*", " ");

        // Harf duyarsız: ilk hâli `Vat` arıyordu ve sonda `vatAmount`
        // ekleyince KAÇIRDI. Kural küçük harfli değişken adını da
        // görmeli.
        Assert.DoesNotMatch(
            new Regex(@"\bvat|\bkdv|""191""|""391""", RegexOptions.IgnoreCase),
            code);
    }

    /// <summary>
    /// MAL KABULE BAĞLI FATURA STOKU İKİNCİ KEZ YAZMAZ.
    ///
    /// Stok mal kabulde girdi, karşılığı 379.01'de bekliyor. Fatura o
    /// borcu kapatır. Fatura yine stoku borçlandırsaydı aynı mal iki
    /// kez bilançoya girer, stok değeri iki katına çıkardı.
    /// </summary>
    [Fact]
    public void MalKabuleBagliFatura_GrIrHesabiniKapatir()
    {
        var code = Source(Path.Combine(
            "Services", "Accounting", "AccountingIntegrationService.cs"));

        Assert.Matches(@"invoice\.GoodsReceiptId is not null", code);
        Assert.Matches(@"ResolveGoodsReceivedNotInvoicedAccountAsync", code);
    }

    /// <summary>
    /// GR/IR HESABI TOHUMLA AÇILIR ve VAR OLANI EZMEZ.
    ///
    /// 379 ana hesabı canlıda fiş kesilemez durumda; alt hesap
    /// açılmasaydı mal kabul fişi hiç kesilemezdi.
    /// </summary>
    [Fact]
    public async Task GrIrHesabi_TohumlaAcilirVeMevcuduEzmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        // Ana hesabı ve alt hesabı üretimdeki tohumla aynı yoldan kurar.
        await TestDataFactory.EnsureStockAccountsAsync(db, company.Id);

        var created = await db.AccountingAccounts.SingleAsync(x =>
            x.CompanyId == company.Id
            && x.Code == GoodsReceivedNotInvoicedAccountSeed.Code);

        Assert.True(created.IsPostingAllowed);
        Assert.Equal(4, created.Level);

        // Mali müşavir adı değiştirmişse tohum ONU EZMEMELİ.
        created.Name = "ELLE DEĞİŞTİRİLDİ";
        await db.SaveChangesAsync();

        await GoodsReceivedNotInvoicedAccountSeed.SeedAsync(db);

        var again = await db.AccountingAccounts.SingleAsync(x =>
            x.CompanyId == company.Id
            && x.Code == GoodsReceivedNotInvoicedAccountSeed.Code);

        Assert.Equal("ELLE DEĞİŞTİRİLDİ", again.Name);
    }

    /// <summary>
    /// TUTARLILIK RAPORU FARKI GÖRÜR.
    ///
    /// Muhasebesiz stok yaratıp raporun bunu yakaladığını doğruluyor:
    /// rapor "her şey yolunda" derse hiçbir işe yaramaz.
    /// </summary>
    [Fact]
    public async Task TutarlilikRaporu_MuhasebesizStokuYakalar()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = scope.ServiceProvider
            .GetRequiredService<IStockAccountingConsistencyService>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        await TestDataFactory.EnsureStockAccountsAsync(db, company.Id);

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

        // Muhasebe fişi OLMADAN stok: tam olarak raporun yakalaması
        // gereken durum.
        db.WarehouseStocks.Add(new WarehouseStock
        {
            WarehouseId = warehouse.Id,
            InventoryItemId = item.Id,
            Quantity = 4m
        });
        await db.SaveChangesAsync();

        /*
         * TASLAK FİŞ MİZANA SAYILMAZ.
         *
         * Kesinleşmemiş fiş mizanda yoktur; sayılsaydı rapor gerçekte
         * olmayan bir "denklik" gösterir ve muhasebesiz stoku örterdi.
         * Bu senaryo ilk turda testte YOKTU: sondada `Posted` filtresini
         * kaldırdığımda hiçbir test düşmedi.
         */
        var stockAccountId = await db.AccountingAccounts
            .Where(x => x.CompanyId == company.Id
                && x.Code == InventoryAccountResolver.ConsumableStockCode)
            .Select(x => x.Id)
            .SingleAsync();

        var draft = new AccountingVoucher
        {
            CompanyId = company.Id,
            VoucherNumber = $"TASLAK-{suffix}",
            Status = AccountingVoucherStatus.Draft,
            VoucherDate = DateTime.UtcNow.Date,
            TotalDebit = 100m,
            TotalCredit = 100m
        };
        db.AccountingVouchers.Add(draft);
        await db.SaveChangesAsync();

        db.AccountingVoucherLines.Add(new AccountingVoucherLine
        {
            AccountingVoucherId = draft.Id,
            AccountingAccountId = stockAccountId,
            LineNumber = 1,
            DebitAmount = 100m,
            CreditAmount = 0m,
            CurrencyCode = "TRY",
            ExchangeRate = 1m
        });
        await db.SaveChangesAsync();

        var result = await report.BuildAsync(company.Id, default);

        Assert.False(result.IsConsistent);

        var consumable = result.Lines.Single(x =>
            x.StockAccountCode == InventoryAccountResolver.ConsumableStockCode);

        Assert.Equal(100m, consumable.StockValue);
        // Taslak fişin 100 TL borcu bakiyeye GİRMEMELİ.
        Assert.Equal(0m, consumable.AccountBalance);
        Assert.Equal(100m, consumable.Difference);
        Assert.Contains("TUTARSIZLIK", result.Summary);
    }
}
