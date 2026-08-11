using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Bildirim motorunun çekirdeği: TEKİLLEŞTİRME ve DURUM.
///
/// Paketin ana kuralı burada: günlük tarama aynı satırı GÜNCELLER,
/// yenisini açmaz. Her tur yeni satır açsaydı bir haftalık vade
/// uyarısı yedi kayıt üretir, "okundu" her gece kaybolur ve bildirim
/// merkezi çöp kutusuna dönerdi.
/// </summary>
[Collection("Integration")]
public sealed class NotificationStoreTests(DatabaseFixture fixture)
{
    private static readonly DateTime Day1 =
        new(2026, 8, 11, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Day2 = Day1.AddDays(1);
    private static readonly DateTime Day3 = Day1.AddDays(2);

    private static readonly string[] ChequeType = ["cheque.due"];

    private async Task<Guid> CreateCompanyAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        return project.CompanyId;
    }

    private static NotificationCandidate Cheque(
        Guid sourceId, string title, decimal amount, DateTime due) =>
        new(
            "cheque.due",
            sourceId,
            due.ToString("yyyy-MM-dd"),
            title,
            "Vadesi yaklaşan çek var.",
            NotificationSeverity.Warning,
            "/finans/cekler",
            due,
            $"Tutar: {amount:N2} TL",
            PermissionCatalog.Keys.FinanceView,
            PermissionCatalog.Keys.FinanceView);

    private async Task<NotificationScanResult> ApplyAsync(
        Guid companyId,
        IReadOnlyCollection<NotificationCandidate> candidates,
        DateTime scanTime)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<NotificationStore>();

