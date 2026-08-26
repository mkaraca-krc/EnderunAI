using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Inventory;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// STOK SATIR KİLİDİ — aynı kalem aynı anda iki yoldan çıkamaz.
///
/// TESTİN ZORLUĞU YARIŞI DETERMİNİSTİK KILMAK. "İki isteği paralel
/// gönder, birinin patlamasını bekle" biçimi kilit olmadan da ÇOĞU
/// ZAMAN yeşil geçer: birinci istek ikincisi okumadan önce biterse
/// ikincisi zaten taze veriyi görür. O yüzden burada satış tarafı
/// AÇIK BİR İŞLEM içinde tutuluyor ve zimmet isteğinin BEKLEDİĞİ
/// ölçülüyor — kilit yoksa beklemez, hemen geçer.
/// </summary>
[Collection("Integration")]
public sealed class StokSatirKilidiTests(DatabaseFixture fixture)
{
    private sealed record Ortam(
        Guid CompanyId, Guid WarehouseId, Guid ItemId, Guid PersonnelId, HttpClient Client);

    private async Task<Ortam> KurAsync(string ek, decimal stok)
    {
        Guid companyId, warehouseId, itemId, personnelId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, ek);
            await TestDataFactory.EnsureStockAccountsAsync(db, company.Id);

            var depo = new Warehouse
            {
                CompanyId = company.Id,
                BranchId = branch.Id,
                Code = $"DP-{ek}",
                Name = "Kilit Test Deposu",
                Type = WarehouseType.Central
            };
            db.Warehouses.Add(depo);

            var kalem = new InventoryItem
            {
                CompanyId = company.Id,
                Code = $"KLM-{ek}",
                Name = "Kilit Test Kalemi",
                Unit = "adet",
                Type = InventoryItemType.Consumable,
                AverageUnitCost = 100m
            };
            db.InventoryItems.Add(kalem);

