using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Yetki kapatma — NEGATİF testler (Block 1).
///
/// Denetimde bulunan üç sızıntının kapandığını KANITLAR:
///
/// B1 — Kariyer uçları maaş döndürüyordu ve izni yalnız personnel.view'dı.
///      Canlıda personnel.view olup salary.view olmayan dört rol var
///      (Şantiye Şefi, Formen, Teknik Koordinatör, İSG Sorumlusu);
///      hepsi herkesin maaşını görebiliyordu.
/// B2 — Kariyer hareketi personnel.create ile personelin maaşını
///      YAZIYORDU; salary.manage aranmıyordu.
/// B3 — Satın alma onay kontrolünde dört uçta hiç izin yoktu; oturum
///      açan herkes proje bütçelerini okuyup değiştirebiliyordu.
/// Ek — Poz kâr marjı ucu projects.view ile açıktı (depo, araç,
///      sekreterya rollerinde de var).
///
/// Desen H2'deki tutar-sızdırmama testinin aynısı: yanıtın HAM METNİ
/// içinde tutar aranıyor. Alan adı değişse bile sızıntı yakalanır.
/// </summary>
[Collection("Integration")]
public sealed class PermissionLeakTests(DatabaseFixture fixture)
{
    private const decimal Salary = 87_654m;

    private sealed record Context(Guid CompanyId, Guid ProjectId, Guid PersonnelId);

    private async Task<HttpClient> AdminAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <summary>Maaşı ve maaş değişikliği geçmişi olan bir personel.</summary>
    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        personnel.MonthlySalary = Salary;

