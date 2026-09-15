using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// BAŞARISIZ GİRİŞ KAYDA GEÇER — PAROLA ASLA.
///
/// ═══ ÖLÇÜLEN BOŞLUK (2026-09-15) ═══
///
/// 16 günlük saldırı incelemesinde *"hangi kullanıcı adları denendi"*
/// sorusu **ÖLÇEMEDİ** kaldı: sistem başarısız girişi kullanıcı adıyla
/// kaydetmiyordu. Portal jetonundaki boşlukla aynı aile.
///
/// Kullanıcı adı sayacı geldiğine göre bu kayıt ŞART: sayaç yanıyor ama
/// KİME KARŞI yandığını göremezsek uyarı sağır kalır.
///
/// ═══ EN SERT ŞART: PAROLA ASLA ═══
///
/// Son test tam bunu tutuyor. Denetim kaydı bir gün dışa aktarılır,
/// bir ekranda gösterilir ya da yedeğe girer; parolanın oraya düşmesi
/// geri alınamaz.
/// </summary>
[Collection("Integration")]
public sealed class BasarisizGirisKaydiTests(DatabaseFixture fixture)
{
    // Fikstürün kendi parolası kullanılıyor — ikinci bir sabit
    // tanımlamak, iki kopyanın zamanla ayrışması demekti.
    private static string Parola => TestUserFactory.Parola;

    private async Task<(string kullanici, DateTime baslangic)> ZeminAsync()
    {
        var baslangic = DateTime.UtcNow.AddSeconds(-5);
        var kurulan = await TestUserFactory.KullaniciKurAsync(fixture, "giris-sonda", []);
        return (kurulan.KullaniciAdi.ToLowerInvariant(), baslangic);
    }

    [Fact]
    public async Task YanlisParola_KullaniciAdiyla_KaydaGecer()
    {
        var (kullanici, baslangic) = await ZeminAsync();
        var istemci = fixture.Factory.CreateClient();

        var yanit = await istemci.PostAsJsonAsync("/api/auth/login",
            new { username = kullanici, password = "yanlis-parola" });

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kayit = await db.Set<SecurityAuditEvent>()
            .Where(x => x.Action == "LoginFailed"
                        && x.ActorUsername == kullanici
                        && x.OccurredAtUtc >= baslangic)
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstOrDefaultAsync();

        Assert.NotNull(kayit);
        Assert.Equal(kullanici, kayit!.ActorUsername);
        Assert.False(string.IsNullOrWhiteSpace(kayit.IpAddress), "IP kaydedilmemiş.");
        Assert.Contains("parola yanlış", kayit.DetailsJson ?? string.Empty);
    }

    [Fact]
    public async Task OLMAYAN_Kullanici_da_KaydaGecer()
    {
        var baslangic = DateTime.UtcNow.AddSeconds(-5);
        var yok = $"hic-olmayan-{Guid.NewGuid():N}"[..24];
        var istemci = fixture.Factory.CreateClient();

        var yanit = await istemci.PostAsJsonAsync("/api/auth/login",
            new { username = yok, password = "herhangi" });

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kayit = await db.Set<SecurityAuditEvent>()
            .Where(x => x.Action == "LoginFailed"
                        && x.ActorUsername == yok.ToLowerInvariant()
                        && x.OccurredAtUtc >= baslangic)
            .FirstOrDefaultAsync();

        // OLMAYAN kullanıcı da kaydedilir: saldırı tam olarak bu —
        // var olmayan adlarla deneme. Kaydedilmezse dövülen hesap
        // görünmez.
        Assert.NotNull(kayit);
        Assert.Contains("kullanıcı yok", kayit!.DetailsJson ?? string.Empty);
    }

    [Fact]
    public async Task PAROLA_ASLA_KAYDA_GECMEZ()
    {
        var (kullanici, baslangic) = await ZeminAsync();
        var istemci = fixture.Factory.CreateClient();

        // Hem DOĞRU hem YANLIŞ parolayla dene: ikisi de sızmamalı.
        await istemci.PostAsJsonAsync("/api/auth/login",
            new { username = kullanici, password = Parola + "-yanlis" });
        await istemci.PostAsJsonAsync("/api/auth/login",
            new { username = kullanici, password = Parola });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kayitlar = await db.Set<SecurityAuditEvent>()
            .Where(x => x.OccurredAtUtc >= baslangic)
            .ToListAsync();

        // POZİTİF KONTROL: ölçecek kayıt var mı — yoksa "parola yok"
        // sonucu kaydın yokluğundan gelirdi (Kural 48).
        Assert.NotEmpty(kayitlar);

        foreach (var k in kayitlar)
        {
            var tumu = $"{k.ActorUsername} {k.Action} {k.DetailsJson} {k.UserAgent} {k.IpAddress}";
            Assert.DoesNotContain(Parola, tumu, StringComparison.Ordinal);
            Assert.DoesNotContain("yanlis-parola", tumu, StringComparison.Ordinal);
        }
    }
}
