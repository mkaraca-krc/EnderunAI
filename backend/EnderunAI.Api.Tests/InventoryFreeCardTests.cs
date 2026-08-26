using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Inventory;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// SERBEST KART ÖZELLİKLERİ (S9): proje bağı, tedarik tipi, galeri.
///
/// Buradaki kuralların ortak amacı tek: özel imal edilmiş bir malzemenin
/// AİT OLDUĞU İŞTEN sessizce kopmasını engellemek. Kopma iki yoldan
/// olur — başka projeye çıkış ve satış — ve ikisi de kapalı.
/// </summary>
[Collection("Integration")]
public sealed class InventoryFreeCardTests(DatabaseFixture fixture)
{
    private sealed record Scene(
        Guid CompanyId,
        Guid WarehouseId,
        Guid OwnProjectId,
        Guid OtherProjectId,
        Guid BoundItemId,
        Guid FreeItemId);

    private static async Task<Scene> BuildAsync(AppDbContext db, string suffix)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var companyId = project.CompanyId;
        var branch = await db.Branches.FirstAsync(x => x.CompanyId == companyId);

        // İkinci proje AYNI şirkette: kural "başka şirket" değil
        // "başka iş" kuralı; aynı şirkette de geçerli olmalı.
        var other = new Project
        {
            CompanyId = companyId,
            BranchId = branch.Id,
            Code = $"PRJ2-{suffix}",
            Name = $"Diğer Proje {suffix}"
        };
        db.Projects.Add(other);

        var warehouse = new Warehouse
        {
            CompanyId = companyId,
            BranchId = branch.Id,
            Code = $"DEPO-{suffix}",
            Name = $"Depo {suffix}",
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        async Task<Guid> CardAsync(string tag, Guid? boundProjectId)
        {
            var item = new InventoryItem
            {
                CompanyId = companyId,
                Code = $"MLZ-{tag}-{suffix}",
                Name = $"Dekoratif Armatür {tag} {suffix}",
                Unit = "adet",
                AverageUnitCost = 1000m,
                ProjectId = boundProjectId,
                SupplyKind = boundProjectId.HasValue
                    ? InventorySupplyKind.CustomManufacture
                    : InventorySupplyKind.Stocked
            };
            db.InventoryItems.Add(item);
            await db.SaveChangesAsync();

            db.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = warehouse.Id,
                InventoryItemId = item.Id,
                Quantity = 10m
            });
            await db.SaveChangesAsync();