        db.HrCareerHistories.Add(new HrCareerHistory
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            ActionType = HrCareerActionType.SalaryChange,
            EffectiveDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PreviousSalary = 70_000m,
            NewSalary = Salary,
            Reason = "Yıllık zam"
        });

        await db.SaveChangesAsync();

        return new Context(project.CompanyId, project.Id, personnel.Id);
    }

    // ---------------- B1: kariyer uçlarında ücret ----------------

    /// <summary>
    /// personnel.view olan ama salary.view olmayan kullanıcı, kariyer
    /// analizinden HİÇBİR ücret rakamı göremiyor.
    /// </summary>
    [Fact]
    public async Task PersonnelViewOnly_SeesNoSalaryInCareerAnalysis()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var response = await client.GetAsync(
            $"/api/hr/career/analysis/{context.PersonnelId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("87654", raw);
        Assert.DoesNotContain("70000", raw);

        var payload = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("currentSalary").ValueKind);
    }

    /// <summary>
    /// Kariyer GEÇMİŞİ de sızdırmıyor: eski/yeni maaş alanları boş
    /// geliyor.
    /// </summary>
    [Fact]
    public async Task PersonnelViewOnly_SeesNoSalaryInCareerHistory()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var response = await client.GetAsync(
            $"/api/hr/career/personnel/{context.PersonnelId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("87654", raw);
        Assert.DoesNotContain("70000", raw);
    }

    /// <summary>
    /// Olumlu kontrol: salary.view olan kullanıcı rakamı GÖRÜYOR.
    /// Maskeleme her şeyi boşaltmıyor.
    /// </summary>
    [Fact]
    public async Task SalaryViewer_StillSeesTheAmounts()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(
            PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.SalaryView);

        var raw = await (await client.GetAsync(
            $"/api/hr/career/analysis/{context.PersonnelId}")).Content
            .ReadAsStringAsync();

        Assert.Contains("87654", raw);
    }

    // ---------------- B2: kariyer hareketiyle maaş yazma ----------------

    /// <summary>
    /// Kariyer hareketi AÇMAK maaş YAZMA yetkisi değildir: canlıda
    /// Teknik Koordinatör'de personnel.create var, salary.manage yok.
    /// </summary>
    [Fact]
    public async Task PersonnelCreateWithoutSalaryManage_CannotWriteSalary()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(
            PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PersonnelCreate);

        var response = await client.PostAsJsonAsync("/api/hr/career/salary-change", new
        {
            personnelId = context.PersonnelId,
            effectiveDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            newSalary = 999_999m,
            reason = "İzinsiz zam"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Maaş gerçekten değişmemiş olmalı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var personnel = await db.Personnel.AsNoTracking()
            .SingleAsync(x => x.Id == context.PersonnelId);

        Assert.Equal(Salary, personnel.MonthlySalary);
    }

    /// <summary>
    /// Maaş içermeyen kariyer hareketi (terfi, departman değişikliği)
    /// engellenmiyor: kural yalnızca tutara dokunanı kapsıyor.
    /// </summary>
    [Fact]
    public async Task PersonnelCreateWithoutSalaryManage_CanStillRecordNonSalaryMovement()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(
            PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PersonnelCreate);

        var response = await client.PostAsJsonAsync("/api/hr/career/promotion", new
        {
            personnelId = context.PersonnelId,
            effectiveDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            reason = "Terfi"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SalaryManage_CanWriteSalary()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(
            PermissionCatalog.Keys.PersonnelView,
            PermissionCatalog.Keys.PersonnelCreate,
            PermissionCatalog.Keys.SalaryManage);

        var response = await client.PostAsJsonAsync("/api/hr/career/salary-change", new
        {
            personnelId = context.PersonnelId,
            effectiveDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            newSalary = 95_000m,
            reason = "Yetkili zam"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------- B3: satın alma onay kontrolü ----------------

    /// <summary>
    /// Oturum açmış olmak yetmiyor: bütçe okuma ve yazma uçları izin
    /// istiyor. Önce sınıfta yalnız [Authorize] vardı.
    /// </summary>
    [Fact]
    public async Task AuthenticatedUserWithoutPurchasing_CannotReadBudgetDashboard()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var response = await client.GetAsync(
            $"/api/procurement/approval-control/dashboard?companyId={context.CompanyId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUserWithoutPurchasing_CannotCreateBudget()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var response = await client.PostAsJsonAsync(
            $"/api/procurement/approval-control/projects/{context.ProjectId}/budgets",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUserWithoutPurchasing_CannotUpdateBudget()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var response = await client.PutAsJsonAsync(
            $"/api/procurement/approval-control/projects/{context.ProjectId}" +
            $"/budgets/{Guid.NewGuid()}",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUserWithoutPurchasing_CannotReadOrderContext()
    {
        await CreateContextAsync();

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var response = await client.GetAsync(
            $"/api/procurement/approval-control/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------------- Kâr marjı ----------------

    /// <summary>
    /// Poz kâr marjı projects.view ile açıktı; o izin depo, araç ve
    /// sekreterya rollerinde de var. Sözleşme marjı oradan görünmemeli.
    /// </summary>
    [Fact]
    public async Task ProjectsViewOnly_CannotReadProfitMargins()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(PermissionCatalog.Keys.ProjectsView);

        var response = await client.GetAsync(
            $"/api/projects/{context.ProjectId}/kar-analizi");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HakedisViewer_CanReadProfitMargins()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.HakedisView);

        var response = await client.GetAsync(
            $"/api/projects/{context.ProjectId}/kar-analizi");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Maliyet ve kârlılık uçları da aynı kapıda: projects.view depo,
    /// araç, sekreterya, satın alma, ön muhasebe, İK ve İSG
    /// rollerinde de var — kâr rakamı oralara gitmemeli.
    /// </summary>
    [Theory]
    [InlineData("cost-analysis")]
    [InlineData("profitability")]
    public async Task ProjectsViewOnly_CannotReadCostAnalysis(string segment)
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(PermissionCatalog.Keys.ProjectsView);

        var response = await client.GetAsync(
            $"/api/projects/{context.ProjectId}/{segment}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProjectsViewOnly_CannotReadProfitabilitySummary()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(PermissionCatalog.Keys.ProjectsView);

        var response = await client.GetAsync(
            $"/api/projects/profitability-summary?companyId={context.CompanyId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HakedisViewer_CanReadCostAnalysis()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(
            PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.HakedisView);

        var response = await client.GetAsync(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------- Geniş süpürme ----------------

    /// <summary>
    /// H2 deseninin genişletilmişi: personnel.view olan bir kullanıcı,
    /// personel odaklı UÇLARIN HİÇBİRİNDEN ücret rakamı göremiyor.
    /// Alan adı değişse bile ham metinde tutar aranıyor.
    /// </summary>
    [Fact]
    public async Task PersonnelViewOnly_SeesNoSalaryAmountAnywhere()
    {
        var context = await CreateContextAsync();

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        string[] paths =
        [
            $"/api/hr/personnel?companyId={context.CompanyId}",
            $"/api/hr/personnel/{context.PersonnelId}",
            $"/api/hr/personnel/veri-eksikleri?companyId={context.CompanyId}",
            $"/api/hr/career?companyId={context.CompanyId}",
            $"/api/hr/career/personnel/{context.PersonnelId}",
            $"/api/hr/career/analysis/{context.PersonnelId}"
        ];

        foreach (var path in paths)
        {
            var response = await client.GetAsync(path);

            if (response.StatusCode == HttpStatusCode.Forbidden)
                continue;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var raw = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("87654", raw);
            Assert.DoesNotContain("70000", raw);
        }
    }

    // ---------------- Yardımcı ----------------

    private async Task<HttpClient> ClientWithAsync(params string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string username;
        const string password = "TestYetki!2026";

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider
                .GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestYetki-{suffix}" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var permissions = await db.Permissions
                .Where(x => permissionKeys.Contains(x.Key))
                .ToListAsync();

            foreach (var permission in permissions)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id
                });
            }

            username = $"yetki-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Test Yetki Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

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
}
