using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Services.Hizir.Briefing;
using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Brifing–bildirim köprüsü (B6 devri).
///
/// DEVRİN KURALI: hesap TEK YERDE. Çek vadesi ve İSG geçerliliği
/// artık motorda hesaplanıyor, brifing sonucu OKUYOR. İkisi kendi
/// sorgusunu yazmaya devam etseydi eşikler zamanla ayrışır ve aynı
/// olay iki yerde iki türlü görünürdü.
///
/// REGRESYON-GÜVENLİK: motorun kapsamadığı kaynaklar (kritik stok,
/// teklif geçerliliği, proje maliyet aşımı, fatura/hakediş onayı)
/// YERİNDE duruyor.
/// </summary>
[Collection("Integration")]
public sealed class NotificationBriefingBridgeTests(DatabaseFixture fixture)
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private async Task<Guid> CreateCompanyAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (await TestDataFactory.CreateProjectAsync(db, suffix)).CompanyId;
    }

    private async Task SeedAsync(
        Guid companyId, string type, string title,
        NotificationSeverity severity, string? requiredPermission)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Notifications.Add(new Notification
        {
            CompanyId = companyId,
            Type = type,
            SourceId = Guid.NewGuid(),
            PeriodKey = "2026-08",
            Title = title,
            Detail = "Ayrıntı",
            Severity = severity,
            TargetPath = "/finans/cekler",
            RequiredPermission = requiredPermission,
            Status = NotificationStatus.Open,
            FirstSeenAtUtc = Now,
            LastSeenAtUtc = Now
        });

        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        Guid companyId, params string[] permissions)
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<NotificationStore>();

        var source = new NotificationBriefingSource(db, store);

        var context = new HizirToolContext(
            Guid.NewGuid(),
            "Test Kullanıcı",
            null,
            [],
            permissions,
            new CurrentDataScopeSnapshot(
                false,
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid> { companyId },
                new HashSet<Guid>(),
                new HashSet<Guid>()));

        return await source.BuildAsync(context, CancellationToken.None);
    }

    /// <summary>
    /// ANA TEST: motordaki bildirim brifingde görünüyor. Brifing artık
    /// kendi çek sorgusunu yazmıyor.
    /// </summary>
    [Fact]
    public async Task NotificationsAppearInTheBriefing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        await SeedAsync(companyId, "cheque.due", "Verilen çek ödenecek — yarın",
            NotificationSeverity.Critical, PermissionCatalog.Keys.FinanceView);

        var items = await BuildAsync(companyId, PermissionCatalog.Keys.FinanceView);

        var item = Assert.Single(items);

        Assert.Equal("Verilen çek ödenecek — yarın", item.Title);
        Assert.Equal(BriefingSeverity.Critical, item.Severity);
        Assert.Equal("/finans/cekler", item.TargetPath);
    }

    /// <summary>
    /// YETKİ BİLDİRİM BAZINDA: finans izni olmayan kullanıcının
    /// brifinginde finans bildirimi belirmiyor. Brifingin kendi
    /// kuralı da buydu; köprü onu bozmuyor.
    /// </summary>
    [Fact]
    public async Task BriefingHidesNotificationsTheUserCannotSee()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        await SeedAsync(companyId, "cheque.due", "Verilen çek ödenecek",
            NotificationSeverity.Critical, PermissionCatalog.Keys.FinanceView);

        var items = await BuildAsync(companyId, PermissionCatalog.Keys.SiteReportsView);

        Assert.Empty(items);
    }

    /// <summary>
    /// BRİFİNG ÖZETTİR: bilgi düzeyindeki kalemler brifinge
    /// girmiyor ve en fazla beş satır dökülüyor. Otuz satır dökmek
    /// brifingi okunmaz hale getirirdi; ayrıntı çanda duruyor.
    /// </summary>
    [Fact]
    public async Task BriefingKeepsOnlyTheMostUrgentItems()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        for (var index = 0; index < 8; index++)
        {
            await SeedAsync(companyId, "cheque.due", $"Kritik {index}",
                NotificationSeverity.Critical, null);
        }

        await SeedAsync(companyId, "cheque.due", "Bilgi kalemi",
            NotificationSeverity.Info, null);

        var items = await BuildAsync(companyId);

        Assert.Equal(5, items.Count);
        Assert.DoesNotContain(items, x => x.Title == "Bilgi kalemi");
    }

    /// <summary>
    /// KAPATILAN BİLDİRİM BRİFİNGE DE GİRMİYOR: kullanıcı çanda
    /// kapattığı bir işi ertesi sabah brifingde yeniden görmemeli.
    /// </summary>
    [Fact]
    public async Task DismissedNotificationDoesNotReachTheBriefing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        await SeedAsync(companyId, "cheque.due", "Kapatılacak",
            NotificationSeverity.Warning, null);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.Notifications.SingleAsync(x => x.CompanyId == companyId);
            row.Status = NotificationStatus.Dismissed;

            await db.SaveChangesAsync();
        }

        var items = await BuildAsync(companyId);

        Assert.Empty(items);
    }

    /// <summary>
    /// REGRESYON-GÜVENLİK: motorun kapsamadığı brifing kaynakları
    /// hâlâ kayıtlı. Devir sırasında hepsi birden kaldırılsaydı
    /// brifing, motorun henüz kapsamadığı her şeyi bir gecede
    /// kaybederdi.
    /// </summary>
    [Fact]
    public void UncoveredBriefingSourcesRemainRegistered()
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var sources = scope.ServiceProvider
            .GetRequiredService<IEnumerable<IHizirBriefingSource>>()
            .Select(x => x.Key)
            .ToList();

        Assert.Contains("bildirimler", sources);
        Assert.Contains("bekleyen_onaylar", sources);
        Assert.Contains("kritik_stok", sources);

        // DEVREDİLENLER ARTIK YOK: aynı çek iki yerden gelip iki kez
        // görünmemeli.
        Assert.DoesNotContain("cek_vadeleri", sources);
        Assert.DoesNotContain("isg_gecerlilik", sources);
    }
}
