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
/// KATEGORİ → MUHASEBE HESABI eşlemesinin sözleşmeleri.
///
/// Şirket ağırlıklı taahhüt işi yaptığı için VARSAYILAN SARF
/// (150 / 740). Ticari mal (153 / 621) işareti bilinçli, ayrı izinli
/// bir eylemdir. Yanlış işaretlenmiş kategori stoku yanlış hesaba
/// taşır ve fark ancak mizanda görülür — o yüzden "unutulursa güvenli
/// tarafta kal" kuralı testle korunuyor.
/// </summary>
[Collection("Integration")]
public sealed class InventoryAccountingKindTests(DatabaseFixture fixture)
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

    /// <summary>
    /// Kategori oluşturma isteği muhasebe karşılığı ALMAZ.
    ///
    /// Alsaydı, kart açan depo sorumlusu farkında olmadan kategoriyi
    /// ticari mal işaretleyebilirdi. Alan yoksa unutmak da mümkün
    /// değil: yeni kategori HER ZAMAN sarf doğar.
    /// </summary>
    [Fact]
    public void KategoriOlusturmaIstegi_MuhasebeKarsiligiAlmaz()
    {
        var source = File.ReadAllText(
            Path.Combine(ApiPath(), "Controllers", "InventoryCategoriesController.cs"));

        var request = Regex.Match(
            source, @"record CategoryRequest\((?<body>[^)]*)\)", RegexOptions.Singleline);

        Assert.True(request.Success, "CategoryRequest bulunamadı — desen değişmiş olabilir.");
        Assert.DoesNotMatch(@"AccountingKind", request.Groups["body"].Value);
    }

    /// <summary>
    /// STOK hesap kodları TEK YERDE. Eşleme ikinci bir yere
    /// kopyalanırsa, biri güncellenip diğeri unutulduğunda stok bir
    /// hesaba girip başkasından çıkar; mizan sessizce şişer.
    ///
    /// Kural 150/153/621'i kapsıyor, 740'ı KAPSAMIYOR: 740 Hizmet
    /// Üretim Maliyeti stoka özgü değil — taşeron faturası
    /// (`SubcontractorInvoiceGenerator`) ve proje maliyeti
    /// sınıflandırması (`ProjectCostClassifier`) da onu kullanıyor ve
    /// bunlar meşru. Tekelleştirilebilen kodlar korunuyor,
    /// edilemeyen için sahte bir güvence verilmiyor.
    /// </summary>
    [Fact]
    public void HesapKodlari_YalnizCozumleyicideGecer()
    {
        // MUAFİYET LİSTESİ BOŞ. S6a'da AccountingIntegrationService
        // buradaydı: alış faturasını kendi ("153", "150") eşlemesiyle
        // yazıyordu. S6b'de çözümleyiciye bağlandı ve muafiyet kalktı.
        var exemptions = Array.Empty<string>();

        var offenders = new List<string>();
        var root = ApiPath();

        foreach (var folder in new[] { "Controllers", "Services" })
        {
            foreach (var file in Directory.GetFiles(
                Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name == "InventoryAccountResolver.cs") continue;
                if (exemptions.Contains(name)) continue;

                var code = Regex.Replace(File.ReadAllText(file), @"/\*[\s\S]*?\*/", " ");
                code = Regex.Replace(code, @"//[^\n]*", " ");

                if (Regex.IsMatch(code, @"""(150|153|621)(\.[0-9.]+)?"""))
                    offenders.Add(name);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Bu dosyalar stok/maliyet hesap kodunu doğrudan yazıyor: "
            + string.Join(", ", offenders)
            + ". Eşleme yalnız InventoryAccountResolver'da durmalı.");
    }

    /// <summary>
    /// Çözümleyici sarfı 150/740'a, ticari malı 153/621'e götürür ve
    /// ARADA ÜÇÜNCÜ BİR SESSİZ SEÇENEK YOKTUR.
    /// </summary>
    [Fact]
    public async Task Cozumleyici_SarfiVeTicariMali_DogruHesaplaraGoturur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = scope.ServiceProvider
            .GetRequiredService<IInventoryAccountResolver>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var accounts = new[] { "150", "153", "621", "740" }
            .Select(code => new AccountingAccount
            {
                CompanyId = company.Id,
                Code = code,
                Name = $"Test {code}"
            })
            .ToArray();

        db.AccountingAccounts.AddRange(accounts);
        await db.SaveChangesAsync();

        Guid IdOf(string code) => accounts.Single(x => x.Code == code).Id;

        Assert.Equal(IdOf("150"), await resolver.ResolveStockAccountAsync(
            company.Id, InventoryAccountingKind.Consumable, default));
        Assert.Equal(IdOf("740"), await resolver.ResolveConsumptionAccountAsync(
            company.Id, InventoryAccountingKind.Consumable, default));

        Assert.Equal(IdOf("153"), await resolver.ResolveStockAccountAsync(
            company.Id, InventoryAccountingKind.TradeGood, default));
        Assert.Equal(IdOf("621"), await resolver.ResolveConsumptionAccountAsync(
            company.Id, InventoryAccountingKind.TradeGood, default));
    }

    /// <summary>
    /// Kategorisiz kart SARF sayılır. Yanlış tarafa düşülecekse
    /// ticari mal tarafı OLMAMALI: 153'e yazılan sarf malzeme, mali
    /// tabloda satılabilir mal gibi görünür.
    /// </summary>
    [Fact]
    public async Task KategorisizKart_SarfSayilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = scope.ServiceProvider
            .GetRequiredService<IInventoryAccountResolver>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var item = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"MLZ-{suffix}",
            Name = $"Kategorisiz {suffix}",
            Unit = "adet"
        };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        Assert.Equal(
            InventoryAccountingKind.Consumable,
            await resolver.ResolveKindAsync(item.Id, default));
    }

    /// <summary>
    /// TİCARİ MAL İŞARETİ DEPO SORUMLUSUNUN İŞİ DEĞİL.
    ///
    /// Kategori açabilen rol (inventory.manage) hesabı değiştiremez;
    /// bu karar mali müşavirin (accounting.manage). Kart/kategori
    /// yönetimi ile muhasebe kararı bilinçli olarak ayrı izinlerde.
    /// </summary>
    [Fact]
    public async Task MuhasebeKarsiligi_DepoSorumlusuTarafindanDegistirilemez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = new InventoryCategory
        {
            Code = $"TEST-{suffix}",
            Name = $"Test Kategori {suffix}",
            SortOrder = 9999
        };
        db.InventoryCategories.Add(category);
        await db.SaveChangesAsync();

        // Varsayılan SARF doğdu mu?
        Assert.Equal(InventoryAccountingKind.Consumable, category.AccountingKind);

        var depocu = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, $"depo-{suffix}", ["Depo Sorumlusu"]);

        var reddedilen = await depocu.PutAsJsonAsync(
            $"/api/inventory/categories/{category.Id}/accounting-kind",
            new { accountingKind = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, reddedilen.StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(
            InventoryAccountingKind.Consumable,
            await verifyDb.InventoryCategories
                .Where(x => x.Id == category.Id)
                .Select(x => x.AccountingKind)
                .SingleAsync());

        // Yetkili el değiştirebiliyor mu (kural kilit değil, izinli)?
        var yetkili = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var kabul = await yetkili.PutAsJsonAsync(
            $"/api/inventory/categories/{category.Id}/accounting-kind",
            new { accountingKind = 1 });

        Assert.Equal(HttpStatusCode.OK, kabul.StatusCode);
    }
}