            return item.Id;
        }

        var bound = await CardAsync("B", project.Id);
        var free = await CardAsync("S", null);

        await TestDataFactory.EnsureStockAccountsAsync(db, companyId);

        return new Scene(companyId, warehouse.Id, project.Id, other.Id, bound, free);
    }

    private static Task<HttpClient> ManagerAsync(DatabaseFixture fixture, string suffix) =>
        TestUserFactory.CreateClientWithRolesAsync(fixture, suffix, ["Genel Müdür"]);

    private static Task<HttpResponseMessage> IssueAsync(
        HttpClient client, Scene scene, Guid itemId, Guid? projectId) =>
        client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = itemId,
            quantity = 1m,
            projectId,
            movementDate = DateTime.UtcNow.Date,
            description = "test çıkışı"
        });

    /// <summary>
    /// BAĞLI KART BAŞKA PROJEYE ÇIKARILAMAZ.
    ///
    /// X için özel imal edilmiş armatür Y'ye giderse o iş malzemesiz
    /// kalır ve kimse fark etmez: stok düşmüş, muhasebe tutmuş, yalnız
    /// malzeme yanlış yere gitmiştir.
    /// </summary>
    [Fact]
    public async Task ProjeBagi_BaskaProjeyeCikisEngellenir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var response = await IssueAsync(client, scene, scene.BoundItemId, scene.OtherProjectId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("başka bir işe", body.GetProperty("message").GetString() ?? "");

        // Stok DEĞİŞMEDİ: engel gerçekten çıkıştan önce çalıştı.
        var quantity = await db.WarehouseStocks
            .AsNoTracking()
            .Where(x => x.InventoryItemId == scene.BoundItemId)
            .Select(x => x.Quantity)
            .SingleAsync();

        Assert.Equal(10m, quantity);
    }

    /// <summary>
    /// PROJESİZ ÇIKIŞ DA ENGELLENİR: kart bir işe bağlıyken genel
    /// gidere (770) yazılamaz — bağ o zaman hiçbir şey ifade etmezdi.
    /// </summary>
    [Fact]
    public async Task ProjeBagi_ProjesizCikisEngellenir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var response = await IssueAsync(client, scene, scene.BoundItemId, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// KENDİ PROJESİNE ÇIKIŞ SERBEST — kural yasak değil, YÖNLENDİRME.
    /// Bu test kuralın fazla geniş olmadığını kanıtlıyor.
    /// </summary>
    [Fact]
    public async Task ProjeBagi_KendiProjesineCikisSerbest()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var response = await IssueAsync(client, scene, scene.BoundItemId, scene.OwnProjectId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// BAĞSIZ KART ETKİLENMEZ: katalog kalemleri her zamanki gibi
    /// çıkabilmeli, yoksa kural bütün depoyu kilitlerdi.
    /// </summary>
    [Fact]
    public async Task ProjeBagi_BagsizKartHerProjeyeCikabilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var response = await IssueAsync(client, scene, scene.FreeItemId, scene.OtherProjectId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// HAREKET TARİHİ EKSİKSE 400 — 500 DEĞİL.
    ///
    /// Alan zorunluydu ama doğrulanmıyordu: boş gelince akış muhasebe
    /// fişine kadar iniyor ve orada patlıyordu; kullanıcı Türkçe uyarı
    /// yerine "sunucu hatası" görüyordu. S9 testleri yazılırken çıktı.
    /// </summary>
    [Fact]
    public async Task Cikis_HareketTarihiEksikseIstekReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var response = await client.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = scene.FreeItemId,
            quantity = 1m,
            projectId = scene.OwnProjectId,
            description = "tarihsiz çıkış"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Hareket tarihi", body.GetProperty("message").GetString() ?? "");
    }

    /// <summary>
    /// STOK SEVİYESİ YALNIZ STOKLU KARTTA (S8 ile tutarlılık).
    ///
    /// "Özel imalat"ta asgari seviye kendi kendisiyle çelişir: o kart
    /// tekildir, ikmal edilecek bir seviyesi yoktur.
    /// </summary>
    [Fact]
    public async Task TedarikTipi_StokluOlmayanKartaSeviyeTanimlanamaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var response = await client.PostAsJsonAsync("/api/stock-levels", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = scene.BoundItemId,
            minimumQuantity = 5m,
            maximumQuantity = (decimal?)20m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("stoklu bir malzeme değil", body.GetProperty("message").GetString() ?? "");
    }

    /// <summary>
    /// GALERİ: İLK GÖRSEL KENDİLİĞİNDEN KAPAK OLUR.
    ///
    /// Kullanıcıyı tek görselli kartta ayrıca "kapak yap" demeye
    /// zorlamak, liste ekranını görselsiz bırakan en sık hataydı.
    /// </summary>
    [Fact]
    public async Task Galeri_IlkGorselKendiliğindenKapakOlur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var first = await UploadPhotoAsync(client, scene.BoundItemId, "on.jpg");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await UploadPhotoAsync(client, scene.BoundItemId, "detay.jpg");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var photos = await client.GetFromJsonAsync<JsonElement>(
            $"/api/inventory/items/{scene.BoundItemId}/fotograflar");

        var covers = photos.EnumerateArray()
            .Where(x => x.GetProperty("isCover").GetBoolean())
            .ToList();

        Assert.Equal(2, photos.GetArrayLength());
        Assert.Single(covers);
        Assert.Equal("on.jpg", covers[0].GetProperty("originalName").GetString());
    }

    /// <summary>
    /// KAPAK SİLİNİRSE SIRADAKİ DEVRALIR.
    ///
    /// Devralmasaydı galeri dolu ama kapaksız kalır, liste görselsiz
    /// görünürdü — kullanıcı görselin silindiğini değil KAYBOLDUĞUNU
    /// sanardı.
    /// </summary>
    [Fact]
    public async Task Galeri_KapakSilininceSiradakiDevralir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        await UploadPhotoAsync(client, scene.BoundItemId, "on.jpg");
        await UploadPhotoAsync(client, scene.BoundItemId, "detay.jpg");

        var photos = await client.GetFromJsonAsync<JsonElement>(
            $"/api/inventory/items/{scene.BoundItemId}/fotograflar");

        var coverId = photos.EnumerateArray()
            .Single(x => x.GetProperty("isCover").GetBoolean())
            .GetProperty("id").GetGuid();

        var delete = await client.DeleteAsync($"/api/inventory/fotograflar/{coverId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var after = await client.GetFromJsonAsync<JsonElement>(
            $"/api/inventory/items/{scene.BoundItemId}/fotograflar");

        var remaining = after.EnumerateArray().ToList();

        Assert.Single(remaining);
        Assert.True(
            remaining[0].GetProperty("isCover").GetBoolean(),
            "Kapak silindikten sonra kalan görsel kapak olmalıydı.");
    }

    /// <summary>
    /// GALERİ YALNIZ GÖRSEL ALIR. Paylaşılan yükleme servisi PDF'e de
    /// izin veriyor (belge modülleri onu kullanıyor); şart burada.
    /// </summary>
    [Fact]
    public async Task Galeri_GorselOlmayanDosyaReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var response = await UploadPhotoAsync(client, scene.BoundItemId, "katalog.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// PROJEYE BAĞLI KART SATILAMAZ.
    ///
    /// Satış da bir ÇIKIŞTIR: X projesi için özel imal edilmiş armatürün
    /// tezgâhtan satılması o işi malzemesiz bırakır ve kimse fark etmez —
    /// stok düşmüş, muhasebe tutmuş, yalnız malzeme yanlış yere
    /// gitmiştir. Depodan çıkışta uygulanan kuralın aynısı.
    ///
    /// SERVİS SEVİYESİNDE: satışın stok ayağı tek kapıdan geçiyor
    /// (`IStockSaleIssuer`, S5). Kuralı orada doğrulamak, perakende ve
    /// fatura yollarının İKİSİNİ birden kapsar.
    /// </summary>
    [Fact]
    public async Task Satis_ProjeyeBagliKartSatilamaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var issuer = scope.ServiceProvider.GetRequiredService<IStockSaleIssuer>();

        // İŞLEM AÇILIYOR — çıkış kapısı satır kilidi alıyor ve
        // `FOR UPDATE` işlem dışında hiçbir şey korumaz. Kilit servisi
        // bu durumda sessizce geçmek yerine hata veriyor; test de
        // gerçek çağıranın yaptığını yapmalı (perakende satış, satış
        // faturası ve zimmet hepsi işlem içinde çağırıyor).
        await using var transaction = await db.Database.BeginTransactionAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            issuer.IssueAsync(
                scene.CompanyId,
                scene.WarehouseId,
                [new StockSaleLine(scene.BoundItemId, 1m, "Dekoratif Armatür", "TEST-1")],
                DateTime.UtcNow.Date,
                null,
                CancellationToken.None));

        Assert.Contains("projeye bağlı", error.Message);

        // Stok değişmedi.
        var quantity = await db.WarehouseStocks
            .AsNoTracking()
            .Where(x => x.InventoryItemId == scene.BoundItemId)
            .Select(x => x.Quantity)
            .SingleAsync();

        Assert.Equal(10m, quantity);
    }

    /// <summary>
    /// BAĞSIZ KART SATILABİLİR — kural satışı topyekûn kapatmıyor.
    /// </summary>
    [Fact]
    public async Task Satis_BagsizKartSatilabilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var issuer = scope.ServiceProvider.GetRequiredService<IStockSaleIssuer>();

        // İŞLEM AÇILIYOR — gerekçe için bkz. Satis_ProjeyeBagliKartSatilamaz.
        await using var transaction = await db.Database.BeginTransactionAsync();

        var costs = await issuer.IssueAsync(
            scene.CompanyId,
            scene.WarehouseId,
            [new StockSaleLine(scene.FreeItemId, 2m, "Katalog Armatür", "TEST-2")],
            DateTime.UtcNow.Date,
            null,
            CancellationToken.None);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        Assert.Single(costs);
        Assert.Equal(2000m, costs[0].TotalCost);
    }

    private static async Task<HttpResponseMessage> UploadPhotoAsync(
        HttpClient client, Guid itemId, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        content.Add(bytes, "file", fileName);

        return await client.PostAsync(
            $"/api/inventory/items/{itemId}/fotograflar", content);
    }

    /// <summary>
    /// KART BAŞKA ŞİRKETİN PROJESİNE BAĞLANAMAZ.
    ///
    /// Bağlanabilseydi bir şirketin malzemesi diğerinin işine kilitlenir,
    /// üstelik proje maliyeti de yanlış şirkete yazılırdı. Koruma vardı
    /// ama TESTSİZDİ — sonda turunda ortaya çıktı: şirket kontrolü
    /// kaldırıldığında hiçbir test düşmüyordu.
    /// </summary>
    [Fact]
    public async Task ProjeBagi_BaskaSirketinProjesineBaglanamaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        // Kendi şirketiyle birlikte gelen YABANCI proje.
        var foreign = await TestDataFactory.CreateProjectAsync(db, $"{suffix}x");

        var client = await ManagerAsync(fixture, suffix);

        var response = await client.PutAsJsonAsync(
            $"/api/inventory/items/{scene.FreeItemId}",
            new
            {
                name = "Katalog Armatür",
                unit = "adet",
                type = 0,
                isActive = true,
                projectId = foreign.Id,
                supplyKind = 1
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("şirketine ait değil", body.GetProperty("message").GetString() ?? "");

        // Kart DEĞİŞMEDİ.
        var stored = await db.InventoryItems
            .AsNoTracking()
            .Where(x => x.Id == scene.FreeItemId)
            .Select(x => x.ProjectId)
            .SingleAsync();

        Assert.Null(stored);
    }

    /// <summary>
    /// PROJE SÜZGECİ: "bu iş için hangi kartlar açıldı" sorusu
    /// cevaplanabilmeli.
    /// </summary>
    [Fact]
    public async Task ProjeSuzgeci_YalnizOProjeyeBagliKartlariDondurur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var items = await client.GetFromJsonAsync<JsonElement>(
            $"/api/inventory/items?companyId={scene.CompanyId}&projectId={scene.OwnProjectId}");

        var ids = items.EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .ToList();

        Assert.Contains(scene.BoundItemId, ids);
        Assert.DoesNotContain(scene.FreeItemId, ids);
    }
}
