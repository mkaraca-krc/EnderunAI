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
/// Görevlendirme ve onay akışı.
///
/// İK talebi açar, GM onaylar. Onaysız görev maliyet ve harcırah
/// üretmez. Görev TÜRÜ hangi maliyet yolunun çalışacağını belirler:
/// yalnız çalışma görevlendirmesinde gün maliyeti hedefe kayar,
/// keşif ve ziyarette sadece masraf yansır.
/// </summary>
[Collection("Integration")]
public sealed class PersonnelDutyTests(DatabaseFixture fixture)
{
    private static readonly DateTime Start =
        new(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime End =
        new(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Context(
        Guid CompanyId, Guid PersonnelId, Guid ActiveProjectId, Guid SurveyProjectId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        project.Status = ProjectStatus.Active;

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        // Aday proje ayrı kavram değil: keşif statüsündeki projenin
        // kendisi.
        var survey = new Project
        {
            CompanyId = project.CompanyId,
            // Proje şubeye bağlı: mevcut projenin şubesi kullanılıyor.
            BranchId = project.BranchId,
            Code = $"KSF-{suffix}",
            Name = $"Keşif İşi {suffix}",
            Status = ProjectStatus.Kesif,
            CurrencyCode = "TRY"
        };

        db.Projects.Add(survey);
        await db.SaveChangesAsync();

        return new Context(project.CompanyId, personnel.Id, project.Id, survey.Id);
    }

    private async Task<HttpClient> ClientWithAsync(
        string[] permissionKeys, string? roleName = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestGorev!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestGorev-{suffix}" };
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

            username = $"gorev-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Görev Test Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

            // Onay yetkisi ROL ADINA bakıyor; seed'li rol
            // DEĞİŞTİRİLMEDEN ikinci rol olarak bağlanıyor.
            if (roleName is not null)
            {
                var named = await db.Roles.SingleOrDefaultAsync(x => x.Name == roleName);

                if (named is null)
                {
                    named = new AppRole { Name = roleName };
                    db.Roles.Add(named);
                    await db.SaveChangesAsync();
                }

                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = named.Id });
            }

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

    /// <summary>
    /// Harcırahı da girebilen İК: tutar YAZMAK ek ödeme yetkisine
    /// bağlı, görevin kendisini açmak değil.
    /// </summary>
    private static readonly string[] HrPermissions =
        [PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PersonnelEdit,
         PermissionCatalog.Keys.ExtraPaymentView];

    private static object Body(
        Context context,
        int dutyType = 0,
        Guid? targetProjectId = null,
        DateTime? start = null,
        DateTime? end = null,
        decimal allowance = 1_500m) => new
    {
        companyId = context.CompanyId,
        personnelId = context.PersonnelId,
        dutyType,
        targetProjectId = targetProjectId ?? context.ActiveProjectId,
        startDate = start ?? Start,
        endDate = end ?? End,
        isOutOfCity = true,
        dailyAllowance = allowance,
        purpose = "Termin baskısı, ekip takviyesi"
    };

    private async Task<Guid> CreateDutyAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/hr/gorevlendirmeler", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        return payload.GetProperty("id").GetGuid();
    }

    // ---------------- Onay akışı ----------------

    /// <summary>
    /// İK talebi açar; görev ONAY BEKLİYOR durumunda doğar ve açan
    /// damgalanır. Onaysız görev maliyet üretmemeli.
    /// </summary>
    [Fact]
    public async Task Duty_StartsAsRequestedWithStamp()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(HrPermissions);
        var id = await CreateDutyAsync(client, Body(context));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var duty = await db.PersonnelDuties.AsNoTracking().SingleAsync(x => x.Id == id);

        Assert.Equal(PersonnelDutyStatus.Requested, duty.Status);
        Assert.NotNull(duty.RequestedByUserId);
        Assert.Null(duty.ApprovedAtUtc);

