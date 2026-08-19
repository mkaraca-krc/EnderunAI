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
/// KATEGORİ + ÖZELLİK ŞABLONU (S1).
///
/// Bu şablon stok paketinin temeli: kartın ADI, MÜKERRER ENGELİ ve
/// BİRİM KİLİDİ hep buradan türeyecek. Şablon yanlışsa üstüne kurulan
/// her şey yanlış olur.
///
/// Kategori SİSTEM GENELİ (şirkete bağlı değil) — "kablo tavası" her
/// şirkette aynı şeydir.
/// </summary>
[Collection("Integration")]
public sealed class InventoryCategoryTests(DatabaseFixture fixture)
{
    private async Task<JsonElement> GetCategoriesAsync()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var response = await client.GetAsync("/api/inventory/categories");

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Category(JsonElement body, string code) =>
        body.EnumerateArray().Single(x => x.GetProperty("code").GetString() == code);

    private static List<string> Options(JsonElement category, string attributeCode) =>
        category.GetProperty("attributes").EnumerateArray()
            .Single(x => x.GetProperty("code").GetString() == attributeCode)
            .GetProperty("options").EnumerateArray()
            .Select(x => x.GetProperty("value").GetString()!)
            .ToList();

    [Fact]
    public async Task Tohum_OnDortKategoriKurar()
    {
        var body = await GetCategoriesAsync();

        var codes = body.EnumerateArray()
            .Select(x => x.GetProperty("code").GetString()!)
            .ToList();

        // 12 STANDART + 2 SERBEST.
        foreach (var expected in new[]
        {
            "KABLO_TAVASI", "KABLO_MERDIVENI", "KABLO", "OTOMAT_SALTER",
            "KACAK_AKIM_ROLESI", "PRIZ_ANAHTAR", "ARMATUR_STANDART",
            "BORU_KANAL", "PANO", "BUSBAR", "TOPRAKLAMA", "SARF",
            "DEKORATIF_AYDINLATMA", "OZEL_IMALAT"
        })
        {
            Assert.Contains(expected, codes);
        }
    }

    /// <summary>
    /// ÇOK BİRİMLİ KATEGORİ — tasarımın can alıcı noktası.
    ///
    /// Topraklama hem metre (bakır şerit) hem adet (toprak çubuğu)
    /// taşır; sarf kg/paket/adet olabilir. Tek birimlik bir alan
    /// olsaydı bu kategoriler ya bölünecek ya birim serbest kalacaktı.
    /// </summary>
    [Fact]
    public async Task CokBirimliKategoriler_IzinVerilenBirimListesiTasir()
    {
        var body = await GetCategoriesAsync();

        var topraklama = Category(body, "TOPRAKLAMA")
            .GetProperty("units").EnumerateArray()
            .Select(x => x.GetString()!).ToList();

        Assert.Equal(2, topraklama.Count);
        Assert.Contains("adet", topraklama);
        Assert.Contains("metre", topraklama);

        var sarf = Category(body, "SARF")
            .GetProperty("units").EnumerateArray()
            .Select(x => x.GetString()!).ToList();

        Assert.Equal(3, sarf.Count);
        Assert.Contains("kg", sarf);

        // Tek birimli kategoride liste TEK elemanlı — davranış aynı.
        var tava = Category(body, "KABLO_TAVASI")
            .GetProperty("units").EnumerateArray()
            .Select(x => x.GetString()!).ToList();

        Assert.Single(tava);
        Assert.Equal("metre", tava[0]);
    }

    /// <summary>
    /// KAÇAK AKIM RÖLESİNDE KUTUP 2P/4P — otomatın 1P/3P'si DEĞİL.
    ///
    /// Şablonda kaçak akım rölesinin kutup seçenekleri verilmemişti;
    /// otomatınkini kopyalamak elektriksel olarak yanlış veri
    /// üretirdi (röle monofazede 2P, trifazede 4P olur). Karar
    /// kullanıcıdan alındı ve bu test onu sabitliyor.
    /// </summary>
    [Fact]
    public async Task KacakAkimRolesi_KutupSecenekleri2PVe4P()
    {
        var body = await GetCategoriesAsync();

        var role = Options(Category(body, "KACAK_AKIM_ROLESI"), "KUTUP");

        Assert.Equal(["2P", "4P"], role);

        // Otomat şalterinki AYRI kalmalı.
        var otomat = Options(Category(body, "OTOMAT_SALTER"), "KUTUP");

        Assert.Contains("1P", otomat);
        Assert.Contains("3P", otomat);
        Assert.DoesNotContain("2P", otomat);
    }

    /// <summary>
    /// Kablo merdiveni ölçü ve kaplama listelerini kablo tavasıyla
    /// PAYLAŞIR (karar). Ayrışırlarsa aynı ürün iki farklı değerle
    /// tanımlanabilir hâle gelir.
    /// </summary>
    [Fact]
    public async Task KabloMerdiveni_TavaylaAyniOlcuVeKaplamaListesi()
    {
        var body = await GetCategoriesAsync();

        var tava = Category(body, "KABLO_TAVASI");
        var merdiven = Category(body, "KABLO_MERDIVENI");

        Assert.Equal(Options(tava, "OLCU"), Options(merdiven, "OLCU"));
        Assert.Equal(Options(tava, "KAPLAMA"), Options(merdiven, "KAPLAMA"));
    }

    /// <summary>
    /// SERBEST kategoride özellik YOK — ad elle yazılır.
    /// </summary>
    [Fact]
    public async Task SerbestKategoriler_OzellikTasimaz()
    {
        var body = await GetCategoriesAsync();

        foreach (var code in new[] { "DEKORATIF_AYDINLATMA", "OZEL_IMALAT" })
        {
            var category = Category(body, code);

            Assert.Equal(1, category.GetProperty("kind").GetInt32());
            Assert.Empty(category.GetProperty("attributes").EnumerateArray());
        }
    }

    [Fact]
    public async Task SerbestKategoriye_OzellikEklenemez()
    {
        var body = await GetCategoriesAsync();
        var serbestId = Category(body, "DEKORATIF_AYDINLATMA").GetProperty("id").GetGuid();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            $"/api/inventory/categories/{serbestId}/attributes",
            new { code = "RENK", name = "Renk", sortOrder = 10, isRequired = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// BİRİMSİZ KATEGORİ AÇILAMAZ: birim kilidi kartın birim
    /// seçmesine dayanıyor, seçilecek liste boşsa kural çöker.
    /// </summary>
    [Fact]
    public async Task BirimsizKategori_Reddedilir()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/inventory/categories", new
        {
            code = $"BIRIMSIZ{Guid.NewGuid():N}"[..20],
            name = "Birimsiz Kategori",
            kind = 0,
            units = Array.Empty<string>(),
            sortOrder = 999
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// TOHUM SADECE EKLER: kullanıcının ekrandan yaptığı değişiklik
    /// yeniden başlatmada ezilmemeli.
    /// </summary>
    [Fact]
    public async Task Tohum_MevcutKategoriyiEzmez()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tava = await db.InventoryCategories
            .SingleAsync(x => x.Code == "KABLO_TAVASI");

        var original = tava.Name;
        tava.Name = "Kullanıcının Değiştirdiği Ad";
        await db.SaveChangesAsync();

        await InventoryCategorySeed.SeedAsync(db);

        var afterSeed = await db.InventoryCategories
            .AsNoTracking()
            .SingleAsync(x => x.Code == "KABLO_TAVASI");

        Assert.Equal("Kullanıcının Değiştirdiği Ad", afterSeed.Name);

        tava.Name = original;
        await db.SaveChangesAsync();
    }
}
