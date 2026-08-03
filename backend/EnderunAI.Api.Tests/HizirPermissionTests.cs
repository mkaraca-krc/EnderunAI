using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Hızır yetki sızma testleri.
///
/// Paketin en kritik güvencesi: Hızır'ın veri erişimi, soruyu soran
/// kullanıcının kendi izinleriyle sınırlıdır. Model "şunu gösterme"
/// talimatıyla değil, veriyi hiç alamayarak engellenir. Bu testler
/// araç katmanını doğrudan sınar — LLM'e hiç ihtiyaç duymadan, çünkü
/// koruma modelde değil araçta.
/// </summary>
[Collection("Integration")]
public sealed class HizirPermissionTests(DatabaseFixture fixture)
{
    /// <summary>Ücret verisine erişemeyecek roller.</summary>
    public static TheoryData<string> SalaryRestrictedRoles =>
        new() { "Şantiye Şefi", "Formen", "Sekreterya", "Teknik Ofis", "Teknik Koordinatör" };

    /// <summary>Finans verisine erişemeyecek roller.</summary>
    public static TheoryData<string> FinanceRestrictedRoles =>
        new() { "Şantiye Şefi", "Formen", "Sekreterya", "Depo Sorumlusu" };

    private static readonly CurrentDataScopeSnapshot GlobalScope = new(
        HasGlobalAccess: true,
        CompanyIds: new HashSet<Guid>(),
        BranchIds: new HashSet<Guid>(),
        ProjectIds: new HashSet<Guid>(),
        VisibleCompanyIds: new HashSet<Guid>(),
        VisibleBranchIds: new HashSet<Guid>(),
        SiteIds: new HashSet<Guid>());

    /// <summary>
    /// Rolün gerçek (seed edilmiş) izin setiyle bir araç bağlamı kurar.
    /// İzinler koddan değil veritabanından okunur — testin canlı yetki
    /// yapılandırmasını doğrulaması için.
    /// </summary>
    private async Task<HizirToolContext> ContextForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var permissions = await db.Roles
            .Where(x => x.Name == roleName)
            .SelectMany(x => db.RolePermissions
                .Where(rp => rp.RoleId == x.Id)
                .Select(rp => rp.Permission.Key))
            .ToListAsync();

        Assert.NotEmpty(permissions);