        // 4-8 Mayıs = 5 gün, 5 × 1.500 = 7.500
        Assert.Equal(5, duty.DayCount);
        Assert.Equal(7_500m, duty.TotalAllowance);
    }

    /// <summary>Onayı yalnız GM verebiliyor; İK kendi talebini onaylayamıyor.</summary>
    [Fact]
    public async Task Approval_RequiresTheGeneralManagerRole()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var hr = await ClientWithAsync(HrPermissions);
        var id = await CreateDutyAsync(hr, Body(context));

        var response = await hr.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{id}/onayla", new { decisionNote = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GeneralManager_ApprovesWithStamp()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var hr = await ClientWithAsync(HrPermissions);
        var id = await CreateDutyAsync(hr, Body(context));

        var gm = await ClientWithAsync(HrPermissions, roleName: "Genel Müdür");

        var response = await gm.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{id}/onayla", new { decisionNote = "Uygun" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var duty = await db.PersonnelDuties.AsNoTracking().SingleAsync(x => x.Id == id);

        Assert.Equal(PersonnelDutyStatus.Approved, duty.Status);
        Assert.NotNull(duty.ApprovedByUserId);
        Assert.NotNull(duty.ApprovedAtUtc);
    }

    /// <summary>Gerekçesiz ret, talebi açana hiçbir bilgi vermez.</summary>
    [Fact]
    public async Task Rejection_RequiresAReason()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var hr = await ClientWithAsync(HrPermissions);
        var id = await CreateDutyAsync(hr, Body(context));

        var gm = await ClientWithAsync(HrPermissions, roleName: "Genel Müdür");

        var response = await gm.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{id}/reddet",
            new { decisionNote = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- Görev türü kuralları ----------------

    /// <summary>
    /// Yalnız ÇALIŞMA görevlendirmesi gün maliyetini kaydırır.
    /// Ziyaret ve keşifte kişi orada imalat üretmiyor.
    /// </summary>
    [Theory]
    [InlineData(0, true)]   // Çalışma
    [InlineData(1, false)]  // Keşif
    [InlineData(2, false)]  // Ziyaret
    public async Task OnlyWorkDuty_ShiftsLaborCost(int dutyType, bool shifts)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(HrPermissions);

        var target = dutyType == 1
            ? context.SurveyProjectId
            : context.ActiveProjectId;

        var id = await CreateDutyAsync(
            client, Body(context, dutyType, target));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var duty = await db.PersonnelDuties.AsNoTracking().SingleAsync(x => x.Id == id);

        Assert.Equal(shifts, duty.ShiftsLaborCost);

        var raw = await (await client.GetAsync(
            $"/api/hr/gorevlendirmeler?personnelId={context.PersonnelId}"))
            .Content.ReadAsStringAsync();

        Assert.Contains($"\"shiftsLaborCost\":{shifts.ToString().ToLowerInvariant()}", raw);
    }

    /// <summary>
    /// Keşif görevi yalnız keşif statüsündeki projeye açılabiliyor;
    /// kazanılınca proje aktife alınır ve masraf olduğu yerde kalır.
    /// </summary>
    [Fact]
    public async Task SurveyDuty_RequiresASurveyStatusProject()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(HrPermissions);

        var response = await client.PostAsJsonAsync(
            "/api/hr/gorevlendirmeler",
            Body(context, dutyType: 1, targetProjectId: context.ActiveProjectId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Keşif statüsündeki projeye çalışma ya da ziyaret görevi
    /// açılamıyor: orada henüz yapılacak iş yok.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task WorkAndVisit_CannotTargetASurveyProject(int dutyType)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(HrPermissions);

        var response = await client.PostAsJsonAsync(
            "/api/hr/gorevlendirmeler",
            Body(context, dutyType, targetProjectId: context.SurveyProjectId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- Çakışma ----------------

    /// <summary>
    /// Aynı personelin aynı güne ikinci görevi açılamıyor: gün
    /// maliyeti tek projeye sayılmalı ve kişi aynı anda iki yerde
    /// olamaz.
    /// </summary>
    [Fact]
    public async Task OverlappingDuty_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(HrPermissions);

        await CreateDutyAsync(client, Body(context));

        var response = await client.PostAsJsonAsync(
            "/api/hr/gorevlendirmeler",
            Body(context, start: Start.AddDays(2), end: End.AddDays(2)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Reddedilen görev çakışma saymıyor.</summary>
    [Fact]
    public async Task RejectedDuty_DoesNotBlockNewOnes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var hr = await ClientWithAsync(HrPermissions);
        var id = await CreateDutyAsync(hr, Body(context));

        var gm = await ClientWithAsync(HrPermissions, roleName: "Genel Müdür");

        await gm.PostAsJsonAsync($"/api/hr/gorevlendirmeler/{id}/reddet",
            new { decisionNote = "Bütçe uygun değil" });

        var response = await hr.PostAsJsonAsync(
            "/api/hr/gorevlendirmeler", Body(context));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EndBeforeStart_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(HrPermissions);

        var response = await client.PostAsJsonAsync(
            "/api/hr/gorevlendirmeler",
            Body(context, start: End, end: Start));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- Tutar gizliliği ----------------

    /// <summary>
    /// NEGATİF TEST: saha personeli görevi görür ama HARCIRAH
    /// TUTARINI görmez. Harcırah elden ödeme niteliğinde.
    /// </summary>
    [Fact]
    public async Task PersonnelViewOnly_SeesDutyButNoAmount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var hr = await ClientWithAsync(HrPermissions);
        await CreateDutyAsync(hr, Body(context));

        var field = await ClientWithAsync([PermissionCatalog.Keys.PersonnelView]);

        var raw = await (await field.GetAsync(
            $"/api/hr/gorevlendirmeler?personnelId={context.PersonnelId}"))
            .Content.ReadAsStringAsync();

        // Görev görünüyor.
        Assert.Contains("Termin baskısı", raw);

        // Tutar hiç gelmiyor.
        Assert.DoesNotContain("1500", raw);
        Assert.DoesNotContain("7500", raw);
        Assert.Contains("\"amountsHidden\":true", raw);
    }

    /// <summary>
    /// Olumlu kontrol: elden ödeme yetkisi olan tutarı görüyor.
    /// </summary>
    [Fact]
    public async Task ExtraPaymentViewer_SeesTheAllowance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var hr = await ClientWithAsync(HrPermissions);
        await CreateDutyAsync(hr, Body(context));

        var viewer = await ClientWithAsync(
            [PermissionCatalog.Keys.PersonnelView,
             PermissionCatalog.Keys.ExtraPaymentView]);

        var raw = await (await viewer.GetAsync(
            $"/api/hr/gorevlendirmeler?personnelId={context.PersonnelId}"))
            .Content.ReadAsStringAsync();

        Assert.Contains("\"amountsHidden\":false", raw);
        Assert.Contains("7500", raw);
    }
}
