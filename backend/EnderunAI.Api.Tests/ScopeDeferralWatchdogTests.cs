using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// G3 ERTELEME BEKÇİSİ.
///
/// G3/2-3-4 paketleri ertelenirken gerekçe ölçülmüştü: canlıda
/// kapsamı sınırlı AKTİF kullanıcı yok ve tek şirket var, yani temel
/// çizgideki ~450 kapsamsız okuma GİZİL bir borç.
///
/// Erteleme iki koşula bağlıydı. Bunları "şu sorguyu çalıştır" diye
/// bir belgeye yazmak yetmezdi: o sorguyu birinin çalıştırmayı
/// hatırlaması gerekir ve hatırlamaz. Koşul gerçekleştiği gün
/// sistemin kendisi haber vermeli.
/// </summary>
[Collection("Integration")]
public sealed class ScopeDeferralWatchdogTests(DatabaseFixture fixture)
{
    private static async Task<int> UyariSayisiAsync(AppDbContext db, string action) =>
        await db.SecurityAuditEvents.CountAsync(x => x.Action == action);

    /// <summary>
    /// KAPSAMI SINIRLI AKTİF KULLANICI → UYARI.
    ///
    /// `ScopeType != All` olan aktif kullanıcı, kapsam süzgeçlerinin
    /// gerçekten devreye girdiği ilk andır: o andan itibaren
    /// kapsamsız okumalar gizil borç olmaktan çıkıp fiilî sızıntı
    /// haline gelir.
    /// </summary>
    [Fact]
    public async Task KapsamiSinirliAktifKullanici_UyariUretir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bekci = scope.ServiceProvider.GetRequiredService<ScopeDeferralWatchdog>();

        // Bugünün uyarısı zaten yazılmışsa temizle: bekçi gün başına
        // bir kez yazıyor.
        await db.SecurityAuditEvents
            .Where(x => x.Action == ScopeDeferralWatchdog.ActionScopedUser)
            .ExecuteDeleteAsync();

        var proje = await TestDataFactory.CreateProjectAsync(db, $"BKC{suffix}");

        var kullanici = new AppUser
        {
            Username = $"kapsamli-{suffix}",
            FullName = "Kapsamlı Kullanıcı",
            PasswordHash = "x",
            PasswordSalt = "x",
            IsActive = true
        };

        db.Users.Add(kullanici);

        // ŞİRKET KAPSAMI — global değil.
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = kullanici.Id,
            ScopeType = DataScopeType.Company,
            CompanyId = proje.CompanyId
        });

        await db.SaveChangesAsync();

        await bekci.CheckAsync(CancellationToken.None);

        Assert.Equal(1, await UyariSayisiAsync(
            db, ScopeDeferralWatchdog.ActionScopedUser));

        var uyari = await db.SecurityAuditEvents
            .AsNoTracking()
            .Where(x => x.Action == ScopeDeferralWatchdog.ActionScopedUser)
            .SingleAsync();

        Assert.Contains("ertelemesi sona erdi", uyari.DetailsJson!);

        /*
         * ADI KONMUŞ BORÇ UYARININ İÇİNDE OLMALI.
         *
         * Uyarı düştüğü gün, onu okuyan kişinin NEYE bakacağını da
         * okuması gerekir. Borcun adı ayrı bir belgede dursaydı, uyarı
         * "bir şey oldu" der ve o belgeyi kimse açmazdı.
         */
        Assert.Contains("KPI KAPSAM SÜZGECİ", uyari.DetailsJson!, StringComparison.Ordinal);
    }

    /// <summary>
    /// UYARI GÜNDE BİR KEZ. Tek satır kaçar, tekrarlayan satır fark
    /// edilir — ama günde birden fazla yazılsa kayıt gürültüye boğulur
    /// ve asıl bilgi yine kaybolur.
    /// </summary>
    [Fact]
    public async Task Uyari_AyniGunTekrarYazilmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bekci = scope.ServiceProvider.GetRequiredService<ScopeDeferralWatchdog>();

        await db.SecurityAuditEvents
            .Where(x => x.Action == ScopeDeferralWatchdog.ActionScopedUser)
            .ExecuteDeleteAsync();

        var proje = await TestDataFactory.CreateProjectAsync(db, $"TKR{suffix}");

        var kullanici = new AppUser
        {
            Username = $"tekrar-{suffix}",
            FullName = "Tekrar Testi",
            PasswordHash = "x",
            PasswordSalt = "x",
            IsActive = true
        };

        db.Users.Add(kullanici);
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = kullanici.Id,
            ScopeType = DataScopeType.Company,
            CompanyId = proje.CompanyId
        });

        await db.SaveChangesAsync();

        // ÜÇ KEZ KOŞ.
        for (var i = 0; i < 3; i++)
            await bekci.CheckAsync(CancellationToken.None);

        Assert.Equal(1, await UyariSayisiAsync(
            db, ScopeDeferralWatchdog.ActionScopedUser));
    }

    /// <summary>
    /// İKİNCİ ŞİRKET → UYARI. Tek şirketle şirket izolasyonu
    /// ölçülemez; ikincisi eklendiği an bütün kapsam açıkları
    /// ölçülebilir ve sömürülebilir hale gelir.
    /// </summary>
    [Fact]
    public async Task IkinciSirket_UyariUretir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bekci = scope.ServiceProvider.GetRequiredService<ScopeDeferralWatchdog>();

        await db.SecurityAuditEvents
            .Where(x => x.Action == ScopeDeferralWatchdog.ActionSecondCompany)
            .ExecuteDeleteAsync();

        /*
         * ÖN KOŞULUNU KENDİ KURAR — BAŞKA TESTLERİN YAN ETKİSİNE DAYANMAZ.
         *
         * Eskiden tek şirket açıp "test veritabanında zaten birden çok
         * şirket var, her test kendi şirketini açıyor" varsayıyordu.
         * Tam takımda doğruydu; TEK BAŞINA koşturulduğunda kırmızı
         * veriyordu (ölçüldü, KAPI/1 sırasında). Yeşilliği başka
         * testlerin sırasına bağlı bir test, ölçtüğünü sandığın şeyi
         * ölçmez — bugün süzgeçle koşan biri onu "bozuk" sanır ve
         * gerçek bir kırmızıyla ayırt edemez.
         */
        await TestDataFactory.CreateProjectAsync(db, $"SRKA{suffix}");
        await TestDataFactory.CreateProjectAsync(db, $"SRKB{suffix}");

        var aktifSirket = await db.Companies.CountAsync(x => x.IsActive);
        Assert.True(
            aktifSirket > 1,
            $"Test kurulumu birden çok şirket üretmeliydi; sayılan: {aktifSirket}.");

        await bekci.CheckAsync(CancellationToken.None);

        Assert.Equal(1, await UyariSayisiAsync(
            db, ScopeDeferralWatchdog.ActionSecondCompany));
    }
}
