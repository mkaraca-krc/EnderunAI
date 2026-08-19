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
/// KART AÇMA: OTOMATİK KOD, OTOMATİK AD, MÜKERRER ENGELİ (S2).
///
/// Bu üçü stok bölünmesini engelleyen mekanizma. Aynı malzeme iki
/// farklı kartla açılırsa stok ikiye bölünür, maliyet ortalaması
/// bozulur ve "elimizde ne var" sorusu cevapsız kalır.
/// </summary>
[Collection("Integration")]
public sealed class InventoryItemCreationTests(DatabaseFixture fixture)
{
    private async Task<Guid> CompanyAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
        return company.Id;
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

    private async Task<HttpResponseMessage> CreateAsync(
        Guid companyId, JsonElement category, Guid[] optionIds, string unit = "metre",
        string? name = null)
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        return await client.PostAsJsonAsync("/api/inventory/items", new
        {
            companyId,
            categoryId = category.GetProperty("id").GetGuid(),
            unit,
            optionIds,
            name,
            minimumStock = 0m,
            type = 0
        });
    }

    /// <summary>
    /// AD ÖZELLİKLERDEN ÜRETİLİR — kullanıcı yazmaz.
    /// </summary>
    [Fact]
    public async Task StandartKart_AdiOzelliklerdenUretir()
    {
        var companyId = await CompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var tava = await CategoryAsync("KABLO_TAVASI");

        var response = await CreateAsync(companyId, tava,
        [
            OptionId(tava, "OLCU", "200"),
            OptionId(tava, "KALINLIK", "1.5"),
            OptionId(tava, "CINS", "Perfore"),
            OptionId(tava, "KAPLAMA", "Sıcak Daldırma Galvaniz")
        ]);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var name = body.GetProperty("name").GetString();

        Assert.Equal("Kablo Tavası 200 1.5 Perfore Sıcak Daldırma Galvaniz", name);
    }

    /// <summary>
    /// KOD OTOMATİK, ANLAMSIZ VE ARTAN. Kullanıcı hiç girmiyor.
    /// </summary>
    [Fact]
    public async Task Kod_OtomatikVeArtan()
    {
        var companyId = await CompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var tava = await CategoryAsync("KABLO_TAVASI");
        var merdiven = await CategoryAsync("KABLO_MERDIVENI");

        var first = await CreateAsync(companyId, tava,
        [
            OptionId(tava, "OLCU", "100"),
            OptionId(tava, "KALINLIK", "1.0"),
            OptionId(tava, "CINS", "Kapalı"),
            OptionId(tava, "KAPLAMA", "Pregalvaniz")
        ]);
        first.EnsureSuccessStatusCode();

        var second = await CreateAsync(companyId, merdiven,
        [
            OptionId(merdiven, "OLCU", "300"),
            OptionId(merdiven, "KAPLAMA", "Paslanmaz")
        ]);
        second.EnsureSuccessStatusCode();

        var firstCode = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString()!;
        var secondCode = (await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString()!;

        // Ön ek YOK, yalnız rakam.
        Assert.Matches("^[0-9]+$", firstCode);
        Assert.True(long.Parse(firstCode) >= 100_001);
        Assert.Equal(long.Parse(firstCode) + 1, long.Parse(secondCode));
    }

    /// <summary>
    /// MÜKERRER ENGELİ: aynı kategori+özellik kombinasyonu ikinci kez
    /// açılamaz. Bölünmüş stok böyle engellenir.
    /// </summary>
    [Fact]
    public async Task AyniKombinasyon_IkinciKezAcilamaz()
    {
        var companyId = await CompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var tava = await CategoryAsync("KABLO_TAVASI");

        Guid[] secim =
        [
            OptionId(tava, "OLCU", "400"),
            OptionId(tava, "KALINLIK", "2.0"),
            OptionId(tava, "CINS", "Delikli"),
            OptionId(tava, "KAPLAMA", "Boyalı")
        ];

        (await CreateAsync(companyId, tava, secim)).EnsureSuccessStatusCode();

        var again = await CreateAsync(companyId, tava, secim);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var body = await again.Content.ReadAsStringAsync();
        Assert.Contains("zaten var", body);
    }

    /// <summary>
    /// SEÇİM SIRASI İMZAYI DEĞİŞTİRMEZ — özellikler koda göre
    /// sıralanıyor. Aksi hâlde aynı malzeme farklı sırayla seçilerek
    /// ikinci kez açılabilirdi.
    /// </summary>
    [Fact]
    public async Task SecimSirasi_MukerrerEngeliniDelemez()
    {
        var companyId = await CompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var tava = await CategoryAsync("KABLO_TAVASI");

        var olcu = OptionId(tava, "OLCU", "500");
        var kalinlik = OptionId(tava, "KALINLIK", "0.8");
        var cins = OptionId(tava, "CINS", "Perfore");
        var kaplama = OptionId(tava, "KAPLAMA", "Paslanmaz");

        (await CreateAsync(companyId, tava, [olcu, kalinlik, cins, kaplama]))
            .EnsureSuccessStatusCode();

        // TERS sırayla aynı seçim.
        var reversed = await CreateAsync(companyId, tava, [kaplama, cins, kalinlik, olcu]);

        Assert.Equal(HttpStatusCode.Conflict, reversed.StatusCode);
    }

    /// <summary>
    /// MÜKERRER ŞİRKET İÇİ: başka şirket aynı malzemeyi kendi kartıyla
    /// tutabilir. Kategori sözlüğü ortak, kartlar şirkete ait.
    /// </summary>
    [Fact]
    public async Task BaskaSirket_AyniMalzemeyiAcabilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var firstCompany = await CompanyAsync($"A{suffix}"[..8]);
        var secondCompany = await CompanyAsync($"B{suffix}"[..8]);

        var tava = await CategoryAsync("KABLO_TAVASI");

        Guid[] secim =
        [
            OptionId(tava, "OLCU", "50"),
            OptionId(tava, "KALINLIK", "1.2"),
            OptionId(tava, "CINS", "Kapalı"),
            OptionId(tava, "KAPLAMA", "Boyalı")
        ];

        (await CreateAsync(firstCompany, tava, secim)).EnsureSuccessStatusCode();
        (await CreateAsync(secondCompany, tava, secim)).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// BİRİM KİLİDİ: kategorinin izin vermediği birim reddedilir.
    /// </summary>
    [Fact]
    public async Task IzinsizBirim_Reddedilir()
    {
        var companyId = await CompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var tava = await CategoryAsync("KABLO_TAVASI");

        // Kablo tavası yalnız metre.
        var response = await CreateAsync(companyId, tava,
        [
            OptionId(tava, "OLCU", "100"),
            OptionId(tava, "KALINLIK", "1.0"),
            OptionId(tava, "CINS", "Perfore"),
            OptionId(tava, "KAPLAMA", "Boyalı")
        ], unit: "adet");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("metre", body);
    }

    /// <summary>
    /// ÇOK BİRİMLİ KATEGORİDE her iki birim de kabul edilir ve kart
    /// birimini SABİTLER.
    /// </summary>
    [Fact]
    public async Task CokBirimliKategori_HerIkiBirimiKabulEder()
    {
        var companyId = await CompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var topraklama = await CategoryAsync("TOPRAKLAMA");

        var serit = await CreateAsync(companyId, topraklama,
            [OptionId(topraklama, "TIP", "Bakır Şerit")], unit: "metre");
        serit.EnsureSuccessStatusCode();

        var cubuk = await CreateAsync(companyId, topraklama,
            [OptionId(topraklama, "TIP", "Toprak Çubuğu")], unit: "adet");
        cubuk.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// ZORUNLU ÖZELLİK EKSİKSE kart açılmaz — yarım tanımlı kart,
    /// mükerrer engelini de anlamsız kılardı.
    /// </summary>
    [Fact]
    public async Task EksikZorunluOzellik_Reddedilir()
    {
        var companyId = await CompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var tava = await CategoryAsync("KABLO_TAVASI");

        var response = await CreateAsync(companyId, tava,
            [OptionId(tava, "OLCU", "200")]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Kalınlık", body);
    }

    /// <summary>
    /// SERBEST TİPTE: ad elle yazılır ve MÜKERRER ENGELİ UYGULANMAZ —
    /// iki dekoratif armatür aynı adı taşıyabilir.
    /// </summary>
    [Fact]
    public async Task SerbestTip_AdElleYazilirVeMukerrerSerbesttir()
    {
        var companyId = await CompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var dekoratif = await CategoryAsync("DEKORATIF_AYDINLATMA");

        var adsiz = await CreateAsync(companyId, dekoratif, [], unit: "adet");
        Assert.Equal(HttpStatusCode.BadRequest, adsiz.StatusCode);

        var first = await CreateAsync(companyId, dekoratif, [], unit: "adet",
            name: "Lento Sarkıt 3'lü Siyah Gold");
        first.EnsureSuccessStatusCode();

        // AYNI ad ikinci kez açılabilir — her dekoratif ürün tekildir.
        var second = await CreateAsync(companyId, dekoratif, [], unit: "adet",
            name: "Lento Sarkıt 3'lü Siyah Gold");
        second.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// ARŞİVDEKİ MÜKERRER: yeni kart açtırmak yerine geri açmaya
    /// yönlendirir. Aksi hâlde arşivleme mükerrer engelini delerdi.
    /// </summary>
    [Fact]
    public async Task ArsivdekiMukerrer_GeriAcmayaYonlendirir()
    {
        var companyId = await CompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var pano = await CategoryAsync("PANO");

        Guid[] secim =
        [
            OptionId(pano, "TIP", "Dağıtım"),
            OptionId(pano, "SIRA", "54")
        ];

        var created = await CreateAsync(companyId, pano, secim, unit: "adet");
        created.EnsureSuccessStatusCode();

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.InventoryItems.SingleAsync(x => x.Id == id);
            item.IsActive = false;
            await db.SaveChangesAsync();
        }

        var again = await CreateAsync(companyId, pano, secim, unit: "adet");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var body = await again.Content.ReadAsStringAsync();
        Assert.Contains("ARŞİVDE", body);
    }
}
