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
/// DEPO KONUMLARI: BÖLGE → RAF → KAT ve KATEGORİ VARSAYILANLARI (S3).
///
/// İki kural bu testlerin omurgası:
///
/// 1. BÖLGE TİPİ BELİRLEYİCİ. Raflı bölgede raf ve kat zorunlu; AÇIK
///    bölgede (dış metal oda, büyük tavalar) konum yalnız bölge
///    seviyesindedir. Rafa sığmayan malzemeden raf/kat istemek olmayan
///    bir ayrıntıyı zorunlu kılmak olurdu.
///
/// 2. VARSAYILAN KONUM DEPO × KATEGORİ. Kategori SİSTEM GENELİ ("kablo
///    tavası" her şirkette aynı), konum ise belirli bir şirketin
///    belirli bir deposundaki fiziksel yer. Kategoriye varsayılan konum
///    konsaydı ikinci şirket eklendiğinde YANLIŞ yeri gösterirdi.
/// </summary>
[Collection("Integration")]
public sealed class WarehouseLocationTests(DatabaseFixture fixture)
{
    private async Task<(Guid CompanyId, Guid WarehouseId)> WarehouseAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DEPO-{suffix}",
            Name = $"Merkez Depo {suffix}",
            Type = WarehouseType.Central
        };

        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        return (company.Id, warehouse.Id);
    }

    private async Task<JsonElement> CategoryAsync(string code)
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var body = await client.GetFromJsonAsync<JsonElement>("/api/inventory/categories");

        return body.EnumerateArray().Single(x => x.GetProperty("code").GetString() == code);
    }

    private static Guid OptionId(JsonElement category, string attributeCode, string value) =>
        category.GetProperty("attributes").EnumerateArray()
            .Single(x => x.GetProperty("code").GetString() == attributeCode)
            .GetProperty("options").EnumerateArray()
            .Single(x => x.GetProperty("value").GetString() == value)
            .GetProperty("id").GetGuid();

    /// <summary>
    /// RAFLI BÖLGE toplu kurulur: raf sayısı ve her rafın kat sayısı.
    /// </summary>
    [Fact]
    public async Task RafliBolge_RafVeKatlariyla_Kurulur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (_, warehouseId) = await WarehouseAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync(
            $"/api/warehouses/{warehouseId}/locations/zones",
            new { code = "ODA2", name = "Oda 2", kind = 0, sortOrder = 20,
                  shelfCount = 3, levelsPerShelf = 2 });

        created.EnsureSuccessStatusCode();

        var body = await client.GetFromJsonAsync<JsonElement>(
            $"/api/warehouses/{warehouseId}/locations");

        var zone = body.GetProperty("zones").EnumerateArray().Single();
        var shelves = zone.GetProperty("shelves").EnumerateArray().ToList();

        Assert.Equal(3, shelves.Count);
        Assert.All(shelves, shelf =>
            Assert.Equal(2, shelf.GetProperty("levels").GetArrayLength()));
    }

    /// <summary>
    /// AÇIK BÖLGEDE RAF TANIMLANMAZ.
    /// </summary>
    [Fact]
    public async Task AcikBolge_RafKabulEtmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (_, warehouseId) = await WarehouseAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            $"/api/warehouses/{warehouseId}/locations/zones",
            new { code = "DISMETAL", name = "Dış Metal Oda", kind = 1, sortOrder = 90,
                  shelfCount = 2, levelsPerShelf = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var ok = await client.PostAsJsonAsync(
            $"/api/warehouses/{warehouseId}/locations/zones",
            new { code = "DISMETAL", name = "Dış Metal Oda", kind = 1, sortOrder = 90,
                  shelfCount = 0, levelsPerShelf = 0 });

        ok.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// RAFLI BÖLGEDE VARSAYILAN KONUM raf ve kat İSTER; açık bölgede
    /// raf/kat verilemez.
    /// </summary>
    [Fact]
    public async Task VarsayilanKonum_BolgeTipineUyar()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (_, warehouseId) = await WarehouseAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var tava = await CategoryAsync("KABLO_TAVASI");

        await client.PostAsJsonAsync($"/api/warehouses/{warehouseId}/locations/zones",
            new { code = "ODA1", name = "Oda 1", kind = 0, sortOrder = 10,
                  shelfCount = 2, levelsPerShelf = 2 });

        await client.PostAsJsonAsync($"/api/warehouses/{warehouseId}/locations/zones",
            new { code = "ACIK", name = "Dış Metal Oda", kind = 1, sortOrder = 90,
                  shelfCount = 0, levelsPerShelf = 0 });

        var locations = await client.GetFromJsonAsync<JsonElement>(
            $"/api/warehouses/{warehouseId}/locations");

        var zones = locations.GetProperty("zones").EnumerateArray().ToList();
        var rafli = zones.Single(x => x.GetProperty("code").GetString() == "ODA1");
        var acik = zones.Single(x => x.GetProperty("code").GetString() == "ACIK");

        var categoryId = tava.GetProperty("id").GetGuid();

        // Raflı bölgede raf/kat OLMADAN reddedilir.
        var eksik = await client.PutAsJsonAsync(
            $"/api/warehouses/{warehouseId}/locations/defaults",
            new { categoryId, zoneId = rafli.GetProperty("id").GetGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, eksik.StatusCode);

        // Açık bölgede raf VERİLİRSE reddedilir.
        var shelfId = rafli.GetProperty("shelves").EnumerateArray().First()
            .GetProperty("id").GetGuid();

        var fazla = await client.PutAsJsonAsync(
            $"/api/warehouses/{warehouseId}/locations/defaults",
            new { categoryId, zoneId = acik.GetProperty("id").GetGuid(), shelfId });

        Assert.Equal(HttpStatusCode.BadRequest, fazla.StatusCode);

        // Açık bölgede YALNIZ bölge kabul edilir.
        var dogru = await client.PutAsJsonAsync(
            $"/api/warehouses/{warehouseId}/locations/defaults",
            new { categoryId, zoneId = acik.GetProperty("id").GetGuid() });

        dogru.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// KART AÇILINCA KONUM OTOMATİK GELİR — depodaki kategori
    /// varsayılanından.
    /// </summary>
    [Fact]
    public async Task KartAcilinca_DepodakiVarsayilanKonumUygulanir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, warehouseId) = await WarehouseAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var tava = await CategoryAsync("KABLO_TAVASI");

        await client.PostAsJsonAsync($"/api/warehouses/{warehouseId}/locations/zones",
            new { code = "ODA3", name = "Oda 3", kind = 0, sortOrder = 30,
                  shelfCount = 2, levelsPerShelf = 3 });

        var locations = await client.GetFromJsonAsync<JsonElement>(
            $"/api/warehouses/{warehouseId}/locations");

        var zone = locations.GetProperty("zones").EnumerateArray().Single();
        var shelf = zone.GetProperty("shelves").EnumerateArray().Skip(1).First();
        var level = shelf.GetProperty("levels").EnumerateArray().Skip(1).First();

        var zoneId = zone.GetProperty("id").GetGuid();
        var shelfId = shelf.GetProperty("id").GetGuid();
        var levelId = level.GetProperty("id").GetGuid();

        (await client.PutAsJsonAsync(
            $"/api/warehouses/{warehouseId}/locations/defaults",
            new { categoryId = tava.GetProperty("id").GetGuid(), zoneId, shelfId, levelId }))
            .EnsureSuccessStatusCode();

        var created = await client.PostAsJsonAsync("/api/inventory/items", new
        {
            companyId,
            categoryId = tava.GetProperty("id").GetGuid(),
            unit = "metre",
            warehouseId,
            optionIds = new[]
            {
                OptionId(tava, "OLCU", "300"),
                OptionId(tava, "KALINLIK", "1.2"),
                OptionId(tava, "CINS", "Perfore"),
                OptionId(tava, "KAPLAMA", "Pregalvaniz")
            },
            minimumStock = 0m,
            type = 0
        });

        created.EnsureSuccessStatusCode();

        var itemId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.InventoryItems.AsNoTracking().SingleAsync(x => x.Id == itemId);

        Assert.Equal(zoneId, item.WarehouseZoneId);
        Assert.Equal(shelfId, item.WarehouseShelfId);
        Assert.Equal(levelId, item.WarehouseShelfLevelId);
    }

    /// <summary>
    /// AÇIK BÖLGEYE atanan kartta raf ve kat NULL kalır — elle
    /// gönderilse bile temizlenir.
    /// </summary>
    [Fact]
    public async Task AcikBolgeKarti_RafVeKatTasimaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, warehouseId) = await WarehouseAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var tava = await CategoryAsync("KABLO_TAVASI");

        await client.PostAsJsonAsync($"/api/warehouses/{warehouseId}/locations/zones",
            new { code = "RAFLI", name = "Oda 1", kind = 0, sortOrder = 10,
                  shelfCount = 1, levelsPerShelf = 1 });

        await client.PostAsJsonAsync($"/api/warehouses/{warehouseId}/locations/zones",
            new { code = "ACIKB", name = "Dış Metal Oda", kind = 1, sortOrder = 90,
                  shelfCount = 0, levelsPerShelf = 0 });

        var locations = await client.GetFromJsonAsync<JsonElement>(
            $"/api/warehouses/{warehouseId}/locations");

        var zones = locations.GetProperty("zones").EnumerateArray().ToList();
        var acik = zones.Single(x => x.GetProperty("code").GetString() == "ACIKB");
        var rafli = zones.Single(x => x.GetProperty("code").GetString() == "RAFLI");

        var yabanciRaf = rafli.GetProperty("shelves").EnumerateArray().First()
            .GetProperty("id").GetGuid();

        var created = await client.PostAsJsonAsync("/api/inventory/items", new
        {
            companyId,
            categoryId = tava.GetProperty("id").GetGuid(),
            unit = "metre",
            zoneId = acik.GetProperty("id").GetGuid(),
            shelfId = yabanciRaf,
            optionIds = new[]
            {
                OptionId(tava, "OLCU", "500"),
                OptionId(tava, "KALINLIK", "2.0"),
                OptionId(tava, "CINS", "Kapalı"),
                OptionId(tava, "KAPLAMA", "Paslanmaz")
            },
            minimumStock = 0m,
            type = 0
        });

        created.EnsureSuccessStatusCode();

        var itemId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.InventoryItems.AsNoTracking().SingleAsync(x => x.Id == itemId);

        Assert.Equal(acik.GetProperty("id").GetGuid(), item.WarehouseZoneId);
        Assert.Null(item.WarehouseShelfId);
        Assert.Null(item.WarehouseShelfLevelId);
    }

    /// <summary>
    /// RAF QR'I: "bu rafta ne var" listesi.
    /// </summary>
    [Fact]
    public async Task RafIcerigi_OKafta_DuranKartlariDoner()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, warehouseId) = await WarehouseAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var pano = await CategoryAsync("PANO");

        await client.PostAsJsonAsync($"/api/warehouses/{warehouseId}/locations/zones",
            new { code = "ODAQ", name = "Oda Q", kind = 0, sortOrder = 10,
                  shelfCount = 2, levelsPerShelf = 1 });

        var locations = await client.GetFromJsonAsync<JsonElement>(
            $"/api/warehouses/{warehouseId}/locations");

        var zone = locations.GetProperty("zones").EnumerateArray().Single();
        var shelves = zone.GetProperty("shelves").EnumerateArray().ToList();

        var hedefRaf = shelves[0].GetProperty("id").GetGuid();
        var digerRaf = shelves[1].GetProperty("id").GetGuid();

        async Task<Guid> KartAc(string sira, Guid shelfId)
        {
            var response = await client.PostAsJsonAsync("/api/inventory/items", new
            {
                companyId,
                categoryId = pano.GetProperty("id").GetGuid(),
                unit = "adet",
                zoneId = zone.GetProperty("id").GetGuid(),
                shelfId,
                optionIds = new[]
                {
                    OptionId(pano, "TIP", "Dağıtım"),
                    OptionId(pano, "SIRA", sira)
                },
                minimumStock = 0m,
                type = 0
            });

            response.EnsureSuccessStatusCode();

            return (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetGuid();
        }

        var burada = await KartAc("24", hedefRaf);
        var baskaRafta = await KartAc("36", digerRaf);

        var shelfBody = await client.GetFromJsonAsync<JsonElement>(
            $"/api/warehouses/{warehouseId}/locations/shelves/{hedefRaf}/items");

        var ids = shelfBody.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(burada, ids);
        Assert.DoesNotContain(baskaRafta, ids);
    }
}
