using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Tarama servisi: kaynak yalıtımı ve uçlar.
///
/// ANA KURAL: bir kaynağın hatası turu düşürmez ve — daha önemlisi —
/// o kaynağın açık bildirimlerini SESSİZCE KAPATMAZ. Hata durumunda
/// boş liste geçilseydi, çözülmemiş işler "kaynak kalktı" sayılıp
/// kaybolurdu.
/// </summary>
[Collection("Integration")]
public sealed class NotificationScannerTests(DatabaseFixture fixture)
{
    private static readonly DateTime Now =
        new(2026, 8, 11, 3, 0, 0, DateTimeKind.Utc);

    /// <summary>Sabit bir aday üreten kaynak.</summary>
    private sealed class StubSource(
        string key, string type, Func<Guid, IReadOnlyList<NotificationCandidate>> build)
        : INotificationSource
    {
        public string Key => key;

        public IReadOnlyCollection<string> OwnedTypes => [type];

        public Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
            NotificationScanContext context, CancellationToken cancellationToken) =>
            Task.FromResult(build(context.CompanyId));
    }

    /// <summary>Her zaman patlayan kaynak.</summary>
    private sealed class FailingSource(string key, string type) : INotificationSource
    {
        public string Key => key;

        public IReadOnlyCollection<string> OwnedTypes => [type];

        public Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
            NotificationScanContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Kaynak sorgusu bozuk.");
    }

    private async Task<Guid> CreateCompanyAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (await TestDataFactory.CreateProjectAsync(db, suffix)).CompanyId;
    }

    private async Task<NotificationScanReport> RunAsync(
        params INotificationSource[] sources)
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Tarayıcı yazma için her turda taze bir depo çözüyor; fabrika
        // doğrudan uygulamanın kapsayıcısından geliyor.
        var scopeFactory = fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>();

        var scanner = new NotificationScanner(
            db, scopeFactory, sources, NullLogger<NotificationScanner>.Instance);

        return await scanner.RunAsync(Now, CancellationToken.None);
    }

    private static NotificationCandidate Candidate(
        string type, Guid sourceId, string title) =>
        new(type, sourceId, "2026-08", title, "Ayrıntı",
            NotificationSeverity.Warning, "/finans", Now.AddDays(3));

    // ---------------- Yalıtım ----------------

    /// <summary>
    /// ANA TEST: bir kaynak patlıyor, DİĞERİ çalışmaya devam ediyor.
    /// Hata bütün turu düşürseydi tek bozuk sorgu yüzünden o gece
    /// hiçbir hatırlatma üretilmezdi.
    /// </summary>
    [Fact]
    public async Task OneFailingSource_DoesNotStopTheOthers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        var candidateId = Guid.NewGuid();

        var healthy = new StubSource(
            $"saglikli-{suffix}", $"ok.{suffix}",
            id => id == companyId
                ? [Candidate($"ok.{suffix}", candidateId, "Çalışan kaynak")]
                : []);

        var report = await RunAsync(
            new FailingSource($"bozuk-{suffix}", $"bozuk.{suffix}"), healthy);

        Assert.True(report.HasErrors);

        var failing = report.Sources.Single(x => x.Source == $"bozuk-{suffix}");
        Assert.NotNull(failing.Error);

        var working = report.Sources.Single(x => x.Source == $"saglikli-{suffix}");
        Assert.Null(working.Error);
        Assert.True(working.Created >= 1);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await db.Notifications
            .AnyAsync(x => x.CompanyId == companyId && x.Type == $"ok.{suffix}"));
    }

    /// <summary>
    /// HATA VEREN KAYNAK KAPATMA YAPMAZ: patlayan kaynağın önceden
    /// açılmış bildirimleri AÇIK kalıyor. Kapansaydı, çözülmemiş bir
    /// iş sırf sorgu bozuk diye ekrandan silinirdi.
    /// </summary>
    [Fact]
    public async Task FailingSource_DoesNotCloseItsExistingNotifications()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var type = $"kirilgan.{suffix}";
        var sourceId = Guid.NewGuid();

        // Önce sağlıklı tur: bildirim açılıyor. Kaynak YALNIZ hedef
        // şirkete üretiyor — tarayıcı bütün şirketleri geziyor.
        await RunAsync(new StubSource(
            $"kaynak-{suffix}", type,
            id => id == companyId ? [Candidate(type, sourceId, "Açık iş")] : []));

        // Sonraki tur patlıyor.
        var report = await RunAsync(new FailingSource($"kaynak-{suffix}", type));

        Assert.True(report.HasErrors);
        Assert.Equal(0, report.Closed);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.Notifications
            .SingleAsync(x => x.CompanyId == companyId && x.Type == type);

        Assert.Equal(NotificationStatus.Open, row.Status);
    }

    /// <summary>
    /// Kaynak HİÇ ADAY ÜRETMESE bile kendi türlerini kapatabiliyor —
    /// türler adaylardan değil OwnedTypes'tan geliyor. Adaylardan
    /// çıkarılsaydı çözülen son iş kapanmaz, bildirim ilelebet açık
    /// kalırdı.
    /// </summary>
    [Fact]
    public async Task SourceWithNoCandidates_StillClosesItsOwnTypes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var type = $"kapanan.{suffix}";
        var sourceId = Guid.NewGuid();

        await RunAsync(new StubSource(
            $"kaynak-{suffix}", type,
            id => id == companyId ? [Candidate(type, sourceId, "Son iş")] : []));

        var report = await RunAsync(new StubSource(
            $"kaynak-{suffix}", type, _ => []));

        Assert.Equal(1, report.Closed);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.Notifications
            .SingleAsync(x => x.CompanyId == companyId && x.Type == type);

        Assert.Equal(NotificationStatus.Closed, row.Status);
    }

    // ---------------- Uçlar ----------------

    private async Task<HttpClient> ClientWithAsync(string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestBildirim!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestBildirim-{suffix}" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var permissions = await db.Permissions
                .Where(x => permissionKeys.Contains(x.Key))
                .ToListAsync();

            foreach (var permission in permissions)
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id
                });

            username = $"bildirim-{suffix}";
            var hash = passwords.Hash(password);

            db.Users.Add(new AppUser
            {
                Username = username,
                FullName = "Bildirim Test",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            });

            await db.SaveChangesAsync();

            var user = await db.Users.SingleAsync(x => x.Username == username);

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = user.Id,
                ScopeType = DataScopeType.All
            });

            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private async Task<Guid> SeedNotificationAsync(
        Guid companyId, string? requiredPermission,
        string? amountDetail = null, string? amountPermission = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = new Notification
        {
            CompanyId = companyId,
            Type = $"test.{Guid.NewGuid():N}"[..20],
            SourceId = Guid.NewGuid(),
            PeriodKey = "2026-08",
            Title = "Vadesi yaklaşan çek",
            Detail = "Vadesi yaklaşan bir çek var.",
            AmountDetail = amountDetail,
            AmountPermission = amountPermission,
            Severity = NotificationSeverity.Warning,
            TargetPath = "/finans/cekler",
            RequiredPermission = requiredPermission,
            DueDate = Now.AddDays(3),
            Status = NotificationStatus.Open,
            FirstSeenAtUtc = Now,
            LastSeenAtUtc = Now
        };

        db.Notifications.Add(row);
        await db.SaveChangesAsync();

        return row.Id;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Okundu işaretlenince okunmamış sayısı düşüyor — çanın işi bu.
    /// </summary>
    [Fact]
    public async Task MarkingAsRead_DropsTheUnreadCount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        var id = await SeedNotificationAsync(
            companyId, PermissionCatalog.Keys.FinanceView);

        var client = await ClientWithAsync([PermissionCatalog.Keys.FinanceView]);

        var before = await ReadAsync(await client.GetAsync(
            $"/api/bildirimler?companyId={companyId}"));

        Assert.Equal(1, before.GetProperty("unreadCount").GetInt32());

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/bildirimler/{id}/okundu", null)).StatusCode);

        var after = await ReadAsync(await client.GetAsync(
            $"/api/bildirimler?companyId={companyId}"));

        Assert.Equal(0, after.GetProperty("unreadCount").GetInt32());
        Assert.Equal(1, after.GetProperty("items").GetArrayLength());
    }

    /// <summary>
    /// Kapatılan bildirim listeden düşüyor; geçmişe bakmak isteyen
    /// includeHandled ile görebiliyor.
    /// </summary>
    [Fact]
    public async Task DismissedNotificationLeavesTheDefaultList()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        var id = await SeedNotificationAsync(companyId, null);

        var client = await ClientWithAsync([PermissionCatalog.Keys.DashboardView]);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/bildirimler/{id}/kapat", null)).StatusCode);

        var list = await ReadAsync(await client.GetAsync(
            $"/api/bildirimler?companyId={companyId}"));

        Assert.Equal(0, list.GetProperty("items").GetArrayLength());

        var all = await ReadAsync(await client.GetAsync(
            $"/api/bildirimler?companyId={companyId}&includeHandled=true"));

        Assert.Equal(1, all.GetProperty("items").GetArrayLength());
    }

    /// <summary>
    /// GEÇMİŞ TARİHE ERTELENEMEZ: erteleme anında dolmuş sayılır ve
    /// kullanıcı hiçbir şey olmamış gibi görürdü.
    /// </summary>
    [Fact]
    public async Task SnoozeRejectsAPastDate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        var id = await SeedNotificationAsync(companyId, null);

        var client = await ClientWithAsync([PermissionCatalog.Keys.DashboardView]);

        var response = await client.PostAsJsonAsync(
            $"/api/bildirimler/{id}/ertele",
            new { until = DateTime.UtcNow.AddDays(-1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// GÖREMEDİĞİNİ DEĞİŞTİREMEZ: yetkisi olmayan kullanıcı listede
    /// görmediği bildirimi kapatamıyor.
    /// </summary>
    [Fact]
    public async Task UserCannotDismissANotificationTheyCannotSee()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        var id = await SeedNotificationAsync(
            companyId, PermissionCatalog.Keys.FinanceView);

        var outsider = await ClientWithAsync([PermissionCatalog.Keys.SiteReportsView]);

        var list = await ReadAsync(await outsider.GetAsync(
            $"/api/bildirimler?companyId={companyId}"));

        Assert.Equal(0, list.GetProperty("items").GetArrayLength());

        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.PostAsync($"/api/bildirimler/{id}/kapat", null)).StatusCode);
    }

    /// <summary>
    /// TUTAR MASKESİ: izni olan tutarlı metni, olmayan tutarsız
    /// metni görüyor. Aynı bildirim, iki farklı gövde.
    /// </summary>
    [Fact]
    public async Task AmountTextIsShownOnlyToUsersWithTheAmountPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        await SeedNotificationAsync(
            companyId,
            requiredPermission: null,
            amountDetail: "Tutar: 250.000,00 TL",
            amountPermission: PermissionCatalog.Keys.FinanceView);

        var finance = await ClientWithAsync([PermissionCatalog.Keys.FinanceView]);

        var withAmount = await ReadAsync(await finance.GetAsync(
            $"/api/bildirimler?companyId={companyId}"));

        Assert.Contains("250.000",
            withAmount.GetProperty("items")[0].GetProperty("detail").GetString()!);

        var site = await ClientWithAsync([PermissionCatalog.Keys.SiteReportsView]);

        var masked = await ReadAsync(await site.GetAsync(
            $"/api/bildirimler?companyId={companyId}"));

        var detail = masked.GetProperty("items")[0].GetProperty("detail").GetString()!;

        Assert.DoesNotContain("250.000", detail);
        Assert.Equal("Vadesi yaklaşan bir çek var.", detail);
    }

    /// <summary>
    /// Elle tarama ucu sistem yönetimi izninde: tarama bütün
    /// şirketleri geziyor.
    /// </summary>
    [Fact]
    public async Task ManualScanEndpointRequiresSystemPermission()
    {
        var outsider = await ClientWithAsync([PermissionCatalog.Keys.FinanceView]);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.PostAsync("/api/bildirimler/tara", null)).StatusCode);

        var admin = await ClientWithAsync([PermissionCatalog.Keys.SystemUsersManage]);

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsync("/api/bildirimler/tara", null)).StatusCode);
    }
}
