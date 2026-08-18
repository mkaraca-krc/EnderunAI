using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// POZ LİSTESİ KIRPILMASI KULLANICIYA SÖYLENMELİ.
///
/// Kütüphane canlıda 23.531 poz taşıyor ve uç bunu bir tavanla
/// kırpıyor — DOĞRU karar, 23 bin satır tarayıcıyı kilitler.
///
/// Hata tavanda değil, tavanın GİZLİ olmasındaydı: uç yalnız diziyi
/// döndürüyordu, ekran da gelen kaydı sayıp "Toplam Poz: 100" diye
/// gösteriyordu. Yani kullanıcı kütüphanesinde 100 poz olduğunu
/// sanıyordu ve 101. pozun var olmadığı sonucuna varıyordu.
///
/// Bu testler tavanın kendisini değil, tavanın RAPORLANMASINI korur.
/// Uç toplam sayıyı kaybederse buradan düşer.
/// </summary>
[Collection("Integration")]
public sealed class PositionListTruncationTests(DatabaseFixture fixture)
{
    private const int VarsayilanTavan = 100;

    private async Task<Guid> SeedPositionsAsync(int count, string suffix) =>
        await SeedPositionsAsync(count, suffix, "Kablo kanalı", "Buat montajı");

    private async Task<Guid> SeedPositionsAsync(
        int count, string suffix, string ciftAd, string tekAd)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        for (var i = 0; i < count; i++)
        {
            db.EngineeringPositions.Add(new EngineeringPosition
            {
                CompanyId = company.Id,
                // Kod sırası deterministik olsun: uç Code'a göre sıralıyor.
                Code = $"{suffix}.{i:0000}",
                Name = i % 2 == 0 ? $"{ciftAd} {i}" : $"{tekAd} {i}",
                Unit = "m",
                Source = EngineeringPositionSource.Official,
                Status = EngineeringPositionStatus.Active
            });
        }

        await db.SaveChangesAsync();
        return company.Id;
    }

    private async Task<JsonElement> GetAsync(string query)
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var response = await client.GetAsync($"/api/engineering-positions?{query}");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// ASIL GÜVENCE: kırpılmış listede toplam sayı GERÇEK sayıdır,
    /// dönen kayıt sayısı değil.
    /// </summary>
    [Fact]
    public async Task KirpilmisListe_ToplamiGercekSayiyiSoyler()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await SeedPositionsAsync(130, suffix);

        var body = await GetAsync($"companyId={companyId}");

        Assert.Equal(130, body.GetProperty("total").GetInt32());
        Assert.Equal(VarsayilanTavan, body.GetProperty("items").GetArrayLength());
        Assert.True(body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(VarsayilanTavan, body.GetProperty("take").GetInt32());
    }

    /// <summary>
    /// Tavan yetiyorsa kırpılma BİLDİRİLMEZ — yanlış uyarı da bir hata.
    /// </summary>
    [Fact]
    public async Task TavanYetiyorsa_KirpilmaBildirilmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await SeedPositionsAsync(12, suffix);

        var body = await GetAsync($"companyId={companyId}");

        Assert.Equal(12, body.GetProperty("total").GetInt32());
        Assert.Equal(12, body.GetProperty("items").GetArrayLength());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    /// <summary>
    /// Tavan yükseltilince tüm kayıtlar gelir ve kırpılma biter.
    /// </summary>
    [Fact]
    public async Task TavanYukseltilince_TumKayitlarDoner()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await SeedPositionsAsync(130, suffix);

        var body = await GetAsync($"companyId={companyId}&take=500");

        Assert.Equal(130, body.GetProperty("total").GetInt32());
        Assert.Equal(130, body.GetProperty("items").GetArrayLength());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    /// <summary>
    /// TOPLAM SÜZGEÇTEN SONRA SAYILIR.
    ///
    /// Kütüphane toplamını döndürmek kolay olurdu ama arama yapan
    /// kullanıcıya yanlış bilgi verirdi: "23.531 kayıttan 100'ü"
    /// yazarken aramaya uyan yalnız 65 kayıt olabilir.
    /// </summary>
    [Fact]
    public async Task Toplam_SuzgectenSonraSayilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await SeedPositionsAsync(130, suffix);

        // 130 kaydın yarısı "Buat montajı" adını taşıyor.
        var body = await GetAsync($"companyId={companyId}&search=Buat");

        Assert.Equal(65, body.GetProperty("total").GetInt32());
        Assert.Equal(65, body.GetProperty("items").GetArrayLength());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    /// <summary>
    /// TOPLAM ŞİRKET SÜZGECİNE UYAR — başka şirketin kayıtları sayıma
    /// girmez.
    ///
    /// NEDEN AYRI TEST: diğer testler tek şirket tohumluyor, yani
    /// süzgeç hiç uygulanmasa bile yalıtılmış veritabanında doğru sayı
    /// çıkar. Bu test AYNI KOŞUDA iki şirket kuruyor; süzgeç toplama
    /// uygulanmazsa 5 yerine 12 görür.
    ///
    /// Bu boşluk gerçekten ısırdı: `items` süzülürken `total`
    /// süzülmüyordu ve testler tek başına GEÇİYORDU (bkz. DURUM §5/17 —
    /// sebep sonda sonrası yeniden derlenmeyen ikiliydi).
    /// </summary>
    [Fact]
    public async Task Toplam_BaskaSirketinKayitlariniSaymaz()
    {
        var a = await SeedPositionsAsync(
            5, Guid.NewGuid().ToString("N")[..8], "Alfa", "Alfa");
        await SeedPositionsAsync(
            7, Guid.NewGuid().ToString("N")[..8], "Beta", "Beta");

        var body = await GetAsync($"companyId={a}");

        Assert.Equal(5, body.GetProperty("total").GetInt32());

        foreach (var item in body.GetProperty("items").EnumerateArray())
            Assert.Equal(a, item.GetProperty("companyId").GetGuid());
    }

    /// <summary>
    /// Tavanın ÜSTÜNDE bir değer istenirse uç kendi sınırına düşer —
    /// ve o zaman da kırpılmayı bildirir.
    /// </summary>
    [Fact]
    public async Task TavanAsiriIstenirse_UcKendiSinirinaDuser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await SeedPositionsAsync(130, suffix);

        var body = await GetAsync($"companyId={companyId}&take=99999");

        // 99999 kabul edilmez; uç varsayılana döner.
        Assert.Equal(VarsayilanTavan, body.GetProperty("take").GetInt32());
        Assert.Equal(130, body.GetProperty("total").GetInt32());
        Assert.True(body.GetProperty("hasMore").GetBoolean());
    }
}
