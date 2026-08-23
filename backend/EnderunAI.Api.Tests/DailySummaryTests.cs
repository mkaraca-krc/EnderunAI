using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Services.Email;
using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// GÜNLÜK E-POSTA ÖZETİ.
///
/// Bu özellik canlıya çıktığı gün GERÇEK İNSANLARA e-posta göndermeye
/// başlıyor; yanlış içerik ya da yanlış alıcı geri alınamaz. Bu
/// yüzden dört kademeli bayrak (off/dryrun/test/on) ve bu testler.
/// </summary>
[Collection("Integration")]
public sealed class DailySummaryTests(DatabaseFixture fixture)
{
    /// <summary>Gönderilenleri sayan sahte e-posta servisi.</summary>
    private sealed class SahteEposta : IEmailService
    {
        public readonly List<string> Gonderilenler = [];
        public bool Patlasin { get; set; }
        public string? PatlayacakAdres { get; set; }

        public bool IsConfigured => true;

        public Task SendAsync(
            string toEmail, string? toName, string subject, string htmlBody,
            CancellationToken cancellationToken = default)
        {
            if (Patlasin && (PatlayacakAdres is null ||
                string.Equals(PatlayacakAdres, toEmail, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("SMTP sondası: gönderim başarısız.");
            }

            Gonderilenler.Add(toEmail);
            return Task.CompletedTask;
        }
    }

    private static DailySummaryService Servis(
        AppDbContext db, IEmailService eposta) =>
        new(db, eposta, NullLogger<DailySummaryService>.Instance);

    private static async Task<AppUser> KullaniciAsync(
        AppDbContext db, string suffix, bool ozetIstiyor = true)
    {
        var kullanici = new AppUser
        {
            Username = $"ozet-{suffix}",
            FullName = $"Özet Kullanıcı {suffix}",
            Email = $"ozet-{suffix}@ornek.test",
            PasswordHash = "x",
            PasswordSalt = "x",
            IsActive = true
        };

        db.Users.Add(kullanici);

        if (!ozetIstiyor)
        {
            db.UserUiPreferences.Add(new UserUiPreference
            {
                UserId = kullanici.Id,
                DailySummaryEmailEnabled = false
            });
        }

        await db.SaveChangesAsync();
        return kullanici;
    }

    private static async Task GorevAsync(
        AppDbContext db, Project proje, Guid atanan, DateTime? termin = null)
    {
        db.WorkTasks.Add(new WorkTask
        {
            CompanyId = proje.CompanyId,
            ProjectId = proje.Id,
            TaskNumber = $"TEST-OZT-{Guid.NewGuid():N}"[..20],
            Title = "Özet testi görevi",
            Status = WorkTaskStatus.Open,
            AssignedToUserId = atanan,
            DueDate = termin
        });

        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------
    // SAAT DİLİMİ
    // ---------------------------------------------------------------

    /// <summary>
    /// 07:00 TÜRKİYE = 04:00 UTC.
    ///
    /// Sunucu `Etc/UTC` ve Türkiye sabit UTC+3 (yaz saati YOK). Kodda
    /// "07:00" yazıp sunucunun UTC olduğunu unutmak, özetin sabah
    /// 10'da gitmesi demekti.
    ///
    /// YEREL MAKİNE AYARINA GÜVENİLMİYOR: sabit doğrudan sınanıyor.
    /// </summary>
    [Fact]
    public void GonderimSaati_UtcDortOlmali()
    {
        Assert.Equal(4, DailySummaryService.GonderimSaatiUtc);

        // Türkiye karşılığı 07:00 — dönüşüm burada açıkça yazılı.
        var turkiye = new TimeSpan(DailySummaryService.GonderimSaatiUtc, 0, 0)
            + TimeSpan.FromHours(3);

        Assert.Equal(new TimeSpan(7, 0, 0), turkiye);
    }

    // ---------------------------------------------------------------
    // KADEMELER
    // ---------------------------------------------------------------

    [Fact]
    public async Task ModOff_HicEpostaGondermez()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var eposta = new SahteEposta();

        var gonderilen = await Servis(db, eposta)
            .RunAsync(DailySummaryMode.Off, [], CancellationToken.None);

        Assert.Equal(0, gonderilen);
        Assert.Empty(eposta.Gonderilenler);
    }

    /// <summary>
    /// KURU KOŞU: tarama koşar, e-posta GİTMEZ.
    /// </summary>
    [Fact]
    public async Task ModDryRun_HesaplarAmaGondermez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"OZT{suffix}");

        var kullanici = await KullaniciAsync(db, suffix);
        await GorevAsync(db, proje, kullanici.Id);

        var eposta = new SahteEposta();

        var gonderilen = await Servis(db, eposta)
            .RunAsync(DailySummaryMode.DryRun, [], CancellationToken.None);

        Assert.Equal(0, gonderilen);
        Assert.Empty(eposta.Gonderilenler);

        // AMA HESAPLADI: özet satırı üretilmiş olmalı.
        var satirlar = await Servis(db, eposta).HesaplaAsync(CancellationToken.None);

