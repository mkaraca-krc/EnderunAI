using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Email;
using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        /// <summary>
        /// `true` ise HERHANGİ bir çağrı testi düşürür.
        ///
        /// "Gönderilen sayısı 0" demek yetmez: gönderim kodu çalışıp
        /// bir şey göndermemiş de olabilir. `DryRun`'da istenen şey
        /// GÖNDERİM YOLUNA HİÇ GİRİLMEMESİ.
        /// </summary>
        public bool CagriYasak { get; set; }
        public bool Patlasin { get; set; }
        public string? PatlayacakAdres { get; set; }

        public bool IsConfigured => true;

        public Task SendAsync(
            string toEmail, string? toName, string subject, string htmlBody,
            CancellationToken cancellationToken = default)
        {
            if (CagriYasak)
            {
                throw new InvalidOperationException(
                    "SMTP istemcisi çağrıldı — DryRun'da gönderim yoluna " +
                    "HİÇ girilmemeliydi.");
            }

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
        IServiceScope scope, IEmailService eposta) =>
        new(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            eposta,
            scope.ServiceProvider.GetRequiredService<IUserAuthorizationService>(),
            NullLogger<DailySummaryService>.Instance);

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

        /*
         * KAPSAM KAYDI ŞART.
         *
         * Özet artık kapsam süzgecinden geçiyor: kapsam kaydı
         * olmayan kullanıcı HİÇBİR ŞEY görmüyor (güvenli taraf).
         * Testte global kapsam veriliyor ki ölçülen şey kapsam
         * süzgeci değil, testin asıl konusu olsun — kapsam
         * sınaması ayrı testte.
         */
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = kullanici.Id,
            ScopeType = DataScopeType.All
        });

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
            Kind = WorkTaskKind.IsEmri,
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

        var gonderilen = await Servis(scope, eposta)
            .RunAsync(DailySummaryMode.Kapali, CancellationToken.None);

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

        var gonderilen = await Servis(scope, eposta)
            .RunAsync(DailySummaryMode.DryRun, CancellationToken.None);

        Assert.Equal(0, gonderilen);
        Assert.Empty(eposta.Gonderilenler);

        // AMA HESAPLADI: özet satırı üretilmiş olmalı.
        var satirlar = await Servis(scope, eposta).HesaplaAsync(CancellationToken.None);

        Assert.Contains(satirlar, x => x.UserId == kullanici.Id);
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

        await Servis(scope, eposta)
            .RunAsync(DailySummaryMode.Acik, CancellationToken.None);

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

        await Servis(scope, eposta)
            .RunAsync(DailySummaryMode.Acik, CancellationToken.None);

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

        await Servis(scope, eposta)
            .RunAsync(DailySummaryMode.Acik, CancellationToken.None);

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

/// <summary>
/// DRYRUN GÜVENCELERİ — ÜÇ SÖZ.
///
///   1. Gönderim yoluna HİÇ girilmez (sahte istemci değil, çağrı yok).
///   2. Kayda kişisel veri yazılmaz.
///   3. Her alıcı yalnız KENDİ KAPSAMINDAKİ satırları görür.
/// </summary>
[Collection("Integration")]
public sealed class DailySummaryDryRunTests(DatabaseFixture fixture)
{
    /// <summary>
    /// ÇAĞRI SAYAN istemci.
    ///
    /// İLK SÜRÜM HATA FIRLATIYORDU VE TEST AÇIĞI VARDI: `RunAsync`
    /// her kişiyi kendi `try/catch`'inde çalıştırdığı için fırlatılan
    /// hata YUTULUYOR, `gonderilen` sayacı da artmadığı için test
    /// yeşil kalıyordu. Sabotaj (dryrun'da gönderim yolunu açmak)
    /// testi kırmadı — açığı sonda gösterdi.
    ///
    /// Sayaç yutulamaz: çağrı olduysa sayı artar.
    /// </summary>
    private sealed class CagriSayan : IEmailService
    {
        public int CagriSayisi { get; private set; }

        public bool IsConfigured => true;

