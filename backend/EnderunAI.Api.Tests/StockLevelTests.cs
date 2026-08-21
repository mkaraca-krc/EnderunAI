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
/// ASGARİ/AZAMİ STOK SEVİYESİ ve satın alma talebi önerisi (S8).
///
/// Buradaki kurallar tek bir soruyu koruyor: "hangi depoda ne eksik ve
/// ne kadar alınmalı". Eşik depoya ait olduğu için aynı malzemenin iki
/// deposu birbirinin eksiğini kapatmaz; miktar ise ancak azami
/// tanımlıysa önerilir — kaç adet alınacağı tahmin edilmez.
/// </summary>
[Collection("Integration")]
public sealed class StockLevelTests(DatabaseFixture fixture)
{
    private sealed record Scene(
        Guid CompanyId,
        Guid ProjectId,
        Guid MerkezId,
        Guid SantiyeId,
        Guid ItemId);

    private static async Task<Scene> BuildAsync(
        AppDbContext db,
        string suffix,
        decimal merkezStock = 3m,
        decimal santiyeStock = 500m)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var companyId = project.CompanyId;
        var branch = await db.Branches.FirstAsync(x => x.CompanyId == companyId);

        var merkez = new Warehouse
        {
            CompanyId = companyId,
            BranchId = branch.Id,
            Code = $"MRK-{suffix}",
            Name = $"Merkez Depo {suffix}",
            Type = WarehouseType.Central
        };

        var santiye = new Warehouse
        {
            CompanyId = companyId,
            BranchId = branch.Id,
            Code = $"SNT-{suffix}",
            Name = $"Şantiye Depo {suffix}",
            Type = WarehouseType.Central
        };

        db.Warehouses.AddRange(merkez, santiye);

        var item = new InventoryItem
        {
            CompanyId = companyId,
            Code = $"MLZ-{suffix}",
            Name = $"Kablo {suffix}",
            Unit = "metre",
            AverageUnitCost = 40m
        };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        db.WarehouseStocks.AddRange(
            new WarehouseStock
            {
                WarehouseId = merkez.Id,
                InventoryItemId = item.Id,
                Quantity = merkezStock
            },
            new WarehouseStock
            {
                WarehouseId = santiye.Id,
                InventoryItemId = item.Id,
                Quantity = santiyeStock
            });

        await db.SaveChangesAsync();

