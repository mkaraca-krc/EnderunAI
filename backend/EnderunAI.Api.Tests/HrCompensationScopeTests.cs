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
/// EK ÜCRET — KAPSAM SIZINTISI KAPALI MI.
///
/// Ek ücret MAAŞ BİLGİSİDİR. Uçta kapsam süzgeci yoktu: şirket
/// izolasyonu yalnız isteğe bağlı `companyId` parametresine
/// dayanıyordu, yani parametre gönderilmediğinde BÜTÜN şirketlerin
/// kayıtları dönüyordu. Adres çubuğundan parametresiz çağırmak
/// yetiyordu.
///
/// BURADA ÜÇ SÖZ SINANIYOR:
///   1. Liste: A şirketinin kullanıcısı B'nin kayıtlarını GÖRMEZ —
///      companyId hiç göndermese bile.
///   2. Tekil kayıt: kimliği ELLE yazsa da göremez (404). Liste
///      süzülüp tekil uç açık bırakılsaydı sızıntı sürerdi.
///   3. Global erişimli kullanıcı hepsini görmeye DEVAM eder —
///      koruma, meşru erişimi kapatmamalı.
/// </summary>
[Collection("Integration")]
public sealed class HrCompensationScopeTests(DatabaseFixture fixture)
{
    private sealed record Sahne(
        Guid SirketA, Guid SirketB, Guid KayitA, Guid KayitB);

    /*
     * ROL SEÇİMİ TESADÜF DEĞİL: "Admin" rol ADI tek başına global
     * erişim veriyor (CurrentDataScopeService). O rolle yapılan bir
     * kapsam testi süzgeci HİÇ çalıştırmaz ve hiçbir şey kanıtlamaz —
     * ilk sürümde tam olarak bu oldu, test kırmızı verdi.
     *
     * "İK Sorumlusu" ek ücreti görme iznine sahip ama global erişimi
     * yok; kapsam süzgeci ancak böyle bir kullanıcıda sınanabilir.
     */
    private static async Task<Sahne> KurAsync(AppDbContext db, string suffix)
    {
        var a = await TestDataFactory.CreateProjectAsync(db, $"A{suffix}");
        var b = await TestDataFactory.CreateProjectAsync(db, $"B{suffix}");

        var kayitlar = new List<HrCompensationComponent>();

        foreach (var (sirket, kod) in new[]
                 {
                     (a.CompanyId, $"EKA{suffix}"),
                     (b.CompanyId, $"EKB{suffix}")
                 })
        {
            var kayit = new HrCompensationComponent
            {
                CompanyId = sirket,
                Code = kod,
                Name = $"Ek ücret {kod}",
                Amount = 1000m,
                EffectiveStartDate = DateTime.UtcNow.Date,
                IsActive = true
            };

            db.HrCompensationComponents.Add(kayit);
            kayitlar.Add(kayit);
        }

        await db.SaveChangesAsync();

        return new Sahne(a.CompanyId, b.CompanyId, kayitlar[0].Id, kayitlar[1].Id);
    }

    [Fact]
    public async Task AKullanicisi_BSirketininKayitlariniListedeGormez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "ek-ucret-a", ["İK Sorumlusu"], sahne.SirketA);

        // companyId HİÇ GÖNDERİLMİYOR — eski davranışta bu, bütün
        // şirketlerin kayıtlarını döndürüyordu.
        var yanit = await client.GetFromJsonAsync<JsonElement>(
            "/api/hr/compensation-components?pageSize=200");

        var kodlar = yanit.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("code").GetString())
            .ToList();

        Assert.Contains($"EKA{suffix}", kodlar);
        Assert.DoesNotContain($"EKB{suffix}", kodlar);
    }

    [Fact]
    public async Task AKullanicisi_BKaydiniKimlikleIsterse_VeriDonmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "ek-ucret-a2", ["İK Sorumlusu"], sahne.SirketA);

        // Kendi kaydı açılıyor — koruma meşru erişimi kapatmamalı.
        var kendi = await client.GetAsync(
            $"/api/hr/compensation-components/{sahne.KayitA}");

        Assert.Equal(HttpStatusCode.OK, kendi.StatusCode);

        // Başka şirketin kaydı KİMLİKLE isteniyor.
        var yabanci = await client.GetAsync(
            $"/api/hr/compensation-components/{sahne.KayitB}");

        Assert.Equal(HttpStatusCode.NotFound, yabanci.StatusCode);

        // VERİ SIZMIYOR: 404 gövdesinde kaydın hiçbir alanı olmamalı.
        var govde = await yabanci.Content.ReadAsStringAsync();

        Assert.DoesNotContain($"EKB{suffix}", govde);
    }

    [Fact]
    public async Task GlobalErisimliKullanici_HepsiniGormeyeDevamEder()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        // Varsayılan yardımcı `All` kapsamı veriyor.
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.GetFromJsonAsync<JsonElement>(
            "/api/hr/compensation-components?pageSize=200");

        var kodlar = yanit.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("code").GetString())
            .ToList();

        Assert.Contains($"EKA{suffix}", kodlar);
        Assert.Contains($"EKB{suffix}", kodlar);
    }

    /// <summary>
    /// Sayfalama gerçekten sunucuda: toplam sayı sayfadan bağımsız.
    /// </summary>
    [Fact]
    public async Task Sayfalama_ToplamSayfadanBagimsiz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var ilkSayfa = await client.GetFromJsonAsync<JsonElement>(
            "/api/hr/compensation-components?page=1&pageSize=1");

        Assert.Single(ilkSayfa.GetProperty("items").EnumerateArray());

        // Toplam, DÖNEN SATIR SAYISI DEĞİL: sayfadan hesaplansaydı
        // 1 yazardı ve kullanıcı "tek kayıt var" sanırdı.
        Assert.True(ilkSayfa.GetProperty("total").GetInt32() >= 2);
        Assert.True(ilkSayfa.GetProperty("hasMore").GetBoolean());
    }
}