        public Task SendAsync(
            string toEmail, string? toName, string subject, string htmlBody,
            CancellationToken cancellationToken = default)
        {
            CagriSayisi++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Kaydedilen günlük satırlarını toplayan sağlayıcı.</summary>
    private sealed class YakalayanLogger : ILogger<DailySummaryService>
    {
        public readonly List<string> Satirlar = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Satirlar.Add(formatter(state, exception));

            // Yapılandırılmış günlükte değerler ayrı alanlara gider;
            // yasak veri orada da geçmemeli.
            if (state is IEnumerable<KeyValuePair<string, object?>> ciftler)
            {
                foreach (var cift in ciftler)
                    Satirlar.Add($"{cift.Key}={cift.Value}");
            }
        }
    }

    private static DailySummaryService Servis(
        IServiceScope scope, IEmailService eposta, ILogger<DailySummaryService> logger) =>
        new(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            eposta,
            scope.ServiceProvider.GetRequiredService<IUserAuthorizationService>(),
            logger);

    private static async Task<AppUser> KullaniciAsync(
        AppDbContext db, string suffix, string? adSoyad = null)
    {
        var kullanici = new AppUser
        {
            Username = $"kuru-{suffix}",
            FullName = adSoyad ?? $"Kuru Kullanıcı {suffix}",
            Email = $"kuru-{suffix}@ornek.test",
            PasswordHash = "x",
            PasswordSalt = "x",
            IsActive = true
        };

        db.Users.Add(kullanici);

        // Kapsam kaydı şart — bkz. yukarıdaki yardımcı.
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = kullanici.Id,
            ScopeType = DataScopeType.All
        });