        return await store.ApplyAsync(
            companyId, ChequeType, candidates, scanTime, CancellationToken.None);
    }

    private async Task<List<Notification>> RowsAsync(Guid companyId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Notifications
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.FirstSeenAtUtc)
            .ToListAsync();
    }

    // ---------------- Tekilleştirme ----------------

    /// <summary>
    /// ANA TEST: üç gün üst üste aynı vade taranıyor, TEK satır
    /// kalıyor. Her turda yeni satır açılsaydı çan üç kez aynı şeyi
    /// söylerdi.
    /// </summary>
    [Fact]
    public async Task RepeatedScans_UpdateTheSameRowInsteadOfCreatingNewOnes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var chequeId = Guid.NewGuid();
        var due = Day1.AddDays(5);

        var first = await ApplyAsync(
            companyId, [Cheque(chequeId, "Çek vadesi 5 gün sonra", 100_000m, due)], Day1);

        Assert.Equal(1, first.Created);
        Assert.Equal(0, first.Updated);

        var second = await ApplyAsync(
            companyId, [Cheque(chequeId, "Çek vadesi 4 gün sonra", 100_000m, due)], Day2);

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Updated);

        var third = await ApplyAsync(
            companyId, [Cheque(chequeId, "Çek vadesi 3 gün sonra", 100_000m, due)], Day3);

        Assert.Equal(0, third.Created);

        var rows = await RowsAsync(companyId);

        Assert.Single(rows);

        // Metin TAZELENDİ: kullanıcı en güncel hâlini görüyor.
        Assert.Equal("Çek vadesi 3 gün sonra", rows[0].Title);
        Assert.Equal(Day1, rows[0].FirstSeenAtUtc);
        Assert.Equal(Day3, rows[0].LastSeenAtUtc);
    }

    /// <summary>
    /// Aynı kaynağın FARKLI DÖNEMİ ayrı bildirimdir: kredi taksitinde
    /// eylül ve ekim ayrı hatırlatmalardır, biri diğerini ezmemeli.
    /// </summary>
    [Fact]
    public async Task DifferentPeriodsOfTheSameSource_AreSeparateNotifications()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var sourceId = Guid.NewGuid();

        await ApplyAsync(companyId,
        [
            Cheque(sourceId, "Eylül taksiti", 10_000m, Day1.AddDays(20)),
            Cheque(sourceId, "Ekim taksiti", 10_000m, Day1.AddDays(50))
        ], Day1);

        var rows = await RowsAsync(companyId);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(x => x.PeriodKey).Distinct().Count());
    }

    /// <summary>
    /// OKUNDU KAYBOLMAZ: kullanıcı okuduktan sonra tarama devam etse
    /// de durum korunuyor. Korunmasaydı her gece bütün bildirimler
    /// okunmamışa dönerdi.
    /// </summary>
    [Fact]
    public async Task ScanDoesNotResetTheReadState()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var chequeId = Guid.NewGuid();
        var due = Day1.AddDays(5);

        await ApplyAsync(companyId, [Cheque(chequeId, "Çek", 5_000m, due)], Day1);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Notifications.SingleAsync(x => x.CompanyId == companyId);

            row.Status = NotificationStatus.Read;
            row.ReadAtUtc = Day1;

            await db.SaveChangesAsync();
        }

        await ApplyAsync(companyId, [Cheque(chequeId, "Çek", 5_000m, due)], Day2);

        var rows = await RowsAsync(companyId);

        Assert.Equal(NotificationStatus.Read, rows[0].Status);
        Assert.Equal(Day1, rows[0].ReadAtUtc);
    }

    // ---------------- Kaynak kalkınca kapanma ----------------

    /// <summary>
    /// KAYNAK KALKINCA KAPANIR: çek ödendi, tarama artık onu
    /// üretmiyor → bildirim kendiliğinden kapanıyor. Kapanmasaydı
    /// bildirim merkezi çözülmüş işlerle dolar ve güvenilirliğini
    /// yitirirdi.
    /// </summary>
    [Fact]
    public async Task NotificationClosesItselfWhenTheSourceDisappears()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var chequeId = Guid.NewGuid();

        await ApplyAsync(
            companyId, [Cheque(chequeId, "Çek", 5_000m, Day1.AddDays(3))], Day1);

        // Çek ödendi: tarama artık aday üretmiyor.
        var result = await ApplyAsync(companyId, [], Day2);

        Assert.Equal(1, result.Closed);

        var rows = await RowsAsync(companyId);

        Assert.Equal(NotificationStatus.Closed, rows[0].Status);
        Assert.Equal(Day2, rows[0].ClosedAtUtc);
    }

    /// <summary>
    /// Kaynak GERİ GELİRSE bildirim yeniden açılır: iptal edilip
    /// tekrar açılan bir çekin uyarısı da geri gelmeli.
    /// </summary>
    [Fact]
    public async Task ClosedNotificationReopensWhenTheSourceComesBack()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var chequeId = Guid.NewGuid();
        var due = Day1.AddDays(3);

        await ApplyAsync(companyId, [Cheque(chequeId, "Çek", 5_000m, due)], Day1);
        await ApplyAsync(companyId, [], Day2);
        await ApplyAsync(companyId, [Cheque(chequeId, "Çek", 5_000m, due)], Day3);

        var rows = await RowsAsync(companyId);

        Assert.Single(rows);
        Assert.Equal(NotificationStatus.Open, rows[0].Status);
        Assert.Null(rows[0].ClosedAtUtc);
    }

    /// <summary>
    /// KAPATMA KAYNAK BAZINDA: bir kaynak boş dönünce BAŞKA türlerin
    /// bildirimleri kapanmıyor. Hepsi tek seferde kapatılsaydı, hata
    /// veren tek bir kaynak bütün bildirim merkezini süpürürdü.
    /// </summary>
    [Fact]
    public async Task OneSourceReturningNothing_DoesNotCloseOtherTypes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Notifications.Add(new Notification
            {
                CompanyId = companyId,
                Type = "isg.expiring",
                SourceId = Guid.NewGuid(),
                PeriodKey = "-",
                Title = "Sağlık raporu bitiyor",
                Severity = NotificationSeverity.Warning,
                Status = NotificationStatus.Open,
                FirstSeenAtUtc = Day1,
                LastSeenAtUtc = Day1
            });

            await db.SaveChangesAsync();
        }

        // Çek kaynağı boş dönüyor; yalnız kendi türünü kapatabilir.
        var result = await ApplyAsync(companyId, [], Day2);

        Assert.Equal(0, result.Closed);

        var rows = await RowsAsync(companyId);

        Assert.Single(rows);
        Assert.Equal(NotificationStatus.Open, rows[0].Status);
    }

    // ---------------- Erteleme ----------------

    /// <summary>
    /// Ertelenen bildirim süre dolana kadar listede görünmez, dolunca
    /// yeniden açılır.
    /// </summary>
    [Fact]
    public async Task SnoozedNotificationHidesUntilItsTimeAndThenReopens()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var chequeId = Guid.NewGuid();
        var due = Day1.AddDays(10);

        await ApplyAsync(companyId, [Cheque(chequeId, "Çek", 5_000m, due)], Day1);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Notifications.SingleAsync(x => x.CompanyId == companyId);

            row.Status = NotificationStatus.Snoozed;
            row.SnoozedUntil = Day3;

            await db.SaveChangesAsync();
        }

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<NotificationStore>();

            // Erteleme sürerken görünmüyor.
            var hidden = await store.ListVisibleAsync(
                companyId, [PermissionCatalog.Keys.FinanceView], false, Day2,
                CancellationToken.None);

            Assert.Empty(hidden);

            // Süre dolunca görünüyor.
            var shown = await store.ListVisibleAsync(
                companyId, [PermissionCatalog.Keys.FinanceView], false, Day3,
                CancellationToken.None);

            Assert.Single(shown);
        }

        // Sonraki tarama durumu Açık'a çeviriyor.
        await ApplyAsync(companyId, [Cheque(chequeId, "Çek", 5_000m, due)], Day3);

        var rows = await RowsAsync(companyId);

        Assert.Equal(NotificationStatus.Open, rows[0].Status);
        Assert.Null(rows[0].SnoozedUntil);
    }

    // ---------------- Yetki ----------------

    /// <summary>
    /// BİLDİRİM İLGİLİ ROLE GİDER: finans bildirimi finans izni
    /// olmayan kullanıcıya HİÇ gelmez.
    /// </summary>
    [Fact]
    public async Task NotificationsAreFilteredByTheRequiredPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        await ApplyAsync(
            companyId,
            [Cheque(Guid.NewGuid(), "Çek", 5_000m, Day1.AddDays(3))],
            Day1);

        using var scope = fixture.Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<NotificationStore>();

        var finance = await store.ListVisibleAsync(
            companyId, [PermissionCatalog.Keys.FinanceView], false, Day1,
            CancellationToken.None);

        Assert.Single(finance);

        var site = await store.ListVisibleAsync(
            companyId, [PermissionCatalog.Keys.SiteReportsView], false, Day1,
            CancellationToken.None);

        Assert.Empty(site);
    }

    /// <summary>
    /// TUTAR AYRI ALANDA: tutarsız metin herkese açık, tutarlı metin
    /// ayrı izinde. Tek metinden tutarı çalışma anında ayıklamak
    /// kırılgan olurdu — bir gün biçim değişir, maske sessizce
    /// delinirdi.
    /// </summary>
    [Fact]
    public async Task AmountTextIsStoredSeparatelyFromTheSafeText()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);

        await ApplyAsync(
            companyId,
            [Cheque(Guid.NewGuid(), "Çek", 250_000m, Day1.AddDays(3))],
            Day1);

        var rows = await RowsAsync(companyId);

        Assert.DoesNotContain("250", rows[0].Detail ?? "");
        Assert.Contains("250", rows[0].AmountDetail ?? "");
        Assert.Equal(PermissionCatalog.Keys.FinanceView, rows[0].AmountPermission);
    }
}
