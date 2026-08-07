using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Elden ödemenin proje ve ŞANTİYE bazında dağıtımı (EP4).
///
/// Asıl güvence ORANLI DAĞITIM: bir personel ay içinde iki şantiyede
/// çalıştıysa elden ödemesi gün sayısına bölünmeli. Her birime aylık
/// tutarın tamamı yazılsaydı toplam maliyet gerçekte ödenenin katı
/// çıkardı ve şantiye kârlılığı sistematik olarak kötü görünürdü.
///
/// İkinci güvence İZOLASYON: elden payı HrProjectLaborCosts defterine
/// YAZILMAZ, okuma anında yetkiyle eklenir. Yetkisiz kullanıcı yalnızca
/// resmî rakamı görür.
/// </summary>
[Collection("Integration")]
public sealed class ExtraPaymentAllocationTests(DatabaseFixture fixture)
{
    private const decimal ExtraMonthly = 30_000m;

    private sealed record Context(
        Guid CompanyId, Guid ProjectId, Guid PersonnelId,
        Guid SiteAId, Guid SiteBId);

    /// <summary>
    /// İki şantiyesi olan bir proje ve elden ödemesi olan bir personel
    /// kurar; puantaj günleri testte ayrıca yazılır.
    /// </summary>
    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var siteA = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-A-{suffix}",
            Name = "A Şantiyesi"
        };
        var siteB = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-B-{suffix}",
            Name = "B Şantiyesi"
        };

        db.ProjectSites.AddRange(siteA, siteB);

        var personnel = new Personnel
        {
            CompanyId = project.CompanyId,
            EmployeeNumber = $"ALLOC-{suffix}",
            FirstName = "Dağıtım",
            LastName = "Testi",
            EmploymentStartDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = PersonnelStatus.Active
        };
        db.Personnel.Add(personnel);

        db.PersonnelExtraPayments.Add(new PersonnelExtraPayment
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            MonthlyAmount = ExtraMonthly,
            EffectiveStartDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();

        return new Context(
            project.CompanyId, project.Id, personnel.Id, siteA.Id, siteB.Id);
    }

    /// <summary>
    /// Onaylı puantaj günleri yazar.
    /// </summary>
    private async Task AddAttendanceAsync(
        Context context, Guid? siteId, int year, int month, int fromDay, int dayCount,
        bool approved = true)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        for (var i = 0; i < dayCount; i++)
        {
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                CompanyId = context.CompanyId,
                ProjectId = context.ProjectId,
                ProjectSiteId = siteId,
                PersonnelId = context.PersonnelId,
                WorkDate = new DateTime(
                    year, month, fromDay + i, 0, 0, 0, DateTimeKind.Utc),
                Status = (int)AttendanceStatus.Worked,
                NormalHours = 7.5m,
                TotalHours = 7.5m,
                IsApproved = approved
            });
        }

        await db.SaveChangesAsync();
    }

    private ExtraPaymentAllocationService Allocation(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ExtraPaymentAllocationService>();

    /// <summary>
    /// BU PAKETİN ASIL GÜVENCESİ: iki şantiyede çalışılan ay, elden
    /// ödeme gün sayısına ORANLA bölünmeli ve toplamı aylık tutarı
    /// AŞMAMALI.
    /// </summary>
    [Fact]
    public async Task SplitsBetweenSitesByWorkedDays()
    {
        var context = await CreateContextAsync();

        // Mart: A'da 6 gün, B'de 4 gün → toplam 10 gün
        await AddAttendanceAsync(context, context.SiteAId, 2026, 3, 1, 6);
        await AddAttendanceAsync(context, context.SiteBId, 2026, 3, 10, 4);

        using var scope = fixture.Factory.Services.CreateScope();

        var shares = await Allocation(scope).GetSiteSharesAsync(
            context.CompanyId, context.ProjectId, default);

        // 30.000 × 6/10 = 18.000 ; 30.000 × 4/10 = 12.000
        Assert.Equal(18_000m, shares.BySite[context.SiteAId]);
        Assert.Equal(12_000m, shares.BySite[context.SiteBId]);

        // Toplam aylık tutarı aşmamalı
        Assert.Equal(ExtraMonthly, shares.Total);
    }

    /// <summary>
    /// Proje toplamı, şantiye paylarının toplamına eşit olmalı; iki
    /// ekran farklı rakam göstermemeli.
    /// </summary>
    [Fact]
    public async Task ProjectShareEqualsSumOfSiteShares()
    {
        var context = await CreateContextAsync();

        await AddAttendanceAsync(context, context.SiteAId, 2026, 4, 1, 5);
        await AddAttendanceAsync(context, context.SiteBId, 2026, 4, 10, 5);

        using var scope = fixture.Factory.Services.CreateScope();
        var allocation = Allocation(scope);

        var projectShare = await allocation.GetProjectShareAsync(
            context.CompanyId, context.ProjectId, default);

        var siteShares = await allocation.GetSiteSharesAsync(
            context.CompanyId, context.ProjectId, default);

        Assert.Equal(ExtraMonthly, projectShare);
        Assert.Equal(projectShare, siteShares.Total);
    }

    /// <summary>
    /// Şantiyesi girilmemiş puantaj günü uydurma bir şantiyeye
    /// dağıtılmamalı; ayrı (null) anahtarda toplanmalı.
    /// </summary>
    [Fact]
    public async Task DaysWithoutSite_AreKeptSeparate()
    {
        var context = await CreateContextAsync();

        await AddAttendanceAsync(context, context.SiteAId, 2026, 5, 1, 5);
        await AddAttendanceAsync(context, null, 2026, 5, 10, 5);

        using var scope = fixture.Factory.Services.CreateScope();

        var shares = await Allocation(scope).GetSiteSharesAsync(
            context.CompanyId, context.ProjectId, default);

        Assert.Equal(15_000m, shares.BySite[context.SiteAId]);
        // Şantiyesiz gün ayrı duruyor, hiçbir şantiyeye dağıtılmıyor
        Assert.Equal(15_000m, shares.Unassigned);
    }

    /// <summary>
    /// Onaylanmamış puantaj dağıtıma girmemeli: henüz kesinleşmemiş bir
    /// gün maliyet üretmez.
    /// </summary>
    [Fact]
    public async Task UnapprovedAttendance_IsExcluded()
    {
        var context = await CreateContextAsync();

        await AddAttendanceAsync(context, context.SiteAId, 2026, 6, 1, 5);
        await AddAttendanceAsync(
            context, context.SiteBId, 2026, 6, 10, 5, approved: false);

        using var scope = fixture.Factory.Services.CreateScope();

        var shares = await Allocation(scope).GetSiteSharesAsync(
            context.CompanyId, context.ProjectId, default);

        // Yalnızca onaylı 5 gün var → tamamı A'ya
        Assert.Equal(ExtraMonthly, shares.BySite[context.SiteAId]);
        Assert.False(shares.BySite.ContainsKey(context.SiteBId));
    }

    /// <summary>
    /// Elden ödemenin yürürlükte OLMADIĞI ay için pay üretilmemeli.
    /// </summary>
    [Fact]
    public async Task MonthBeforeExtraPaymentStarted_ProducesNoShare()
    {
        var context = await CreateContextAsync();

        // Elden ödeme 2024-01-01'de başlıyor; 2023 ayına pay çıkmamalı
        await AddAttendanceAsync(context, context.SiteAId, 2023, 6, 1, 5);

        using var scope = fixture.Factory.Services.CreateScope();

        var shares = await Allocation(scope).GetSiteSharesAsync(
            context.CompanyId, context.ProjectId, default);

        Assert.Empty(shares.BySite);
        Assert.Equal(0m, shares.Unassigned);
    }

    // ---------- Uç seviyesinde: izolasyon ----------

    private async Task<HttpClient> CreateClientForRoleAsync(
        string roleName, string? deniedPermissionKey = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "ExtraPayment!2026";
        var username = $"test-alloc-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = user.Id,
            ScopeType = DataScopeType.All
        });

        if (deniedPermissionKey is not null)
        {
            var permission = await db.Permissions
                .SingleAsync(x => x.Key == deniedPermissionKey);

            db.UserPermissionOverrides.Add(new UserPermissionOverride
            {
                UserId = user.Id,
                PermissionId = permission.Id,
                Effect = PermissionOverrideEffect.Deny
            });
        }

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <summary>
    /// Yetkili kullanıcı şantiye kırılımında resmî, elden ve gerçek
    /// tutarı ayrı ayrı görmeli.
    /// </summary>
    [Fact]
    public async Task Breakdown_ShowsExtraPaymentToAuthorizedRole()
    {
        var context = await CreateContextAsync();
        await AddAttendanceAsync(context, context.SiteAId, 2026, 7, 1, 10);

        var client = await CreateClientForRoleAsync("Genel Müdür");

        var breakdown = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/labor-cost-breakdown");

        Assert.False(breakdown.GetProperty("extraPaymentHidden").GetBoolean());
        Assert.Equal(
            ExtraMonthly,
            breakdown.GetProperty("projectExtraPaymentTotal").GetDecimal());

        var site = breakdown.GetProperty("sites").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == context.SiteAId);

        Assert.Equal(ExtraMonthly, site.GetProperty("extraPaymentAmount").GetDecimal());
        // Resmî işçilik defteri boş; gerçek = resmî + elden
        Assert.Equal(0m, site.GetProperty("officialAmount").GetDecimal());
        Assert.Equal(ExtraMonthly, site.GetProperty("actualAmount").GetDecimal());
    }

    /// <summary>
    /// KRİTİK: elden izni olmayan kullanıcı yalnızca RESMÎ rakamı
    /// görmeli; elden alanları null gelmeli. Şantiye maliyeti
    /// personnel.view ile okunuyor, elden tutar oradan sızmamalı.
    /// </summary>
    [Fact]
    public async Task Breakdown_HidesExtraPaymentWhenPermissionDenied()
    {
        var context = await CreateContextAsync();
        await AddAttendanceAsync(context, context.SiteAId, 2026, 8, 1, 10);

        var client = await CreateClientForRoleAsync(
            "Genel Müdür", PermissionCatalog.Keys.ExtraPaymentView);

        var breakdown = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/labor-cost-breakdown");

        Assert.True(breakdown.GetProperty("extraPaymentHidden").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            breakdown.GetProperty("projectExtraPaymentTotal").ValueKind);

        var site = breakdown.GetProperty("sites").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == context.SiteAId);

        Assert.Equal(
            JsonValueKind.Null, site.GetProperty("extraPaymentAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null, site.GetProperty("actualAmount").ValueKind);

        // Resmî rakam ve geriye uyumlu alan her hâlükârda dönmeli
        Assert.Equal(0m, site.GetProperty("officialAmount").GetDecimal());
        Assert.Equal(0m, site.GetProperty("amount").GetDecimal());
    }

    /// <summary>
    /// Elden payı işçilik defterine YAZILMAMALI; okuma anında
    /// ekleniyor. Defter kirletilseydi personnel.view olan herkese
    /// sızardı.
    /// </summary>
    [Fact]
    public async Task Breakdown_DoesNotWriteExtraPaymentToLedger()
    {
        var context = await CreateContextAsync();
        await AddAttendanceAsync(context, context.SiteAId, 2026, 9, 1, 10);

        var client = await CreateClientForRoleAsync("Genel Müdür");

        await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/labor-cost-breakdown");

        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.HrProjectLaborCosts
            .Where(x => x.ProjectId == context.ProjectId)
            .ToListAsync());
    }
}