            db.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = depo.Id,
                InventoryItemId = kalem.Id,
                Quantity = stok
            });

            var personel = await TestDataFactory.CreatePersonnelAsync(db, company.Id, ek);
            await db.SaveChangesAsync();

            companyId = company.Id;
            warehouseId = depo.Id;
            itemId = kalem.Id;
            personnelId = personel.Id;
        }

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, ek, ["Satın Alma Sorumlusu"], companyId);

        return new Ortam(companyId, warehouseId, itemId, personnelId, client);
    }

    private static object ZimmetIstegi(Ortam o, decimal miktar) => new
    {
        companyId = o.CompanyId,
        personnelId = o.PersonnelId,
        warehouseId = o.WarehouseId,
        inventoryItemId = o.ItemId,
        assignmentDate = DateTime.UtcNow,
        miktar
    };

    private async Task<decimal> StokAsync(Ortam o)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.WarehouseStocks
            .Where(x => x.WarehouseId == o.WarehouseId && x.InventoryItemId == o.ItemId)
            .Select(x => x.Quantity)
            .SingleAsync();
    }

    /// <summary>
    /// EŞZAMANLI SATIŞ + ZİMMET: BİRİ GEÇER, DİĞERİ TEMİZ HATA ALIR.
    ///
    /// Depoda 1 adet var. Satış işlemi açılıp malı düşürüyor ama
    /// KAYDETMİYOR (işlem açık). Bu sırada gelen zimmet isteği satır
    /// kilidinde BEKLEMEK ZORUNDA. Kilit kaldırılırsa zimmet beklemez,
    /// eski miktarı okur, "yeterli" der ve tek maldan iki çıkış olur.
    ///
    /// Kilidin varlığı iki bağımsız gözlemle ölçülüyor:
    ///   1. Satış işlemi açıkken zimmet isteği TAMAMLANMIYOR.
    ///   2. Satış kesinleşince zimmet "stok yetersiz" hatası alıyor.
    /// Yalnız ikincisine bakılsaydı sabotaj kaçabilirdi.
    /// </summary>
    [Fact]
    public async Task EszamanliSatisVeZimmet_BiriGecerDigeriTemizHataAlir()
    {
        var o = await KurAsync($"kl{DateTime.UtcNow:ffffff}", stok: 1m);

        using var satisScope = fixture.Factory.Services.CreateScope();
        var satisDb = satisScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issuer = satisScope.ServiceProvider.GetRequiredService<IStockSaleIssuer>();

        await using var satisIslemi = await satisDb.Database.BeginTransactionAsync();

        await issuer.IssueAsync(
            o.CompanyId,
            o.WarehouseId,
            [new StockSaleLine(o.ItemId, 1m, "kilit testi satış", "SAT-KLT-1")],
            DateTime.UtcNow,
            userId: null,
            CancellationToken.None);

        await satisDb.SaveChangesAsync();

        // Satış işlemi AÇIK. Zimmet isteği kilitte beklemeli.
        var zimmet = o.Client.PostAsJsonAsync(
            "/api/hr/assets/from-inventory", ZimmetIstegi(o, 1m));

        var bitenGorev = await Task.WhenAny(zimmet, Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.False(
            ReferenceEquals(bitenGorev, zimmet),
            "Zimmet isteği, satış işlemi hâlâ açıkken tamamlandı: satır "
            + "kilidi alınmıyor. Kilitsiz akışta iki istek de aynı "
            + "miktarı okur ve tek maldan iki çıkış yapılır.");

        await satisIslemi.CommitAsync();

        var cevap = await zimmet;

        Assert.Equal(HttpStatusCode.BadRequest, cevap.StatusCode);
        Assert.Contains(
            "yetersiz",
            await cevap.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0m, await StokAsync(o));
    }

    /// <summary>
    /// AYNI ANDA GELEN İKİ ZİMMET: TOPLAM ÇIKIŞ STOĞU AŞMAZ.
    ///
    /// Yukarıdaki test kilidin BEKLETTİĞİNİ ölçüyor; bu test gerçek
    /// paralellikte SONUCUN tutarlı kaldığını ölçüyor. Değişmez kural:
    /// kalan stok = başlangıç − (başarılı istek sayısı × miktar).
    /// Kilitsiz akışta iki istek de başarılı olur ama stok 0'da kalır
    /// ve eşitlik bozulur.
    /// </summary>
    [Fact]
    public async Task EszamanliIkiZimmet_CikisToplamiStogiAsmaz()
    {
        var o = await KurAsync($"k2{DateTime.UtcNow:ffffff}", stok: 1m);

        var ikinci = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, $"k2b{DateTime.UtcNow:ffffff}", ["Satın Alma Sorumlusu"], o.CompanyId);

        var cevaplar = await Task.WhenAll(
            o.Client.PostAsJsonAsync("/api/hr/assets/from-inventory", ZimmetIstegi(o, 1m)),
            ikinci.PostAsJsonAsync("/api/hr/assets/from-inventory", ZimmetIstegi(o, 1m)));

        var basarili = cevaplar.Count(x => x.StatusCode == HttpStatusCode.OK);

        Assert.Equal(1, basarili);
        Assert.Equal(1m - basarili, await StokAsync(o));
    }

    /// <summary>
    /// AYNI KALEM İKİ SATIR: İKİNCİ SATIR BİRİNCİYİ SİLMEZ.
    ///
    /// Kilit, izlenen kaydı tazeliyor. Tazeleme koşulsuz yapılsaydı
    /// aynı kalemi iki satırda içeren bir belge kendi kendini bozardı:
    /// ikinci satır, birincinin henüz kaydedilmemiş düşüşünü geri alır
    /// ve 5 stoktan 2+2 çıkınca 3 yerine 1 kalırdı. Korumanın kendisi
    /// korumak istediği hatayı üretirdi.
    /// </summary>
    [Fact]
    public async Task AyniKalemIkiSatir_IkinciSatirBirinciyiSilmez()
    {
        var o = await KurAsync($"k3{DateTime.UtcNow:ffffff}", stok: 5m);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issuer = scope.ServiceProvider.GetRequiredService<IStockSaleIssuer>();

        await using var islem = await db.Database.BeginTransactionAsync();

        await issuer.IssueAsync(
            o.CompanyId,
            o.WarehouseId,
            [
                new StockSaleLine(o.ItemId, 2m, "satır bir", "SAT-K3-1"),
                new StockSaleLine(o.ItemId, 2m, "satır iki", "SAT-K3-2")
            ],
            DateTime.UtcNow,
            userId: null,
            CancellationToken.None);

        await db.SaveChangesAsync();
        await islem.CommitAsync();

        Assert.Equal(1m, await StokAsync(o));
    }

    /// <summary>
    /// İŞLEM DIŞINDA KİLİT: SESSİZ GEÇMEZ, HATA VERİR.
    ///
    /// `FOR UPDATE` işlem dışında yalnız o ifade boyunca tutar. Sessiz
    /// geçilseydi, kilidi çağırdığı için korunduğunu sanan ama hiç
    /// korunmayan bir akış üretilirdi — kapatması en pahalı hata türü.
    /// </summary>
    [Fact]
    public async Task IslemDisindaKilit_TemizHataVerir()
    {
        var o = await KurAsync($"k4{DateTime.UtcNow:ffffff}", stok: 1m);

        using var scope = fixture.Factory.Services.CreateScope();
        var kilit = scope.ServiceProvider.GetRequiredService<IStokSatirKilidi>();

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(
            () => kilit.KilitleAsync(o.WarehouseId, o.ItemId, CancellationToken.None));

        Assert.Contains("işlem", hata.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// DEPODAN ÇIKIŞ UCU: KİLİTTEN SONRA YETERLİLİK YENİDEN SORULUR.
    ///
    /// `InventoryController.Issue` yeterlilik kontrolünü işlem
    /// AÇILMADAN ÖNCE yapıyordu. Kilidi işlem içine koyup kontrolü
    /// yerinde bırakmak hiçbir şey çözmezdi: istek kilitte bekler,
    /// sırası gelince malın gittiğini görmeden düşerdi.
    ///
    /// Bu test tam o satırı koruyor — kilitten sonraki ikinci kontrol
    /// silinirse burası kırmızıya döner.
    /// </summary>
    [Fact]
    public async Task DepodanCikis_KilittenSonraYeterlilikYenidenSorulur()
    {
        var o = await KurAsync($"k5{DateTime.UtcNow:ffffff}", stok: 1m);

        using var satisScope = fixture.Factory.Services.CreateScope();
        var satisDb = satisScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issuer = satisScope.ServiceProvider.GetRequiredService<IStockSaleIssuer>();

        await using var satisIslemi = await satisDb.Database.BeginTransactionAsync();

        await issuer.IssueAsync(
            o.CompanyId,
            o.WarehouseId,
            [new StockSaleLine(o.ItemId, 1m, "kilit testi satış", "SAT-K5-1")],
            DateTime.UtcNow,
            userId: null,
            CancellationToken.None);

        await satisDb.SaveChangesAsync();

        var cikis = o.Client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = o.WarehouseId,
            inventoryItemId = o.ItemId,
            quantity = 1m,
            movementDate = DateTime.UtcNow,
            description = "kilit testi çıkış"
        });

        var biten = await Task.WhenAny(cikis, Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.False(
            ReferenceEquals(biten, cikis),
            "Depodan çıkış isteği, satış işlemi açıkken tamamlandı: "
            + "satır kilidi alınmıyor.");

        await satisIslemi.CommitAsync();

        var cevap = await cikis;

        Assert.Equal(HttpStatusCode.Conflict, cevap.StatusCode);
        Assert.Equal(0m, await StokAsync(o));
    }
}