        return new HizirToolContext(
            Guid.NewGuid(),
            $"Test {roleName}",
            new[] { roleName },
            permissions,
            GlobalScope);
    }

    private IHizirToolRegistry Registry(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IHizirToolRegistry>();

    [Theory]
    [MemberData(nameof(SalaryRestrictedRoles))]
    public async Task PayrollTool_IsNotOfferedToRestrictedRoles(string roleName)
    {
        var context = await ContextForRoleAsync(roleName);

        using var scope = fixture.Factory.Services.CreateScope();
        var available = Registry(scope).AvailableFor(context);

        // Araç modele hiç tanıtılmaz.
        Assert.DoesNotContain(available, x => x.Name == "bordro_ozeti");
    }

    [Theory]
    [MemberData(nameof(FinanceRestrictedRoles))]
    public async Task FinanceTools_AreNotOfferedToRestrictedRoles(string roleName)
    {
        var context = await ContextForRoleAsync(roleName);

        using var scope = fixture.Factory.Services.CreateScope();
        var available = Registry(scope).AvailableFor(context);

        Assert.DoesNotContain(available, x => x.Name == "cek_defteri");
        Assert.DoesNotContain(available, x => x.Name == "nakit_akis");
        Assert.DoesNotContain(available, x => x.Name == "muhasebe_ozeti");
    }

    [Fact]
    public async Task AuthorizedRoles_DoGetTheirTools()
    {
        var hr = await ContextForRoleAsync("İK Sorumlusu");
        var finance = await ContextForRoleAsync("Finans Sorumlusu");

        using var scope = fixture.Factory.Services.CreateScope();
        var registry = Registry(scope);

        Assert.Contains(registry.AvailableFor(hr), x => x.Name == "bordro_ozeti");
        Assert.Contains(registry.AvailableFor(finance), x => x.Name == "cek_defteri");
        Assert.Contains(registry.AvailableFor(finance), x => x.Name == "nakit_akis");
    }

    /// <summary>
    /// Asıl sızma senaryosu: Şantiye Şefi "herkesin maaşı ne" diye
    /// sorduğunda model bordro aracını yine de çağırmayı denerse, araç
    /// katmanı reddetmeli ve HİÇBİR ücret rakamı dönmemeli.
    /// </summary>
    [Fact]
    public async Task PayrollTool_IsRefusedEvenIfCalledDirectly()
    {
        var context = await ContextForRoleAsync("Şantiye Şefi");

        using var scope = fixture.Factory.Services.CreateScope();
        var tool = Registry(scope).Find("bordro_ozeti");

        Assert.NotNull(tool);
        Assert.Equal(PermissionCatalog.Keys.SalaryView, tool!.RequiredPermission);
        Assert.False(context.Has(tool.RequiredPermission!));
    }

    [Theory]
    [MemberData(nameof(SalaryRestrictedRoles))]
    public async Task KnowledgeBase_DoesNotDescribePagesUserCannotOpen(string roleName)
    {
        var context = await ContextForRoleAsync(roleName);

        using var scope = fixture.Factory.Services.CreateScope();
        var knowledgeBase = scope.ServiceProvider
            .GetRequiredService<IHizirKnowledgeBase>();

        var result = knowledgeBase.Search("bordro maaş ücret", context.Permissions);

        // Ücret/bordro sayfaları salary.view gerektiriyor; bu rollere
        // hiçbir bordro sayfası tarif edilmemeli.
        Assert.DoesNotContain("/insan-kaynaklari/bordro", result);
        Assert.DoesNotContain("/insan-kaynaklari/ucret-kartlari", result);
        Assert.DoesNotContain("/insan-kaynaklari/maliyet-raporu", result);
    }

    [Fact]
    public async Task KnowledgeBase_DescribesPayrollPagesToAuthorizedRole()
    {
        var context = await ContextForRoleAsync("İK Sorumlusu");

        using var scope = fixture.Factory.Services.CreateScope();
        var knowledgeBase = scope.ServiceProvider
            .GetRequiredService<IHizirKnowledgeBase>();

        var result = knowledgeBase.Search("bordro", context.Permissions);

        Assert.Contains("/insan-kaynaklari/bordro", result);
    }

    /// <summary>
    /// Saha rolüne kılavuz modu çalışmalı: günlük rapor girişini tarif
    /// edebilmeli. Yetki kısıtı, yardım alamamak anlamına gelmemeli.
    /// </summary>
    [Fact]
    public async Task KnowledgeBase_GuidesFieldRoleToDailyReport()
    {
        var context = await ContextForRoleAsync("Formen");

        using var scope = fixture.Factory.Services.CreateScope();
        var knowledgeBase = scope.ServiceProvider
            .GetRequiredService<IHizirKnowledgeBase>();

        var result = knowledgeBase.Search("günlük rapor nereden girilir", context.Permissions);

        Assert.Contains("Günlük", result);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public async Task EveryRole_HasGuideAndPendingApprovalTools()
    {
        // İzin gerektirmeyen araçlar herkese açık olmalı.
        var context = await ContextForRoleAsync("Formen");

        using var scope = fixture.Factory.Services.CreateScope();
        var available = Registry(scope).AvailableFor(context);

        Assert.Contains(available, x => x.Name == "kilavuz_ara");
        Assert.Contains(available, x => x.Name == "bekleyen_onaylar");
    }

    /// <summary>
    /// Veri yoksa araç açıkça "KAYIT YOK" demeli — sistem talimatı bu
    /// işareti görünce uydurmayı yasaklıyor.
    /// </summary>
    [Fact]
    public async Task Tools_ReportMissingDataExplicitly()
    {
        var context = await ContextForRoleAsync("İK Sorumlusu");

        using var scope = fixture.Factory.Services.CreateScope();
        var tool = Registry(scope).Find("bordro_ozeti");

        var outcome = await tool!.ExecuteAsync(
            context,
            new Dictionary<string, object?> { ["yil"] = 1999L, ["ay"] = 1L },
            CancellationToken.None);

        Assert.Contains("KAYIT YOK", outcome.Content);
        Assert.False(outcome.Denied);
    }

    /// <summary>
    /// Site kapsamlı kullanıcı yalnızca kendi şantiyesinin günlük
    /// raporlarını görebilmeli.
    /// </summary>
    [Fact]
    public async Task DailyReportTool_RespectsSiteScope()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid visibleSiteId;
        Guid hiddenSiteId;

        using (var setup = fixture.Factory.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);

            var visible = new ProjectSite
            {
                ProjectId = project.Id,
                Code = $"GOR-{suffix}",
                Name = "Görünen Şantiye"
            };
            var hidden = new ProjectSite
            {
                ProjectId = project.Id,
                Code = $"GIZ-{suffix}",
                Name = "Gizli Şantiye"
            };

            db.ProjectSites.AddRange(visible, hidden);
            await db.SaveChangesAsync();

            visibleSiteId = visible.Id;
            hiddenSiteId = hidden.Id;

            db.ProjectSiteDailyReports.AddRange(
                new ProjectSiteDailyReport
                {
                    ProjectSiteId = visibleSiteId,
                    ReportDate = DateTime.UtcNow.Date,
                    WorkerCount = 5,
                    Status = ProjectSiteDailyReportStatus.Approved
                },
                new ProjectSiteDailyReport
                {
                    ProjectSiteId = hiddenSiteId,
                    ReportDate = DateTime.UtcNow.Date,
                    WorkerCount = 9,
                    Status = ProjectSiteDailyReportStatus.Approved
                });

            await db.SaveChangesAsync();
        }

        var siteScope = new CurrentDataScopeSnapshot(
            HasGlobalAccess: false,
            CompanyIds: new HashSet<Guid>(),
            BranchIds: new HashSet<Guid>(),
            ProjectIds: new HashSet<Guid>(),
            VisibleCompanyIds: new HashSet<Guid>(),
            VisibleBranchIds: new HashSet<Guid>(),
            SiteIds: new HashSet<Guid> { visibleSiteId });

        var permissions = new[] { PermissionCatalog.Keys.SiteReportsView };

        var context = new HizirToolContext(
            Guid.NewGuid(), "Test Şef", new[] { "Şantiye Şefi" }, permissions, siteScope);

        using var scope = fixture.Factory.Services.CreateScope();
        var tool = Registry(scope).Find("santiye_gunluk_raporlari");

        var outcome = await tool!.ExecuteAsync(
            context, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.Contains("Görünen Şantiye", outcome.Content);
        Assert.DoesNotContain("Gizli Şantiye", outcome.Content);
    }
}