        return new Scene(companyId, project.Id, merkez.Id, santiye.Id, item.Id);
    }

    private static Task<HttpClient> ManagerAsync(DatabaseFixture fixture, string suffix) =>
        TestUserFactory.CreateClientWithRolesAsync(fixture, suffix, ["Genel Müdür"]);

    private static async Task SaveLevelAsync(
        HttpClient client,
        Guid warehouseId,
        Guid itemId,
        decimal minimum,
        decimal? maximum)
    {
        var response = await client.PostAsJsonAsync("/api/stock-levels", new
        {
            warehouseId,
            inventoryItemId = itemId,
            minimumQuantity = minimum,
            maximumQuantity = maximum
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// SEVİYE DEPOYA AİT: şantiyede duran 500 metre, merkezdeki 3
    /// metrelik eksiği KAPATMAZ.
    ///
    /// Kart üzerinde tek bir asgari olsaydı toplam 503 metre görünür ve
    /// merkez deposu boşken hiçbir uyarı çıkmazdı. Bu testin koruduğu
    /// şey tam olarak o körlüğün geri gelmemesi.
    /// </summary>
    [Fact]
    public async Task Seviye_DepoBazinda_BaskaDepodakiStokEksigiKapatmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 50m, 200m);

        var rows = await client.GetFromJsonAsync<JsonElement>(
            $"/api/stock-levels?companyId={scene.CompanyId}&belowMinimumOnly=true");

        var row = rows.EnumerateArray().Single(
            x => x.GetProperty("inventoryItemId").GetGuid() == scene.ItemId);

        Assert.Equal(scene.MerkezId, row.GetProperty("warehouseId").GetGuid());
        Assert.Equal(3m, row.GetProperty("currentQuantity").GetDecimal());
        Assert.True(row.GetProperty("isBelowMinimum").GetBoolean());
    }

    /// <summary>
    /// BAKİYE SATIRI OLMAYAN KALEM DE UYARIR — en acil hâl budur.
    ///
    /// İç birleşim kullanılsaydı depoda hiç görülmemiş (ya da tamamen
    /// tükenip satırı hiç açılmamış) malzeme sessiz kalırdı.
    /// </summary>
    [Fact]
    public async Task Seviye_StokSatiriHicYoksa_MevcutSifirSayilirVeUyarir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        // Üçüncü depo: bu malzeme oraya hiç girmemiş.
        var branch = await db.Branches.FirstAsync(x => x.CompanyId == scene.CompanyId);
        var bos = new Warehouse
        {
            CompanyId = scene.CompanyId,
            BranchId = branch.Id,
            Code = $"BOS-{suffix}",
            Name = $"Boş Depo {suffix}",
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(bos);
        await db.SaveChangesAsync();

        var client = await ManagerAsync(fixture, suffix);

        await SaveLevelAsync(client, bos.Id, scene.ItemId, 10m, 100m);

        var rows = await client.GetFromJsonAsync<JsonElement>(
            $"/api/stock-levels?warehouseId={bos.Id}&belowMinimumOnly=true");

        var row = rows.EnumerateArray().Single();

        Assert.Equal(0m, row.GetProperty("currentQuantity").GetDecimal());
        Assert.True(row.GetProperty("isDepleted").GetBoolean());
        Assert.Equal(100m, row.GetProperty("suggestedQuantity").GetDecimal());
    }

    /// <summary>
    /// ÖNERİ = AZAMİ − MEVCUT, ve azami yoksa öneri YOK.
    ///
    /// "Asgarinin iki katı" gibi bir katsayı uydurulsaydı sistem
    /// kimsenin vermediği bir sipariş kararını vermiş olurdu. Uyarı
    /// yine çıkıyor — eksik olan miktar önerisi, uyarının kendisi değil.
    /// </summary>
    [Fact]
    public async Task Oneri_AzamiEksiMevcut_AzamiYoksaOneriUretilmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, merkezStock: 3m);

        var client = await ManagerAsync(fixture, suffix);

        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 50m, 200m);

        var withMax = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/stock-levels?warehouseId={scene.MerkezId}"))
            .EnumerateArray().Single();

        Assert.Equal(197m, withMax.GetProperty("suggestedQuantity").GetDecimal());

        // Azami kaldırılıyor: uyarı duruyor, öneri düşüyor.
        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 50m, null);

        var withoutMax = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/stock-levels?warehouseId={scene.MerkezId}"))
            .EnumerateArray().Single();

        Assert.True(withoutMax.GetProperty("isBelowMinimum").GetBoolean());
        Assert.Equal(JsonValueKind.Null, withoutMax.GetProperty("suggestedQuantity").ValueKind);
    }

    /// <summary>
    /// ASGARİYE TAM OTURAN STOK DA UYARIR.
    ///
    /// Eşik "&lt;" olsaydı asgariye inmiş kalem bir birim daha çıkana
    /// kadar sessiz kalırdı; oysa asgari zaten "buradan aşağı düşme"
    /// çizgisi. Ekran, bildirim ve brifing aynı karşılaştırmayı
    /// kullanıyor.
    /// </summary>
    [Fact]
    public async Task Esik_AsgariyeEsitMiktarDaUyariUretir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, merkezStock: 50m);

        var client = await ManagerAsync(fixture, suffix);

        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 50m, 200m);

        var row = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/stock-levels?warehouseId={scene.MerkezId}&belowMinimumOnly=true"))
            .EnumerateArray().Single();

        Assert.Equal(50m, row.GetProperty("currentQuantity").GetDecimal());
        Assert.True(row.GetProperty("isBelowMinimum").GetBoolean());
    }

    /// <summary>
    /// ASGARİSİ SIFIR OLAN SEVİYE KABUL EDİLMEZ.
    ///
    /// Satırın varlığı takibin kendisi; sıfır asgari "her zaman kritik"
    /// demek olurdu ve uyarı listesi anlamını yitirirdi. Takibi
    /// bırakmanın yolu satırı silmek.
    /// </summary>
    [Fact]
    public async Task Seviye_SifirAsgariReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var response = await client.PostAsJsonAsync("/api/stock-levels", new
        {
            warehouseId = scene.MerkezId,
            inventoryItemId = scene.ItemId,
            minimumQuantity = 0m,
            maximumQuantity = (decimal?)100m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// AZAMİ ASGARİDEN BÜYÜK OLMAK ZORUNDA: aksi halde öneri negatif
    /// çıkar ve "eksil" anlamına gelen bir sipariş önerilirdi.
    /// </summary>
    [Fact]
    public async Task Seviye_AzamiAsgariyeEsitVeyaKucukReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var response = await client.PostAsJsonAsync("/api/stock-levels", new
        {
            warehouseId = scene.MerkezId,
            inventoryItemId = scene.ItemId,
            minimumQuantity = 50m,
            maximumQuantity = (decimal?)50m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// AYNI DEPO+MALZEME İÇİN İKİNCİ SATIR AÇILMAZ: ikinci kayıt
    /// mevcudu günceller. İki satır olsaydı hangi eşiğin geçerli
    /// olduğu belirsizleşirdi.
    /// </summary>
    [Fact]
    public async Task Seviye_IkinciKayitYeniSatirAcmazGunceller()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 50m, 200m);
        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 80m, 300m);

        var count = await db.WarehouseStockLevels
            .CountAsync(x => x.WarehouseId == scene.MerkezId &&
                             x.InventoryItemId == scene.ItemId);

        Assert.Equal(1, count);

        var row = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/stock-levels?warehouseId={scene.MerkezId}"))
            .EnumerateArray().Single();

        Assert.Equal(80m, row.GetProperty("minimumQuantity").GetDecimal());
        Assert.Equal(300m, row.GetProperty("maximumQuantity").GetDecimal());
    }

    /// <summary>
    /// ÖNERİDEN AÇILAN TALEP TASLAKTIR ve normal onay yolundan geçer.
    ///
    /// Doğrudan onaylı açılsaydı stok uyarısı, kimsenin bakmadığı bir
    /// harcama emrine dönerdi.
    /// </summary>
    [Fact]
    public async Task Talep_OneriDenAcilirTaslakOlarakVeKalemleriTasir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, merkezStock: 3m);

        var client = await ManagerAsync(fixture, suffix);

        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 50m, 200m);

        var response = await client.PostAsJsonAsync("/api/stock-levels/satin-alma-talebi", new
        {
            warehouseId = scene.MerkezId,
            projectId = scene.ProjectId,
            requestedByName = "Depo Sorumlusu",
            priority = 2,
            lines = new[]
            {
                new { inventoryItemId = scene.ItemId, quantity = 197m }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = body.GetProperty("purchaseRequestId").GetGuid();

        var created = await db.PurchaseRequests
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleAsync(x => x.Id == requestId);

        Assert.Equal(PurchaseRequestStatus.Draft, created.Status);
        Assert.Equal(scene.ProjectId, created.ProjectId);

        var line = Assert.Single(created.Items);
        Assert.Equal(scene.ItemId, line.InventoryItemId);
        Assert.Equal(197m, line.Quantity);

        // İzin sürülebilir olmalı: talebi hangi deponun hangi eşiği
        // doğurdu, kalem notundan okunabiliyor.
        Assert.Contains("stok seviyesi", line.Notes ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// SEVİYESİ TANIMLI OLMAYAN KALEM BU YOLDAN TALEP EDİLEMEZ.
    ///
    /// Bu uç "asgarinin altına düştü" gerekçesiyle talep açıyor;
    /// gerekçesiz kalemi de kabul etseydi otomasyon kapısı, denetimsiz
    /// bir elle talep kapısına dönüşürdü.
    ///
    /// MESAJ DA DENETLENİYOR, DURUM KODU YETMİYOR — bunu sonda öğretti.
    /// Koruma kaldırıldığında akış birkaç satır sonra `levels.Single()`
    /// üzerinde patlıyor, denetleyici o istisnayı da 409'a çeviriyor ve
    /// test aradaki farkı GÖREMİYORDU. Fark kullanıcıda: açıklayıcı
    /// Türkçe gerekçe yerine "Sequence contains no elements" okurdu.
    /// </summary>
    [Fact]
    public async Task Talep_SeviyesiTanimsizKalemReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        var response = await client.PostAsJsonAsync("/api/stock-levels/satin-alma-talebi", new
        {
            warehouseId = scene.MerkezId,
            projectId = scene.ProjectId,
            requestedByName = "Depo Sorumlusu",
            priority = 1,
            lines = new[]
            {
                new { inventoryItemId = scene.ItemId, quantity = 10m }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var message = body.GetProperty("message").GetString() ?? "";

        Assert.Contains("stok seviyesi tanımlı değil", message);
    }

    /// <summary>
    /// BAŞKA ŞİRKETİN PROJESİNE İKMAL TALEBİ AÇILAMAZ: açılabilseydi
    /// bir şirketin deposu, başka şirketin bütçesine yazılırdı.
    /// </summary>
    [Fact]
    public async Task Talep_BaskaSirketinProjesiReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        // Kendi şirketiyle birlikte gelen ayrı bir proje.
        var foreign = await TestDataFactory.CreateProjectAsync(db, $"{suffix}x");

        var client = await ManagerAsync(fixture, suffix);

        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 50m, 200m);

        var response = await client.PostAsJsonAsync("/api/stock-levels/satin-alma-talebi", new
        {
            warehouseId = scene.MerkezId,
            projectId = foreign.Id,
            requestedByName = "Depo Sorumlusu",
            priority = 1,
            lines = new[]
            {
                new { inventoryItemId = scene.ItemId, quantity = 100m }
            }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// BAŞKA ŞİRKETİN MALZEMESİNE SEVİYE TANIMLANAMAZ.
    /// </summary>
    [Fact]
    public async Task Seviye_BaskaSirketinMalzemesiReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);
        var other = await BuildAsync(db, $"{suffix}y");

        var client = await ManagerAsync(fixture, suffix);

        var response = await client.PostAsJsonAsync("/api/stock-levels", new
        {
            warehouseId = scene.MerkezId,
            inventoryItemId = other.ItemId,
            minimumQuantity = 10m,
            maximumQuantity = (decimal?)100m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// TAKİP BIRAKILIP YENİDEN AÇILABİLİR.
    ///
    /// Satır yumuşak siliniyor; tekil indeks silinmişleri de kapsasaydı
    /// aynı malzeme için takip bir daha AÇILAMAZDI — kullanıcı hatasını
    /// düzeltemez, kaydı da göremezdi. Kısmi indeks bunu engelliyor.
    /// </summary>
    [Fact]
    public async Task Seviye_SilindiktenSonraYenidenTanimlanabilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ManagerAsync(fixture, suffix);

        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 50m, 200m);

        var levelId = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/stock-levels?warehouseId={scene.MerkezId}"))
            .EnumerateArray().Single()
            .GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.DeleteAsync($"/api/stock-levels/{levelId}")).StatusCode);

        // Aynı depo + aynı malzeme yeniden tanımlanabilmeli.
        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 30m, 120m);

        var row = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/stock-levels?warehouseId={scene.MerkezId}"))
            .EnumerateArray().Single();

        Assert.Equal(30m, row.GetProperty("minimumQuantity").GetDecimal());
    }

    /// <summary>
    /// TAKİBİ KALDIRMAK UYARIYI SUSTURUR: seviye satırı silinince kalem
    /// listeden düşer, stok değişmez.
    /// </summary>
    [Fact]
    public async Task Seviye_SilinceUyariDuserStokDegismez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, merkezStock: 3m);

        var client = await ManagerAsync(fixture, suffix);

        await SaveLevelAsync(client, scene.MerkezId, scene.ItemId, 50m, 200m);

        var row = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/stock-levels?warehouseId={scene.MerkezId}"))
            .EnumerateArray().Single();

        var levelId = row.GetProperty("id").GetGuid();

        var delete = await client.DeleteAsync($"/api/stock-levels/{levelId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var after = await client.GetFromJsonAsync<JsonElement>(
            $"/api/stock-levels?warehouseId={scene.MerkezId}");

        Assert.Equal(0, after.GetArrayLength());

        var stock = await db.WarehouseStocks
            .AsNoTracking()
            .SingleAsync(x => x.WarehouseId == scene.MerkezId &&
                              x.InventoryItemId == scene.ItemId);

        Assert.Equal(3m, stock.Quantity);
    }
}