        Assert.Contains(satirlar, x => x.UserId == kullanici.Id);
    }

    /// <summary>
    /// TEST MODU: yalnız listedeki adreslere gider.
    /// </summary>
    [Fact]
    public async Task ModTest_YalnizListedekiAdreseGonderir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"OZT{suffix}");

        var listedeki = await KullaniciAsync(db, $"listede-{suffix}");
        var listeDisi = await KullaniciAsync(db, $"disarda-{suffix}");

        await GorevAsync(db, proje, listedeki.Id);
        await GorevAsync(db, proje, listeDisi.Id);

        var eposta = new SahteEposta();

        await Servis(db, eposta).RunAsync(
            DailySummaryMode.Test,
            [listedeki.Email!],
            CancellationToken.None);

        Assert.Contains(listedeki.Email!, eposta.Gonderilenler);
        Assert.DoesNotContain(listeDisi.Email!, eposta.Gonderilenler);
    }

    // ---------------------------------------------------------------
    // BOŞ ÖZET
    // ---------------------------------------------------------------

    /// <summary>
    /// YAPACAK İŞİ OLMAYANA E-POSTA GİTMEZ.
    ///
    /// "0 açık göreviniz var" e-postası, zilin kapatılmasıyla aynı
    /// sonucu doğurur: insanlar okumamayı öğrenir.
    /// </summary>
    [Fact]
    public async Task IsiOlmayanKisiye_EpostaGitmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Görevi, bildirimi, hiçbir şeyi olmayan kullanıcı.
        var bos = await KullaniciAsync(db, $"bos-{suffix}");

        var eposta = new SahteEposta();

        await Servis(db, eposta)
            .RunAsync(DailySummaryMode.On, [], CancellationToken.None);

        Assert.DoesNotContain(bos.Email!, eposta.Gonderilenler);
    }

    // ---------------------------------------------------------------
    // KULLANICI TERCİHİ
    // ---------------------------------------------------------------

    /// <summary>
    /// KAPATAN KİŞİYE GİTMEZ — zil ve uygulama içi bildirim
    /// etkilenmiyor, yalnız e-posta.
    ///
    /// Bu seçenek olmasaydı e-postayı istemeyen kişi onu filtreye
    /// atardı; sonra gerçekten önemli bir e-posta da aynı filtreye
    /// düşerdi.
    /// </summary>
    [Fact]
    public async Task OzetiKapatanKisiye_EpostaGitmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"OZT{suffix}");

        var kapatan = await KullaniciAsync(db, $"kapali-{suffix}", ozetIstiyor: false);
        var acik = await KullaniciAsync(db, $"acik-{suffix}");

        await GorevAsync(db, proje, kapatan.Id);
        await GorevAsync(db, proje, acik.Id);

        var eposta = new SahteEposta();

        await Servis(db, eposta)
            .RunAsync(DailySummaryMode.On, [], CancellationToken.None);

        Assert.DoesNotContain(kapatan.Email!, eposta.Gonderilenler);
        Assert.Contains(acik.Email!, eposta.Gonderilenler);
    }

    // ---------------------------------------------------------------
    // KİŞİ BAZINDA HATA SINIRI
    // ---------------------------------------------------------------

    /// <summary>
    /// BİR KİŞİNİN HATASI TURU DURDURMAZ.
    ///
    /// Döngünün dışında tek bir try olsaydı ilk hata turu bitirir ve
    /// sonraki kişiler sessizce atlanırdı — tek kişinin bozuk adresi
    /// yüzünden kimse özet almazdı.
    /// </summary>
    [Fact]
    public async Task BirKisininHatasi_TuruDurdurmaz_VeKaydaDuser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"OZT{suffix}");

        var birinci = await KullaniciAsync(db, $"a-{suffix}");
        var patlayan = await KullaniciAsync(db, $"b-{suffix}");
        var ucuncu = await KullaniciAsync(db, $"c-{suffix}");

        foreach (var k in new[] { birinci, patlayan, ucuncu })
            await GorevAsync(db, proje, k.Id);

        var onceki = await db.SecurityAuditEvents
            .CountAsync(x => x.Action == "DailySummaryEmailFailed");

        var eposta = new SahteEposta
        {
            Patlasin = true,
            PatlayacakAdres = patlayan.Email
        };

        await Servis(db, eposta)
            .RunAsync(DailySummaryMode.On, [], CancellationToken.None);

        // DİĞER İKİSİ ALDI.
        Assert.Contains(birinci.Email!, eposta.Gonderilenler);
        Assert.Contains(ucuncu.Email!, eposta.Gonderilenler);
        Assert.DoesNotContain(patlayan.Email!, eposta.Gonderilenler);

        // HATA SESSİZ KALMADI.
        var sonraki = await db.SecurityAuditEvents
            .CountAsync(x => x.Action == "DailySummaryEmailFailed");

        Assert.True(
            sonraki > onceki,
            "E-posta gönderimi başarısız oldu ama hiçbir kayıt üretilmedi.");
    }
}