        await db.SaveChangesAsync();
        return kullanici;
    }

    private static async Task GorevAsync(
        AppDbContext db, Project proje, Guid atanan, string baslik)
    {
        db.WorkTasks.Add(new WorkTask
        {
            CompanyId = proje.CompanyId,
            ProjectId = proje.Id,
            TaskNumber = $"TEST-KRU-{Guid.NewGuid():N}"[..20],
            Title = baslik,
            Kind = WorkTaskKind.IsEmri,
            Status = WorkTaskStatus.Open,
            AssignedToUserId = atanan
        });

        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------
    // 1) GÖNDERİM YOLUNA HİÇ GİRİLMEZ
    // ---------------------------------------------------------------

    /// <summary>
    /// "Gönderilen sayısı 0" demek YETMEZ: gönderim kodu çalışıp bir
    /// şey göndermemiş de olabilir. İstenen şey gönderim yoluna HİÇ
    /// girilmemesi — bir gün o yolda yan etki doğduğunda (kota,
    /// harici kayıt, sıraya yazma) fark ortaya çıkar.
    ///
    /// İstemci çağrıldığında hata fırlatıyor; test hatasız geçerse
    /// çağrılmamış demektir.
    /// </summary>
    [Fact]
    public async Task DryRun_SmtpIstemcisiniHicCagirmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"KRU{suffix}");

        var kullanici = await KullaniciAsync(db, suffix);
        await GorevAsync(db, proje, kullanici.Id, "Kuru koşu görevi");

        var logger = new YakalayanLogger();
        var eposta = new CagriSayan();

        var gonderilen = await Servis(scope, eposta, logger)
            .RunAsync(DailySummaryMode.DryRun, CancellationToken.None);

        Assert.Equal(0, gonderilen);

        /*
         * ASIL İDDİA: SMTP İSTEMCİSİ HİÇ ÇAĞRILMADI.
         *
         * "Gönderilen sayısı 0" yetmez — hata fırlatan bir istemcide
         * de sayaç 0 kalır, üstelik hata kişi bazında yutulur.
         * Çağrı sayacı yutulamaz.
         */
        Assert.Equal(0, eposta.CagriSayisi);

        // ÖZET YİNE DE ÜRETİLDİ: kuru koşu kaydı yazılmış olmalı.
        Assert.Contains(logger.Satirlar, x => x.Contains("kuru koşu"));
    }

    // ---------------------------------------------------------------
    // 2) KAYDA KİŞİSEL VERİ YAZILMAZ
    // ---------------------------------------------------------------

    /// <summary>
    /// YASAK ALAN LİSTESİ: görev başlığı, kişi adı, kullanıcı adı,
    /// e-posta adresi.
    ///
    /// Kuru koşu kaydının amacı "kaç kişiye ne kadar iş gidecek"
    /// sorusunu cevaplamak; kimin hangi işi var sorusunu değil.
    /// Günlük dosyası kişisel veri tutulacak yer değil ve bir kez
    /// okunup atılacak bir bilgi için o riski almaya gerek yok.
    /// </summary>
    [Fact]
    public async Task DryRun_KaydaKisiselVeriYazmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"KVR{suffix}");

        const string adSoyad = "Ayşe Yılmaz Testkullanıcı";
        const string gorevBasligi = "Gizli kalması gereken görev başlığı";

        var kullanici = await KullaniciAsync(db, suffix, adSoyad);
        await GorevAsync(db, proje, kullanici.Id, gorevBasligi);

        var logger = new YakalayanLogger();

        await Servis(scope, new CagriSayan(), logger)
            .RunAsync(DailySummaryMode.DryRun, CancellationToken.None);

        var tumKayit = string.Join("\n", logger.Satirlar);

        // YASAK ALANLAR — hiçbiri geçmemeli.
        Assert.DoesNotContain(gorevBasligi, tumKayit);
        Assert.DoesNotContain(adSoyad, tumKayit);
        Assert.DoesNotContain(kullanici.Username, tumKayit);
        Assert.DoesNotContain(kullanici.Email!, tumKayit);

        // AMA SAYILAR VAR: kayıt boş değil, işini yapıyor.
        Assert.Contains(logger.Satirlar, x => x.Contains("aliciSayisi"));
        Assert.Contains(logger.Satirlar, x => x.Contains("uretimSuresiMs"));
    }

    // ---------------------------------------------------------------
    // 3) KAPSAM DIŞI SATIR ALINMAZ
    // ---------------------------------------------------------------

    /// <summary>
    /// KAPSAM DIŞI GÖREV ÖZETE GİRMEZ.
    ///
    /// "Bana atanmış" süzgeci tek başına yetmez: kapsam
    /// değişikliğinden ÖNCE atanmış bir görev, kullanıcı artık o
    /// şirketi göremese bile üzerinde kalır. Özet, kullanıcının
    /// göremeyeceği bir kaydın varlığını SAYI OLARAK BİLE
    /// sızdırmamalı.
    /// </summary>
    [Fact]
    public async Task Alici_KapsamDisiSatirAlmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kendiProje = await TestDataFactory.CreateProjectAsync(db, $"KPA{suffix}");
        var yabanciProje = await TestDataFactory.CreateProjectAsync(db, $"KPB{suffix}");

        /*
         * KAPSAMI A ŞİRKETİYLE SINIRLI KULLANICI.
         *
         * Rol "İK Sorumlusu": "Admin" rol ADI tek başına global erişim
         * verdiği için burada kullanılamaz — kapsam süzgeci hiç
         * çalışmaz ve test hiçbir şey kanıtlamazdı.
         */
        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, $"kuru-kapsam-{suffix}", ["İK Sorumlusu"], kendiProje.CompanyId);

        var benKimim = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            "/api/auth/me");

        var kullaniciId = benKimim.GetProperty("id").GetGuid();

        // Kullanıcıya HER İKİ şirketten de görev atanıyor.
        await GorevAsync(db, kendiProje, kullaniciId, "Kapsam içi görev");
        await GorevAsync(db, yabanciProje, kullaniciId, "Kapsam dışı görev");

        var satirlar = await Servis(scope, new CagriSayan(), new YakalayanLogger())
            .HesaplaAsync(CancellationToken.None);

        var satir = satirlar.SingleOrDefault(x => x.UserId == kullaniciId);

        Assert.NotNull(satir);

        // YALNIZ KAPSAM İÇİ GÖREV SAYILDI.
        Assert.Equal(1, satir!.OpenTaskCount);
    }
}
